using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.Json;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitNinja.Services
{
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string ReleaseTitle { get; set; } = "";
        public string InstallerDownloadUrl { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
    }

    public class UpdateService
    {
        private readonly string _currentVersion;
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly HttpClient _httpClient;

        public UpdateService(string currentVersion, string repoOwner, string repoName)
        {
            _currentVersion = currentVersion;
            _repoOwner = repoOwner;
            _repoName = repoName;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("GitNinja", _currentVersion));
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }

        // ── Silent check on startup ───────────────────────────────────────────
        public async Task<UpdateCheckResult?> CheckSilentAsync()
        {
            try
            {
                return await FetchLatestReleaseAsync();
            }
            catch
            {
                return null;
            }
        }

        // ── Full check with spinner output ────────────────────────────────────
        public async Task<UpdateCheckResult?> CheckAndDisplayAsync()
        {
            UpdateCheckResult? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync("Checking for updates...", async ctx =>
                {
                    result = await FetchLatestReleaseAsync();
                });

            if (result == null)
            {
                OutputService.Error("Could not reach GitHub. Check your internet connection.");
                return null;
            }

            if (!result.UpdateAvailable)
            {
                OutputService.Success($"GitNinja is up to date (v{result.CurrentVersion})");
                return null;
            }

            return result;
        }

        // ── Download installer with progress bar ──────────────────────────────
        public async Task<string?> DownloadInstallerAsync(string downloadUrl, string version)
        {
            var fileName = $"GitNinja-Setup-{version}.exe";
            var downloadDir = Path.Combine(Path.GetTempPath(), "GitNinja");
            var outputPath = Path.Combine(downloadDir, fileName);

            Directory.CreateDirectory(downloadDir);

            try
            {
                using var response = await _httpClient.GetAsync(
                    downloadUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    OutputService.Error($"Download failed: {response.StatusCode}");
                    return null;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var buffer = new byte[8192];
                long downloaded = 0;

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(
                    outputPath, FileMode.Create, FileAccess.Write);

                await AnsiConsole.Progress()
                    .Columns(new ProgressColumn[]
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new DownloadedColumn(),
                        new TransferSpeedColumn(),
                        new RemainingTimeColumn()
                    })
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask(
                            $"[cyan]Downloading GitNinja v{version}[/]",
                            maxValue: totalBytes > 0 ? totalBytes : 100);

                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                            downloaded += bytesRead;
                            task.Increment(bytesRead);

                            if (totalBytes <= 0)
                                task.MaxValue = downloaded + 1024;
                        }

                        task.Value = task.MaxValue;
                    });

                OutputService.Success($"Downloaded to: {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                OutputService.Error($"Download error: {ex.Message}");
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                return null;
            }
        }

        // ── Open release page in browser ──────────────────────────────────────
        public void OpenDownloadPage()
        {
            var url = $"https://github.com/{_repoOwner}/{_repoName}/releases/latest";
            OpenUrl(url);
        }

        // ── Launch Inno Setup installer ───────────────────────────────────────
        public void LaunchInstaller(string installerPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                };

                Process.Start(startInfo);

                OutputService.Success("Installer launched! GitNinja will close now.");
                OutputService.Info("After installation completes, reopen your terminal.");
            }
            catch (Exception ex)
            {
                OutputService.Error($"Could not launch installer: {ex.Message}");
                OutputService.Info($"Run it manually: {installerPath}");
            }
        }

        // ── Fetch release info from GitHub API ────────────────────────────────
        private async Task<UpdateCheckResult?> FetchLatestReleaseAsync()
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    LatestVersion = _currentVersion
                };

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (release?.TagName == null) return null;

            var latest = ParseVersion(release.TagName);
            var current = ParseVersion(_currentVersion);

            var installerAsset = release.Assets?
                .FirstOrDefault(a =>
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.Contains("Setup") ||
                     a.Name.Contains("Installer") ||
                     a.Name.Contains("setup")));

            return new UpdateCheckResult
            {
                UpdateAvailable = latest > current,
                CurrentVersion = _currentVersion,
                LatestVersion = release.TagName.TrimStart('v', 'V'),
                ReleaseTitle = release.Name,
                InstallerDownloadUrl = installerAsset?.BrowserDownloadUrl ?? "",
                ReleasePageUrl = release.HtmlUrl
            };
        }

        // ── Cross-platform URL opener (private helper) ────────────────────────
        private static void OpenUrl(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
                else
                    Process.Start("xdg-open", url);
            }
            catch
            {
                AnsiConsole.MarkupLine($"  [grey]Could not open browser. Visit manually: {url}[/]");
            }
        }

        private static Version ParseVersion(string v)
        {
            v = v.TrimStart('v', 'V');
            var dash = v.IndexOf('-');
            if (dash > 0) v = v[..dash];
            Version.TryParse(v, out var version);
            return version ?? new Version(0, 0, 0);
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}