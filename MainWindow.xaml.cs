using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TempCleaner;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    private readonly StartupOptions _opts;
    private readonly bool _isAdmin;
    private readonly ObservableCollection<CleanCategory> _categories = new(Categories.Build());
    private readonly ObservableCollection<BigFile> _bigFiles = new();
    private CancellationTokenSource? _bigCts;
    private bool _busy;

    public MainWindow(StartupOptions opts)
    {
        _opts = opts;
        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        InitializeComponent();

        CategoryList.ItemsSource = _categories;
        BigGrid.ItemsSource = _bigFiles;
        foreach (var c in _categories)
        {
            c.CanSelect = !c.NeedsAdmin || _isAdmin;
            c.PropertyChanged += Category_PropertyChanged;
        }

        if (_isAdmin)
        {
            AdminText.Text = "Đang chạy quyền admin";
            AdminText.Foreground = (Brush)FindResource("OkBrush");
            AdminBadge.Background = (Brush)FindResource("OkBgBrush");
            RunAsAdminButton.Visibility = Visibility.Collapsed;
            Title += " (Admin)";
        }
        else
        {
            AdminText.Text = "Chế độ thường · 3 nhóm cần quyền admin";
            AdminText.Foreground = (Brush)FindResource("WarnBrush");
            AdminBadge.Background = (Brush)FindResource("WarnBgBrush");
        }

        RefreshDrives();
        foreach (var d in DriveVm.Read()) BigRootBox.Items.Add(d.Name + "\\");
        BigRootBox.Text = BigRootBox.Items.Contains("D:\\") ? "D:\\" : BigRootBox.Items.Count > 0 ? (string)BigRootBox.Items[0] : "C:\\";
        if (_opts.BigTab) Tabs.SelectedIndex = 1;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Luôn quét ngay khi mở: chỉ đọc, không xoá gì.
        await ScanAllAsync();
        if (_opts.BigScan is { Length: > 0 } root)
        {
            BigRootBox.Text = root;
            await BigScanAsync();
        }
        if (_opts.Screenshot is { Length: > 0 } shot)
        {
            await CaptureAsync(shot);
            if (_opts.Exit) Close();
        }
    }

    // ---------------- Dọn file tạm ----------------

    private void Category_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanCategory.IsChecked) or nameof(CleanCategory.Bytes) or nameof(CleanCategory.CheckEnabled))
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        var sel = _categories.Where(c => c.IsChecked && c.CheckEnabled).ToList();
        long bytes = sel.Sum(c => c.Bytes);
        SelectionText.Text = sel.Count == 0
            ? "Chưa chọn nhóm nào"
            : $"Đã chọn {sel.Count} nhóm · sẽ giải phóng khoảng {Fmt.Size(bytes)}";
        CleanButton.IsEnabled = !_busy && sel.Count > 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        AgeFilterBox.IsEnabled = !busy;
        UpdateSelection();
    }

    private async Task ScanAllAsync()
    {
        if (_busy) return;
        SetBusy(true);
        ScanStatus.Text = "Đang quét…";
        var sw = Stopwatch.StartNew();
        var cutoff = DateTime.UtcNow - RecentWindow;
        await Task.WhenAll(_categories.Select(c => c.ScanAsync(cutoff, _isAdmin)));
        long total = _categories.Where(c => c.CanSelect).Sum(c => c.Bytes);
        long adminOnly = _categories.Where(c => !c.CanSelect).Sum(c => c.Bytes);
        ScanStatus.Text = $"Quét xong trong {sw.Elapsed.TotalSeconds.ToString("0.0", Fmt.Vi)} giây · có thể dọn {Fmt.Size(total)}"
                          + (adminOnly > 0 ? $" (+{Fmt.Size(adminOnly)} cần admin)" : "");
        SetBusy(false);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAllAsync();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _categories) if (c.CheckEnabled) c.IsChecked = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _categories) c.IsChecked = false;
    }

    private void AgeFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_categories == null) return;   // gọi trong InitializeComponent
        foreach (var c in _categories) c.AgeFilterOn = AgeFilterBox.IsChecked == true;
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var sel = _categories.Where(c => c.IsChecked && c.CheckEnabled).ToList();
        if (sel.Count == 0 || _busy) return;

        long estimate = sel.Sum(c => c.Bytes);
        var msg = "Xoá vĩnh viễn nội dung của các nhóm sau?\n\n"
                  + string.Join("\n", sel.Select(c => $"•  {c.Name}: {Fmt.Size(c.Bytes)}"))
                  + $"\n\nTổng khoảng {Fmt.Size(estimate)}. File đang bị khoá sẽ được bỏ qua."
                  + (sel.Any(c => c.IsRecycleBin) ? "\nThùng rác sẽ bị dọn sạch, không khôi phục được." : "");
        if (MessageBox.Show(this, msg, "Xác nhận dọn", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            != MessageBoxResult.Yes) return;

        SetBusy(true);
        ResultText.Text = "Đang dọn…";
        var before = DriveVm.Read().ToDictionary(d => d.Name, d => d.Free);
        bool ageOn = AgeFilterBox.IsChecked == true;
        var cutoff = DateTime.UtcNow - RecentWindow;

        var total = new CleanStats();
        foreach (var c in sel)
        {
            total.Add(await c.CleanAsync(c.UseAgeFilter && ageOn ? cutoff : null));
            c.IsChecked = false;
        }
        await Task.WhenAll(sel.Select(c => c.ScanAsync(DateTime.UtcNow - RecentWindow, _isAdmin, keepStatus: true)));

        RefreshDrives();
        var deltas = DriveVm.Read()
            .Where(d => before.TryGetValue(d.Name, out var b) && d.Free - b > 1024 * 1024)
            .Select(d => $"{d.Name} trống thêm {Fmt.Size(d.Free - before[d.Name])}");

        ResultText.Text = $"Đã xoá {Fmt.N(total.Deleted)} file · bỏ qua {Fmt.N(total.Skipped)} file đang bị khoá / không có quyền"
                          + (total.Kept > 0 ? $" · giữ lại {Fmt.N(total.Kept)} file mới" : "")
                          + $" · giải phóng thực {Fmt.Size(total.Freed)}"
                          + (deltas.Any() ? " (" + string.Join(", ", deltas) + ")" : "");
        SetBusy(false);
    }

    private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--autoscan")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, "Đã huỷ yêu cầu quyền admin. App tiếp tục chạy ở chế độ thường.",
                "Temp Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshDrives() => DriveList.ItemsSource = DriveVm.Read();

    // ---------------- Tìm file lớn ----------------

    private async void BigScan_Click(object sender, RoutedEventArgs e) => await BigScanAsync();

    private void BigStop_Click(object sender, RoutedEventArgs e) => _bigCts?.Cancel();

    private async Task BigScanAsync()
    {
        if (_bigCts != null) return;
        var root = BigRootBox.Text.Trim();
        if (root.Length == 2 && root[1] == ':') root += "\\";
        if (!Directory.Exists(root))
        {
            BigStatus.Text = "Không tìm thấy thư mục: " + root;
            return;
        }
        if (!double.TryParse(BigMinBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var minMb) || minMb < 1)
            minMb = 500;
        long minBytes = (long)(minMb * 1024 * 1024);

        var roots = new List<string> { root };
        if (BigWslBox.IsChecked == true)
            roots.AddRange(Categories.VirtualDiskRoots()
                .Where(r => !r.StartsWith(root, StringComparison.OrdinalIgnoreCase)));

        _bigCts = new CancellationTokenSource();
        var ct = _bigCts.Token;
        BigScanButton.IsEnabled = false;
        BigStopButton.IsEnabled = true;
        _bigFiles.Clear();

        var seen = new Counter();
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => BigStatus.Text = $"Đang quét… {Fmt.N(Volatile.Read(ref seen.Value))} mục";
        timer.Start();

        int denied = 0;
        var found = await Task.Run(() =>
        {
            var all = new List<FileEntry>();
            foreach (var r in roots)
            {
                if (ct.IsCancellationRequested) break;
                all.AddRange(Cleaner.List(new Target(r), out var d, minSize: minBytes, seen: seen, ct: ct));
                denied += d;
            }
            return all.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                      .OrderByDescending(f => f.Length).Take(1000).ToList();
        });
        timer.Stop();

        foreach (var f in found)
            _bigFiles.Add(new BigFile { FullPath = f.Path, Length = f.Length, Modified = f.LastWriteUtc.ToLocalTime() });

        long sum = found.Sum(f => f.Length);
        BigStatus.Text = (ct.IsCancellationRequested ? "Đã dừng" : "Xong")
                         + $" sau {sw.Elapsed.TotalSeconds.ToString("0.0", Fmt.Vi)} giây · {Fmt.N(seen.Value)} mục"
                         + (denied > 0 ? $" · {Fmt.N(denied)} thư mục không đọc được" : "");
        BigSummary.Text = $"{Fmt.N(found.Count)} file ≥ {Fmt.Size(minBytes)}, tổng {Fmt.Size(sum)}. Chỉ liệt kê, app không xoá file nào ở đây.";

        _bigCts.Dispose();
        _bigCts = null;
        BigScanButton.IsEnabled = true;
        BigStopButton.IsEnabled = false;
    }

    private void BigGrid_DoubleClick(object sender, MouseButtonEventArgs e) => OpenLocation_Click(sender, e);

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (BigGrid.SelectedItem is not BigFile f) return;
        try { Process.Start("explorer.exe", $"/select,\"{f.FullPath}\""); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Temp Cleaner"); }
    }

    // ---------------- Chụp màn hình (--screenshot) ----------------

    private async Task CaptureAsync(string path)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(Root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
