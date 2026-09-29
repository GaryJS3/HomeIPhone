using Microsoft.Extensions.Options;

namespace HomeIPhone;

public sealed record TftpFileInfo(string Name, long Size, DateTime LastModifiedUtc);

public sealed class TftpFileService(IOptions<PhoneServerOptions> options)
{
    public const long MaxUploadBytes = 512L * 1024 * 1024;

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
            return new TftpFileInfo(destination.Name, length, destination.LastWriteTimeUtc);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
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
}
