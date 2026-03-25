using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class StartCommand
    {
        private readonly GitRunner _runner;
        private readonly ContextAnalyzer _analyzer;
        private readonly SafetyService _safety;
        private readonly bool _preview;

        public StartCommand(GitRunner runner, ContextAnalyzer analyzer,
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
            var context = _analyzer.Analyze();

            // Already on a child branch
            if (!SafetyService.ProtectedBranches.Contains(context.CurrentBranch.ToLower()))
            {
                OutputService.Success($"Already on branch '{context.CurrentBranch}' — ready to code!");
                OutputService.BlankLine();
                return;
            }

            OutputService.Info($"You are on '{context.CurrentBranch}' — a new branch will be created.");
            OutputService.BlankLine();

            // Branch name input with cancel option
            AnsiConsole.MarkupLine("  [grey]Type 'cancel' to go back to menu[/]");

            var branchName = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]  Branch name:[/]")
            ).Trim().Replace(" ", "-").ToLower();

            // Handle cancel
            if (branchName == "cancel")
            {
                OutputService.Info("Cancelled. Returning to menu...");
                OutputService.BlankLine();
                return;
            }

            if (string.IsNullOrEmpty(branchName))
            {
                OutputService.Error("Branch name cannot be empty.");
                return;
            }

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                AnsiConsole.MarkupLine($"[grey]    git pull origin {Markup.Escape(context.CurrentBranch)}[/]");
                AnsiConsole.MarkupLine($"[grey]    git checkout -b {Markup.Escape(branchName)}[/]");
                OutputService.BlankLine();
                return;
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .Start($"Pulling latest from origin/{context.CurrentBranch}...", ctx =>
                {
                    var pull = _runner.Run($"pull origin {context.CurrentBranch}");
                    if (!pull.Success)
                    {
                        OutputService.Error($"Pull failed: {pull.Error}");
                        return;
                    }

                    ctx.Status($"Creating branch '{branchName}'...");
                    var checkout = _runner.Run($"checkout -b {branchName}");
                    if (!checkout.Success)
                    {
                        OutputService.Error($"Could not create branch: {checkout.Error}");
                        return;
                    }
                });

            OutputService.Success($"Switched to new branch '{branchName}' — start coding!");
            OutputService.BlankLine();
        }
    }
}