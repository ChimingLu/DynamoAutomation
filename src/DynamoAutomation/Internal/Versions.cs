using System;
using System.Globalization;

namespace DynamoAutomation.Internal
{
    internal static class Versions
    {
        public const int MinSupported = 2020;
        public const int MaxSupported = 2027;

        /// <summary>
        /// Accepts 2024, 2024.0, "2024" or "R2024" (values often come from config files as strings).
        /// </summary>
        public static int ParseRevitVersion(object revitVersion)
        {
            if (revitVersion == null)
                throw new ArgumentNullException(nameof(revitVersion), "Revit version is required, e.g. 2024.");

            int version;
            switch (revitVersion)
            {
                case int i: version = i; break;
                case long l: version = (int)l; break;
                case double d: version = (int)Math.Round(d); break;
                case float f: version = (int)Math.Round(f); break;
                default:
                    string text = Convert.ToString(revitVersion, CultureInfo.InvariantCulture).Trim().TrimStart('R', 'r');
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                        throw new ArgumentException($"Could not read a Revit version from [{revitVersion}]. Use a year such as 2024.", nameof(revitVersion));
                    break;
            }

            if (version < 2017)
                throw new ArgumentException($"Revit {version} is not supported by DynamoAutomation 2.x. Use DynamoAutomation 1.x for Revit 2016 and older.", nameof(revitVersion));
            return version;
        }
    }
}
