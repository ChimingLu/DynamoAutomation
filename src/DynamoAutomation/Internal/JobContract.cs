namespace DynamoAutomation.Internal
{
    /// <summary>
    /// The contract between the batch runner (Batch.RunGraph, in the master graph) and the
    /// Job nodes inside the worker graph that runs in each Revit process.
    /// </summary>
    internal static class JobContract
    {
        /// <summary>Environment variable set on each Revit process; holds the path of its job file.</summary>
        public const string EnvironmentVariable = "DYNAMOAUTOMATION_JOB";

        /// <summary>Marker line written into every generated journal.</summary>
        public const string JournalMarker = "' DynamoAutomation job";

        // Job file keys
        public const string KeyJobId = "jobId";
        public const string KeyBatchId = "batchId";
        public const string KeyJobIndex = "jobIndex";
        public const string KeyJobCount = "jobCount";
        public const string KeyModelPath = "modelPath";
        public const string KeyWorkspacePath = "workspacePath";
        public const string KeyOutputDirectory = "outputDirectory";
        public const string KeySaveMode = "saveMode";
        public const string KeyResultFile = "resultFile";
        public const string KeyLogFile = "logFile";
        public const string ParameterPrefix = "param.";

        // Result file keys (written by Job.Finish)
        public const string KeyStatus = "status";
        public const string KeyMessage = "message";
        public const string KeyDocument = "document";
        public const string KeySavedPath = "savedPath";
        public const string KeyFinishedAt = "finishedAt";

        // Save modes
        public const string SaveNone = "none";
        public const string SaveOverwrite = "overwrite";
        public const string SaveCopy = "copy";
    }
}
