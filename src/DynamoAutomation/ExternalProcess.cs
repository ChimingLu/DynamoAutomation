using System;
using System.Diagnostics;
using System.IO;

namespace DynamoAutomation
{
    /// <summary>
    /// Starts external programs from a graph.
    /// </summary>
    public class ExternalProcess
    {
        private ExternalProcess() { }

        /// <summary>
        /// Runs a program and waits for it to exit.
        /// </summary>
        /// <param name="processPath">The program to run.</param>
        /// <param name="arguments">Command-line arguments.</param>
        /// <param name="timeoutMinutes">Stop the program after this many minutes (0 = wait forever).</param>
        /// <returns>The program's exit code (-1 if it was stopped after the timeout).</returns>
        public static int Run(string processPath, string arguments = "", int timeoutMinutes = 0)
        {
            if (!File.Exists(processPath))
                throw new FileNotFoundException($"Program not found: {processPath}", processPath);
            using (var process = System.Diagnostics.Process.Start(new ProcessStartInfo(processPath, arguments ?? "") { UseShellExecute = false }))
            {
                if (timeoutMinutes <= 0)
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
                if (process.WaitForExit((int)Math.Min(int.MaxValue, timeoutMinutes * 60_000L)))
                    return process.ExitCode;
                try { process.Kill(); } catch (InvalidOperationException) { }
                return -1;
            }
        }
    }
}
