using System.Windows;

namespace TempCleaner;

/// <summary>
/// Tham số dòng lệnh (dùng cho kiểm thử / chụp màn hình):
///   --autoscan                 quét ngay khi mở
///   --tab big                  mở tab "Tìm file lớn"
///   --bigscan D:\              quét file lớn ở đường dẫn này khi mở
///   --screenshot file.png      chụp cửa sổ sau khi quét xong
///   --exit                     thoát sau khi chụp
///   --test-clean dir out.txt   chạy Cleaner.Clean trên dir, ghi kết quả ra out.txt
/// </summary>
public sealed class StartupOptions
{
    public bool AutoScan, BigTab, Exit;
    public string? BigScan, Screenshot;

    public static StartupOptions Parse(string[] a)
    {
        var o = new StartupOptions();
        for (int i = 0; i < a.Length; i++)
        {
            string Next() => i + 1 < a.Length ? a[++i] : "";
            switch (a[i].ToLowerInvariant())
            {
                case "--autoscan": o.AutoScan = true; break;
                case "--tab": o.BigTab = Next() == "big"; break;
                case "--bigscan": o.BigScan = Next(); break;
                case "--screenshot": o.Screenshot = Next(); break;
                case "--exit": o.Exit = true; break;
            }
        }
        return o;
    }
}

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var a = e.Args;
        if (a.Length >= 3 && a[0] == "--test-clean")
        {
            var r = Cleaner.Clean(new[] { new Target(a[1]) }, null);
            File.WriteAllText(a[2], $"deleted={r.Deleted} skipped={r.Skipped} kept={r.Kept} freed={r.Freed} dirs={r.DirsRemoved}");
            Shutdown();
            return;
        }
        var w = new MainWindow(StartupOptions.Parse(a));
        MainWindow = w;
        w.Show();
    }
}
