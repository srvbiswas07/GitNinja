using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class UpdateCommand
    {
        private readonly bool _preview;
        private readonly UpdateService _updateService;

        public UpdateCommand(bool preview = false)
        {
            _preview = preview;
            var version = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";

            _updateService = new UpdateService(version, "srvbiswas07", "gitninja");
        }

        public void Execute()
        {
            if (_preview)
            {
                AnsiConsole.MarkupLine("[grey]  Preview mode:[/]");
                AnsiConsole.MarkupLine("[grey]    1. Query GitHub API for latest release[/]");
                AnsiConsole.MarkupLine("[grey]    2. Compare version numbers[/]");
                AnsiConsole.MarkupLine("[grey]    3. Download installer with progress bar[/]");
                AnsiConsole.MarkupLine("[grey]    4. Ask user confirmation before installing[/]");
                AnsiConsole.MarkupLine("[grey]    5. Launch Inno Setup installer silently[/]");
                return;
            }

            // Check for update
            var result = _updateService.CheckAndDisplayAsync().GetAwaiter().GetResult();

            if (result == null) return; // Up to date or no internet

            // Show update info
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Update Available[/]").RuleStyle("yellow"));
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[cyan]Info[/]").Width(18))
                .AddColumn(new TableColumn("[cyan]Details[/]"));

            table.AddRow("[white]Current version[/]", $"[red]v{result.CurrentVersion}[/]");
            table.AddRow("[white]Latest version[/]", $"[green]v{result.LatestVersion}[/]");
            table.AddRow("[white]Release name[/]", $"[white]{Markup.Escape(result.ReleaseTitle)}[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // No installer asset found — send to browser
            if (string.IsNullOrEmpty(result.InstallerDownloadUrl))
            {
                AnsiConsole.MarkupLine("  [yellow]No installer found in this release.[/]");
                AnsiConsole.MarkupLine($"  [blue]Download manually: {result.ReleasePageUrl}[/]");
                return;
            }

            // Ask user what to do
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  What would you like to do?[/]")
                    .AddChoices(new[]
                    {
                        "Download and install now",
                        "Open release page in browser",
                        "Remind me later"
                    })
            );

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Download and install now":
                    RunDownloadAndInstall(result);
                    break;

                case "Open release page in browser":
                    _updateService.OpenDownloadPage();
                    AnsiConsole.MarkupLine("  [green]Opened release page in your browser.[/]");
                    break;

                case "Remind me later":
                    AnsiConsole.MarkupLine("  [grey]No problem — you can update anytime from the menu.[/]");
                    break;
            }
        }

        private void RunDownloadAndInstall(Services.UpdateCheckResult result)
        {
            AnsiConsole.WriteLine();
            OutputService.Info("Starting download...");
            AnsiConsole.WriteLine();

            // Download with progress bar
            var installerPath = _updateService
                .DownloadInstallerAsync(result.InstallerDownloadUrl, result.LatestVersion)
                .GetAwaiter().GetResult();

            if (installerPath == null)
            {
                OutputService.Error("Download failed. Try again or download manually.");
                AnsiConsole.MarkupLine($"  [blue]{result.ReleasePageUrl}[/]");
                return;
            }

            AnsiConsole.WriteLine();

            // Confirm before launching installer
            var confirmed = AnsiConsole.Confirm(
                "  [cyan]Ready to install. GitNinja will close and the installer will open. Continue?[/]",
                defaultValue: true);

            if (!confirmed)
            {
                AnsiConsole.WriteLine();
                OutputService.Info($"Installer saved at: {installerPath}");
                OutputService.Info("Run it manually whenever you are ready.");
                return;
            }

            AnsiConsole.WriteLine();
            _updateService.LaunchInstaller(installerPath);

            // Small delay so user can read the message then exit
            Task.Delay(2000).Wait();
            Environment.Exit(0);
        }
    }
}