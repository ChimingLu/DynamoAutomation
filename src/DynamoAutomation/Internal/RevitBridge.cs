using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DynamoAutomation.Internal
{
    /// <summary>
    /// Reaches the Revit document of the running Dynamo for Revit session by reflection
    /// (RevitServices / RevitAPI are loaded by Dynamo for Revit, never referenced here),
    /// so this library still loads in Dynamo Sandbox and in every Revit 2020-2027 host.
    /// </summary>
    internal static class RevitBridge
    {
        private static Type FindType(string assemblyName, string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == assemblyName)
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(t => t != null);
        }

        private static object StaticInstance(string assemblyName, string typeName)
        {
            Type type = FindType(assemblyName, typeName)
                ?? throw new InvalidOperationException("Job.Finish only works in Dynamo for Revit (" + typeName + " not loaded).");
            return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        }

        /// <summary>The active Revit document (Autodesk.Revit.DB.Document).</summary>
        public static object CurrentDocument()
        {
            object manager = StaticInstance("RevitServices", "RevitServices.Persistence.DocumentManager");
            return manager.GetType().GetProperty("CurrentDBDocument").GetValue(manager)
                ?? throw new InvalidOperationException("No Revit document is open.");
        }

        public static string Title(object document) => (string)document.GetType().GetProperty("Title").GetValue(document);

        public static string PathName(object document) => (string)document.GetType().GetProperty("PathName").GetValue(document);

        /// <summary>
        /// Commits the transaction Dynamo keeps open during a run; Revit cannot save while it is open.
        /// </summary>
        public static void CloseDynamoTransaction()
        {
            object manager = StaticInstance("RevitServices", "RevitServices.Transactions.TransactionManager");
            manager.GetType().GetMethod("ForceCloseTransaction", Type.EmptyTypes).Invoke(manager, null);
        }

        public static void Save(object document)
        {
            document.GetType().GetMethod("Save", Type.EmptyTypes).Invoke(document, null);
        }

        /// <summary>Saves a copy; workshared models are saved as a new central file.</summary>
        public static void SaveAs(object document, string path)
        {
            Assembly revitApi = document.GetType().Assembly;
            object options = Activator.CreateInstance(revitApi.GetType("Autodesk.Revit.DB.SaveAsOptions"));
            options.GetType().GetProperty("OverwriteExistingFile").SetValue(options, true);
            if ((bool)document.GetType().GetProperty("IsWorkshared").GetValue(document))
            {
                object sharing = Activator.CreateInstance(revitApi.GetType("Autodesk.Revit.DB.WorksharingSaveAsOptions"));
                sharing.GetType().GetProperty("SaveAsCentral").SetValue(sharing, true);
                options.GetType().GetMethod("SetWorksharingOptions").Invoke(options, new[] { sharing });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            document.GetType().GetMethod("SaveAs", new[] { typeof(string), options.GetType() })
                .Invoke(document, new[] { path, options });
        }

        public static bool IsWorkshared(object document) =>
            (bool)document.GetType().GetProperty("IsWorkshared").GetValue(document);
    }
}
