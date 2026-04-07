using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class SettingsModel : PageModel
{
    private readonly SettingsService _settingsService;

    [BindProperty]
    public UiSettings Input { get; set; } = new();

    public bool Saved { get; set; }

    public SettingsModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void OnGet()
    {
        Input = _settingsService.Current;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _settingsService.SaveAsync(Input);
        Saved = true;
        return Page();
    }
}
