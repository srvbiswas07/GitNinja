using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class UndoCommand
    {
        private readonly GitRunner _runner;
        private readonly SafetyService _safety;
        private readonly bool _preview;

        public UndoCommand(GitRunner runner, SafetyService safety, bool preview = false)
        {
            _runner = runner;
            _safety = safety;
            _preview = preview;
        }

        public void Execute()
        {
            OutputService.BlankLine();

            var lastCommit = _runner.Run("log -1 --pretty=format:\"%h — %s\"");
            if (!lastCommit.Success)
            {
                OutputService.Error("No commits found to undo.");
                return;
            }

            OutputService.Warning($"Last commit: {lastCommit.Output}");
            OutputService.BlankLine();

            if (_preview)
            {
                OutputService.Info("Preview — these commands will run:");
                AnsiConsole.MarkupLine("[grey]    git reset --soft HEAD~1[/]");
                OutputService.BlankLine();
                return;
            }

            var confirmed = OutputService.Confirm(
                "This will undo your last commit. Your changes will be kept but uncommitted.");

            if (!confirmed)
            {
                OutputService.Info("Cancelled — nothing changed.");
                return;
            }

            var reset = _runner.Run("reset --soft HEAD~1");
            if (!reset.Success)
            {
                OutputService.Error($"Undo failed: {reset.Error}");
                return;
            }

            OutputService.Success("Last commit undone — changes are still here, ready to re-commit.");
            OutputService.BlankLine();
        }
    }
}