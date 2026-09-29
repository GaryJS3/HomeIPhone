using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace HomeIPhone;

public sealed record TftpFileInfo(string Name, long Size, DateTime LastModifiedUtc);
public sealed record TftpUploadResult(string SourceName, bool Extracted, IReadOnlyList<TftpFileInfo> Files);

public sealed class TftpFileService(IOptions<PhoneServerOptions> options)
{
    public const long MaxUploadBytes = 512L * 1024 * 1024;
    public const long MaxArchiveExtractedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxArchiveEntries = 5000;

    private string RootPath => Path.GetFullPath(Path.Combine(options.Value.DataPath, "tftp"));

    public IReadOnlyList<TftpFileInfo> List()
    {
        var root = EnsureRoot();
        return root.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => !file.Name.StartsWith(".upload-", StringComparison.Ordinal) && IsRegularFile(file))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new TftpFileInfo(file.Name, file.Length, file.LastWriteTimeUtc))
            .ToList();
    }

    public FileStream? OpenRead(string filename)
    {
        if (!TftpServerService.SafeFilename(filename)) return null;
        var file = Resolve(filename);
        return file.Exists && IsRegularFile(file)
            ? file.Open(FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
    }

    public async Task<TftpFileInfo> SaveAsync(string filename, Stream source, long? declaredLength, CancellationToken cancellationToken = default)
    {
        if (!TftpServerService.SafeFilename(filename)) throw new ArgumentException("TFTP filenames must be plain files without path separators.");
        if (declaredLength is > MaxUploadBytes) throw new InvalidOperationException($"Firmware files cannot exceed {MaxUploadBytes / (1024 * 1024)} MiB.");

        var root = EnsureRoot();
        var destination = Resolve(filename);
        if (destination.Exists && !IsRegularFile(destination)) throw new InvalidOperationException("The target filename is not a regular file.");
        var temporary = Path.Combine(root.FullName, $".upload-{Guid.NewGuid():N}");
        long length = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > MaxUploadBytes) throw new InvalidOperationException($"Firmware files cannot exceed {MaxUploadBytes / (1024 * 1024)} MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            File.Move(temporary, destination.FullName, true);
            var saved = new FileInfo(destination.FullName);
            return new TftpFileInfo(saved.Name, saved.Length, saved.LastWriteTimeUtc);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<TftpUploadResult> ImportAsync(string filename, Stream source, long? declaredLength, CancellationToken cancellationToken = default)
    {
        if (!TftpServerService.SafeFilename(filename))
            throw new ArgumentException("TFTP filenames must be plain files without path separators.");
        if (!IsArchive(filename))
            return new TftpUploadResult(filename, false, [await SaveAsync(filename, source, declaredLength, cancellationToken)]);
        if (declaredLength is > MaxUploadBytes)
            throw new InvalidOperationException($"Firmware archives cannot exceed {MaxUploadBytes / (1024 * 1024)} MiB.");

        var root = EnsureRoot();
        var temporaryArchive = Path.Combine(root.FullName, $".upload-{Guid.NewGuid():N}");
        long length = 0;
        try
        {
            await using (var output = new FileStream(temporaryArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > MaxUploadBytes)
                        throw new InvalidOperationException($"Firmware archives cannot exceed {MaxUploadBytes / (1024 * 1024)} MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            return await ExtractArchiveAsync(filename, temporaryArchive, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
        }
    }

    public bool Delete(string filename)
    {
        if (!TftpServerService.SafeFilename(filename)) throw new ArgumentException("TFTP filenames must be plain files without path separators.");
        var file = Resolve(filename);
        if (!file.Exists) return false;
        if (!IsRegularFile(file)) throw new InvalidOperationException("The target filename is not a regular file.");
        file.Delete();
        return true;
    }

    private DirectoryInfo EnsureRoot()
    {
        var root = Directory.CreateDirectory(RootPath);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("The TFTP directory cannot be a symbolic link.");
        return root;
    }

    private FileInfo Resolve(string filename) => new(Path.Combine(EnsureRoot().FullName, filename));

    private static bool IsRegularFile(FileInfo file) => !file.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private async Task<TftpUploadResult> ExtractArchiveAsync(string sourceName, string archivePath, CancellationToken cancellationToken)
    {
        var root = EnsureRoot();
        var staging = Directory.CreateDirectory(Path.Combine(root.FullName, $".extract-{Guid.NewGuid():N}"));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long extractedBytes = 0;
        var entryCount = 0;
        try
        {
            await using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            await using Stream tarStream = IsGzipArchive(sourceName) ? new GZipStream(archive, CompressionMode.Decompress) : archive;
            using var reader = new TarReader(tarStream, leaveOpen: false);
            var entryFiles = new List<(string Name, string Path)>();
            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken: cancellationToken)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entryCount > MaxArchiveEntries)
                    throw new InvalidOperationException($"Firmware archives cannot contain more than {MaxArchiveEntries} entries.");
                if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
                    continue;
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile))
                    throw new InvalidOperationException($"Archive entry '{entry.Name}' is not a regular file.");

                var name = FlatSafeName(entry.Name);
                if (!names.Add(name))
                    throw new InvalidOperationException($"Archive contains duplicate file '{name}'.");
                if (entry.Length is < 0 or > MaxUploadBytes)
                    throw new InvalidOperationException($"Archive file '{name}' exceeds the {MaxUploadBytes / (1024 * 1024)} MiB per-file limit.");
                if (extractedBytes > MaxArchiveExtractedBytes - entry.Length)
                    throw new InvalidOperationException($"Archive contents exceed the {MaxArchiveExtractedBytes / (1024 * 1024)} MiB extracted-size limit.");

                var stagedPath = Path.Combine(staging.FullName, name);
                await using var input = entry.DataStream ?? throw new InvalidOperationException($"Archive entry '{name}' has no file data.");
                await using var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
                var copied = await CopyEntryAsync(input, output, MaxArchiveExtractedBytes - extractedBytes, cancellationToken);
                if (copied != entry.Length)
                    throw new InvalidOperationException($"Archive entry '{name}' ended before its declared size.");
                extractedBytes += copied;
                entryFiles.Add((name, stagedPath));
            }

            if (entryFiles.Count == 0)
                throw new InvalidOperationException("The archive contains no regular files.");

            var saved = new List<TftpFileInfo>(entryFiles.Count);
            foreach (var (name, stagedPath) in entryFiles)
            {
                var destination = Resolve(name);
                if (destination.Exists && !IsRegularFile(destination))
                    throw new InvalidOperationException($"The target filename '{name}' is not a regular file.");
                File.Move(stagedPath, destination.FullName, true);
                var file = new FileInfo(destination.FullName);
                saved.Add(new TftpFileInfo(file.Name, file.Length, file.LastWriteTimeUtc));
            }
            return new TftpUploadResult(sourceName, true, saved);
        }
        finally
        {
            if (staging.Exists) staging.Delete(true);
        }
    }

    private static async Task<long> CopyEntryAsync(Stream source, Stream destination, long remaining, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > remaining)
                throw new InvalidOperationException("Archive contents exceed the extracted-size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }

    private static bool IsArchive(string filename) => filename.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static bool IsGzipArchive(string filename) => filename.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static string FlatSafeName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Archive entry '{entryName}' has an absolute path.");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
            throw new InvalidOperationException($"Archive entry '{entryName}' has an unsafe path.");
        var name = segments[^1];
        if (!TftpServerService.SafeFilename(name))
            throw new InvalidOperationException($"Archive entry '{entryName}' has an unsafe filename.");
        return name;
    }
}
