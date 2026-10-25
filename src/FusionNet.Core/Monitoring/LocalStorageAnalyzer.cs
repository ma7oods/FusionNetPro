using System.Diagnostics;
using System.Security.Cryptography;

namespace FusionNet.Core.Monitoring;

public sealed record StorageEntry(string Path, long ObservedBytes);
public sealed record StorageReport(string Root, DateTimeOffset TimestampUtc, bool Complete, string Coverage,
    IReadOnlyList<StorageEntry> LargestFiles, IReadOnlyList<StorageEntry> LargestFolders,
    IReadOnlyList<StorageEntry> OldDownloads, IReadOnlyList<StorageEntry> InstallerCandidates,
    IReadOnlyList<IReadOnlyList<string>> IdenticalPhotoCandidates);
public interface ILocalStorageAnalyzer
{
    Task<StorageReport> ScanAsync(string root, bool comparePhotoContents, CancellationToken token);
}

public sealed class LocalStorageAnalyzer : ILocalStorageAnalyzer
{
    public Task<StorageReport> ScanAsync(string root, bool comparePhotoContents, CancellationToken token) =>
        Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullRoot.StartsWith(@"\\") || !Directory.Exists(fullRoot)) throw new ArgumentException("اختر مجلدًا محليًا موجودًا.");
            var drive = new DriveInfo(Path.GetPathRoot(fullRoot)!);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) throw new ArgumentException("لا يدعم الفحص مسارات الشبكة.");
            DirectoryInfo? ancestor = new(fullRoot);
            while (ancestor is not null)
            {
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("اختر مسارًا مباشرًا؛ روابط المجلدات مستثناة.");
                ancestor = ancestor.Parent;
            }
            var watch = Stopwatch.StartNew();
            var pending = new Stack<string>(); pending.Push(fullRoot);
            var files = new List<(string Path, long Size, DateTime Written)>();
            var folders = new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
            var errors = 0; var links = 0; var visited = 0;
            while (pending.Count > 0 && files.Count < 20000 && visited < 10000 && watch.Elapsed < TimeSpan.FromSeconds(20))
            {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop(); visited++;
                try
                {
                    foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if (files.Count >= 20000 || watch.Elapsed >= TimeSpan.FromSeconds(20)) break;
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                        if (item is DirectoryInfo) { if (pending.Count < 10000) pending.Push(item.FullName); else errors++; continue; }
                        if (item is not FileInfo file) continue;
                        files.Add((file.FullName,file.Length,file.LastWriteTimeUtc));
                        var parent = file.Directory;
                        while (parent is not null && (parent.FullName.Equals(fullRoot,StringComparison.OrdinalIgnoreCase) || parent.FullName.StartsWith(rootPrefix,StringComparison.OrdinalIgnoreCase)))
                        {
                            folders.TryGetValue(parent.FullName,out var size);
                            folders[parent.FullName] = checked(size + file.Length);
                            parent = parent.Parent;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { errors++; }
            }
            var capped = pending.Count > 0 || files.Count >= 20000 || visited >= 10000 || watch.Elapsed >= TimeSpan.FromSeconds(20);
            var duplicates = new List<IReadOnlyList<string>>();
            var compared = 0; long comparedBytes = 0;
            if (comparePhotoContents)
            {
                string[] photos = [".jpg",".jpeg",".png",".gif",".bmp",".webp",".tif",".tiff",".heic"];
                var candidates = files.Where(file => file.Size <= 20 * 1024 * 1024 && photos.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))
                    .GroupBy(file => file.Size).Where(group => group.Count() > 1).SelectMany(group => group).Take(101).ToArray();
                var hashes = new Dictionary<string,List<string>>();
                foreach (var file in candidates.Take(100))
                {
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed >= TimeSpan.FromSeconds(20) || comparedBytes + file.Size > 100 * 1024 * 1024) { capped = true; break; }
                    try
                    {
                        if ((File.GetAttributes(file.Path) & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false));
                        var current = new FileInfo(file.Path);
                        if (current.Length != file.Size || current.LastWriteTimeUtc != file.Written) { errors++; continue; }
                        if (!hashes.TryGetValue(hash,out var matches)) hashes[hash]=matches=[];
                        matches.Add(file.Path); compared++; comparedBytes+=file.Size;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; }
                }
                duplicates.AddRange(hashes.Values.Where(group => group.Count>1).Take(20));
                if (candidates.Length>100) capped=true;
            }
            StorageEntry[] Top(IEnumerable<(string Path,long Size,DateTime Written)> entries) => entries.OrderByDescending(file=>file.Size).Take(20).Select(file=>new StorageEntry(file.Path,file.Size)).ToArray();
            var old = files.Where(file=>file.Written < DateTime.UtcNow.AddDays(-180) && file.Path.Split(Path.DirectorySeparatorChar).Contains("Downloads",StringComparer.OrdinalIgnoreCase));
            var installers = files.Where(file=>new[]{".msi",".msix",".appx"}.Contains(Path.GetExtension(file.Path).ToLowerInvariant()) ||
                Path.GetExtension(file.Path).Equals(".exe",StringComparison.OrdinalIgnoreCase) && (Path.GetFileName(file.Path).Contains("setup",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file.Path).Contains("install",StringComparison.OrdinalIgnoreCase)));
            return new StorageReport(fullRoot,DateTimeOffset.UtcNow,!capped&&errors==0&&links==0,
                $"أحجام منطقية للبيانات المقروءة، وليست قياس المساحة المخصصة. ملفات: {files.Count}، مجلدات: {visited}، غير مقروءة: {errors}، روابط مستثناة: {links}. الحد: 20000 ملف/10000 مجلد ونحو20 ثانية. {(capped?"وصل الفحص لحد التغطية؛ الأحجام جزئية.":"")} مقارنة الصور: {compared} ملفًا/بحد100MB، تطابق bytes فقط وفق SHA256، وليست صورًا متشابهة بصريًا. قدم التنزيل حسب آخر تعديل؛ مرشح المثبّت لا يثبت أنه غير مستخدم.",
                Top(files), folders.Where(folder=>!folder.Key.Equals(fullRoot,StringComparison.OrdinalIgnoreCase)).OrderByDescending(folder=>folder.Value).Take(20).Select(folder=>new StorageEntry(folder.Key,folder.Value)).ToArray(), Top(old),Top(installers),duplicates);
        },token);
}
