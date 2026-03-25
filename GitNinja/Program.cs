using GitNinja.Commands;
using GitNinja.Core;
using GitNinja.Services;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitNinja
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            ShowBanner();

            // Step 1: Git installed?
            if (!IsGitInstalled())
            {
                HandleGitNotInstalled();
                return;
            }

            // Step 2: Resolve working directory
            var workingDir = ResolveWorkingDirectory();
            if (workingDir == null)
            {
                AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                return;
            }

            // Step 3: Boot services with resolved directory
            var runner = new GitRunner(workingDir);
            var analyzer = new ContextAnalyzer(runner);
            var safety = new SafetyService(analyzer);
            var suggest = new SuggestionService(runner);

            // Step 4: Is it a Git repo?
            if (!runner.IsGitRepository())
            {
                HandleNotAGitRepo(workingDir, runner, analyzer, safety, suggest);
                return;
            }

            // Step 5: Route or show menu
            if (args.Length > 0)
            {
                var command = args[0].ToLower();
                var preview = args.Contains("--preview");
                RunCommand(command, preview, runner, analyzer, safety, suggest);
            }
            else
            {
                RunMenu(runner, analyzer, safety, suggest);
            }
        }

        // ── Git installed check ───────────────────────────────────────────────
        static bool IsGitInstalled()
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void HandleGitNotInstalled()
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[red]Git Not Found[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [red]✖  Git is not installed or not in your PATH.[/]");
            AnsiConsole.MarkupLine("  [grey]GitNinja needs Git to work.[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  What would you like to do?[/]")
                    .AddChoices(new[]
                    {
                        "Open Git download page in browser",
                        "Show manual install instructions",
                        "Exit"
                    })
            );

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Open Git download page in browser":
                    OpenUrl("https://git-scm.com/downloads");
                    AnsiConsole.MarkupLine("  [green]✔  Opened https://git-scm.com/downloads in your browser.[/]");
                    AnsiConsole.MarkupLine("  [grey]  After installing Git, restart your terminal and run gitninja again.[/]");
                    break;

                case "Show manual install instructions":
                    ShowGitInstallInstructions();
                    break;

                case "Exit":
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    break;
            }

            AnsiConsole.WriteLine();
        }

        static void ShowGitInstallInstructions()
        {
            AnsiConsole.WriteLine();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AnsiConsole.MarkupLine("  [cyan]Windows — choose one:[/]");
                AnsiConsole.MarkupLine("  [white]Option 1:[/] [grey]Download installer → https://git-scm.com/download/win[/]");
                AnsiConsole.MarkupLine("  [white]Option 2:[/] [grey]Run in PowerShell:[/]");
                AnsiConsole.MarkupLine("  [grey]    winget install --id Git.Git -e --source winget[/]");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                AnsiConsole.MarkupLine("  [cyan]macOS — run in terminal:[/]");
                AnsiConsole.MarkupLine("  [grey]    brew install git[/]");
                AnsiConsole.MarkupLine("  [grey]  or: xcode-select --install[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("  [cyan]Linux — run in terminal:[/]");
                AnsiConsole.MarkupLine("  [grey]    sudo apt install git        (Ubuntu/Debian)[/]");
                AnsiConsole.MarkupLine("  [grey]    sudo dnf install git        (Fedora)[/]");
                AnsiConsole.MarkupLine("  [grey]    sudo pacman -S git          (Arch)[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey]After installing, restart your terminal and run gitninja again.[/]");
            AnsiConsole.WriteLine();
        }

        // ── Working directory resolver ────────────────────────────────────────
        static string? ResolveWorkingDirectory()
        {
            var current = Directory.GetCurrentDirectory();

            // If running from bin/Debug or bin/Release, ask user to navigate
            if (current.Contains("bin\\Debug") || current.Contains("bin/Debug") ||
                current.Contains("bin\\Release") || current.Contains("bin/Release"))
            {
                AnsiConsole.MarkupLine("  [yellow]  Running from build output directory.[/]");
                AnsiConsole.MarkupLine("  [grey]  GitNinja needs to run from inside a Git project folder.[/]");
                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  How would you like to proceed?[/]")
                        .AddChoices(new[]
                        {
                    "Browse to my project folder",
                    "Type a folder path manually",
                    "Exit"
                        })
                );

                AnsiConsole.WriteLine();

                return choice switch
                {
                    "Browse to my project folder" => BrowseDirectories(Directory.GetCurrentDirectory()),
                    "Type a folder path manually" => AskForPath(),
                    _ => null
                };
            }

            return current;
        }

        static string? AskForDirectory()
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  How would you like to proceed?[/]")
                    .AddChoices(new[]
                    {
                        "Type a project folder path",
                        "Browse recent directories",
                        "Exit"
                    })
            );

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Type a project folder path":
                    return AskForPath();

                case "Browse recent directories":
                    return BrowseDirectories(Directory.GetCurrentDirectory());

                default:
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    return null;
            }
        }

        static string? AskForPath()
        {
            var path = AnsiConsole.Ask<string>("  [cyan]Enter full path to your project:[/]").Trim();

            if (!Directory.Exists(path))
            {
                AnsiConsole.MarkupLine($"  [red]✖  Directory not found: {Markup.Escape(path)}[/]");
                return null;
            }

            Directory.SetCurrentDirectory(path);
            AnsiConsole.MarkupLine($"  [green]✔  Navigated to: {Markup.Escape(path)}[/]");
            AnsiConsole.WriteLine();
            return path;
        }

        static string? BrowseDirectories(string startPath)
        {
            var current = startPath;

            while (true)
            {
                AnsiConsole.MarkupLine($"  [grey]Current: {Markup.Escape(current)}[/]");
                AnsiConsole.WriteLine();

                var entries = new List<string>();

                // Add parent option
                var parent = Directory.GetParent(current);
                if (parent != null) entries.Add(".. (go up)");

                // Add subdirectories
                try
                {
                    var dirs = Directory.GetDirectories(current)
                        .Select(d => Path.GetFileName(d) ?? d)
                        .Where(d => !d.StartsWith("."))
                        .OrderBy(d => d)
                        .Take(20)
                        .ToList();
                    entries.AddRange(dirs);
                }
                catch
                {
                    AnsiConsole.MarkupLine("  [red]✖  Cannot read this directory.[/]");
                    return null;
                }

                entries.Add("✔  Use this folder");
                entries.Add("✖  Cancel");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  Select a folder:[/]")
                        .PageSize(15)
                        .AddChoices(entries)
                );

                AnsiConsole.WriteLine();

                if (choice == "✔  Use this folder")
                {
                    Directory.SetCurrentDirectory(current);
                    AnsiConsole.MarkupLine($"  [green]✔  Selected: {Markup.Escape(current)}[/]");
                    AnsiConsole.WriteLine();
                    return current;
                }

                if (choice == "✖  Cancel") return null;

                if (choice == ".. (go up)")
                {
                    current = parent!.FullName;
                    continue;
                }

                current = Path.Combine(current, choice);
            }
        }

        // ── Not a git repo handler ────────────────────────────────────────────
        static void HandleNotAGitRepo(string workingDir, GitRunner runner,
                                       ContextAnalyzer analyzer, SafetyService safety,
                                       SuggestionService suggest)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]No Git Repository Found[/]").RuleStyle("yellow"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [yellow]⚠  '{Markup.Escape(workingDir)}'[/]");
            AnsiConsole.MarkupLine("  [grey]  This folder is not a Git repository.[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  What would you like to do?[/]")
                    .AddChoices(new[]
                    {
                        "Initialize a new Git repo here  (git init)",
                        "Navigate to a different folder",
                        "Exit"
                    })
            );

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Initialize a new Git repo here  (git init)":
                    InitializeRepo(workingDir, runner, analyzer, safety, suggest);
                    break;

                case "Navigate to a different folder":
                    var newDir = BrowseDirectories(workingDir);
                    if (newDir != null)
                    {
                        var newRunner = new GitRunner(newDir);
                        var newAnalyzer = new ContextAnalyzer(newRunner);

                        if (!newRunner.IsGitRepository())
                        {
                            AnsiConsole.MarkupLine("  [red]✖  That folder is also not a Git repository.[/]");
                            AnsiConsole.MarkupLine("  [grey]  Try again or initialize a new repo.[/]");
                        }
                        else
                        {
                            var newSafety = new SafetyService(newAnalyzer);
                            var newSuggest = new SuggestionService(newRunner);
                            RunMenu(newRunner, newAnalyzer, newSafety, newSuggest);
                        }
                    }
                    break;

                case "Exit":
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    break;
            }
        }

        static void InitializeRepo(string path, GitRunner runner,
                                    ContextAnalyzer analyzer, SafetyService safety,
                                    SuggestionService suggest)
        {
            var result = runner.Run("init");
            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"  [red]✖  git init failed: {Markup.Escape(result.Error)}[/]");
                return;
            }

            AnsiConsole.MarkupLine("  [green]✔  Git repository initialized![/]");
            AnsiConsole.WriteLine();

            // Ask for initial branch name
            var branch = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Default branch name:[/]")
                    .AddChoices("main", "master", "develop")
            );

            runner.Run($"checkout -b {branch}");
            AnsiConsole.MarkupLine($"  [green]✔  Default branch set to '{branch}'[/]");
            AnsiConsole.WriteLine();

            // Continue to main menu
            RunMenu(runner, analyzer, safety, suggest);
        }

        // ── Open URL cross-platform ───────────────────────────────────────────
        static void OpenUrl(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
                else
                    Process.Start("xdesktop-open", url);
            }
            catch
            {
                AnsiConsole.MarkupLine($"  [grey]Could not open browser. Visit manually: {url}[/]");
            }
        }

        // ── Interactive menu ──────────────────────────────────────────────────
        static void RunMenu(GitRunner runner, ContextAnalyzer analyzer,
                    SafetyService safety, SuggestionService suggestion)
        {
            while (true) // keep showing menu after each command finishes
            {
                var context = analyzer.Analyze();

                AnsiConsole.Write(new Rule("[cyan]Current Status[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"  [cyan]{"Branch:",-12}[/] [white]{Markup.Escape(context.CurrentBranch)}[/]");

                if (context.HasUncommittedChanges)
                    AnsiConsole.MarkupLine($"  [yellow]{"Changes:",-12}[/] {context.ChangedFiles.Count} file(s) not committed");
                else
                    AnsiConsole.MarkupLine($"  [green]{"Status:",-12}[/] Working tree clean");

                if (context.IsBehindOrigin)
                    AnsiConsole.MarkupLine($"  [yellow]{"Sync:",-12}[/] Behind main by {context.CommitsBehind} commit(s)");

                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  What do you want to do?[/]")
                        .PageSize(10)
                        .HighlightStyle(new Style(foreground: Color.Blue))
                        .AddChoices(new[]
                        {
                            "start    >>  Create new branch from latest main",
                            "save     >>  Stage, commit and push my changes",
                            "sync     >>  Pull latest main into my branch",
                            "status   >>  Show branch and file status",
                            "cleanup  >>  Delete merged branches",
                            "undo     >>  Undo last commit safely",
                            "help     >>  Show all commands",
                            "exit     >>  Quit"
                        })
                );

                AnsiConsole.WriteLine();

                var command = choice.Split(">>")[0].Trim();

                // Exit is the only thing that breaks the loop
                if (command == "exit")
                {
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    break;
                }

                RunCommand(command, false, runner, analyzer, safety, suggestion);

                // Small pause so user can read the output before menu refreshes
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return to menu...[/]");
                Console.ReadKey(intercept: true);
                AnsiConsole.Clear();
                ShowBanner();
            }
        }

        // ── Command router ────────────────────────────────────────────────────
        static void RunCommand(string command, bool preview,
                               GitRunner runner, ContextAnalyzer analyzer,
                               SafetyService safety, SuggestionService suggestion)
        {
            switch (command)
            {
                case "start":
                    new StartCommand(runner, analyzer, safety, preview).Execute();
                    break;
                case "save":
                    new SaveCommand(runner, analyzer, safety, suggestion, preview).Execute();
                    break;
                case "sync":
                    new SyncCommand(runner, analyzer, preview).Execute();
                    break;
                case "status":
                    new StatusCommand(analyzer).Execute();
                    break;
                case "cleanup":
                    new CleanupCommand(runner, analyzer, safety, preview).Execute();
                    break;
                case "undo":
                    new UndoCommand(runner, safety, preview).Execute();
                    break;
                case "help":
                    ShowHelp();
                    break;
                case "exit":
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    break;
                default:
                    AnsiConsole.MarkupLine($"  [red]✖  Unknown command '[white]{Markup.Escape(command)}[/]'[/]");
                    AnsiConsole.MarkupLine("  [grey]  Run [white]gitninja[/] with no args to open the menu.[/]");
                    break;
            }
        }

        // ── Banner ────────────────────────────────────────────────────────────
        static void ShowBanner()
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(
                new FigletText("GitNinja")
                    .Centered()
                    .Color(Color.Blue));
            AnsiConsole.MarkupLine("[grey]        Smart Git Workflow CLI — v1.0[/]");
            AnsiConsole.WriteLine();
        }

        // ── Help table ────────────────────────────────────────────────────────
        static void ShowHelp()
        {
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[cyan]GitNinja Commands[/]")
                .AddColumn(new TableColumn("[cyan]Command[/]").Width(12))
                .AddColumn(new TableColumn("[cyan]What it does[/]"))
                .AddColumn(new TableColumn("[cyan]Example[/]").Width(30));

            table.AddRow("[white]start[/]", "Create new branch from latest main", "[grey]gitninja start[/]");
            table.AddRow("[white]save[/]", "Stage, commit and push changes", "[grey]gitninja save[/]");
            table.AddRow("[white]sync[/]", "Pull latest main into your branch", "[grey]gitninja sync[/]");
            table.AddRow("[white]status[/]", "Show branch and file status", "[grey]gitninja status[/]");
            table.AddRow("[white]cleanup[/]", "Delete all merged branches", "[grey]gitninja cleanup[/]");
            table.AddRow("[white]undo[/]", "Safely undo the last commit", "[grey]gitninja undo[/]");
            table.AddRow("[white]help[/]", "Show this screen", "[grey]gitninja help[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey]Tip: add [white]--preview[/] to any command to see what runs without executing.[/]");
            AnsiConsole.MarkupLine("  [grey]Tip: run [white]gitninja[/] with no args for the interactive menu.[/]");
            AnsiConsole.WriteLine();
        }
    }
}