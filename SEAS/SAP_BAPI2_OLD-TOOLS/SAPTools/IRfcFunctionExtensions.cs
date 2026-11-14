using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPTools
{
    public static class IRfcFunctionExtensions
    {
        public static DataSet ToDataSet(this IRfcFunction fn)
        {
            var ds = new DataSet();
            for (int i = 0; i < fn.Metadata.ParameterCount; i++)
            {
                var name = fn.Metadata[i].Name;
                var o = fn.GetObject(i);
                if (o != null)
                {
                    if (o is SAP.Middleware.Connector.IRfcStructure structure)
                    {
                        var dt = structure.ToDataTable(name);
                        ds.Tables.Add(dt);
                    }
                    else if (o is IRfcTable table)
                    {
                        var dt = table.ToDataTable(name);
                        ds.Tables.Add(dt);
                    }
                    else
                    {
                        //Console.WriteLine(o.GetType().FullName);
                        //Console.WriteLine($"Parameter {i}: {name}");
                        //Console.WriteLine(o.GetType());
                        //Console.WriteLine(new string('=', 80));
                    }
                }
            }
            return ds;
        }
    }
}
