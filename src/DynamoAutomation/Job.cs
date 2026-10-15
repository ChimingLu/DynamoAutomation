using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.DesignScript.Runtime;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Use these nodes inside the graph that Batch.RunGraph runs on each model.
    /// They read the job that started the current Revit session. When the graph runs
    /// normally (not from a batch) they return safe defaults so it can be tested by hand.
    /// </summary>
    public class Job
    {
        private Job() { }

        private static Dictionary<string, string> Values
        {
            get
            {
                string jobFile = Environment.GetEnvironmentVariable(JobContract.EnvironmentVariable);
                if (string.IsNullOrEmpty(jobFile) || !File.Exists(jobFile))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return KeyValueFile.Read(jobFile);
            }
        }

        private static string Get(string key, string fallback) =>
            Values.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;

        /// <summary>
        /// True when this Revit session was started by Batch.RunGraph.
        /// </summary>
        /// <returns>True inside a batch run.</returns>
        public static bool IsBatchRun()
        {
            return Values.ContainsKey(JobContract.KeyModelPath);
        }

        /// <summary>
        /// The model file this job is processing. Outside a batch it returns manualRunPath,
        /// e.g. wire Document.FilePath in so the same graph also works when run by hand.
        /// </summary>
        /// <param name="manualRunPath">Returned when the graph is not part of a batch.</param>
        /// <returns>Full path of the model.</returns>
        public static string ModelPath(string manualRunPath = "")
        {
            return Get(JobContract.KeyModelPath, manualRunPath ?? "");
        }

        /// <summary>
        /// The batch output folder. Outside a batch: a DynamoAutomation folder in the user's temp folder.
        /// </summary>
        /// <returns>Folder path (created if missing).</returns>
        public static string OutputDirectory()
        {
            string dir = Get(JobContract.KeyOutputDirectory, Path.Combine(Path.GetTempPath(), "DynamoAutomation"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Position of this model in the batch (0-based; 0 when not in a batch).
        /// </summary>
        /// <returns>Job index.</returns>
        public static int Index()
        {
            return int.TryParse(Get(JobContract.KeyJobIndex, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;
        }

        /// <summary>
        /// Reads a value passed through parameterKeys / parameterValues of Batch.RunGraph.
        /// </summary>
        /// <param name="key">Parameter name.</param>
        /// <param name="defaultValue">Returned when the parameter was not given (or not in a batch).</param>
        /// <returns>The parameter value as text.</returns>
        public static string Parameter(string key, string defaultValue = "")
        {
            return Get(JobContract.ParameterPrefix + key, defaultValue);
        }

        /// <summary>
        /// Put this at the very end of the worker graph. In a batch run it saves the model as
        /// Batch.RunGraph's saveMode asks, reports the result to the batch and closes Revit.
        /// Connect the graph's last results to waitFor so Dynamo runs everything else first.
        /// Outside a batch it does nothing, so the graph can be tested safely by hand.
        /// </summary>
        /// <param name="waitFor">Connect the final outputs of the graph here (any value or list; a list is taken whole, Finish runs once).</param>
        /// <param name="message">Optional text to store in the batch summary for this model.</param>
        /// <returns>"not in batch" when run by hand; in a batch Revit closes before it returns.</returns>
        public static string Finish([ArbitraryDimensionArrayImport] object waitFor, string message = "")
        {
            if (!IsBatchRun()) return "not in batch: nothing saved, Revit stays open";

            var values = Values;
            string status = "ok", title = "", savedPath = "";
            try
            {
                object doc = RevitBridge.CurrentDocument();
                title = RevitBridge.Title(doc);
                string mode = Get(JobContract.KeySaveMode, JobContract.SaveNone);
                if (mode != JobContract.SaveNone)
                {
                    RevitBridge.CloseDynamoTransaction();
                    if (mode == JobContract.SaveOverwrite)
                    {
                        if (RevitBridge.IsWorkshared(doc))
                            throw new InvalidOperationException("Overwrite is not supported for workshared models; use saveMode \"copy\" with openDetached.");
                        RevitBridge.Save(doc);
                        savedPath = RevitBridge.PathName(doc);
                    }
                    else
                    {
                        savedPath = Path.Combine(OutputDirectory(), "models", Path.GetFileName(ModelPath()));
                        RevitBridge.SaveAs(doc, savedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                status = "failed";
                Exception inner = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                message = (string.IsNullOrEmpty(message) ? "" : message + " | ") + inner.Message;
            }

            Log($"Finish: {status} {message}".Trim());
            if (values.TryGetValue(JobContract.KeyResultFile, out string resultFile))
            {
                KeyValueFile.Write(resultFile, new[]
                {
                    new KeyValuePair<string, string>(JobContract.KeyStatus, status),
                    new KeyValuePair<string, string>(JobContract.KeyMessage, message ?? ""),
                    new KeyValuePair<string, string>(JobContract.KeyDocument, title),
                    new KeyValuePair<string, string>(JobContract.KeySavedPath, savedPath),
                    new KeyValuePair<string, string>(JobContract.KeyFinishedAt, DateTime.Now.ToString("s", CultureInfo.InvariantCulture)),
                });
            }
            // Ending the process (instead of closing through the UI) avoids save prompts that would stop the batch.
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return status;
        }

        /// <summary>
        /// Writes a line to this job's log (or to DynamoAutomation_log.txt in the output folder
        /// when not in a batch) and passes the message through.
        /// </summary>
        /// <param name="message">Text to log.</param>
        /// <returns>The message, unchanged.</returns>
        public static string Log(string message)
        {
            string logFile = Get(JobContract.KeyLogFile, Path.Combine(OutputDirectory(), "DynamoAutomation_log.txt"));
            KeyValueFile.AppendShared(logFile,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
            return message;
        }
    }
}
