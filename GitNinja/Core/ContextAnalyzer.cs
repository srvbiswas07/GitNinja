namespace GitNinja.Core
{
    public sealed class RepositoryContext
    {
        public string CurrentBranch { get; init; } = string.Empty;
        public bool IsDetachedHead { get; init; }
        public IReadOnlyList<ChangedFile> ChangedFiles { get; init; } = Array.Empty<ChangedFile>();
        public bool HasUncommittedChanges { get; init; }
        public int? CommitsAhead { get; init; }
        public int? CommitsBehind { get; init; }
        public bool HasUpstream => CommitsAhead.HasValue && CommitsBehind.HasValue;
        public bool IsAheadOfOrigin => CommitsAhead is > 0;
        public bool IsBehindOrigin => CommitsBehind is > 0;
    }

    public sealed class ChangedFile
    {
        public string Path { get; init; } = string.Empty;
        public string? RenamedTo { get; init; }
        public string StatusCode { get; init; } = "??";
        public string StatusDescription { get; init; } = string.Empty;
        public bool IsStaged { get; init; }
        public bool IsUnstaged { get; init; }
        public bool IsUntracked { get; init; }
    }

    public sealed class ContextAnalyzer
    {
        internal readonly GitRunner _runner;

        public ContextAnalyzer(GitRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public RepositoryContext Analyze()
        {
            var (branch, isDetached) = GetCurrentBranch();
            var changedFiles = GetChangedFiles();
            var (ahead, behind) = GetAheadBehind();

            return new RepositoryContext
            {
                CurrentBranch = branch,
                IsDetachedHead = isDetached,
                ChangedFiles = changedFiles,
                HasUncommittedChanges = changedFiles.Count > 0,
                CommitsAhead = ahead,
                CommitsBehind = behind
            };
        }

        public (string Branch, bool IsDetached) GetCurrentBranch()
        {
            var symRef = _runner.Run("symbolic-ref --quiet HEAD");
            if (symRef.Success)
            {
                var branch = symRef.Output
                    .Trim()
                    .Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
                return (branch, false);
            }

            var revParse = _runner.Run("rev-parse --short HEAD");
            var sha = revParse.Success ? revParse.Output.Trim() : "UNKNOWN";
            return ($"HEAD detached at {sha}", true);
        }

        public IReadOnlyList<ChangedFile> GetChangedFiles()
        {
            var result = _runner.Run("status --porcelain");

            if (!result.Success)
                return Array.Empty<ChangedFile>();

            if (string.IsNullOrWhiteSpace(result.Output))
                return Array.Empty<ChangedFile>();

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParsePorcelainLine)
                .Where(f => f is not null)
                .Cast<ChangedFile>()
                .ToList()
                .AsReadOnly();
        }

        public (int? Ahead, int? Behind) GetAheadBehind()
        {
            var result = _runner.Run("rev-list --count --left-right @{u}...HEAD");

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return (null, null);

            var parts = result.Output.Trim().Split('\t');
            if (parts.Length != 2) return (null, null);

            if (int.TryParse(parts[1], out var ahead) &&
                int.TryParse(parts[0], out var behind))
                return (ahead, behind);

            return (null, null);
        }

        private static ChangedFile? ParsePorcelainLine(string line)
        {
            if (line.Length < 4) return null;

            var x = line[0];
            var y = line[1];
            var xy = line[..2];
            var pathSection = line[3..];

            string path;
            string? renamedTo = null;

            var arrowIndex = pathSection.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = pathSection[..arrowIndex].Trim();
                renamedTo = pathSection[(arrowIndex + 4)..].Trim();
            }
            else
            {
                path = pathSection.Trim();
            }

            var isUntracked = xy == "??";
            var isStaged = !isUntracked && x != ' ' && x != '?';
            var isUnstaged = !isUntracked && y != ' ' && y != '?';

            return new ChangedFile
            {
                Path = path,
                RenamedTo = renamedTo,
                StatusCode = xy,
                StatusDescription = DescribeStatus(x, y),
                IsStaged = isStaged,
                IsUnstaged = isUnstaged,
                IsUntracked = isUntracked
            };
        }

        private static string DescribeStatus(char x, char y)
        {
            if (x == '?' && y == '?') return "Untracked";
            if (x == '!' && y == '!') return "Ignored";

            var parts = new List<string>(2);
            if (x != ' ' && x != '?') parts.Add($"Staged {IndexLabel(x)}");
            if (y != ' ' && y != '?') parts.Add($"Unstaged {WorktreeLabel(y)}");

            return parts.Count > 0 ? string.Join(", ", parts) : "Unknown";
        }

        private static string IndexLabel(char c) => c switch
        {
            'A' => "addition",
            'M' => "modification",
            'D' => "deletion",
            'R' => "rename",
            'C' => "copy",
            'U' => "unmerged",
            _ => $"change ({c})"
        };

        private static string WorktreeLabel(char c) => c switch
        {
            'M' => "modification",
            'D' => "deletion",
            'U' => "unmerged",
            _ => $"change ({c})"
        };
    }
}