using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class DeployService
{
    private readonly AppManager _appManager;
    private readonly SystemdService _systemd;
    private readonly HealthCheckService _healthCheck;
    private readonly ServiceFileGenerator _serviceFileGen;
    private readonly NetRunnerConfig _config;
    private readonly SettingsService _settings;
    private readonly ILogger<DeployService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DeployService(
        AppManager appManager,
        SystemdService systemd,
        HealthCheckService healthCheck,
        ServiceFileGenerator serviceFileGen,
        NetRunnerConfig config,
        SettingsService settings,
        ILogger<DeployService> logger)
    {
        _appManager = appManager;
        _systemd = systemd;
        _healthCheck = healthCheck;
        _serviceFileGen = serviceFileGen;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> DeployFromArchive(string appName, Stream archiveStream, string fileName)
    {
        var semaphore = _locks.GetOrAdd(appName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            var appConfig = _appManager.GetAppConfig(appName)
                ?? throw new InvalidOperationException($"App '{appName}' not found");

            var appDir = _appManager.GetAppDir(appName);
            var sourceDir = Path.Combine(appDir, "source");

            // Clean up previous source and v_new (may be owned by app user)
            await CleanDeployFiles(appName);
            Directory.CreateDirectory(sourceDir);

            // Extract archive
            _logger.LogInformation("Extracting archive for {App}", appName);
            await ArchiveHelper.ExtractAsync(archiveStream, fileName, sourceDir);

            // Check for .netrunner file and update config if present
            var netrunnerFile = ArchiveHelper.FindNetrunnerFile(sourceDir);
            if (netrunnerFile != null)
            {
                var ini = IniParser.Parse(await File.ReadAllTextAsync(netrunnerFile));
                var newConfig = AppConfig.FromIni(ini);

                if (!string.IsNullOrEmpty(newConfig.Name) && newConfig.Name != appName)
                    throw new InvalidOperationException(
                        $"App name mismatch: archive has '{newConfig.Name}', expected '{appName}'");

                newConfig.Name = appName;
                await _appManager.UpdateApp(appName, newConfig);
                appConfig = newConfig;
            }

            return await BuildAndDeploy(appName, appConfig, sourceDir, null);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> DeployFromGit(string appName, string cloneUrl, string branch, string? commitId = null)
    {
        var semaphore = _locks.GetOrAdd(appName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            var appConfig = _appManager.GetAppConfig(appName)
                ?? throw new InvalidOperationException($"App '{appName}' not found");

            var appDir = _appManager.GetAppDir(appName);
            var sourceDir = Path.Combine(appDir, "source");

            // Clean up previous source and v_new (may be owned by app user)
            await CleanDeployFiles(appName);

            // Validate branch name to prevent command injection
            if (!Regex.IsMatch(branch, @"^[a-zA-Z0-9/_.\-]+$"))
                throw new ArgumentException($"Invalid branch name: {branch}");

            // Clone
            _logger.LogInformation("Cloning {Url} branch {Branch} for {App}", cloneUrl, branch, appName);
            var cloneResult = await RunProcess("git",
                $"clone --depth 1 --branch {branch} {cloneUrl} {sourceDir}",
                workingDir: appDir,
                timeoutSeconds: 300);

            if (!cloneResult.Success)
                throw new InvalidOperationException($"git clone failed: {cloneResult.Error}");

            // Read .netrunner from cloned repo
            var netrunnerFile = ArchiveHelper.FindNetrunnerFile(sourceDir);
            if (netrunnerFile != null)
            {
                var ini = IniParser.Parse(await File.ReadAllTextAsync(netrunnerFile));
                var newConfig = AppConfig.FromIni(ini);

                if (!string.IsNullOrEmpty(newConfig.Name) && newConfig.Name != appName)
                    throw new InvalidOperationException(
                        $"App name mismatch: repo has '{newConfig.Name}', expected '{appName}'");

                newConfig.Name = appName;
                await _appManager.UpdateApp(appName, newConfig);
                appConfig = newConfig;
            }

            return await BuildAndDeploy(appName, appConfig, sourceDir, commitId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> Rollback(string appName)
    {
        var appDir = _appManager.GetAppDir(appName);
        var v1Dir = Path.Combine(appDir, "releases", "v1");

        // Check that current is v2 and v1 exists
        var currentLink = Path.Combine(appDir, "current");
        var currentTarget = "";
        try
        {
            var target = File.ResolveLinkTarget(currentLink, returnFinalTarget: false);
            currentTarget = target != null ? Path.GetFileName(target.FullName) : "";
        }
        catch { }

        if (currentTarget != "v2" || !Directory.Exists(v1Dir))
            throw new InvalidOperationException("No previous version available for rollback");

        _logger.LogInformation("Rolling back {App} to v1", appName);

        // Atomic symlink swap to v1
        AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v1");

        await _systemd.DaemonReload();
        await _systemd.Restart(appName);

        var appConfig = _appManager.GetAppConfig(appName)!;
        var healthy = await _healthCheck.CheckHealth(
            appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
            appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

        await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Action = "ROLLBACK",
            Result = healthy ? "SUCCESS" : "FAILURE",
            Message = healthy ? "Rolled back to v1" : "Rollback failed, service stopped",
        });

        if (!healthy)
        {
            await _systemd.Stop(appName);
            return false;
        }

        return true;
    }

    private async Task<bool> BuildAndDeploy(string appName, AppConfig appConfig, string sourceDir, string? commitId)
    {
        var appDir = _appManager.GetAppDir(appName);
        var releasesDir = Path.Combine(appDir, "releases");
        var vNewDir = Path.Combine(releasesDir, "v_new");

        // Ensure v_new is clean (source/ already cleaned by caller)
        if (Directory.Exists(vNewDir))
        {
            try { Directory.Delete(vNewDir, true); }
            catch { await CleanDeployFiles(appName); }
        }

        try
        {
            if (appConfig.IsSourceMode)
            {
                // Source mode: build with dotnet publish
                var projectPath = Path.Combine(sourceDir, appConfig.Project);
                if (!File.Exists(projectPath))
                    throw new InvalidOperationException($"Project file not found: {appConfig.Project}");

                // Clean obj dirs to avoid cross-platform NuGet path issues
                foreach (var objDir in Directory.GetDirectories(sourceDir, "obj", SearchOption.AllDirectories))
                    Directory.Delete(objDir, true);

                _logger.LogInformation("Building {App}: dotnet publish", appName);
                var buildResult = await RunProcess(_config.DotnetPath,
                    $"publish \"{projectPath}\" -c Release -o \"{vNewDir}\"",
                    workingDir: sourceDir,
                    timeoutSeconds: _settings.Current.BuildTimeoutSeconds);

                if (!buildResult.Success)
                {
                    var buildError = !string.IsNullOrWhiteSpace(buildResult.Error)
                        ? buildResult.Error
                        : buildResult.Output;
                    _logger.LogError("Build failed for {App}: {Error}", appName, buildError);
                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "DEPLOY",
                        Result = "FAILURE",
                        Message = $"Build failed: {buildError}",
                        Commit = commitId,
                    });
                    CleanupDir(vNewDir);
                    CleanupDir(sourceDir);
                    return false;
                }
            }
            else
            {
                // Artifact mode: copy published files directly
                _logger.LogInformation("Deploying pre-built artifacts for {App}", appName);
                CopyDirectory(sourceDir, vNewDir);
            }

            // Verify DLL exists
            var dllPath = Path.Combine(vNewDir, appConfig.GetDllName());
            if (!File.Exists(dllPath))
            {
                await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = "DEPLOY",
                    Result = "FAILURE",
                    Message = $"Expected DLL not found: {appConfig.GetDllName()}",
                    Commit = commitId,
                });
                CleanupDir(vNewDir);
                CleanupDir(sourceDir);
                return false;
            }

            // Generate and install service file
            string? customContent = null;
            if (!string.IsNullOrEmpty(appConfig.CustomServiceFile))
            {
                var customPath = Path.GetFullPath(Path.Combine(sourceDir, appConfig.CustomServiceFile));
                if (!customPath.StartsWith(Path.GetFullPath(sourceDir), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Path traversal in custom_file: {appConfig.CustomServiceFile}");
                if (File.Exists(customPath))
                    customContent = await File.ReadAllTextAsync(customPath);
            }

            var serviceContent = _serviceFileGen.Generate(appConfig, customContent);
            await File.WriteAllTextAsync(Path.Combine(appDir, "app.service"), serviceContent);
            await _systemd.InstallServiceFile(appName);

            // Set ownership
            await _systemd.ChownApp(appName);

            // Rotate versions BEFORE starting so the app launches from its final path.
            // ASP.NET Core resolves symlinks at startup (getcwd/realpath), so if the app
            // starts from v_new and v_new is later renamed, the resolved path becomes
            // dead — breaking static file serving and lazy-loaded DLL resolution.
            var v1Dir = Path.Combine(releasesDir, "v1");
            var v2Dir = Path.Combine(releasesDir, "v2");

            if (Directory.Exists(v2Dir))
            {
                CleanupDir(v1Dir);
                Directory.Move(v2Dir, v1Dir);
            }
            Directory.Move(vNewDir, v2Dir);

            AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v2");

            await _systemd.DaemonReload();
            await _systemd.Enable(appName);
            await _systemd.Restart(appName);

            // Health check
            var healthy = await _healthCheck.CheckHealth(
                appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
                appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

            if (healthy)
            {
                await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = "DEPLOY",
                    Result = "SUCCESS",
                    Message = "Built and deployed successfully",
                    Commit = commitId,
                });

                _logger.LogInformation("Deploy of {App} succeeded", appName);
            }
            else
            {
                // Auto-rollback
                _logger.LogWarning("Health check failed for {App}, rolling back", appName);

                if (Directory.Exists(v1Dir))
                {
                    AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v1");
                    await _systemd.Restart(appName);

                    var rollbackHealthy = await _healthCheck.CheckHealth(
                        appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
                        appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

                    // Remove failed version so it doesn't become a rollback target
                    if (rollbackHealthy)
                        CleanupDir(v2Dir);

                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "ROLLBACK",
                        Result = rollbackHealthy ? "SUCCESS" : "FAILURE",
                        Message = rollbackHealthy
                            ? "Auto-rolled back to v1"
                            : "Rollback also failed, service stopped",
                        Commit = commitId,
                    });

                    if (!rollbackHealthy)
                        await _systemd.Stop(appName);
                }
                else
                {
                    await _systemd.Stop(appName);
                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "DEPLOY",
                        Result = "FAILURE",
                        Message = "First deploy failed, no rollback available, service stopped",
                        Commit = commitId,
                    });
                }
            }

            CleanupDir(sourceDir);
            return healthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deploy failed for {App}", appName);
            CleanupDir(vNewDir);
            CleanupDir(sourceDir);

            await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = "DEPLOY",
                Result = "FAILURE",
                Message = $"Deploy error: {ex.Message}",
                Commit = commitId,
            });

            throw;
        }
    }

    private static void AtomicSymlinkSwap(string linkPath, string target)
    {
        var dir = Path.GetDirectoryName(linkPath)!;
        var name = Path.GetFileName(linkPath);

        var psi = new ProcessStartInfo
        {
            FileName = "ln",
            Arguments = $"-sfn {target} {name}",
            WorkingDirectory = dir,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ln");
        using (process)
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"Symlink swap failed: {error}");
            }
        }
    }

    private async Task<ProcessResult> RunProcess(string fileName, string arguments,
        string? workingDir = null, int timeoutSeconds = 120)
    {
        _logger.LogInformation("Running: {File} {Args}", fileName, arguments);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? "",
        };

        // Ensure NuGet uses Linux paths, not inherited Windows paths
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? "/tmp";
            psi.Environment["NUGET_PACKAGES"] = Path.Combine(home, ".nuget", "packages");
            psi.Environment["NUGET_FALLBACK_PACKAGES"] = "";
        }

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        using (process)
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return new ProcessResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = error,
                ExitCode = process.ExitCode,
            };
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new ProcessResult
            {
                Success = false,
                Error = $"Process timed out after {timeoutSeconds}s",
                ExitCode = -1,
            };
        }
    }

    private async Task CleanDeployFiles(string appName)
    {
        var appDir = _appManager.GetAppDir(appName);
        var sourceDir = Path.Combine(appDir, "source");
        var vNewDir = Path.Combine(appDir, "releases", "v_new");

        if (!Directory.Exists(sourceDir) && !Directory.Exists(vNewDir))
            return;

        try
        {
            CleanupDir(sourceDir);
            CleanupDir(vNewDir);
        }
        catch
        {
            // Direct deletion failed (files owned by app user), use sudo
            await _systemd.CleanDeploy(appName);
        }
    }

    private static void CleanupDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private record ProcessResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = "";
        public string Error { get; init; } = "";
        public int ExitCode { get; init; }
    }
}
