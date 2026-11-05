using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace TempCleaner;

public readonly record struct FileEntry(string Path, long Length, DateTime LastWriteUtc, FileAttributes Attributes);

/// <summary>Một thư mục (hoặc mẫu file trong thư mục) thuộc một nhóm cần dọn.</summary>
public sealed record Target(string Root, string Pattern = "*", bool Recursive = true, string[]? Exclude = null);

public sealed class ScanStats
{
    public long Bytes, Files, RecentBytes, RecentFiles;
    public int Denied;
}

public sealed class CleanStats
{
    public long Deleted, Skipped, Kept, Freed, DirsRemoved;
    public void Add(CleanStats o)
    {
        Deleted += o.Deleted; Skipped += o.Skipped; Kept += o.Kept; Freed += o.Freed; DirsRemoved += o.DirsRemoved;
    }
}

public sealed class Counter { public long Value; }

/// <summary>
/// Duyệt cây thư mục. Không đi vào junction/symlink (để không xoá nhầm ra ngoài),
/// không dừng khi gặp thư mục không có quyền đọc, chỉ đếm lại.
/// </summary>
internal sealed class Walker : FileSystemEnumerator<FileEntry>
{
    private readonly string _pattern = "*";
    private readonly string[] _exclude = Array.Empty<string>();
    private readonly long _minSize;
    private readonly bool _dirsOnly;
    private readonly Counter? _seen;
    public int Denied;

    public Walker(string root, bool recursive, string pattern = "*", string[]? exclude = null,
                  long minSize = 0, bool dirsOnly = false, Counter? seen = null)
        : base(root, new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = false,      // để ContinueOnError đếm được thư mục bị từ chối
            AttributesToSkip = 0,            // lấy cả file ẩn / hệ thống
            ReturnSpecialDirectories = false,
        })
    {
        _pattern = pattern;
        _exclude = exclude ?? Array.Empty<string>();
        _minSize = minSize;
        _dirsOnly = dirsOnly;
        _seen = seen;
    }

    private bool IsExcluded(ref FileSystemEntry e)
    {
        if (_exclude.Length == 0) return false;
        var p = e.ToFullPath();
        foreach (var x in _exclude)
            if (string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (_seen != null) _seen.Value++;
        if (_dirsOnly)
            return entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !IsExcluded(ref entry);
        if (entry.IsDirectory || entry.Length < _minSize) return false;
        return _pattern == "*" || FileSystemName.MatchesSimpleExpression(_pattern, entry.FileName);
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
        => (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !IsExcluded(ref entry);

    protected override FileEntry TransformEntry(ref FileSystemEntry entry)
        => new(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime, entry.Attributes);

    protected override bool ContinueOnError(int error)
    {
        if (error == 5) Denied++;   // ERROR_ACCESS_DENIED
        return true;
    }
}

public static class Cleaner
{
    /// <summary>Liệt kê file an toàn: không bao giờ ném lỗi, trả về số thư mục không đọc được.</summary>
    public static List<FileEntry> List(Target t, out int denied, bool dirsOnly = false,
                                       long minSize = 0, Counter? seen = null, CancellationToken ct = default)
    {
        var list = new List<FileEntry>();
        denied = 0;
        if (!Directory.Exists(t.Root)) return list;
        try
        {
            using var w = new Walker(t.Root, t.Recursive, t.Pattern, t.Exclude, minSize, dirsOnly, seen);
            while (!ct.IsCancellationRequested)
            {
                bool ok;
                try { ok = w.MoveNext(); }
                catch { denied++; break; }
                if (!ok) break;
                list.Add(w.Current);
            }
            denied += w.Denied;
        }
        catch (UnauthorizedAccessException) { denied++; }
        catch (IOException) { denied++; }
        return list;
    }

    public static ScanStats Measure(IEnumerable<Target> targets, DateTime recentCutoffUtc)
    {
        var s = new ScanStats();
        foreach (var t in targets)
        {
            var files = List(t, out var denied);
            foreach (var f in files)
            {
                s.Bytes += f.Length; s.Files++;
                if (f.LastWriteUtc > recentCutoffUtc) { s.RecentBytes += f.Length; s.RecentFiles++; }
            }
            s.Denied += denied;
        }
        return s;
    }

    /// <param name="keepNewerThanUtc">null = xoá hết; có giá trị = giữ file/thư mục sửa sau mốc này.</param>
    public static CleanStats Clean(IEnumerable<Target> targets, DateTime? keepNewerThanUtc)
    {
        var r = new CleanStats();
        foreach (var t in targets)
        {
            foreach (var f in List(t, out _))
            {
                if (keepNewerThanUtc is { } cut && f.LastWriteUtc > cut) { r.Kept++; continue; }
                try
                {
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(f.Path, FileAttributes.Normal);
                    File.Delete(f.Path);
                    r.Deleted++;
                    r.Freed += f.Length;
                }
                catch { r.Skipped++; }   // file đang bị khoá / không có quyền: bỏ qua
            }

            if (!t.Recursive) continue;
            // Xoá thư mục con đã trống, sâu nhất trước. Không bao giờ xoá chính thư mục gốc.
            var dirs = List(t, out _, dirsOnly: true);
            foreach (var d in dirs.OrderByDescending(d => d.Path.Length))
            {
                if (keepNewerThanUtc is { } cut && d.LastWriteUtc > cut) continue;
                try { Directory.Delete(d.Path, recursive: false); r.DirsRemoved++; }
                catch { /* còn file hoặc đang dùng */ }
            }
        }
        return r;
    }
}

public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint flags);

    private const uint NoConfirm = 0x1, NoProgress = 0x2, NoSound = 0x4;

    public static (long Bytes, long Items) Query()
    {
        var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref i) == 0 ? (i.i64Size, i.i64NumItems) : (0, 0);
    }

    public static ScanStats Measure()
    {
        var (b, n) = Query();
        return new ScanStats { Bytes = b, Files = n };
    }

    public static CleanStats Empty()
    {
        var before = Query();
        if (before.Items > 0) SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirm | NoProgress | NoSound);
        var after = Query();
        return new CleanStats
        {
            Deleted = Math.Max(0, before.Items - after.Items),
            Skipped = after.Items,
            Freed = Math.Max(0, before.Bytes - after.Bytes),
        };
    }
}
