using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;
using System.IO.Compression;

namespace Lfmt.NetRunner.Pages.App;

public class CreateModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly DeployService _deployService;

    public string? Error { get; set; }

    public CreateModel(AppManager appManager, DeployService deployService)
    {
        _appManager = appManager;
        _deployService = deployService;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(IFormFile archive)
    {
        if (archive == null || archive.Length == 0)
        {
            Error = "No file uploaded";
            return Page();
        }

        // Extract to temp dir to read .netrunner
        var tempDir = Path.Combine(Path.GetTempPath(), "netrunner-create-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            await ExtractArchive(archive, tempDir);

            // Find and parse .netrunner
            var netrunnerPath = FindNetrunnerFile(tempDir);
            if (netrunnerPath == null)
            {
                Error = "Archive does not contain a .netrunner file";
                return Page();
            }

            var ini = IniParser.Parse(await System.IO.File.ReadAllTextAsync(netrunnerPath));
            var config = AppConfig.FromIni(ini);

            if (string.IsNullOrEmpty(config.Name))
            {
                Error = ".netrunner file is missing the app name";
                return Page();
            }

            // Create the app
            await _appManager.CreateApp(config);

            // Deploy from the same archive
            using var stream = archive.OpenReadStream();
            await _deployService.DeployFromArchive(config.Name, stream, archive.FileName);

            return RedirectToPage("/App/Detail", new { name = config.Name });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static async Task ExtractArchive(IFormFile file, string destDir)
    {
        if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destDir);
        }
        else
        {
            // Save to temp file for tar
            var tmpFile = Path.GetTempFileName();
            try
            {
                await using (var fs = System.IO.File.Create(tmpFile))
                    await file.CopyToAsync(fs);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"xzf \"{tmpFile}\" -C \"{destDir}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = System.Diagnostics.Process.Start(psi)!;
                await process.WaitForExitAsync();
            }
            finally
            {
                System.IO.File.Delete(tmpFile);
            }
        }
    }

    private static string? FindNetrunnerFile(string dir)
    {
        var path = Path.Combine(dir, ".netrunner");
        if (System.IO.File.Exists(path)) return path;

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            path = Path.Combine(subDir, ".netrunner");
            if (System.IO.File.Exists(path)) return path;
        }

        return null;
    }
}
