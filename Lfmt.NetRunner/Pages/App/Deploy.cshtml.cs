using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class DeployModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly DeployService _deployService;

    public string Name { get; set; } = "";

    public DeployModel(AppManager appManager, DeployService deployService)
    {
        _appManager = appManager;
        _deployService = deployService;
    }

    public IActionResult OnGet(string name)
    {
        Name = name;
        if (_appManager.GetAppConfig(name) == null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string name, IFormFile archive)
    {
        Name = name;
        if (_appManager.GetAppConfig(name) == null) return NotFound();

        using var stream = archive.OpenReadStream();
        await _deployService.DeployFromArchive(name, stream, archive.FileName);

        return RedirectToPage("/App/Detail", new { name });
    }
}
