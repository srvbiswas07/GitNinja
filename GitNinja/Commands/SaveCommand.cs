using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class SaveCommand
    {
        private readonly GitRunner _runner;
        private readonly ContextAnalyzer _analyzer;
        private readonly SafetyService _safety;
        private readonly SuggestionService _suggestion;
        private readonly bool _preview;

        public SaveCommand(GitRunner runner, ContextAnalyzer analyzer,
                           SafetyService safety, SuggestionService suggestion,
                           bool preview = false)
        {
            _runner = runner;
            _analyzer = analyzer;
            _safety = safety;
            _suggestion = suggestion;
            _preview = preview;
        }

        public void Execute()
        {
            OutputService.BlankLine();
            var context = _analyzer.Analyze();

            // Safety check
            var safety = _safety.CheckCommitSafety();
            if (!safety.IsSafe)
            {
                OutputService.Error(safety.WarningMessage!);
                return;
            }

            if (!context.HasUncommittedChanges)
            {
                OutputService.Warning("Nothing to commit — working tree is clean.");
                return;
            }

            // Show files to be committed
            OutputService.Info($"Files to be committed ({context.ChangedFiles.Count}):");
            foreach (var file in context.ChangedFiles)
                OutputService.FileRow(file.StatusCode, file.Path, file.IsStaged, file.IsUntracked);

            OutputService.BlankLine();

            // Commit message — suggest + let user choose
            var suggested = _suggestion.SuggestCommitMessage();

            var messageChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Commit message:[/]")
                    .AddChoices(new[]
                    {
                        $"Use suggested:  \"{suggested}\"",
                        "Write my own message"
                    })
            );

            string commitMessage;
            if (messageChoice.StartsWith("Use suggested"))
            {
                commitMessage = suggested;
            }
            else
            {
                commitMessage = AnsiConsole.Ask<string>("[cyan]  Your message:[/]");
            }

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                AnsiConsole.MarkupLine("[grey]    git add .[/]");
                AnsiConsole.MarkupLine($"[grey]    git commit -m \"{Markup.Escape(commitMessage)}\"[/]");
                AnsiConsole.MarkupLine($"[grey]    git push origin {Markup.Escape(context.CurrentBranch)}[/]");
                OutputService.BlankLine();
                return;
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .Start("Staging changes...", ctx =>
                {
                    // Stage
                    var add = _runner.Run("add .");
                    if (!add.Success) { OutputService.Error($"Staging failed: {add.Error}"); return; }
                    OutputService.Success("All changes staged");

                    // Commit
                    ctx.Status("Committing...");
                    var commit = _runner.Run($"commit -m \"{commitMessage}\"");
                    if (!commit.Success) { OutputService.Error($"Commit failed: {commit.Error}"); return; }
                    OutputService.Success($"Committed: \"{commitMessage}\"");

                    // Push
                    ctx.Status($"Pushing to origin/{context.CurrentBranch}...");
                    var push = _runner.Run($"push origin {context.CurrentBranch}");
                    if (!push.Success)
                    {
                        push = _runner.Run($"push --set-upstream origin {context.CurrentBranch}");
                        if (!push.Success) { OutputService.Error($"Push failed: {push.Error}"); return; }
                    }

                    OutputService.Success($"Pushed to origin/{context.CurrentBranch}");
                });

            OutputService.BlankLine();
        }
    }
}