using System;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace SAPBAPI_Direct
{
    internal static class Log
    {
        private const string SourceName = "SAP";
        private const string ScriptName = "SAPBAPI_Direct";

        private static readonly object Sync = new object();
        private static string _file = DefaultFile;
        private static string _executedBy;

        internal static string DefaultFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SAPBAPI_Direct", "SAPBAPI_Direct.log");
            }
        }

        internal static void Configure(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                _file = DefaultFile;
                return;
            }

            file = file.Trim();
            _file = Directory.Exists(file) || file.EndsWith("\\", StringComparison.Ordinal)
                ? Path.Combine(file, "SAPBAPI_Direct.log")
                : file;
        }

        internal static void Info(string message, string account = "", string detail = "", long duration = 0)
        {
            Write("Information", message, null, account, detail, duration);
        }

        internal static void Warn(string message, string account = "", string detail = "", long duration = 0)
        {
            Write("Warning", message, null, account, detail, duration);
        }

        internal static void Error(string message, Exception ex = null, string account = "", string detail = "", long duration = 0)
        {
            Write("Error", message, ex, account, detail, duration);
        }

        private static void Write(string level, string message, Exception ex, string account, string detail, long duration)
        {
            try
            {
                string line = BuildLine(level, message, ex, account, detail, duration);
                lock (Sync)
                {
                    string dir = Path.GetDirectoryName(_file);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.AppendAllText(_file, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        private static string BuildLine(string level, string message, Exception ex, string account, string detail, long duration)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"@t\":\"");
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            sb.Append("\",\"@m\":");
            AppendJson(sb, message ?? "");
            sb.Append(",\"@l\":");
            AppendJson(sb, level);
            sb.Append(",\"Exception\":");
            if (ex == null)
                sb.Append("null");
            else
                AppendJson(sb, BapiReturn.FormatException(ex));
            sb.Append(",\"Source\":");
            AppendJson(sb, SourceName);
            sb.Append(",\"Script\":");
            AppendJson(sb, ScriptName);
            sb.Append(",\"Detail\":");
            AppendJson(sb, detail ?? "");
            sb.Append(",\"Account\":");
            AppendJson(sb, account ?? "");
            sb.Append(",\"Duration\":");
            sb.Append(duration);
            sb.Append(",\"Executed_by\":");
            AppendJson(sb, ExecutedBy());
            sb.Append("}");
            return sb.ToString();
        }

        private static string ExecutedBy()
        {
            if (_executedBy != null)
                return _executedBy;

            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                _executedBy = id != null && !string.IsNullOrEmpty(id.Name) ? id.Name : Environment.UserName;
            }
            catch
            {
                _executedBy = Environment.UserName ?? "";
            }

            return _executedBy;
        }

        private static void AppendJson(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (string.IsNullOrEmpty(value))
            {
                sb.Append('"');
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
