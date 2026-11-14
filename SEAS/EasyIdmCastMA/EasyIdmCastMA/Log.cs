using System;
using System.IO;

namespace EasyIdmCastMA
{
    /// <summary>
    /// Textovy log. Konektor bezi v miiserver.exe, takze je to hlavni diagnostika.
    /// </summary>
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static string _file = DefaultFile;
        private static bool _verbose;

        internal static string DefaultFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "EasyIdmCastMA", "EasyIdmCastMA.log");
            }
        }

        internal static void Configure(string file, bool verbose)
        {
            _verbose = verbose;

            if (string.IsNullOrWhiteSpace(file))
            {
                _file = DefaultFile;
                return;
            }

            file = file.Trim();
            _file = Directory.Exists(file) || file.EndsWith("\\", StringComparison.Ordinal)
                ? Path.Combine(file, "EasyIdmCastMA.log")
                : file;
        }

        internal static void Info(string message) { Write("INFO", message); }
        internal static void Warn(string message) { Write("WARN", message); }
        internal static void Error(string message) { Write("ERROR", message); }

        internal static void Error(string message, Exception ex)
        {
            Write("ERROR", message + Environment.NewLine + ex);
        }

        internal static void Debug(string message)
        {
            if (_verbose)
                Write("DEBUG", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    string dir = Path.GetDirectoryName(_file);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.AppendAllText(_file, string.Format(
                        "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine));
                }
            }
            catch
            {
                // selhani logu nesmi shodit import
            }
        }
    }
}
