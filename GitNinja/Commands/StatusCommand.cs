using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;

namespace GitNinja.Commands
{
    public class StatusCommand
    {
        private readonly ContextAnalyzer _analyzer;

        public StatusCommand(ContextAnalyzer analyzer)
        {
            _analyzer = analyzer;
        }

        public void Execute()
        {
            OutputService.BlankLine();

            var context = _analyzer.Analyze(); // ONE call, stored in variable

            // Branch line
            var branchValue = context.IsDetachedHead
                ? $"[red]{Markup.Escape(context.CurrentBranch)} (DETACHED)[/]"
                : $"[white]{Markup.Escape(context.CurrentBranch)}[/]";

            AnsiConsole.MarkupLine($"  [cyan]{"Branch:",-12}[/] {branchValue}");

            // Upstream
            if (context.HasUpstream)
            {
                if (context.IsAheadOfOrigin)
                    OutputService.Warning($"Ahead of origin by {context.CommitsAhead} commit(s) — run 'gitninja save'");
                if (context.IsBehindOrigin)
                    OutputService.Warning($"Behind by {context.CommitsBehind} commit(s) — run 'gitninja sync'");
                if (!context.IsAheadOfOrigin && !context.IsBehindOrigin)
                    OutputService.Success("Up to date with origin");
            }
            else
            {
                OutputService.Warning("No upstream set — branch not pushed yet");
            }

            OutputService.BlankLine();

            // Files table
            if (context.HasUncommittedChanges)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn(new TableColumn("[cyan]Status[/]").Centered().Width(10))
                    .AddColumn(new TableColumn("[cyan]File[/]"))
                    .AddColumn(new TableColumn("[cyan]State[/]").Width(22));

                foreach (var file in context.ChangedFiles)
                {
                    var color = file.IsUntracked ? "grey"
                              : file.IsStaged ? "green"
                                                 : "yellow";
                    var state = file.IsUntracked ? "[grey]Untracked[/]"
                              : file.IsStaged ? "[green]Staged[/]"
                                                 : "[yellow]Unstaged[/]";

                    table.AddRow(
                        $"[{color}]{Markup.Escape(file.StatusCode)}[/]",
                        $"[{color}]{Markup.Escape(file.Path)}[/]",
                        state
                    );
                }

                AnsiConsole.Write(table);
            }
            else
            {
                OutputService.Success("Working tree clean — nothing to commit");
            }

            OutputService.BlankLine();
        }
    }
}