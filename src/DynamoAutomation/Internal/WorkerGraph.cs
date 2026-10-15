using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamoAutomation.Internal
{
    /// <summary>
    /// Makes a worker graph's Python nodes runnable in the target Dynamo.
    /// No single Python engine exists in every Dynamo for Revit 2020-2027:
    ///   Dynamo 2.3-2.6  IronPython2 only          Dynamo 2.13-3.x  CPython3
    ///   Dynamo 2.7-2.12 IronPython2 + CPython3    Dynamo 4.x       PythonNet3
    /// A Python node saved without an "Engine" is read as IronPython2, so a graph authored in
    /// Dynamo 2.x fails silently in 3.x/4.x. When the graph needs it, a copy with the engine set
    /// is written to a "_DynamoAutomation" subfolder next to the graph (inside the same trusted
    /// location) and that copy is used for the batch. The Python code itself must be written to
    /// run under every engine it may be switched to.
    /// </summary>
    internal static class WorkerGraph
    {
        private const string IronPython2 = "IronPython2", CPython3 = "CPython3", PythonNet3 = "PythonNet3";

        private static readonly Regex EngineValue = new Regex("\"Engine\"\\s*:\\s*\"(?<engine>[A-Za-z0-9]+)\"");

        public static string PrepareForRevit(string workspacePath, int revitVersion, out string note)
        {
            note = "";
            if (!Version.TryParse(RevitInstall.DynamoVersion(revitVersion), out Version dynamo)) return workspacePath;

            string[] available = AvailableEngines(dynamo);
            string target = available[0];
            string text = File.ReadAllText(workspacePath, Encoding.UTF8);

            // 1. Python nodes without an Engine are IronPython2 to Dynamo; give them one when needed.
            int missing = 0;
            if (!available.Contains(IronPython2))
            {
                foreach (int brace in PythonNodesWithoutEngine(text).OrderByDescending(i => i))
                {
                    text = text.Insert(brace + 1, "\r\n      \"Engine\": \"" + target + "\",");
                    missing++;
                }
            }

            // 2. Explicit engines this Dynamo does not have.
            int switched = 0;
            text = EngineValue.Replace(text, m =>
            {
                if (available.Contains(m.Groups["engine"].Value)) return m.Value;
                switched++;
                return "\"Engine\": \"" + target + "\"";
            });

            if (missing + switched == 0) return workspacePath;

            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspacePath)), "_DynamoAutomation");
            string copy = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(workspacePath)}.Dynamo{dynamo.Major}.{dynamo.Minor}.{target}.dyn");
            Directory.CreateDirectory(folder);
            File.WriteAllText(copy, text, new UTF8Encoding(false));
            note = $"{missing + switched} Python node(s) set to {target} for Dynamo {dynamo.Major}.{dynamo.Minor}: {copy}";
            return copy;
        }

        private static string[] AvailableEngines(Version dynamo)
        {
            // First entry = engine to switch to.
            if (dynamo.Major >= 4) return new[] { PythonNet3 };
            if (dynamo.Major == 3 || dynamo.Minor >= 13) return new[] { CPython3 };
            if (dynamo.Minor >= 7) return new[] { CPython3, IronPython2 };
            return new[] { IronPython2 };
        }

        /// <summary>
        /// Positions of the '{' that opens each Python node object lacking an "Engine" key.
        /// A small string-aware scan of the JSON, so Python code inside the nodes cannot confuse it.
        /// </summary>
        private static List<int> PythonNodesWithoutEngine(string json)
        {
            var result = new List<int>();
            var stack = new Stack<(int start, bool python, bool engine)>();
            string lastKey = null;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    int end = i + 1;
                    var token = new StringBuilder();
                    while (end < json.Length && json[end] != '"')
                    {
                        if (json[end] == '\\') { token.Append(json[end + 1]); end += 2; continue; }
                        token.Append(json[end++]);
                    }
                    int next = end + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        if (next < json.Length && json[next] == ':')
                        {
                            lastKey = token.ToString();
                            if (lastKey == "Engine") top.engine = true;
                        }
                        else if (lastKey == "NodeType" && token.ToString() == "PythonScriptNode")
                        {
                            top.python = true;
                        }
                        stack.Push(top);
                    }
                    i = end;
                }
                else if (c == '{')
                {
                    stack.Push((i, false, false));
                    lastKey = null;
                }
                else if (c == '}' && stack.Count > 0)
                {
                    var obj = stack.Pop();
                    if (obj.python && !obj.engine) result.Add(obj.start);
                }
            }
            return result;
        }
    }
}
