using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;
using System.Diagnostics;

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

            // Show files
            OutputService.Info($"Files to be committed ({context.ChangedFiles.Count}):");
            foreach (var file in context.ChangedFiles)
                OutputService.FileRow(file.StatusCode, file.Path, file.IsStaged, file.IsUntracked);
            OutputService.BlankLine();

            // Commit message
            var suggested = _suggestion.SuggestCommitMessage();
            var messageChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Commit message:[/]")
                    .AddChoices(new[] {
                        $"Use suggested:  \"{suggested}\"",
                        "Write my own message"
                    })
            );

            string commitMessage = messageChoice.StartsWith("Use suggested")
                ? suggested
                : AnsiConsole.Ask<string>("[cyan]  Your message:[/]");

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                AnsiConsole.MarkupLine("[grey]    git add .[/]");
                AnsiConsole.MarkupLine($"[grey]    git commit -m \"{Markup.Escape(commitMessage)}\"[/]");
                AnsiConsole.MarkupLine($"[grey]    git push origin {Markup.Escape(context.CurrentBranch)}[/]");
                OutputService.BlankLine();
                return;
            }

            // Execute save
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .Start("Saving changes...", ctx =>
                {
                    ctx.Status("Staging...");
                    var add = _runner.Run("add .");
                    if (!add.Success) { OutputService.Error($"Staging failed: {add.Error}"); return; }

                    ctx.Status("Committing...");
                    var commit = _runner.Run($"commit -m \"{commitMessage}\"");
                    if (!commit.Success) { OutputService.Error($"Commit failed: {commit.Error}"); return; }

                    ctx.Status($"Pushing to origin/{context.CurrentBranch}...");
                    var push = _runner.Run($"push origin {context.CurrentBranch}");
                    if (!push.Success)
                    {
                        push = _runner.Run($"push --set-upstream origin {context.CurrentBranch}");
                        if (!push.Success) { OutputService.Error($"Push failed: {push.Error}"); return; }
                    }
                });

            OutputService.Success($"✔ Saved and pushed to origin/{context.CurrentBranch}!");
            OutputService.BlankLine();

            // Only offer PR flow if on feature branch (not protected branch)
            if (!SafetyService.ProtectedBranches.Contains(context.CurrentBranch.ToLower()))
            {
                OpenPullRequestPage(context.CurrentBranch);

                // Pause for user to merge PR in GitHub
                OutputService.Info("PR page opened in browser.");
                OutputService.Info("1. Create and merge the PR in GitHub");
                OutputService.Info("2. Then return here and press any key to continue...");
                Console.ReadKey(intercept: true);
                OutputService.BlankLine();

                // Auto-sync after PR merge
                SyncAfterPrMerge(context.CurrentBranch);
            }
        }

        private void OpenPullRequestPage(string branchName)
        {
            var remoteResult = _runner.Run("remote get-url origin");
            if (!remoteResult.Success) return;

            var remoteUrl = remoteResult.Output.Trim();
            var defaultBranch = GetDefaultBranch();
            var prUrl = BuildPrUrl(remoteUrl, defaultBranch, branchName);

            try
            {
                Process.Start(new ProcessStartInfo(prUrl) { UseShellExecute = true });
            }
            catch
            {
                OutputService.Error($"Open manually: {prUrl}");
            }
        }

        private string GetDefaultBranch()
        {
            var result = _runner.Run("symbolic-ref refs/remotes/origin/HEAD --short");
            if (result.Success)
            {
                var output = result.Output.Trim(); // "origin/main" or "origin/master"
                return output.Replace("origin/", "");
            }
            return "main"; // fallback
        }

        private string BuildPrUrl(string remoteUrl, string baseBranch, string headBranch)
        {
            // HTTPS: https://github.com/user/repo.git
            if (remoteUrl.StartsWith("https://github.com/"))
            {
                var clean = remoteUrl.Replace(".git", "");
                return $"{clean}/compare/{baseBranch}...{headBranch}?expand=1";
            }

            // SSH: git@github.com:user/repo.git
            if (remoteUrl.StartsWith("git@github.com:"))
            {
                var clean = remoteUrl.Replace("git@github.com:", "https://github.com/")
                                     .Replace(".git", "");
                return $"{clean}/compare/{baseBranch}...{headBranch}?expand=1";
            }

            return remoteUrl;
        }

        private void SyncAfterPrMerge(string featureBranch)
        {
            OutputService.Info("Syncing feature branch with master...");
            OutputService.BlankLine();

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .Start("Syncing...", ctx =>
                {
                    var defaultBranch = GetDefaultBranch();

                    // 1. Checkout master/main
                    ctx.Status($"Checking out {defaultBranch}...");
                    var checkoutMaster = _runner.Run($"checkout {defaultBranch}");
                    if (!checkoutMaster.Success)
                    {
                        OutputService.Error($"Failed: {checkoutMaster.Error}");
                        return;
                    }

                    // 2. Pull latest
                    ctx.Status($"Pulling latest {defaultBranch}...");
                    var pull = _runner.Run($"pull origin {defaultBranch}");
                    if (!pull.Success)
                    {
                        OutputService.Error($"Pull failed: {pull.Error}");
                        return;
                    }

                    // 3. Checkout feature
                    ctx.Status($"Checking out {featureBranch}...");
                    var checkoutFeature = _runner.Run($"checkout {featureBranch}");
                    if (!checkoutFeature.Success)
                    {
                        OutputService.Error($"Failed: {checkoutFeature.Error}");
                        return;
                    }

                    // 4. Merge master into feature
                    ctx.Status($"Merging {defaultBranch} into {featureBranch}...");
                    var merge = _runner.Run($"merge {defaultBranch}");
                    if (!merge.Success)
                    {
                        OutputService.Error($"Merge failed: {merge.Error}");
                        OutputService.Warning("Resolve conflicts manually, then run 'gitninja sync'");
                        return;
                    }

                    // 5. Push
                    ctx.Status("Pushing...");
                    var push = _runner.Run($"push origin {featureBranch}");
                    if (!push.Success)
                    {
                        OutputService.Error($"Push failed: {push.Error}");
                        return;
                    }
                });

            OutputService.Success($"✔ Feature branch synced with {GetDefaultBranch()}!");
            OutputService.BlankLine();
        }
    }
}