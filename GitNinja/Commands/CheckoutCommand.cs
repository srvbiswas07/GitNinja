using GitNinja.Core;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class CheckoutCommand
    {
        private readonly GitRunner _runner;
        private readonly ContextAnalyzer _analyzer;
        private readonly bool _preview;

        public CheckoutCommand(GitRunner runner, ContextAnalyzer analyzer, bool preview)
        {
            _runner = runner;
            _analyzer = analyzer;
            _preview = preview;
        }

        public void Execute()
        {
            var context = _analyzer.Analyze();
            var branches = GetLocalBranches();

            if (branches.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]✖  No other branches found.[/]");
                return;
            }

            // Add cancel option
            branches.Add("✖  Cancel (go back)");

            var selectedBranch = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Select a branch to checkout:[/]")
                    .PageSize(10)
                    .AddChoices(branches)
            );

            if (selectedBranch == "✖  Cancel (go back)")
            {
                AnsiConsole.MarkupLine("[grey]  Cancelled.[/]");
                return;
            }

            if (_preview)
            {
                AnsiConsole.MarkupLine("[grey]Preview: would checkout " + selectedBranch + "[/]");
                return;
            }

            var result = _runner.Run($"checkout {selectedBranch}");
            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]✔  Switched to branch '{selectedBranch}'[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✖  Failed: {result.Error}[/]");
            }
        }

        private List<string> GetLocalBranches()
        {
            var result = _runner.Run("branch --format=%(refname:short)");
            if (!result.Success) return new List<string>();

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();
        }
    }
}