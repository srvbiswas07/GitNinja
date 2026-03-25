using Spectre.Console;
using System.Runtime.InteropServices;

namespace GitNinja.Services
{
    public static class OutputService
    {
        // Safe symbols that work on ALL terminals including old Windows CMD
        private static readonly bool _supportsUnicode = SupportsUnicode();

        private static string Ok => _supportsUnicode ? "✔" : "[OK]";
        private static string Err => _supportsUnicode ? "✖" : "[ERR]";
        private static string Warn => _supportsUnicode ? "⚠" : "[WARN]";
        private static string Arr => _supportsUnicode ? "→" : ">>";

        private static bool SupportsUnicode()
        {
            try
            {
                // Windows CMD older versions don't support unicode well
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Console.OutputEncoding.CodePage == 65001; // UTF-8
                }
                return true; // Linux/macOS always fine
            }
            catch
            {
                return false;
            }
        }

        public static void Success(string message) =>
            AnsiConsole.MarkupLine($"[green]  {Ok}  {Markup.Escape(message)}[/]");

        public static void Warning(string message) =>
            AnsiConsole.MarkupLine($"[yellow]  {Warn}  {Markup.Escape(message)}[/]");

        public static void Error(string message) =>
            AnsiConsole.MarkupLine($"[red]  {Err}  {Markup.Escape(message)}[/]");

        public static void Info(string message) =>
            AnsiConsole.MarkupLine($"[cyan]  {Arr}  {Markup.Escape(message)}[/]");

        public static void Label(string label, string value) =>
            AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(label),-12}[/] [white]{Markup.Escape(value)}[/]");

        public static void Divider() =>
            AnsiConsole.MarkupLine("[grey]  " + new string('-', 44) + "[/]");

        public static void BlankLine() =>
            AnsiConsole.WriteLine();

        public static void FileRow(string status, string path, bool isStaged, bool isUntracked)
        {
            var color = isUntracked ? "grey" : isStaged ? "green" : "yellow";
            AnsiConsole.MarkupLine($"[{color}]       {Markup.Escape(status)}  {Markup.Escape(path)}[/]");
        }

        public static bool Confirm(string message) =>
            AnsiConsole.Confirm($"[yellow]  {Warn}  {Markup.Escape(message)}[/]", defaultValue: false);

        public static string AskText(string prompt) =>
            AnsiConsole.Ask<string>($"[cyan]  {Markup.Escape(prompt)}[/]");
    }
}