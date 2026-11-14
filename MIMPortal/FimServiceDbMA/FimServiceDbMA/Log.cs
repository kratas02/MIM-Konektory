using System;
using System.IO;

namespace FimServiceDbMA
{
    /// <summary>
    /// Zapis do textoveho logu. Konektor bezi v procesu miiserver.exe, ktery vlastni vyjimky
    /// zobrazuje jen castecne, takze log je hlavni diagnosticky nastroj.
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
                    "FimServiceDbMA", "FimServiceDbMA.log");
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

            // Zadana muze byt cesta k souboru nebo jen slozka
            _file = Directory.Exists(file) || file.EndsWith("\\", StringComparison.Ordinal)
                ? Path.Combine(file, "FimServiceDbMA.log")
                : file;
        }

        internal static void Info(string message)
        {
            Write("INFO", message);
        }

        internal static void Warn(string message)
        {
            Write("WARN", message);
        }

        internal static void Error(string message)
        {
            Write("ERROR", message);
        }

        internal static void Error(string message, Exception ex)
        {
            Write("ERROR", message + Environment.NewLine + ex);
        }

        /// <summary>Zapise se jen kdyz je zapnuty verbose log.</summary>
        internal static void Debug(string message)
        {
            if (_verbose)
                Write("DEBUG", message);
        }

        internal static bool IsVerbose
        {
            get { return _verbose; }
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
                // Selhani logovani nesmi shodit import
            }
        }
    }
}
