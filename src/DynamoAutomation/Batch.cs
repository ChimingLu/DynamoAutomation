using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Runs one Dynamo graph on many Revit models, one Revit process per model.
    /// Run this from Dynamo Sandbox, DynamoCLI or any Dynamo for Revit session.
    /// </summary>
    public class Batch
    {
        private Batch() { }

        private static readonly Regex BackupFile = new Regex(@"\.\d{4}\.(rvt|rfa)$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Lists Revit files in a folder, skipping Revit backup copies such as model.0001.rvt.
        /// </summary>
        /// <param name="directory">Folder to search.</param>
        /// <param name="includeSubfolders">Also search subfolders.</param>
        /// <param name="includeFamilies">Include .rfa family files as well as .rvt projects.</param>
        /// <returns>Full paths of the Revit files, sorted by name.</returns>
        public static List<string> FindModels(string directory, bool includeSubfolders = false, bool includeFamilies = false)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Folder not found: {directory}");
            var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> files = Directory.EnumerateFiles(directory, "*.rvt", option);
            if (includeFamilies) files = files.Concat(Directory.EnumerateFiles(directory, "*.rfa", option));
            return files.Where(f => !BackupFile.IsMatch(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// Runs a Dynamo graph on each Revit model. Each model gets its own Revit process,
        /// opened through a journal file. The worker graph must end with Job.Finish, which
        /// saves (optional) and closes Revit. Dynamo waits until all models are done.
        /// Inside the worker graph, use the Job nodes to read the current model, output folder and parameters.
        /// </summary>
        /// <param name="modelPaths">Revit files to process (.rvt / .rfa).</param>
        /// <param name="workspacePath">The Dynamo graph (.dyn) to run on every model.</param>
        /// <param name="outputDirectory">Folder for journals, logs, the batch summary CSV and saved copies.</param>
        /// <param name="revitVersion">Revit version to run, e.g. 2024. The models are upgraded in memory if older.</param>
        /// <param name="saveMode">"none" (do not save), "copy" (save a copy into outputDirectory\models) or "overwrite" (save the original file).</param>
        /// <param name="openDetached">Detach workshared models from central when opening (recommended for central models).</param>
        /// <param name="maxParallel">How many Revit processes may run at the same time.</param>
        /// <param name="timeoutMinutes">Stop a Revit process that runs longer than this (0 = no limit).</param>
        /// <param name="parameterKeys">Optional names of values to pass to the graph (read them with Job.Parameter).</param>
        /// <param name="parameterValues">Values matching parameterKeys.</param>
        /// <param name="run">Set to true to start the batch. While false the node only checks the inputs.</param>
        /// <returns>models, status, messages, durationSeconds and the path of the summary CSV.</returns>
        public static Dictionary<string, object> RunGraph(List<string> modelPaths, string workspacePath, string outputDirectory,
            object revitVersion, string saveMode = "none", bool openDetached = false, int maxParallel = 1, int timeoutMinutes = 60,
            List<string> parameterKeys = null, List<string> parameterValues = null, bool run = false)
        {
            int version = Versions.ParseRevitVersion(revitVersion);
            string revitExe = RevitInstall.ExecutablePath(version);
            bool showDynamo = RevitInstall.DynamoNeedsWindowForJournal(version);
            saveMode = NormalizeSaveMode(saveMode);
            if (openDetached && saveMode == JobContract.SaveOverwrite)
                throw new ArgumentException("A detached model cannot be saved over its central file. Use saveMode \"copy\".", nameof(saveMode));
            if (modelPaths == null || modelPaths.Count == 0)
                throw new ArgumentException("No models given.", nameof(modelPaths));
            if (!File.Exists(workspacePath))
                throw new FileNotFoundException($"Dynamo graph not found: {workspacePath}", workspacePath);
            var parameters = PairParameters(parameterKeys, parameterValues);
            maxParallel = Math.Max(1, maxParallel);

            var models = modelPaths.Select(Path.GetFullPath).ToList();
            var jobs = models.Select((m, i) => new JobRun { Index = i, ModelPath = m }).ToList();

            string trustProblem = RevitInstall.UntrustedGraphProblem(version, workspacePath);
            if (!run)
            {
                foreach (var job in jobs)
                {
                    job.Status = !File.Exists(job.ModelPath) ? "missing" : trustProblem != null ? "blocked" : "ready";
                    job.Message = !File.Exists(job.ModelPath) ? "Model file not found."
                        : trustProblem ?? "Ready. Set run to true to start.";
                }
                return ToResult(jobs, null, 0);
            }
            if (trustProblem != null) throw new InvalidOperationException(trustProblem);

            // Python nodes need an engine that exists in the target Dynamo (see WorkerGraph).
            string graphToRun = WorkerGraph.PrepareForRevit(Path.GetFullPath(workspacePath), version, out string engineNote);

            string batchId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string batchDir = Path.Combine(Path.GetFullPath(outputDirectory), "_batches", batchId);
            Directory.CreateDirectory(batchDir);
            string summaryFile = Path.Combine(Path.GetFullPath(outputDirectory), $"batch_{batchId}.csv");
            Report.AppendCsvRow(summaryFile,
                new List<object> { "index", "model", "status", "seconds", "savedPath", "message" });
            if (engineNote.Length > 0)
                Report.AppendLine(Path.Combine(batchDir, "batch_log.txt"), engineNote);

            var clock = Stopwatch.StartNew();
            using (var gate = new SemaphoreSlim(maxParallel))
            {
                var tasks = jobs.Select(job => Task.Run(() =>
                {
                    gate.Wait();
                    try
                    {
                        RunOne(job, jobs.Count, batchId, batchDir, revitExe, showDynamo, graphToRun,
                            Path.GetFullPath(outputDirectory), saveMode, openDetached, timeoutMinutes, parameters);
                    }
                    catch (Exception ex)
                    {
                        job.Status = "error";
                        job.Message = ex.Message;
                    }
                    finally
                    {
                        gate.Release();
                    }
                    Report.AppendCsvRow(summaryFile, new List<object>
                    {
                        job.Index, job.ModelPath, job.Status, Math.Round(job.Seconds, 1),
                        job.SavedPath, job.Message
                    });
                })).ToArray();
                Task.WaitAll(tasks);
            }

            return ToResult(jobs, summaryFile, clock.Elapsed.TotalSeconds);
        }

        // ---- one model ----------------------------------------------------------------

        private sealed class JobRun
        {
            public int Index;
            public string ModelPath;
            public string Status = "pending";
            public string Message = "";
            public double Seconds;
            public string SavedPath = "";
        }

        private static void RunOne(JobRun job, int jobCount, string batchId, string batchDir, string revitExe,
            bool showDynamo, string workspacePath, string outputDirectory, string saveMode, bool openDetached, int timeoutMinutes,
            List<KeyValuePair<string, string>> parameters)
        {
            if (!File.Exists(job.ModelPath))
            {
                job.Status = "missing";
                job.Message = "Model file not found.";
                return;
            }

            string name = $"{job.Index + 1:000}_{SafeName(Path.GetFileNameWithoutExtension(job.ModelPath))}";
            string jobDir = Path.Combine(batchDir, name);
            Directory.CreateDirectory(jobDir);
            string jobFile = Path.Combine(jobDir, "job.txt");
            string resultFile = Path.Combine(jobDir, "result.txt");
            string logFile = Path.Combine(jobDir, "log.txt");
            string journalFile = Path.Combine(jobDir, "journal.txt");

            var values = new List<KeyValuePair<string, string>>
            {
                Pair(JobContract.KeyJobId, name),
                Pair(JobContract.KeyBatchId, batchId),
                Pair(JobContract.KeyJobIndex, job.Index.ToString(CultureInfo.InvariantCulture)),
                Pair(JobContract.KeyJobCount, jobCount.ToString(CultureInfo.InvariantCulture)),
                Pair(JobContract.KeyModelPath, job.ModelPath),
                Pair(JobContract.KeyWorkspacePath, workspacePath),
                Pair(JobContract.KeyOutputDirectory, outputDirectory),
                Pair(JobContract.KeySaveMode, saveMode),
                Pair(JobContract.KeyResultFile, resultFile),
                Pair(JobContract.KeyLogFile, logFile),
            };
            values.AddRange(parameters.Select(p => Pair(JobContract.ParameterPrefix + p.Key, p.Value)));
            KeyValueFile.Write(jobFile, values);
            Journal.ByWorkspacePath(job.ModelPath, workspacePath, journalFile, openDetached, false, showDynamo);

            var start = new ProcessStartInfo(revitExe, Journal.StartupArguments(journalFile))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(revitExe),
            };
            start.EnvironmentVariables[JobContract.EnvironmentVariable] = jobFile;

            var clock = Stopwatch.StartNew();
            for (int attempt = 1; ; attempt++)
            {
                var attemptClock = Stopwatch.StartNew();
                bool exited;
                WaitForLaunchSlot();
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    exited = timeoutMinutes > 0
                        ? process.WaitForExit((int)Math.Min(int.MaxValue, timeoutMinutes * 60_000L))
                        : WaitForever(process);
                    if (!exited)
                        try { process.Kill(); } catch (InvalidOperationException) { }
                }
                MarkLaunchEvent();
                job.Seconds = clock.Elapsed.TotalSeconds;
                if (!exited)
                {
                    job.Status = "timeout";
                    job.Message = $"Revit did not finish within {timeoutMinutes} minutes and was stopped. " +
                        "Check that the worker graph ends with Job.Finish and opens without errors.";
                    return;
                }
                // Revit that dies this early never got as far as running the graph (usually two
                // Revit processes starting or closing at the same moment): start it once more.
                if (File.Exists(resultFile) || attempt > 1 || attemptClock.Elapsed > EarlyExit)
                    break;
                Report.AppendLine(Path.Combine(batchDir, "batch_log.txt"),
                    $"{name}: Revit exited after {attemptClock.Elapsed.TotalSeconds:0}s without a result, retrying once.");
            }

            if (!File.Exists(resultFile))
            {
                job.Status = "failed";
                job.Message = "Revit closed without reporting a result (did the worker graph reach Job.Finish?). See the Revit journal in " + jobDir;
                return;
            }

            var result = KeyValueFile.Read(resultFile);
            job.Status = Get(result, JobContract.KeyStatus, "unknown");
            job.Message = Get(result, JobContract.KeyMessage, "");
            job.SavedPath = Get(result, JobContract.KeySavedPath, "");
        }

        // Revit fails at startup when another Revit starts or closes at the same moment (shared
        // journal / cache files), so parallel jobs keep a gap between those events.
        private static readonly TimeSpan LaunchGap = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan EarlyExit = TimeSpan.FromSeconds(60);
        private static readonly object LaunchLock = new object();
        private static DateTime lastLaunchEvent = DateTime.MinValue;

        private static void WaitForLaunchSlot()
        {
            lock (LaunchLock)
            {
                TimeSpan wait = lastLaunchEvent + LaunchGap - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    Thread.Sleep(wait);
                lastLaunchEvent = DateTime.UtcNow;
            }
        }

        private static void MarkLaunchEvent()
        {
            lock (LaunchLock)
                lastLaunchEvent = DateTime.UtcNow;
        }

        private static bool WaitForever(System.Diagnostics.Process process)
        {
            process.WaitForExit();
            return true;
        }

        // ---- helpers ------------------------------------------------------------------

        private static Dictionary<string, object> ToResult(List<JobRun> jobs, string summaryFile, double seconds)
        {
            return new Dictionary<string, object>
            {
                { "models", jobs.Select(j => j.ModelPath).ToList() },
                { "status", jobs.Select(j => j.Status).ToList() },
                { "messages", jobs.Select(j => j.Message).ToList() },
                { "durationSeconds", Math.Round(seconds, 1) },
                { "summaryFile", summaryFile ?? "" },
            };
        }

        private static string NormalizeSaveMode(string saveMode)
        {
            string mode = (saveMode ?? JobContract.SaveNone).Trim().ToLowerInvariant();
            if (mode == "" || mode == "false" || mode == "no") mode = JobContract.SaveNone;
            if (mode == "true" || mode == "yes" || mode == "save") mode = JobContract.SaveOverwrite;
            if (mode != JobContract.SaveNone && mode != JobContract.SaveCopy && mode != JobContract.SaveOverwrite)
                throw new ArgumentException("saveMode must be \"none\", \"copy\" or \"overwrite\".", nameof(saveMode));
            return mode;
        }

        private static List<KeyValuePair<string, string>> PairParameters(List<string> keys, List<string> values)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (keys == null) return list;
            if (values == null || values.Count != keys.Count)
                throw new ArgumentException("parameterKeys and parameterValues must have the same number of items.", "parameterValues");
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(keys[i]) || keys[i].Contains("="))
                    throw new ArgumentException($"Invalid parameter name [{keys[i]}].", "parameterKeys");
                list.Add(Pair(keys[i].Trim(), Convert.ToString(values[i], CultureInfo.InvariantCulture)));
            }
            return list;
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        private static string Get(Dictionary<string, string> values, string key, string fallback) =>
            values.TryGetValue(key, out string v) ? v : fallback;

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }
    }
}
