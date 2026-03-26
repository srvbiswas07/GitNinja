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

            // Silent update check — runs in background
            UpdateCheckResult? pendingUpdate = null;
            var version = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";

            var updateService = new UpdateService(version, "srvbiswas07", "gitninja");
            var updateTask = Task.Run(() => updateService.CheckSilentAsync());

            // Show update notice if found (wait max 3 seconds)
            try
            {
                if (updateTask.Wait(TimeSpan.FromSeconds(3)))
                {
                    pendingUpdate = updateTask.Result;
                    if (pendingUpdate?.UpdateAvailable == true)
                    {
                        AnsiConsole.Write(new Rule("[yellow]Update Available[/]").RuleStyle("yellow"));
                        AnsiConsole.MarkupLine(
                            $"  [yellow]GitNinja v{pendingUpdate.LatestVersion} is available![/] " +
                            $"[grey](you have v{pendingUpdate.CurrentVersion})[/]");
                        AnsiConsole.MarkupLine(
                            "  [grey]Select 'Check for Updates' from the menu to install.[/]");
                        AnsiConsole.Write(new Rule().RuleStyle("grey"));
                        AnsiConsole.WriteLine();
                    }
                }
            }
            catch { /* Silent fail */ }

            // Step 1: Git installed?
            if (!IsGitInstalled())
            {
                HandleGitNotInstalled();
                return;
            }

            // Step 2: Resolve working directory
            var workingDir = ResolveWorkingDirectory(pendingUpdate);
            if (workingDir == null)
            {
                AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                return;
            }

            // Save last used project
            var settings = new SettingsService();
            if (IsGitRepository(workingDir))
                settings.LastProjectPath = workingDir;

            // Step 3: Boot services
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
            catch { return false; }
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
                    AnsiConsole.MarkupLine(
                        "  [green]✔  Opened https://git-scm.com/downloads in your browser.[/]");
                    AnsiConsole.MarkupLine(
                        "  [grey]  After installing Git, restart your terminal and run gitninja again.[/]");
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
                AnsiConsole.MarkupLine(
                    "  [white]Option 1:[/] [grey]Download installer → https://git-scm.com/download/win[/]");
                AnsiConsole.MarkupLine("  [white]Option 2:[/] [grey]Run in PowerShell:[/]");
                AnsiConsole.MarkupLine(
                    "  [grey]    winget install --id Git.Git -e --source winget[/]");
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
            AnsiConsole.MarkupLine(
                "  [grey]After installing, restart your terminal and run gitninja again.[/]");
            AnsiConsole.WriteLine();
        }

        // ── Working directory resolver ────────────────────────────────────────
        static string? ResolveWorkingDirectory(UpdateCheckResult? pendingUpdate)
        {
            var current = Directory.GetCurrentDirectory();
            var settings = new SettingsService();

            bool isInstalledLocation =
                current.Contains("Program Files") ||
                current.Contains("Program Files (x86)");

            bool isBuildOutput =
                current.Contains("bin\\Debug") || current.Contains("bin/Debug") ||
                current.Contains("bin\\Release") || current.Contains("bin/Release");

            if (!isInstalledLocation && !isBuildOutput)
                return current;

            AnsiConsole.MarkupLine(isInstalledLocation
                ? "  [yellow]  GitNinja is running from the installation folder.[/]"
                : "  [yellow]  Running from build output directory.[/]");
            AnsiConsole.WriteLine();

            // ── Has a saved last project ──────────────────────────────────────
            if (settings.HasLastProject)
            {
                var lastPath = settings.LastProjectPath!;
                AnsiConsole.MarkupLine(
                    $"  [green]  Last used project:[/] [white]{Markup.Escape(lastPath)}[/]");
                AnsiConsole.WriteLine();

                while (true)
                {
                    var choices = BuildMenuChoices(pendingUpdate, new[]
                    {
                        $"Open last project ({Path.GetFileName(lastPath)})",
                        "Browse to a different project folder",
                        "Type a folder path manually",
                        "Check for Updates",
                        "Exit"
                    });

                    var pick = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[cyan]  What would you like to do?[/]")
                            .AddChoices(choices)
                    );

                    AnsiConsole.WriteLine();

                    // Update option selected
                    if (pick.StartsWith("Install update"))
                    {
                        RunUpdateAndRestart(pendingUpdate!);
                        return null;
                    }

                    switch (pick)
                    {
                        case string s when s.StartsWith("Open last project"):
                            if (IsGitRepository(lastPath))
                            {
                                Directory.SetCurrentDirectory(lastPath);
                                AnsiConsole.MarkupLine(
                                    $"  [green]✔  Opened: {Markup.Escape(lastPath)}[/]");
                                AnsiConsole.WriteLine();
                                return lastPath;
                            }
                            AnsiConsole.MarkupLine(
                                "  [red]✖  Last project is no longer a valid Git repository.[/]");
                            AnsiConsole.WriteLine();
                            break;

                        case "Browse to a different project folder":
                            var browsed = BrowseDirectoriesWithDrives();
                            if (browsed != null && IsGitRepository(browsed))
                            {
                                Directory.SetCurrentDirectory(browsed);
                                settings.LastProjectPath = browsed;
                                return browsed;
                            }
                            break;

                        case "Type a folder path manually":
                            var typed = AskForPathWithRetry();
                            if (typed != null)
                            {
                                settings.LastProjectPath = typed;
                                return typed;
                            }
                            break;

                        case "Check for Updates":
                            new UpdateCommand(preview: false).Execute();
                            pendingUpdate = RefreshUpdateStatus();
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine("[grey]  Press any key to continue...[/]");
                            Console.ReadKey(true);
                            AnsiConsole.Clear();
                            ShowBanner();
                            break;

                        case "Exit":
                            return null;
                    }
                }
            }

            // ── No saved project — browse menu ────────────────────────────────
            while (true)
            {
                var choices = BuildMenuChoices(pendingUpdate, new[]
                {
                    "Browse to my project folder",
                    "Type a folder path manually",
                    "Check for Updates",
                    "Exit"
                });

                var pick = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  How would you like to proceed?[/]")
                        .AddChoices(choices)
                );

                AnsiConsole.WriteLine();

                if (pick.StartsWith("Install update"))
                {
                    RunUpdateAndRestart(pendingUpdate!);
                    return null;
                }

                switch (pick)
                {
                    case "Browse to my project folder":
                        var browsed = BrowseDirectoriesWithDrives();
                        if (browsed == null) break;
                        if (IsGitRepository(browsed))
                        {
                            Directory.SetCurrentDirectory(browsed);
                            settings.LastProjectPath = browsed;
                            return browsed;
                        }
                        AnsiConsole.MarkupLine(
                            $"  [yellow]⚠  '{Markup.Escape(browsed)}' is not a Git repository.[/]");
                        AnsiConsole.MarkupLine(
                            "  [grey]  Browse deeper to find your project folder.[/]");
                        AnsiConsole.WriteLine();
                        var deeper = BrowseDirectories(browsed);
                        if (deeper != null && IsGitRepository(deeper))
                        {
                            Directory.SetCurrentDirectory(deeper);
                            settings.LastProjectPath = deeper;
                            return deeper;
                        }
                        break;

                    case "Type a folder path manually":
                        var typed = AskForPathWithRetry();
                        if (typed != null)
                        {
                            settings.LastProjectPath = typed;
                            return typed;
                        }
                        break;

                    case "Check for Updates":
                        new UpdateCommand(preview: false).Execute();
                        pendingUpdate = RefreshUpdateStatus();
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[grey]  Press any key to continue...[/]");
                        Console.ReadKey(true);
                        AnsiConsole.Clear();
                        ShowBanner();
                        break;

                    case "Exit":
                        return null;
                }
            }
        }

        // ── Build menu choices with update at top if available ────────────────
        static List<string> BuildMenuChoices(
            UpdateCheckResult? pendingUpdate, IEnumerable<string> baseChoices)
        {
            var choices = new List<string>();

            if (pendingUpdate?.UpdateAvailable == true &&
                !string.IsNullOrWhiteSpace(pendingUpdate.InstallerDownloadUrl))
            {
                choices.Add($"Install update v{pendingUpdate.LatestVersion}");
            }

            choices.AddRange(baseChoices);
            return choices;
        }

        // ── Refresh update status after manual check ──────────────────────────
        static UpdateCheckResult? RefreshUpdateStatus()
        {
            try
            {
                var ver = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName().Version?.ToString(3) ?? "1.0.0";
                var svc = new UpdateService(ver, "srvbiswas07", "gitninja");
                return svc.CheckSilentAsync().GetAwaiter().GetResult();
            }
            catch { return null; }
        }

        // ── Download and install update ───────────────────────────────────────
        static void RunUpdateAndRestart(UpdateCheckResult update)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Update GitNinja[/]").RuleStyle("yellow"));
            AnsiConsole.WriteLine();

            // Guard — no installer URL
            if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
            {
                OutputService.Warning("No installer file found in this release.");
                AnsiConsole.MarkupLine(
                    $"  [grey]Download manually: {Markup.Escape(update.ReleasePageUrl)}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }

            var updateService = new UpdateService(
                update.CurrentVersion, "srvbiswas07", "gitninja");

            // Download with progress bar
            var installerPath = updateService
                .DownloadInstallerAsync(update.InstallerDownloadUrl, update.LatestVersion)
                .GetAwaiter().GetResult();

            if (installerPath == null)
            {
                OutputService.Error("Download failed. Try again or download manually.");
                AnsiConsole.MarkupLine(
                    $"  [grey]Release page: {Markup.Escape(update.ReleasePageUrl)}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }

            AnsiConsole.WriteLine();

            // Ask before installing
            var confirmed = AnsiConsole.Confirm(
                "  [cyan]Ready to install. GitNinja will close and the installer will open. Continue?[/]",
                defaultValue: true);

            if (!confirmed)
            {
                AnsiConsole.WriteLine();
                OutputService.Info($"Installer saved at: {installerPath}");
                OutputService.Info("Run it manually whenever you are ready.");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }

            AnsiConsole.WriteLine();
            updateService.LaunchInstaller(installerPath);
            Task.Delay(1500).Wait();
            Environment.Exit(0);
        }

        // ── Helper: check if path is git repo ────────────────────────────────
        static bool IsGitRepository(string path) =>
            Directory.Exists(Path.Combine(path, ".git"));

        // ── Helper: ask for path with retry ──────────────────────────────────
        static string? AskForPathWithRetry()
        {
            while (true)
            {
                AnsiConsole.MarkupLine(
                    "  [grey]Enter a full path like: C:\\Projects\\MyApp[/]");
                AnsiConsole.MarkupLine(
                    "  [grey]Type 'back' to return to previous menu[/]");
                AnsiConsole.WriteLine();

                var path = AnsiConsole
                    .Ask<string>("  [cyan]Enter full path to your project:[/]")
                    .Trim();

                if (path.ToLower() == "back") return null;

                path = AutoCorrectPath(path);

                if (!Directory.Exists(path))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(
                        $"  [red]✖  Directory not found: {Markup.Escape(path)}[/]");
                    AnsiConsole.MarkupLine(
                        "  [yellow]  Please try again or type 'back' to return.[/]");
                    AnsiConsole.WriteLine();
                    continue;
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(
                    $"  [green]✔  Navigated to: {Markup.Escape(path)}[/]");
                AnsiConsole.WriteLine();
                return path;
            }
        }

        // ── Helper: auto-correct short path input ─────────────────────────────
        static string AutoCorrectPath(string path)
        {
            if (path.Length == 1 && char.IsLetter(path[0]))
                return path + ":\\";
            if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':')
                return path + "\\";
            return path.Trim();
        }

        // ── Helper: browse from drive selection ───────────────────────────────
        static string? BrowseDirectoriesWithDrives()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => $"{d.Name} ({FormatBytes(d.TotalFreeSpace)} free)")
                .ToList();

            drives.Add("Cancel (go back)");

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Select a drive:[/]")
                    .PageSize(10)
                    .AddChoices(drives)
            );

            if (selected == "Cancel (go back)") return null;

            var driveRoot = selected.Substring(0, 3);
            return BrowseDirectories(driveRoot);
        }

        // ── Helper: format bytes ──────────────────────────────────────────────
        static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        // ── Helper: folder browser ────────────────────────────────────────────
        static string? BrowseDirectories(string startPath)
        {
            var current = startPath;

            while (true)
            {
                AnsiConsole.MarkupLine($"  [grey]Current: {Markup.Escape(current)}[/]");
                AnsiConsole.WriteLine();

                var entries = new List<string>();
                var parent = Directory.GetParent(current);
                bool isDriveRoot =
                    current.EndsWith(":\\") || current.EndsWith(":");

                if (!isDriveRoot && parent != null)
                    entries.Add("Go up to parent folder");
                else if (isDriveRoot)
                    entries.Add("Back to drive selection");

                try
                {
                    var dirs = Directory.GetDirectories(current)
                        .Select(d => Path.GetFileName(d) ?? d)
                        .Where(d => !d.StartsWith("."))
                        .OrderBy(d => d)
                        .ToList();
                    entries.AddRange(dirs);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"  [red]✖  Cannot read directory: {Markup.Escape(ex.Message)}[/]");
                    return null;
                }

                entries.Add("Use this folder");
                entries.Add("Cancel (go back)");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  Select a folder:[/]")
                        .PageSize(15)
                        .AddChoices(entries)
                );

                AnsiConsole.WriteLine();

                switch (choice)
                {
                    case "Use this folder": return current;
                    case "Cancel (go back)": return null;
                    case "Back to drive selection": return null;
                    case "Go up to parent folder":
                        current = parent!.FullName;
                        break;
                    default:
                        current = Path.Combine(current, choice);
                        break;
                }
            }
        }

        // ── Not a git repo handler ────────────────────────────────────────────
        static void HandleNotAGitRepo(string workingDir, GitRunner runner,
            ContextAnalyzer analyzer, SafetyService safety, SuggestionService suggest)
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
                            OutputService.Error("That folder is also not a Git repository.");
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
            ContextAnalyzer analyzer, SafetyService safety, SuggestionService suggest)
        {
            var result = runner.Run("init");
            if (!result.Success)
            {
                OutputService.Error($"git init failed: {result.Error}");
                return;
            }

            OutputService.Success("Git repository initialized!");
            AnsiConsole.WriteLine();

            var branch = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]  Default branch name:[/]")
                    .AddChoices("main", "master", "develop")
            );

            runner.Run($"checkout -b {branch}");
            OutputService.Success($"Default branch set to '{branch}'");
            AnsiConsole.WriteLine();

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
                    Process.Start("xdg-open", url);
            }
            catch
            {
                AnsiConsole.MarkupLine(
                    $"  [grey]Could not open browser. Visit manually: {url}[/]");
            }
        }

        // ── Interactive menu ──────────────────────────────────────────────────
        static void RunMenu(GitRunner runner, ContextAnalyzer analyzer,
            SafetyService safety, SuggestionService suggestion)
        {
            while (true)
            {
                var context = analyzer.Analyze();

                AnsiConsole.Write(new Rule("[cyan]Current Status[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(
                    $"  [cyan]{"Branch:",-12}[/] [white]{Markup.Escape(context.CurrentBranch)}[/]");

                if (context.HasUncommittedChanges)
                    AnsiConsole.MarkupLine(
                        $"  [yellow]{"Changes:",-12}[/] {context.ChangedFiles.Count} file(s) not committed");
                else
                    AnsiConsole.MarkupLine(
                        $"  [green]{"Status:",-12}[/] Working tree clean");

                if (context.IsBehindOrigin)
                    AnsiConsole.MarkupLine(
                        $"  [yellow]{"Sync:",-12}[/] Behind main by {context.CommitsBehind} commit(s)");

                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[cyan]  What do you want to do?[/]")
                        .PageSize(12)
                        .HighlightStyle(new Style(foreground: Color.Blue))
                        .AddChoices(new[]
                        {
                            "start    >>  Create new branch from latest main",
                            "checkout >>  Switch to existing branch",
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

                if (command == "exit")
                {
                    AnsiConsole.MarkupLine("  [grey]Bye! 👋[/]");
                    break;
                }

                RunCommand(command, false, runner, analyzer, safety, suggestion);

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
                case "checkout":
                    new CheckoutCommand(runner, analyzer, preview).Execute();
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
                default:
                    AnsiConsole.MarkupLine(
                        $"  [red]✖  Unknown command '[white]{Markup.Escape(command)}[/]'[/]");
                    AnsiConsole.MarkupLine(
                        "  [grey]  Run [white]gitninja[/] with no args to open the menu.[/]");
                    break;
            }
        }

        // ── Banner ────────────────────────────────────────────────────────────
        static void ShowBanner()
        {
            var version = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";

            AnsiConsole.WriteLine();
            AnsiConsole.Write(
                new FigletText("GitNinja")
                    .Centered()
                    .Color(Color.Blue));
            AnsiConsole.MarkupLine($"[grey]        Smart Git Workflow CLI — v{version}[/]");
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
            table.AddRow("[white]checkout[/]", "Switch to an existing branch", "[grey]gitninja checkout[/]");
            table.AddRow("[white]save[/]", "Stage, commit and push changes", "[grey]gitninja save[/]");
            table.AddRow("[white]sync[/]", "Pull latest main into your branch", "[grey]gitninja sync[/]");
            table.AddRow("[white]status[/]", "Show branch and file status", "[grey]gitninja status[/]");
            table.AddRow("[white]cleanup[/]", "Delete all merged branches", "[grey]gitninja cleanup[/]");
            table.AddRow("[white]undo[/]", "Safely undo the last commit", "[grey]gitninja undo[/]");
            table.AddRow("[white]help[/]", "Show this screen", "[grey]gitninja help[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "  [grey]Tip: add [white]--preview[/] to any command to see what runs without executing.[/]");
            AnsiConsole.MarkupLine(
                "  [grey]Tip: run [white]gitninja[/] with no args for the interactive menu.[/]");
            AnsiConsole.WriteLine();
        }
    }
}