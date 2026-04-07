using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class EnvModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly SystemdService _systemd;
    private readonly NetRunnerConfig _config;

    public string Name { get; set; } = "";
    public new string Content { get; set; } = "";
    public bool Saved { get; set; }

    public EnvModel(AppManager appManager, SystemdService systemd, NetRunnerConfig config)
    {
        _appManager = appManager;
        _systemd = systemd;
        _config = config;
    }

    public async Task<IActionResult> OnGetAsync(string name)
    {
        Name = name;
        if (_appManager.GetAppConfig(name) == null) return NotFound();

        Content = await _systemd.ReadEnv(name);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string name, string content)
    {
        Name = name;
        if (_appManager.GetAppConfig(name) == null) return NotFound();

        await _systemd.WriteEnv(name, content ?? "");
        Content = content ?? "";
        Saved = true;
        return Page();
    }
}
