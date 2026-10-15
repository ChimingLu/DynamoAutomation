using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Appends results to shared text/CSV files. Safe to call from several Revit
    /// processes of the same batch: writes are retried while another process holds the file.
    /// </summary>
    public class Report
    {
        private Report() { }

        /// <summary>
        /// Appends one line of text to a file (created if missing).
        /// </summary>
        /// <param name="filePath">Text file to append to.</param>
        /// <param name="line">Line to append.</param>
        /// <returns>The file path.</returns>
        public static string AppendLine(string filePath, string line)
        {
            KeyValueFile.AppendShared(filePath, (line ?? "") + Environment.NewLine);
            return filePath;
        }

        /// <summary>
        /// Appends one CSV row (UTF-8 with BOM so Excel shows Chinese text correctly).
        /// When the file is new and a header is given, the header is written first.
        /// </summary>
        /// <param name="filePath">CSV file to append to.</param>
        /// <param name="values">The row's values.</param>
        /// <param name="header">Optional column names, written only when the file does not exist yet.</param>
        /// <returns>The file path.</returns>
        public static string AppendCsvRow(string filePath, List<object> values, List<string> header = null)
        {
            string text = "";
            if (header != null && header.Count > 0 && !File.Exists(filePath))
                text += ToCsv(header.Cast<object>()) + "\r\n";
            text += ToCsv(values ?? new List<object>()) + "\r\n";
            KeyValueFile.AppendShared(filePath, text);
            return filePath;
        }

        private static string ToCsv(IEnumerable<object> values)
        {
            return string.Join(",", values.Select(v =>
            {
                string s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            }));
        }
    }
}
