using Spectre.Console;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GitNinja.Services
{
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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitNinja-UpdateCheck");
            _httpClient.Timeout = TimeSpan.FromSeconds(5); // Don't hang if no internet
        }

        public async Task CheckForUpdateAsync(bool silent = false)
        {
            try
            {
                var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    if (!silent) AnsiConsole.MarkupLine("[grey]Could not check for updates (API error).[/]");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                if (release?.TagName == null)
                {
                    if (!silent) AnsiConsole.MarkupLine("[grey]Could not check for updates (invalid response).[/]");
                    return;
                }

                var latestVersion = ParseVersion(release.TagName);
                var currentVersion = ParseVersion(_currentVersion);

                if (latestVersion > currentVersion)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[yellow]Update Available[/]").RuleStyle("yellow"));
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [yellow]⚠  A new version of GitNinja is available![/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [white]Current version:[/]  [red]{_currentVersion}[/]");
                    AnsiConsole.MarkupLine($"  [white]Latest version:[/]   [green]{release.TagName}[/]");
                    AnsiConsole.MarkupLine($"  [white]Release:[/]          [blue]{release.Name}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [grey]Download: {release.HtmlUrl}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("  [cyan]Run [white]gitninja update[/] to open the download page.[/]");
                    AnsiConsole.WriteLine();
                }
                else if (!silent)
                {
                    AnsiConsole.MarkupLine($"[green]✓ You're running the latest version ({_currentVersion})[/]");
                }
            }
            catch (TaskCanceledException)
            {
                if (!silent) AnsiConsole.MarkupLine("[grey]Update check timed out. Check your internet connection.[/]");
            }
            catch (Exception ex)
            {
                if (!silent) AnsiConsole.MarkupLine($"[grey]Could not check for updates: {ex.Message}[/]");
            }
        }

        public void OpenDownloadPage()
        {
            var url = $"https://github.com/{_repoOwner}/{_repoName}/releases/latest";
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", url);
                }
                else
                {
                    System.Diagnostics.Process.Start("xdg-open", url);
                }
                AnsiConsole.MarkupLine($"[green]✔ Opened {url}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✖ Could not open browser: {ex.Message}[/]");
                AnsiConsole.MarkupLine($"[grey]  Please visit manually: {url}[/]");
            }
        }

        private Version ParseVersion(string versionString)
        {
            versionString = versionString.TrimStart('v', 'V');
            // Handle versions like "1.0.0-beta" by taking only the numeric part
            var dashIndex = versionString.IndexOf('-');
            if (dashIndex > 0) versionString = versionString.Substring(0, dashIndex);

            Version.TryParse(versionString, out var version);
            return version ?? new Version(0, 0, 0);
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
    }
}