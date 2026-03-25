using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class CleanupCommand
    {
        private readonly GitRunner _runner;
        private readonly ContextAnalyzer _analyzer;
        private readonly SafetyService _safety;
        private readonly bool _preview;

        private static readonly string[] ProtectedBranches =
            { "main", "master", "develop", "dev" };

        public CleanupCommand(GitRunner runner, ContextAnalyzer analyzer,
                              SafetyService safety, bool preview = false)
        {
            _runner = runner;
            _analyzer = analyzer;
            _safety = safety;
            _preview = preview;
        }

        public void Execute()
        {
            OutputService.BlankLine();
            OutputService.Info("Fetching remote info...");
            _runner.Run("fetch --prune");

            var merged = _runner.Run("branch --merged");
            var context = _analyzer.Analyze();

            if (!merged.Success || string.IsNullOrWhiteSpace(merged.Output))
            {
                OutputService.Success("No merged branches to clean up.");
                return;
            }

            var mergedBranches = merged.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim().TrimStart('*').Trim())
                .Where(b => !ProtectedBranches.Contains(b.ToLower()))
                .Where(b => b != context.CurrentBranch)
                .ToList();

            if (mergedBranches.Count == 0)
            {
                OutputService.Success("No merged branches to clean up.");
                return;
            }

            // Show branches in a table
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[cyan]Branch[/]"))
                .AddColumn(new TableColumn("[cyan]State[/]").Centered());

            foreach (var branch in mergedBranches)
                table.AddRow(
                    $"[white]{Markup.Escape(branch)}[/]",
                    "[yellow]Merged — safe to delete[/]"
                );

            AnsiConsole.Write(table);
            OutputService.BlankLine();

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                foreach (var branch in mergedBranches)
                {
                    AnsiConsole.MarkupLine($"[grey]    git branch -d {Markup.Escape(branch)}[/]");
                    AnsiConsole.MarkupLine($"[grey]    git push origin --delete {Markup.Escape(branch)}[/]");
                }
                OutputService.BlankLine();
                return;
            }

            var confirmed = OutputService.Confirm(
                $"Delete {mergedBranches.Count} merged branch(es) locally and from remote?");

            if (!confirmed)
            {
                OutputService.Info("Cancelled — no branches deleted.");
                return;
            }

            int deleted = 0;
            foreach (var branch in mergedBranches)
            {
                var local = _runner.Run($"branch -d {branch}");
                if (local.Success) { OutputService.Success($"Deleted local '{branch}'"); deleted++; }
                else OutputService.Warning($"Could not delete local '{branch}'");

                var remote = _runner.Run($"push origin --delete {branch}");
                if (remote.Success) OutputService.Success($"Deleted remote '{branch}'");
                else OutputService.Warning($"Remote '{branch}' already gone or not found");
            }

            OutputService.BlankLine();
            OutputService.Success($"Done — {deleted} branch(es) removed.");
            OutputService.BlankLine();
        }
    }
}