using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Creates Revit journal files that open a model and run a Dynamo graph on it.
    /// Most users should use Batch.RunGraph, which writes these journals for you.
    /// </summary>
    public class Journal
    {
        private Journal() { }

        /// <summary>
        /// Creates a journal that opens a Revit file and runs a Dynamo graph on it, by default
        /// without showing the Dynamo window. The journal ends there: Dynamo runs the graph on Revit's
        /// idle loop, so the graph itself must end with Job.Finish, which saves (optional)
        /// and closes Revit.
        /// </summary>
        /// <param name="revitFilePath">The .rvt or .rfa file to open.</param>
        /// <param name="workspacePath">The Dynamo graph (.dyn) to run on the model.</param>
        /// <param name="journalFilePath">Where to write the journal (.txt).</param>
        /// <param name="openDetached">Detach a workshared model from central when opening.</param>
        /// <param name="discardWorksets">When detaching, discard worksets.</param>
        /// <param name="showDynamo">Show the Dynamo window. Required for Revit 2027+, whose Dynamo only runs a journal graph with its window open.</param>
        /// <returns>The path of the journal file.</returns>
        public static string ByWorkspacePath(string revitFilePath, string workspacePath, string journalFilePath,
            bool openDetached = false, bool discardWorksets = false, bool showDynamo = false)
        {
            var sb = new StringBuilder();
            AppendStart(sb);
            AppendOpen(sb, revitFilePath, openDetached, discardWorksets);
            AppendRunDynamo(sb, workspacePath, showDynamo);
            AnsiText.WriteFile(journalFilePath, sb.ToString());
            return journalFilePath;
        }

        /// <summary>
        /// Builds the Revit command-line arguments that play back a journal.
        /// </summary>
        /// <param name="journalFilePath">The journal to play back.</param>
        /// <param name="noSplash">Hide the Revit splash screen.</param>
        /// <returns>The arguments to pass to Revit.exe.</returns>
        public static string StartupArguments(string journalFilePath, bool noSplash = true)
        {
            return "\"" + Path.GetFullPath(journalFilePath) + "\"" + (noSplash ? " /nosplash" : "");
        }

        /// <summary>
        /// Moves journal files to a folder (for example an archive folder after a batch).
        /// </summary>
        /// <param name="journalFilePaths">Journal files to move.</param>
        /// <param name="destinationDirectory">Target folder; created if missing.</param>
        /// <param name="overwrite">Overwrite files that already exist in the target folder.</param>
        /// <returns>The new paths.</returns>
        public static List<string> MoveFiles(List<string> journalFilePaths, string destinationDirectory, bool overwrite = true)
        {
            Directory.CreateDirectory(destinationDirectory);
            var moved = new List<string>();
            foreach (string source in journalFilePaths)
            {
                if (!File.Exists(source)) continue;
                string target = Path.Combine(destinationDirectory, Path.GetFileName(source));
                if (File.Exists(target))
                {
                    if (!overwrite) continue;
                    File.Delete(target);
                }
                File.Move(source, target);
                moved.Add(target);
            }
            return moved;
        }

        // ---- journal building blocks --------------------------------------------------

        internal static void AppendStart(StringBuilder sb)
        {
            sb.Append("'\r\n");
            sb.Append(JobContract.JournalMarker).Append("\r\n");
            sb.Append("Dim Jrn\r\n");
            sb.Append("Set Jrn = CrsJournalScript\r\n");
            // Answer error dialogs automatically and keep going past journal mismatches.
            sb.Append("Jrn.Directive \"DebugMode\", \"PerformAutomaticActionInErrorDialog\", 1\r\n");
            sb.Append("Jrn.Directive \"DebugMode\", \"PermissiveJournal\", 1\r\n");
        }

        internal static void AppendOpen(StringBuilder sb, string revitFilePath, bool openDetached, bool discardWorksets)
        {
            string path = CheckedPath(revitFilePath, nameof(revitFilePath));
            // Same sequence Revit 2020-2027 record for File > Open.
            sb.Append("Jrn.Command \"Ribbon\" , \"Open an existing project , ID_REVIT_FILE_OPEN\"\r\n");
            if (openDetached)
                sb.Append("Jrn.Data \"FileOpenSubDialog\" , \"DetachCheckBox\", \"True\"\r\n");
            sb.Append("Jrn.Data \"File Name\" , \"IDOK\" , \"").Append(path).Append("\"\r\n");
            sb.Append("Jrn.Data \"WorksetConfig\" , \"Custom\" , 0\r\n");
            if (openDetached)
            {
                string text = discardWorksets ? "Detach and discard worksets" : "Detach and preserve worksets";
                string id = discardWorksets ? "1002" : "1001";
                sb.Append("Jrn.Data \"TaskDialogResult\" , \"Detaching this model will create an independent model. ")
                  .Append("You will be unable to synchronize your changes with the original central model.\" & vbLf & ")
                  .Append("\"What do you want to do?\", \"").Append(text).Append("\", \"").Append(id).Append("\"\r\n");
            }
        }

        internal static void AppendRunDynamo(StringBuilder sb, string workspacePath, bool showDynamo)
        {
            string path = CheckedPath(workspacePath, nameof(workspacePath));
            // DynamoRevit starts and runs the graph on Revit's idle loop after this command
            // returns. Any journal line after it would run before the graph, so the journal
            // ends here and the graph closes Revit with Job.Finish.
            sb.Append("Jrn.Command \"Ribbon\" , \"Launch Dynamo, ID_VISUAL_PROGRAMMING_DYNAMO\"\r\n");
            sb.Append("Jrn.Data \"APIStringStringMapJournalData\", 5, ")
              .Append("\"dynPath\", \"").Append(path).Append("\", ")
              .Append("\"dynShowUI\", \"").Append(showDynamo ? "true" : "false").Append("\", ")
              .Append("\"dynAutomation\", \"false\", ")
              .Append("\"dynPathExecute\", \"true\", ")
              .Append("\"dynModelShutDown\", \"true\"\r\n");
        }


        private static string CheckedPath(string path, string paramName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A file path is required.", paramName);
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException($"File not found: {full}", full);
            if (full.IndexOf('"') >= 0 || full.IndexOf('\n') >= 0 || full.IndexOf('\r') >= 0)
                throw new ArgumentException($"The path [{full}] contains characters that cannot be written into a journal.", paramName);
            AnsiText.Encode(full); // fails early with a clear message if the code page cannot hold the path
            return full;
        }
    }
}
