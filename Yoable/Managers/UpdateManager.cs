using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Yoable.Services;

namespace Yoable.Managers;

/// <summary>
/// Cross-platform update manager for checking and installing updates from GitHub
/// </summary>
public class UpdateManager
{
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private const string GithubApiUrl = "https://api.github.com/repos/Babyhamsta/Yoable/releases/latest";
    private readonly string _currentVersion;
    private readonly string _executablePath;
    private readonly string _applicationDirectory;

    public UpdateManager(
        IDialogService dialogService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        string version)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        _currentVersion = version;
        _executablePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        _applicationDirectory = Path.GetDirectoryName(_executablePath) ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Yoable-Updater");
    }

    /// <summary>
    /// Check for available updates from GitHub
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var release = await GetLatestReleaseAsync();
            if (release == null)
                return null;

            if (!IsNewVersionAvailable(release.tag_name))
                return null;

            return new UpdateInfo
            {
                Version = release.tag_name,
                ReleaseNotes = release.body ?? string.Empty,
                DownloadUrl = release.assets?.Length > 0 ? release.assets[0].browser_download_url : string.Empty
            };
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Update Check Failed", $"Failed to check for updates: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prompt user and download/install update if confirmed
    /// </summary>
    public async Task<bool> CheckAndPromptForUpdateAsync()
    {
        var updateInfo = await CheckForUpdatesAsync();
        if (updateInfo == null)
            return false;

        var message = $"A new version is available: {updateInfo.Version}\n\n" +
                     $"Release Notes:\n{updateInfo.ReleaseNotes}\n\n" +
                     $"Would you like to download and install this update?";

        var shouldUpdate = await _dialogService.ShowConfirmationAsync("Update Available", message);

        if (shouldUpdate && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            // Save info to show changelog after restart
            _settingsService.SetSetting("ShowChangelog", true);
            _settingsService.SetSetting("NewVersion", updateInfo.Version);
            _settingsService.SetSetting("ChangelogContent", updateInfo.ReleaseNotes);
            _settingsService.Save();

            var progress = new Progress<(int current, int total, string message)>();
            await DownloadAndInstallUpdateAsync(updateInfo.DownloadUrl, progress);
            return true;
        }

        return false;
    }

    private async Task<GithubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(GithubApiUrl);
            return JsonSerializer.Deserialize<GithubRelease>(response);
        }
        catch
        {
            return null;
        }
    }

    private bool IsNewVersionAvailable(string newVersion)
    {
        try
        {
            newVersion = newVersion.TrimStart('v');
            var current = _currentVersion.TrimStart('v');
            Version v1 = Version.Parse(current);
            Version v2 = Version.Parse(newVersion);
            return v2 > v1;
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(
        string downloadUrl,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        try
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "yoable_update.zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), "yoable_update_extract");

            progress?.Report((0, 100, "Downloading update..."));
            await DownloadFileAsync(downloadUrl, tempZipPath, progress);

            progress?.Report((90, 100, "Extracting update..."));
            await ExtractUpdateAsync(tempZipPath, tempExtractPath);

            progress?.Report((95, 100, "Preparing installation..."));
            string scriptPath = CreateUpdateScript(tempExtractPath);

            progress?.Report((100, 100, "Restarting application..."));

            // Start the update script
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });
            }
            else
            {
                // Mac/Linux: make script executable and run
                Process.Start("chmod", $"+x \"{scriptPath}\"");
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true
                });
            }

            // Request application shutdown
            // Note: The actual shutdown needs to be handled by the UI layer
            await _dialogService.ShowMessageAsync("Update Ready", "The application will now restart to complete the update.");

            // Exit the application (this should be handled by the main application)
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Update Failed", $"Update installation failed: {ex.Message}");
        }
    }

    private async Task ExtractUpdateAsync(string zipPath, string extractPath)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
        });
    }

    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (var fileStream = new FileStream(destinationPath, FileMode.Create))
        {
            var buffer = new byte[8192];
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;

                if (totalBytes > 0)
                {
                    var progressPercent = (int)((double)totalBytesRead / totalBytes * 90); // 0-90% for download
                    await _dispatcherService.InvokeAsync(() =>
                    {
                        progress?.Report((progressPercent, 100, $"Downloading update: {progressPercent}%"));
                    });
                }
            }
        }
    }

    private string CreateUpdateScript(string updatePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CreateWindowsUpdateScript(updatePath);
        }
        else
        {
            return CreateUnixUpdateScript(updatePath);
        }
    }

    private string CreateWindowsUpdateScript(string updatePath)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), "yoable_update.bat");
        string script = $@"
@echo off
:retry
timeout /t 2 /nobreak >nul
tasklist /FI ""IMAGENAME eq {Path.GetFileName(_executablePath)}"" 2>NUL | find /I /N ""{Path.GetFileName(_executablePath)}"">NUL
if ""%ERRORLEVEL%""==""0"" goto retry

xcopy /s /y ""{updatePath}\*.*"" ""{_applicationDirectory}""
start """" ""{_executablePath}""
rmdir /s /q ""{updatePath}""
del ""%~f0""
exit
";
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private string CreateUnixUpdateScript(string updatePath)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), "yoable_update.sh");
        string execName = Path.GetFileName(_executablePath);

        string script = $@"#!/bin/bash
# Wait for app to close
sleep 2
while pgrep -x ""{execName}"" > /dev/null; do
    sleep 1
done

# Copy update files
cp -rf ""{updatePath}""/* ""{_applicationDirectory}""

# Restart app
""{_executablePath}"" &

# Cleanup
rm -rf ""{updatePath}""
rm -- ""$0""
";
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }
}

/// <summary>
/// Information about an available update
/// </summary>
public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>
/// GitHub release data structure
/// </summary>
public class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string tag_name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string? body { get; set; }

    [JsonPropertyName("assets")]
    public GithubAsset[]? assets { get; set; }
}

/// <summary>
/// GitHub asset data structure
/// </summary>
public class GithubAsset
{
    [JsonPropertyName("browser_download_url")]
    public string browser_download_url { get; set; } = string.Empty;
}
