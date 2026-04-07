using System.Diagnostics;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class SystemdService
{
    private readonly NetRunnerConfig _config;
    private readonly ILogger<SystemdService> _logger;

    public SystemdService(NetRunnerConfig config, ILogger<SystemdService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<string> Start(string appName) => RunSudo("start", appName);
    public Task<string> Stop(string appName) => RunSudo("stop", appName);
    public Task<string> Restart(string appName) => RunSudo("restart", appName);
    public Task<string> Enable(string appName) => RunSudo("enable", appName);
    public Task<string> Disable(string appName) => RunSudo("disable", appName);
    public Task<string> DaemonReload() => RunSudo("daemon-reload");
    public Task<string> InstallServiceFile(string appName) => RunSudo("install-service", appName);
    public Task<string> CreateUser(string appName) => RunSudo("create-user", appName);
    public Task<string> DeleteUser(string appName) => RunSudo("delete-user", appName);
    public Task<string> ChownApp(string appName) => RunSudo("chown-app", appName);

    public async Task<string> GetStatus(string appName)
    {
        return await RunSudo("status", appName);
    }

    public async Task<AppStatus> GetAppStatus(string appName)
    {
        var output = await GetStatus(appName);
        if (output.Contains("Active: active (running)"))
            return AppStatus.Running;
        if (output.Contains("Active: failed"))
            return AppStatus.Failed;
        if (output.Contains("Active: inactive") || output.Contains("could not be found"))
            return AppStatus.Stopped;
        return AppStatus.Unknown;
    }

    public async Task<string> GetJournalLogs(string appName, int lines = 100)
    {
        return await RunSudo("logs", appName, lines.ToString());
    }

    public async Task WriteEnv(string appName, string content)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"{_config.SudoScript} write-env {appName}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        await process.StandardInput.WriteAsync(content);
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            _logger.LogError("write-env failed for {App}: {Error}", appName, error);
            throw new InvalidOperationException($"write-env failed: {error}");
        }
    }

    private async Task<string> RunSudo(params string[] args)
    {
        var arguments = $"{_config.SudoScript} {string.Join(" ", args)}";
        _logger.LogInformation("sudo {Args}", arguments);

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("sudo {Args} failed (exit {Code}): {Error}", arguments, process.ExitCode, error);
            throw new InvalidOperationException($"sudo {args[0]} failed: {error}");
        }

        return output + error;
    }
}
