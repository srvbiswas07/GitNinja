using GitNinja.Core;

namespace GitNinja.Services
{
    public class SafetyCheckResult
    {
        public bool IsSafe { get; set; }
        public string? WarningMessage { get; set; }
    }

    public class SafetyService
    {
        private readonly ContextAnalyzer _analyzer;

        public static readonly string[] ProtectedBranches =
            { "main", "master", "develop", "dev" };

        public SafetyService(ContextAnalyzer analyzer)
        {
            _analyzer = analyzer;
        }

        public SafetyCheckResult CheckCommitSafety()
        {
            var context = _analyzer.Analyze();

            if (ProtectedBranches.Contains(context.CurrentBranch.ToLower()))
                return new SafetyCheckResult
                {
                    IsSafe = false,
                    WarningMessage = $"You are on protected branch '{context.CurrentBranch}'.\n" +
                                     "Run 'gitninja start' to create a new branch first."
                };

            if (context.IsDetachedHead)
                return new SafetyCheckResult
                {
                    IsSafe = false,
                    WarningMessage = "HEAD is detached. Checkout a branch before committing."
                };

            return new SafetyCheckResult { IsSafe = true };
        }

        public bool IsProtectedBranch(string branchName) =>
            ProtectedBranches.Contains(branchName.ToLower());
    }
}