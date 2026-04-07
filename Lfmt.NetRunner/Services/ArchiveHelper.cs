using System.Diagnostics;
using System.IO.Compression;

namespace Lfmt.NetRunner.Services;

public static class ArchiveHelper
{
    public static async Task ExtractAsync(Stream stream, string fileName, string destDir)
    {
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            await ExtractZipAsync(stream, destDir);
        else if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            await ExtractTarGzAsync(stream, destDir);
        else
            throw new InvalidOperationException($"Unsupported archive format: {fileName}");
    }

    public static async Task ExtractAsync(IFormFile file, string destDir)
    {
        using var stream = file.OpenReadStream();
        await ExtractAsync(stream, file.FileName, destDir);
    }

    private static async Task ExtractZipAsync(Stream stream, string destDir)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var destFullPath = Path.GetFullPath(destDir);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // skip directories

            var entryPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));

            // Path traversal protection
            if (!entryPath.StartsWith(destFullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Path traversal detected in archive: {entry.FullName}");

            var entryDir = Path.GetDirectoryName(entryPath)!;
            Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(entryPath, overwrite: true);
        }
    }

    private static async Task ExtractTarGzAsync(Stream stream, string destDir)
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            await using (var fs = File.Create(tmpFile))
                await stream.CopyToAsync(fs);

            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"xzf \"{tmpFile}\" -C \"{destDir}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start tar");
            using (process)
            {
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"tar extract failed: {error}");
            }
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    public static string? FindNetrunnerFile(string dir)
    {
        var path = Path.Combine(dir, ".netrunner");
        if (File.Exists(path)) return path;

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            path = Path.Combine(subDir, ".netrunner");
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
