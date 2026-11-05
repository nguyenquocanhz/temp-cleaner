using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TempCleaner;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public static class Fmt
{
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes; string[] u = { "KB", "MB", "GB", "TB" }; int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
        string f = i <= 0 ? "0" : v >= 100 ? "0" : "0.0";
        return v.ToString(f, Vi) + " " + u[i];
    }

    public static string N(long n) => n.ToString("N0", Vi);
}

public enum StatusKind { None, Info, Warn, Ok, Busy }

public sealed class CleanCategory : Observable
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public string PathHint { get; init; } = "";
    public bool NeedsAdmin { get; init; }
    public bool UseAgeFilter { get; init; }
    public bool IsRecycleBin { get; init; }
    public Func<IEnumerable<Target>> Resolve { get; init; } = Array.Empty<Target>;
    /// <summary>Ghi chú động, ví dụ trình duyệt đang mở.</summary>
    public Func<string?>? Note { get; init; }

    private bool _isChecked, _canSelect = true, _isBusy, _scanned, _ageFilterOn = true;
    private ScanStats _stats = new();
    private string _status = "";
    private StatusKind _statusKind;

    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    public bool CanSelect { get => _canSelect; set { if (Set(ref _canSelect, value)) Raise(nameof(CheckEnabled)); } }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { Raise(nameof(CheckEnabled)); RaiseSizes(); } } }
    public bool CheckEnabled => CanSelect && !IsBusy;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }

    public bool AgeFilterOn { get => _ageFilterOn; set { if (Set(ref _ageFilterOn, value)) RaiseSizes(); } }
    private bool Filtered => UseAgeFilter && AgeFilterOn;

    /// <summary>Dung lượng sẽ giải phóng nếu dọn (đã trừ file mới khi bật lọc 24 giờ).</summary>
    public long Bytes => !_scanned ? 0 : Filtered ? _stats.Bytes - _stats.RecentBytes : _stats.Bytes;
    public long Files => !_scanned ? 0 : Filtered ? _stats.Files - _stats.RecentFiles : _stats.Files;

    /// <summary>Có thư mục không đọc được (thường do thiếu quyền admin) nên số thật có thể lớn hơn.</summary>
    public bool Partial => _scanned && _stats.Denied > 0;

    public string SizeText => IsBusy ? "…" : !_scanned ? "—"
        : Partial && Bytes == 0 ? "Chưa đọc được"
        : (Partial ? "≥ " : "") + Fmt.Size(Bytes);
    public string DetailText
    {
        get
        {
            if (!_scanned) return "";
            var parts = new List<string> { IsRecycleBin ? $"{Fmt.N(Files)} mục" : $"{Fmt.N(Files)} file" };
            if (Filtered && _stats.RecentFiles > 0)
                parts.Add($"giữ {Fmt.Size(_stats.RecentBytes)} mới");
            if (Partial && NeedsAdmin)
                parts.Add("cần admin để đọc hết");
            return string.Join(" · ", parts);
        }
    }

    private void RaiseSizes()
    {
        Raise(nameof(Bytes)); Raise(nameof(Files)); Raise(nameof(SizeText)); Raise(nameof(DetailText));
    }

    private void SetStatus(string text, StatusKind kind) { Status = text; StatusKind = kind; }

    private void SetDefaultStatus(bool isAdmin)
    {
        if (NeedsAdmin && !isAdmin) { SetStatus("Cần quyền admin để xoá", StatusKind.Warn); return; }
        var note = Note?.Invoke();
        if (note != null) { SetStatus(note, StatusKind.Info); return; }
        if (_stats.Denied > 0) { SetStatus($"{Fmt.N(_stats.Denied)} thư mục không đọc được", StatusKind.Info); return; }
        SetStatus("", StatusKind.None);
    }

    public async Task ScanAsync(DateTime recentCutoffUtc, bool isAdmin, bool keepStatus = false)
    {
        CanSelect = !NeedsAdmin || isAdmin;
        if (!CanSelect) IsChecked = false;
        IsBusy = true;
        if (!keepStatus) SetStatus("Đang quét…", StatusKind.Busy);
        try
        {
            _stats = await Task.Run(() => IsRecycleBin ? RecycleBin.Measure() : Cleaner.Measure(Resolve(), recentCutoffUtc));
        }
        catch (Exception ex) { _stats = new ScanStats(); SetStatus("Lỗi khi quét: " + ex.Message, StatusKind.Warn); keepStatus = true; }
        _scanned = true;
        IsBusy = false;
        if (!keepStatus) SetDefaultStatus(isAdmin);
    }

    public async Task<CleanStats> CleanAsync(DateTime? keepNewerThanUtc)
    {
        IsBusy = true;
        SetStatus("Đang dọn…", StatusKind.Busy);
        CleanStats r;
        try
        {
            r = await Task.Run(() => IsRecycleBin ? RecycleBin.Empty() : Cleaner.Clean(Resolve(), keepNewerThanUtc));
            var text = $"Đã xoá {Fmt.N(r.Deleted)} · bỏ qua {Fmt.N(r.Skipped)} · giải phóng {Fmt.Size(r.Freed)}";
            SetStatus(text, r.Skipped > 0 ? StatusKind.Info : StatusKind.Ok);
        }
        catch (Exception ex)
        {
            r = new CleanStats();
            SetStatus("Lỗi khi dọn: " + ex.Message, StatusKind.Warn);
        }
        IsBusy = false;
        return r;
    }
}

public sealed class BigFile
{
    public required string FullPath { get; init; }
    public long Length { get; init; }
    public DateTime Modified { get; init; }
    public string Name => System.IO.Path.GetFileName(FullPath);
    public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? "";
    public string SizeText => Fmt.Size(Length);
    public string ModifiedText => Modified.ToString("dd/MM/yyyy HH:mm", Fmt.Vi);
    public string TypeLabel => System.IO.Path.GetExtension(FullPath).ToLowerInvariant() switch
    {
        ".iso" or ".img" => "Ảnh đĩa",
        ".vhdx" or ".vhd" => "Ổ ảo (WSL / Hyper-V)",
        ".vmdk" or ".vdi" => "Ổ ảo máy ảo",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".xz" or ".zst" => "File nén",
        ".mp4" or ".mkv" or ".mov" or ".avi" => "Video",
        ".exe" or ".msi" => "Bộ cài",
        ".dmp" => "Crash dump",
        ".sys" when Name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) => "Pagefile",
        ".sys" when Name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) => "Ngủ đông",
        "" => "Không đuôi",
        var e => e.TrimStart('.').ToUpperInvariant(),
    };
}

public sealed class DriveVm
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public long Free { get; init; }
    public long Total { get; init; }
    public double UsedPercent => Total == 0 ? 0 : 100.0 * (Total - Free) / Total;
    public string FreeText => $"Còn trống {Fmt.Size(Free)} / {Fmt.Size(Total)}";
    public bool Low => Total > 0 && Free < Total / 10;

    public static List<DriveVm> Read()
    {
        var list = new List<DriveVm>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new DriveVm
                {
                    Name = d.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Ổ cục bộ" : d.VolumeLabel,
                    Free = d.AvailableFreeSpace,
                    Total = d.TotalSize,
                });
            }
            catch { }
        }
        return list;
    }
}

public static class Categories
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string UserTemp => Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
    public static string ClaudeTemp => Path.Combine(UserTemp, "claude");

    private static IEnumerable<string> SubDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<Target> Chromium(string userData)
    {
        foreach (var s in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
            yield return new Target(Path.Combine(userData, s));
        foreach (var profile in SubDirs(userData))
            foreach (var c in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache" })
                yield return new Target(Path.Combine(profile, c));
    }

    private static IEnumerable<Target> Firefox()
    {
        foreach (var root in new[] { Path.Combine(Local, "Mozilla", "Firefox", "Profiles") })
            foreach (var p in SubDirs(root))
                foreach (var c in new[] { "cache2", "startupCache", "thumbnails", "jumpListCache" })
                    yield return new Target(Path.Combine(p, c));
    }

    private static Func<string?> RunningNote(string process, string browser) => () =>
    {
        try
        {
            var procs = Process.GetProcessesByName(process);
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0 ? $"{browser} đang mở: file đang dùng sẽ bị bỏ qua" : null;
        }
        catch { return null; }
    };

    public static List<CleanCategory> Build() => new()
    {
        new()
        {
            Name = "Thư mục tạm của Claude Code", Icon = "", UseAgeFilter = true,
            Description = "Mỗi session một thư mục scratchpad (ảnh chụp, script thử). Bật lọc 24 giờ để không đụng session đang chạy.",
            PathHint = ClaudeTemp,
            Resolve = () => new[] { new Target(ClaudeTemp) },
        },
        new()
        {
            Name = "File tạm của người dùng (%TEMP%)", Icon = "", UseAgeFilter = true,
            Description = "File tạm do các chương trình để lại. Không tính thư mục claude (đã có nhóm riêng).",
            PathHint = UserTemp,
            Resolve = () => new[] { new Target(UserTemp, Exclude: new[] { ClaudeTemp }) },
        },
        new()
        {
            Name = "File tạm của Windows", Icon = "", UseAgeFilter = true, NeedsAdmin = true,
            Description = "File tạm của dịch vụ và bộ cài hệ thống.",
            PathHint = Path.Combine(Win, "Temp"),
            Resolve = () => new[] { new Target(Path.Combine(Win, "Temp")) },
        },
        new()
        {
            Name = "Thùng rác", Icon = "", IsRecycleBin = true,
            Description = "Mọi file đã xoá trên tất cả các ổ. Dọn xong không khôi phục được.",
            PathHint = "Tất cả các ổ",
        },
        new()
        {
            Name = "Bộ nhớ đệm Windows Update", Icon = "", NeedsAdmin = true,
            Description = "Gói cập nhật đã tải về. Windows tải lại nếu còn cần.",
            PathHint = Path.Combine(Win, "SoftwareDistribution", "Download"),
            Resolve = () => new[] { new Target(Path.Combine(Win, "SoftwareDistribution", "Download")) },
        },
        new()
        {
            Name = "Cache Google Chrome", Icon = "",
            Description = "Cache trang web, code và GPU. Không đụng lịch sử, mật khẩu, cookie.",
            PathHint = Path.Combine(Local, "Google", "Chrome", "User Data"),
            Resolve = () => Chromium(Path.Combine(Local, "Google", "Chrome", "User Data")),
            Note = RunningNote("chrome", "Chrome"),
        },
        new()
        {
            Name = "Cache Microsoft Edge", Icon = "",
            Description = "Cache trang web, code và GPU. Không đụng lịch sử, mật khẩu, cookie.",
            PathHint = Path.Combine(Local, "Microsoft", "Edge", "User Data"),
            Resolve = () => Chromium(Path.Combine(Local, "Microsoft", "Edge", "User Data")),
            Note = RunningNote("msedge", "Edge"),
        },
        new()
        {
            Name = "Cache Firefox", Icon = "",
            Description = "cache2, startupCache, thumbnails của mọi profile.",
            PathHint = Path.Combine(Local, "Mozilla", "Firefox", "Profiles"),
            Resolve = Firefox,
            Note = RunningNote("firefox", "Firefox"),
        },
        new()
        {
            Name = "Thumbnail cache", Icon = "",
            Description = "Ảnh thu nhỏ của Explorer. Explorer giữ khoá nên một phần sẽ bị bỏ qua; Windows tự tạo lại.",
            PathHint = Path.Combine(Local, "Microsoft", "Windows", "Explorer"),
            Resolve = () => new[]
            {
                new Target(Path.Combine(Local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", Recursive: false),
                new Target(Path.Combine(Local, "Microsoft", "Windows", "Explorer"), "iconcache_*.db", Recursive: false),
            },
        },
        new()
        {
            Name = "Crash dump của ứng dụng", Icon = "",
            Description = "Dump khi ứng dụng bị crash và báo cáo lỗi Windows (WER) của người dùng.",
            PathHint = Path.Combine(Local, "CrashDumps"),
            Resolve = () => new[]
            {
                new Target(Path.Combine(Local, "CrashDumps")),
                new Target(Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportArchive")),
                new Target(Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportQueue")),
            },
        },
        new()
        {
            Name = "Crash dump hệ thống", Icon = "", NeedsAdmin = true,
            Description = "MEMORY.DMP, Minidump, LiveKernelReports và WER của hệ thống.",
            PathHint = Path.Combine(Win, "Minidump"),
            Resolve = () => new[]
            {
                new Target(Win, "MEMORY.DMP", Recursive: false),
                new Target(Path.Combine(Win, "Minidump")),
                new Target(Path.Combine(Win, "LiveKernelReports"), "*.dmp"),
                new Target(Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive")),
                new Target(Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue")),
            },
        },
    };

    /// <summary>Thư mục chứa ổ ảo WSL / Docker trên ổ C: (kèm thêm khi tìm file lớn).</summary>
    public static IEnumerable<string> VirtualDiskRoots() => new[]
    {
        Path.Combine(Local, "Packages"),
        Path.Combine(Local, "wsl"),
        Path.Combine(Local, "Docker"),
    }.Where(Directory.Exists);
}
