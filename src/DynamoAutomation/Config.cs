using System.Collections.Generic;
using System.IO;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Reads simple configuration files: one "key=value" per line, # starts a comment.
    /// Keeps project-specific paths out of the graph so the same graph can be reused.
    /// </summary>
    public class Config
    {
        private Config() { }

        /// <summary>
        /// Reads all key=value pairs from a configuration file.
        /// </summary>
        /// <param name="filePath">The configuration file.</param>
        /// <returns>A dictionary of the values (keys are not case-sensitive when read with Config.Value).</returns>
        public static Dictionary<string, string> Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}", filePath);
            return new Dictionary<string, string>(KeyValueFile.Read(filePath));
        }

        /// <summary>
        /// Reads one value from a configuration file.
        /// </summary>
        /// <param name="filePath">The configuration file.</param>
        /// <param name="key">The key to read (not case-sensitive).</param>
        /// <param name="defaultValue">Returned when the key is missing.</param>
        /// <returns>The value as text.</returns>
        public static string Value(string filePath, string key, string defaultValue = "")
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}", filePath);
            return KeyValueFile.Read(filePath).TryGetValue(key, out string v) ? v : defaultValue;
        }

        /// <summary>
        /// Makes a path from a config file absolute: relative paths are resolved against rootDirectory.
        /// </summary>
        /// <param name="rootDirectory">Folder that relative paths start from (often the config file's folder).</param>
        /// <param name="path">Absolute or relative path.</param>
        /// <returns>The absolute path.</returns>
        public static string ResolvePath(string rootDirectory, string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(rootDirectory, path));
        }
    }
}
