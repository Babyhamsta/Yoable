using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace YoableWPF.Managers
{
    public class UpdateManager
    {
        private readonly HttpClient httpClient = new HttpClient();
        private readonly MainWindow mainWindow;
        private readonly OverlayManager overlayManager;
        private CancellationTokenSource updateCancellationToken;
        private const string GithubApiUrl = "https://api.github.com/repos/Babyhamsta/Yoable/releases/latest";
        private readonly string currentVersion;
        private readonly string executablePath;
        private readonly string applicationDirectory;
        private readonly Dispatcher dispatcher;

        public UpdateManager(MainWindow window, OverlayManager overlay, string version)
        {
            mainWindow = window;
            overlayManager = overlay;
            currentVersion = version;
            executablePath = Process.GetCurrentProcess().MainModule.FileName;
            applicationDirectory = Path.GetDirectoryName(executablePath);
            dispatcher = Application.Current.Dispatcher;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "YoableWPF-Updater");

            CheckShowChangelog();
        }

        private void CheckShowChangelog()
        {
            if (Properties.Settings.Default.ShowChangelog)
            {
                dispatcher.InvokeAsync(() =>
                {
                    var changelogWindow = new ChangelogWindow(
                        Properties.Settings.Default.NewVersion,
                        Properties.Settings.Default.ChangelogContent
                    );
                    changelogWindow.ShowDialog();
                });
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var release = await GetLatestReleaseAsync();
                if (release == null || !IsNewVersionAvailable(release.tag_name)) return;

                var result = await dispatcher.InvokeAsync(() => MessageBox.Show(
                    mainWindow,
                    $"A new version ({release.tag_name}) is available. Would you like to update?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question));

                if (result == MessageBoxResult.Yes)
                {
                    Properties.Settings.Default.ShowChangelog = true;
                    Properties.Settings.Default.ChangelogContent = release.body;
                    Properties.Settings.Default.NewVersion = release.tag_name;
                    Properties.Settings.Default.Save();

                    await DownloadAndInstallUpdateAsync(release.assets[0].browser_download_url);
                }
            }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() => MessageBox.Show(
                    mainWindow,
                    $"Update check failed: {ex.Message}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
            }
        }

        private async Task<GithubRelease> GetLatestReleaseAsync()
        {
            var response = await httpClient.GetStringAsync(GithubApiUrl);
            return JsonSerializer.Deserialize<GithubRelease>(response);
        }

        private bool IsNewVersionAvailable(string newVersion)
        {
            newVersion = newVersion.TrimStart('v');
            var current = currentVersion.TrimStart('v');
            Version v1 = Version.Parse(current);
            Version v2 = Version.Parse(newVersion);
            return v2 > v1;
        }

        private async Task DownloadAndInstallUpdateAsync(string downloadUrl)
        {
            updateCancellationToken = new CancellationTokenSource();
            await dispatcher.InvokeAsync(() =>
                overlayManager.ShowOverlayWithProgress("Downloading update...", updateCancellationToken));

            try
            {
                string tempZipPath = Path.Combine(Path.GetTempPath(), "update.zip");
                string tempExtractPath = Path.Combine(Path.GetTempPath(), "update_extract");

                await DownloadFileAsync(downloadUrl, tempZipPath);
                await ExtractUpdateAsync(tempZipPath, tempExtractPath);

                string batchPath = CreateUpdateBatchScript(tempExtractPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                await dispatcher.InvokeAsync(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    overlayManager.HideOverlay();
                    MessageBox.Show(
                        mainWindow,
                        $"Update failed: {ex.Message}",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        private async Task ExtractUpdateAsync(string zipPath, string extractPath)
        {
            await dispatcher.InvokeAsync(() =>
                overlayManager.UpdateMessage("Extracting update..."));

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
        }

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(destinationPath, FileMode.Create))
            {
                var buffer = new byte[8192];
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var totalBytesRead = 0L;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (updateCancellationToken.Token.IsCancellationRequested) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((double)totalBytesRead / totalBytes * 100);
                        await dispatcher.InvokeAsync(() =>
                        {
                            overlayManager.UpdateProgress(progress);
                            overlayManager.UpdateMessage($"Downloading update: {progress}%");
                        });
                    }
                }
            }
        }

        private string CreateUpdateBatchScript(string updatePath)
        {
            string batchPath = Path.Combine(Path.GetTempPath(), "update.bat");
            string script = $@"
                @echo off
                :retry
                timeout /t 1 /nobreak >nul
                tasklist /FI ""IMAGENAME eq {Path.GetFileName(executablePath)}"" 2>NUL | find /I /N ""{Path.GetFileName(executablePath)}"">NUL
                if ""%ERRORLEVEL%""==""0"" goto retry

                xcopy /s /y ""{updatePath}\*.*"" ""{applicationDirectory}""
                start """" ""{executablePath}""
                rmdir /s /q ""{updatePath}""
                del ""%~f0""
                exit
            ";
            File.WriteAllText(batchPath, script);
            return batchPath;
        }

        public class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string tag_name { get; set; }

            [JsonPropertyName("body")]
            public string body { get; set; }

            [JsonPropertyName("assets")]
            public GithubAsset[] assets { get; set; }
        }

        public class GithubAsset
        {
            [JsonPropertyName("browser_download_url")]
            public string browser_download_url { get; set; }
        }
    }
}