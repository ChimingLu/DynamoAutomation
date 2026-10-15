using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamoAutomation.Internal
{
    /// <summary>
    /// Revit reads and writes journal files in the system ANSI code page (e.g. Big5 on
    /// Traditional Chinese Windows). Encoding.Default is UTF-8 on .NET 8/10 hosts
    /// (Dynamo 3.x/4.x), so convert through the Win32 API instead of relying on it.
    /// </summary>
    internal static class AnsiText
    {
        private const uint CP_ACP = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WideCharToMultiByte(uint codePage, uint flags,
            [MarshalAs(UnmanagedType.LPWStr)] string wideChars, int wideCharCount,
            byte[] multiBytes, int multiByteCount, IntPtr defaultChar, out bool usedDefaultChar);

        public static byte[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];
            int size = WideCharToMultiByte(CP_ACP, 0, text, text.Length, null, 0, IntPtr.Zero, out _);
            var bytes = new byte[size];
            WideCharToMultiByte(CP_ACP, 0, text, text.Length, bytes, size, IntPtr.Zero, out bool lossy);
            if (lossy)
                throw new ArgumentException(
                    "The journal contains characters that the Windows system code page cannot represent, " +
                    "so Revit would not be able to read it. Rename the file or folder, or change " +
                    "Windows 'Language for non-Unicode programs'.");
            return bytes;
        }

        public static void WriteFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, Encode(text));
        }
    }
}
