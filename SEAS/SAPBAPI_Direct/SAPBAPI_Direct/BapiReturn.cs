using System;
using System.Collections.Generic;
using System.Text;
using SAP.Middleware.Connector;

namespace SAPBAPI_Direct
{
    /// <summary>
    /// BAPI casto neshodi RFC vyjimku – chyba je v RETURN tabulce (TYPE E/A).
    /// Bez teto kontroly export v MIM skonci uspechem a chyba je jen v logu.
    /// Prevzato z OmniSync CheckReturn.
    /// </summary>
    internal static class BapiReturn
    {
        internal static void Check(IRfcFunction fn, string context)
        {
            IRfcTable table;
            try
            {
                table = fn.GetTable("RETURN");
            }
            catch
            {
                return;
            }

            var errors = new List<string>();
            var others = new List<string>();
            foreach (IRfcStructure row in table)
            {
                string type = Safe(row, "TYPE");
                string message = Safe(row, "MESSAGE");
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                string id = Safe(row, "ID");
                string number = Safe(row, "NUMBER");
                string line = type + " " + id + number + ": " + message;

                if (type == "E" || type == "A" || type == "X")
                    errors.Add(line);
                else
                    others.Add(line);
            }

            foreach (string info in others)
                Log.Warn(info, detail: context);

            if (errors.Count > 0)
                throw new InvalidOperationException(context + ": " + string.Join(" | ", errors.ToArray()));
        }

        internal static void Commit(RfcDestination dest)
        {
            IRfcFunction fn = dest.Repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
            fn.SetValue("WAIT", "X");
            fn.Invoke(dest);
        }

        private static string Safe(IRfcStructure row, string field)
        {
            try
            {
                object v = row.GetValue(field);
                return v == null ? "" : v.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }

        internal static string FormatException(Exception ex)
        {
            var sb = new StringBuilder();
            Exception current = ex;
            while (current != null)
            {
                if (sb.Length > 0)
                    sb.Append(" --> ");
                sb.Append(current.GetType().Name);
                sb.Append(": ");
                sb.Append(current.Message);
                current = current.InnerException;
            }
            return sb.ToString();
        }
    }
}
