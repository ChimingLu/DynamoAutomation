using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DynamoAutomation.Internal
{
    /// <summary>
    /// Reads and writes the simple "key=value" text format shared by config files,
    /// job files and result files.
    /// </summary>
    internal static class KeyValueFile
    {
        public static Dictionary<string, string> Read(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in ReadAllLinesShared(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        public static void Write(string path, IEnumerable<KeyValuePair<string, string>> values)
        {
            var sb = new StringBuilder();
            foreach (var kv in values)
            {
                string value = (kv.Value ?? "").Replace("\r", " ").Replace("\n", " ");
                sb.Append(kv.Key).Append('=').Append(value).Append("\r\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Reads a file that another process may still have open.</summary>
        public static IEnumerable<string> ReadAllLinesShared(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }

        /// <summary>Appends text, retrying briefly if another Revit process holds the file.</summary>
        public static void AppendShared(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(stream.Length == 0)))
                    {
                        writer.Write(text);
                    }
                    return;
                }
                catch (IOException) when (attempt < 50)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
