using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Spectre.Console;

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
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            // Proper User-Agent format required by GitHub API
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("GitNinja", _currentVersion)
            );

            // Accept JSON response
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json")
            );
        }

        public async Task CheckForUpdateAsync(bool silent = false)
        {
            try
            {
                var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";

                var response = await _httpClient.GetAsync(url);

                // Handle specific error codes
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var rateLimitRemaining = response.Headers.Contains("X-RateLimit-Remaining")
                        ? response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault()
                        : "0";

                    if (rateLimitRemaining == "0")
                    {
                        if (!silent)
                        {
                            AnsiConsole.MarkupLine("[grey]Update check skipped: GitHub API rate limit exceeded (60 requests/hour).[/]");
                            AnsiConsole.MarkupLine("[grey]Try again later or check manually at:[/]");
                            AnsiConsole.MarkupLine($"[blue]https://github.com/{_repoOwner}/{_repoName}/releases[/]");
                        }
                        return;
                    }

                    if (!silent) AnsiConsole.MarkupLine("[grey]Update check blocked by GitHub.[/]");
                    return;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (!silent) AnsiConsole.MarkupLine("[grey]No releases found yet.[/]");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (!silent) AnsiConsole.MarkupLine($"[grey]Update check failed: {response.StatusCode}[/]");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (release?.TagName == null)
                {
                    if (!silent) AnsiConsole.MarkupLine("[grey]No release information available.[/]");
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
                    AnsiConsole.MarkupLine($"  [blue underline]https://github.com/{_repoOwner}/{_repoName}/releases/latest[/]");
                    AnsiConsole.WriteLine();
                }
                else if (!silent)
                {
                    AnsiConsole.MarkupLine($"[green]✓ GitNinja is up to date ({_currentVersion})[/]");
                }
            }
            catch (TaskCanceledException)
            {
                if (!silent) AnsiConsole.MarkupLine("[grey]Update check timed out.[/]");
            }
            catch (HttpRequestException ex)
            {
                if (!silent) AnsiConsole.MarkupLine($"[grey]Cannot check for updates: {ex.Message}[/]");
            }
            catch (Exception ex)
            {
                if (!silent) AnsiConsole.MarkupLine($"[grey]Update check error: {ex.Message}[/]");
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
            }
            catch
            {
                AnsiConsole.MarkupLine($"[grey]Visit: {url}[/]");
            }
        }

        private Version ParseVersion(string versionString)
        {
            versionString = versionString.TrimStart('v', 'V');
            var dashIndex = versionString.IndexOf('-');
            if (dashIndex > 0) versionString = versionString.Substring(0, dashIndex);

            Version.TryParse(versionString, out var version);
            return version ?? new Version(0, 0, 0);
        }
    }

    // GitHub API response model - ONLY ONE DEFINITION
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