using GitNinja.Core;

namespace GitNinja.Services
{
    public class SuggestionService
    {
        private readonly GitRunner _runner;

        public SuggestionService(GitRunner runner)
        {
            _runner = runner;
        }

        public string SuggestCommitMessage()
        {
            var result = _runner.Run("diff --cached --name-only");
            var files = ParseFiles(result.Output);

            if (files.Count == 0)
            {
                var unstaged = _runner.Run("diff --name-only");
                files = ParseFiles(unstaged.Output);
            }

            if (files.Count == 0) return "Update project files";
            if (files.Count == 1) return BuildFromSingleFile(files[0]);

            var folders = files
                .Select(f => Path.GetDirectoryName(f)?.Replace("\\", "/"))
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .ToList();

            if (folders.Count == 1) return $"Update {folders[0]} module";

            var names = files
                .Select(f => Path.GetFileNameWithoutExtension(f).ToLower())
                .ToList();

            if (names.Any(n => n.Contains("test"))) return "Add or update tests";
            if (names.Any(n => n.Contains("config"))) return "Update configuration";
            if (names.Any(n => n.Contains("readme"))) return "Update documentation";

            return $"Update {files.Count} files";
        }

        private static List<string> ParseFiles(string output) =>
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                  .Select(f => f.Trim())
                  .Where(f => !string.IsNullOrEmpty(f))
                  .ToList();

        private static string BuildFromSingleFile(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath).ToLower();

            if (name.Contains("test")) return $"Add tests for {name.Replace("test", "").Trim()}";
            if (name.Contains("controller")) return $"Update {name.Replace("controller", "").Trim()} controller";
            if (name.Contains("service")) return $"Update {name.Replace("service", "").Trim()} service";
            if (name.Contains("model")) return $"Update {name.Replace("model", "").Trim()} model";
            if (name.Contains("config")) return "Update configuration";
            if (name.Contains("readme")) return "Update README";

            return $"Update {Path.GetFileName(filePath)}";
        }
    }
}