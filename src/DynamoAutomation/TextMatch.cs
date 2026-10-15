using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DynamoAutomation
{
    /// <summary>
    /// Regular-expression helpers. These replace the 1.x Regex custom nodes, which
    /// depended on IronPython and no longer run in Dynamo 3.x/4.x.
    /// Lists are handled by Dynamo's lacing, so pass lists of strings directly.
    /// </summary>
    public class TextMatch
    {
        private TextMatch() { }

        private static RegexOptions Options(bool ignoreCase) =>
            ignoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;

        /// <summary>
        /// True if the pattern occurs anywhere in the text.
        /// </summary>
        /// <param name="text">Text to search.</param>
        /// <param name="pattern">.NET regular expression.</param>
        /// <param name="ignoreCase">Ignore upper/lower case.</param>
        /// <returns>True when the pattern matches.</returns>
        public static bool Contains(string text, string pattern, bool ignoreCase = false)
        {
            return Regex.IsMatch(text ?? "", pattern, Options(ignoreCase));
        }

        /// <summary>
        /// Returns every match of the pattern in the text.
        /// </summary>
        /// <param name="text">Text to search.</param>
        /// <param name="pattern">.NET regular expression.</param>
        /// <param name="ignoreCase">Ignore upper/lower case.</param>
        /// <returns>The matched substrings.</returns>
        public static List<string> Matches(string text, string pattern, bool ignoreCase = false)
        {
            return Regex.Matches(text ?? "", pattern, Options(ignoreCase)).Cast<Match>().Select(m => m.Value).ToList();
        }

        /// <summary>
        /// Replaces every match of the pattern.
        /// </summary>
        /// <param name="text">Text to change.</param>
        /// <param name="pattern">.NET regular expression.</param>
        /// <param name="replacement">Replacement text; $1, $2 refer to groups.</param>
        /// <param name="ignoreCase">Ignore upper/lower case.</param>
        /// <returns>The changed text.</returns>
        public static string Replace(string text, string pattern, string replacement, bool ignoreCase = false)
        {
            return Regex.Replace(text ?? "", pattern, replacement ?? "", Options(ignoreCase));
        }
    }
}
