using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using DynamoAutomation.Internal;

namespace DynamoAutomation
{
    /// <summary>
    /// Finds installed Revit versions and their folders.
    /// </summary>
    public class RevitInstall
    {
        private RevitInstall() { }

        private static string ProgramFilesAutodesk =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk");

        /// <summary>
        /// Lists the Revit versions installed on this computer (e.g. 2021, 2024, 2027).
        /// </summary>
        /// <returns>Installed Revit versions, oldest first.</returns>
        public static List<int> InstalledVersions()
        {
            var versions = new List<int>();
            for (int year = 2017; year <= 2040; year++)
            {
                if (File.Exists(Path.Combine(ProgramFilesAutodesk, "Revit " + year, "Revit.exe")))
                    versions.Add(year);
            }
            return versions;
        }

        /// <summary>
        /// Returns the path of Revit.exe for a Revit version.
        /// </summary>
        /// <param name="revitVersion">Revit version, e.g. 2024 (number or text).</param>
        /// <returns>Full path of Revit.exe.</returns>
        public static string ExecutablePath(object revitVersion)
        {
            int version = Versions.ParseRevitVersion(revitVersion);
            string path = Path.Combine(ProgramFilesAutodesk, "Revit " + version, "Revit.exe");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Revit {version} is not installed (looked for {path}).", path);
            return path;
        }

        /// <summary>
        /// The Dynamo version that ships with a Revit version, e.g. "2.6.1.8786" (empty if none).
        /// </summary>
        /// <param name="revitVersion">Revit version, e.g. 2024 (number or text).</param>
        /// <returns>The Dynamo for Revit version.</returns>
        public static string DynamoVersion(object revitVersion)
        {
            int version = Versions.ParseRevitVersion(revitVersion);
            string core = Path.Combine(ProgramFilesAutodesk, "Revit " + version, "AddIns", "DynamoForRevit", "DynamoCore.dll");
            return File.Exists(core) ? FileVersionInfo.GetVersionInfo(core).FileVersion ?? "" : "";
        }

        /// <summary>
        /// Folders that the Dynamo of a Revit version trusts (Preferences > Security > Trusted Locations).
        /// Dynamo 4.x does not run graphs from other folders in a batch, so keep worker graphs in one of these.
        /// </summary>
        /// <param name="revitVersion">Revit version, e.g. 2027 (number or text).</param>
        /// <returns>The trusted folders found in this user's Dynamo settings.</returns>
        public static List<string> DynamoTrustedLocations(object revitVersion)
        {
            var locations = new List<string>();
            foreach (string settings in DynamoSettingsFiles(Versions.ParseRevitVersion(revitVersion)))
            {
                var xml = new XmlDocument();
                xml.Load(settings);
                foreach (XmlNode node in xml.SelectNodes("/PreferenceSettings/TrustedLocations/string"))
                {
                    string path = node.InnerText.Trim();
                    if (path.Length > 0 && !locations.Contains(path, StringComparer.OrdinalIgnoreCase)) locations.Add(path);
                }
            }
            return locations;
        }

        /// <summary>
        /// Null when Dynamo will run the graph unattended; otherwise an explanation.
        /// Dynamo 4.x blocks graphs outside its trusted locations (observed with Revit 2027);
        /// Dynamo 2.x has no such check.
        /// </summary>
        internal static string UntrustedGraphProblem(int revitVersion, string workspacePath)
        {
            string dynamo = DynamoVersion(revitVersion);
            if (!Version.TryParse(dynamo, out Version v) || v.Major < 4) return null;
            if (!DynamoSettingsFiles(revitVersion).Any())
                return $"Dynamo {v.Major}.{v.Minor} has not been opened in Revit {revitVersion} yet, so its trusted locations are unknown. " +
                       $"Open Dynamo once in Revit {revitVersion}, then add this folder under Preferences > Security > Trusted Locations: {Path.GetDirectoryName(Path.GetFullPath(workspacePath))}";
            string folder = Path.GetDirectoryName(Path.GetFullPath(workspacePath)).TrimEnd('\\') + "\\";
            foreach (string trusted in DynamoTrustedLocations(revitVersion))
            {
                if (folder.StartsWith(Path.GetFullPath(trusted).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            return $"Dynamo {v.Major}.{v.Minor} in Revit {revitVersion} will not run graphs from untrusted folders. " +
                   $"In Revit {revitVersion} open Dynamo > Preferences > Security > Trusted Locations and add: {Path.GetDirectoryName(Path.GetFullPath(workspacePath))}";
        }

        private static IEnumerable<string> DynamoSettingsFiles(int revitVersion)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dynamo", "Dynamo Revit");
            var folders = new List<string>();
            // The user folder is keyed by the Dynamo for Revit version, which can differ from DynamoCore
            // (Revit 2026: DynamoCore 4.2 but folder "3.7"; Revit 2027: DynamoCore 4.0 but folder "27.0").
            if (Version.TryParse(DynamoRevitVersion(revitVersion), out Version ds)) folders.Add($"{ds.Major}.{ds.Minor}");
            if (Version.TryParse(DynamoVersion(revitVersion), out Version v)) folders.Add($"{v.Major}.{v.Minor}");
            return folders.Distinct().Select(f => Path.Combine(root, f, "DynamoSettings.xml")).Where(File.Exists);
        }

        /// <summary>
        /// Dynamo for Revit 27+ (Revit 2027) only opens and runs a journal's dynPath graph when
        /// dynShowUI is true; with false it starts Dynamo but never opens the graph.
        /// Earlier versions run the graph either way, so they keep the window hidden.
        /// </summary>
        internal static bool DynamoNeedsWindowForJournal(int revitVersion) =>
            Version.TryParse(DynamoRevitVersion(revitVersion), out Version v) ? v.Major >= 27 : revitVersion >= 2027;

        private static string DynamoRevitVersion(int revitVersion)
        {
            string ds = Path.Combine(ProgramFilesAutodesk, "Revit " + revitVersion, "AddIns", "DynamoForRevit", "Revit", "DynamoRevitDS.dll");
            return File.Exists(ds) ? FileVersionInfo.GetVersionInfo(ds).FileVersion ?? "" : "";
        }

        /// <summary>
        /// Returns the per-user Revit add-ins folder for a Revit version.
        /// </summary>
        /// <param name="revitVersion">Revit version, e.g. 2024 (number or text).</param>
        /// <param name="allUsers">True for the machine-wide folder under ProgramData.</param>
        /// <returns>The add-ins folder path.</returns>
        public static string AddinsPath(object revitVersion, bool allUsers = false)
        {
            int version = Versions.ParseRevitVersion(revitVersion);
            Environment.SpecialFolder root = allUsers
                ? Environment.SpecialFolder.CommonApplicationData
                : Environment.SpecialFolder.ApplicationData;
            return Path.Combine(Environment.GetFolderPath(root), "Autodesk", "Revit", "Addins", version.ToString());
        }
    }
}
