using System.Diagnostics;

namespace GitNinja.Core
{
    public class GitResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public int ExitCode { get; set; }
    }

    public class GitRunner
    {
        private readonly string _workingDirectory;

        public GitRunner(string? workingDirectory = null)
        {
            _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        }

        public GitResult Run(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = _workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new GitResult
            {
                Success = process.ExitCode == 0,
                Output = output.Trim(),
                Error = error.Trim(),
                ExitCode = process.ExitCode
            };
        }

        public bool IsGitRepository()
        {
            var result = Run("rev-parse --is-inside-work-tree");
            return result.Success;
        }
    }
}