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
            // Get version from assembly (see note below)
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";

            _updateService = new UpdateService(
                currentVersion: version,
                repoOwner: "srvbiswas07",
                repoName: "gitninja"
            );
        }

        public void Execute()
        {
            if (_preview)
            {
                AnsiConsole.MarkupLine("[grey]Preview mode - would check for updates[/]");
                AnsiConsole.MarkupLine("[grey]Commands that would run:[/]");
                AnsiConsole.MarkupLine("  1. Query https://api.github.com/repos/srvbiswas07/gitninja/releases/latest");
                AnsiConsole.MarkupLine("  2. Compare current version with latest release");
                AnsiConsole.MarkupLine("  3. Display update notification if newer version exists");
                return;
            }

            // Check for updates (async method, run synchronously for CLI)
            _updateService.CheckForUpdateAsync(silent: false).GetAwaiter().GetResult();
        }
    }
}