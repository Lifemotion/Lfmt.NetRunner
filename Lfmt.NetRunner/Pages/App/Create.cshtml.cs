using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

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

        var tempDir = Path.Combine(Path.GetTempPath(), "netrunner-create-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            await ArchiveHelper.ExtractAsync(archive, tempDir);

            var netrunnerPath = ArchiveHelper.FindNetrunnerFile(tempDir);
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

            await _appManager.CreateApp(config);

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
}
