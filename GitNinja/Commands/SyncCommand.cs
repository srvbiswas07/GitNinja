using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class SyncCommand
    {
        private readonly GitRunner _runner;
        private readonly ContextAnalyzer _analyzer;
        private readonly bool _preview;

        private static readonly string[] MainBranches =
            { "main", "master", "develop", "dev" };

        public SyncCommand(GitRunner runner, ContextAnalyzer analyzer, bool preview = false)
        {
            _runner = runner;
            _analyzer = analyzer;
            _preview = preview;
        }

        public void Execute()
        {
            OutputService.BlankLine();
            var context = _analyzer.Analyze();
            var currentBranch = context.CurrentBranch;
            var mainBranch = DetectMainBranch();

            if (mainBranch == null)
            {
                OutputService.Error("Could not detect a main branch (main/master/develop/dev).");
                return;
            }

            if (currentBranch.ToLower() == mainBranch.ToLower())
            {
                OutputService.Warning($"You are already on '{mainBranch}'. Use 'gitninja save' to push.");
                return;
            }

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                AnsiConsole.MarkupLine("[grey]    git stash[/]");
                AnsiConsole.MarkupLine($"[grey]    git checkout {Markup.Escape(mainBranch)}[/]");
                AnsiConsole.MarkupLine($"[grey]    git pull origin {Markup.Escape(mainBranch)}[/]");
                AnsiConsole.MarkupLine($"[grey]    git checkout {Markup.Escape(currentBranch)}[/]");
                AnsiConsole.MarkupLine($"[grey]    git merge {Markup.Escape(mainBranch)}[/]");
                AnsiConsole.MarkupLine("[grey]    git stash pop[/]");
                AnsiConsole.MarkupLine($"[grey]    git push origin {Markup.Escape(currentBranch)}[/]");
                OutputService.BlankLine();
                return;
            }

            bool stashed = false;

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .Start("Preparing sync...", ctx =>
                {
                    // Stash if dirty
                    if (context.HasUncommittedChanges)
                    {
                        ctx.Status("Stashing uncommitted changes...");
                        var stash = _runner.Run("stash");
                        if (!stash.Success) { OutputService.Error($"Stash failed: {stash.Error}"); return; }
                        stashed = true;
                        OutputService.Success("Changes stashed");
                    }

                    // Checkout main
                    ctx.Status($"Switching to {mainBranch}...");
                    var checkoutMain = _runner.Run($"checkout {mainBranch}");
                    if (!checkoutMain.Success) { OutputService.Error($"Could not switch to {mainBranch}: {checkoutMain.Error}"); return; }

                    // Pull latest
                    ctx.Status($"Pulling latest from origin/{mainBranch}...");
                    var pull = _runner.Run($"pull origin {mainBranch}");
                    if (!pull.Success) { OutputService.Error($"Pull failed: {pull.Error}"); return; }
                    OutputService.Success($"Got latest {mainBranch}");

                    // Go back
                    ctx.Status($"Switching back to {currentBranch}...");
                    var checkoutBack = _runner.Run($"checkout {currentBranch}");
                    if (!checkoutBack.Success) { OutputService.Error($"Could not switch back: {checkoutBack.Error}"); return; }

                    // Merge
                    ctx.Status($"Merging {mainBranch} into {currentBranch}...");
                    var merge = _runner.Run($"merge {mainBranch}");
                    if (!merge.Success)
                    {
                        OutputService.Error("Merge conflict detected! Resolve conflicts then run 'gitninja save'.");
                        var conflicts = _runner.Run("diff --name-only --diff-filter=U");
                        foreach (var line in conflicts.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            AnsiConsole.MarkupLine($"[red]       ✖  {Markup.Escape(line.Trim())}[/]");
                        return;
                    }
                    OutputService.Success($"Merged {mainBranch} into {currentBranch}");

                    // Pop stash
                    if (stashed)
                    {
                        ctx.Status("Restoring stashed changes...");
                        var pop = _runner.Run("stash pop");
                        if (!pop.Success) OutputService.Warning("Stash pop had conflicts — resolve manually.");
                        else OutputService.Success("Stashed changes restored");
                    }

                    // Push
                    ctx.Status($"Pushing {currentBranch} to origin...");
                    var push = _runner.Run($"push origin {currentBranch}");
                    if (!push.Success)
                    {
                        push = _runner.Run($"push --set-upstream origin {currentBranch}");
                        if (!push.Success) { OutputService.Error($"Push failed: {push.Error}"); return; }
                    }

                    OutputService.Success($"Branch '{currentBranch}' is now up to date with {mainBranch}!");
                });

            OutputService.BlankLine();
        }

        private string? DetectMainBranch()
        {
            foreach (var branch in MainBranches)
            {
                var result = _runner.Run($"show-ref --verify --quiet refs/heads/{branch}");
                if (result.Success) return branch;
            }
            return null;
        }
    }
}