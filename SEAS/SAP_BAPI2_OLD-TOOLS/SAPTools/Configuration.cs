using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPTools
{
    public class ConfiguratioN
    { 
    }
    public class Configuration2 : IDestinationConfiguration
    {
        public bool ChangeEventsSupported()
        {
            return false;
        }

        public event RfcDestinationManager.ConfigurationChangeHandler ConfigurationChanged;

        public RfcConfigParameters GetParameters(string destinationName)
        {

            RfcConfigParameters parms = new RfcConfigParameters();

            //if (destinationName.Equals("mySAPdestination"))
            //{
            //    //http://sewsapw1u1v.seas.sk:8000/sap/bc/srt/rfc/sap/zsksehr_service_mim/010/zsksehr_service_mim/zsksehr_service_mim
            //    // http://SEWSAPW1C1v.seas.sk:8000/sap/bc/srt/rfc/sap/zsksehr_service_mim/010/zsksehr_service_mim/zsksehr_service_mim
            //    //parms.Add(RfcConfigParameters.AppServerHost, "sewsapw1c1v.seas.sk");
            //    //parms.Add(RfcConfigParameters.AppServerHost, "sewsapw1u1v.seas.sk:8000");

            //    if (Environment.MachineName.ToUpper() == "VMSEWMIMT01V")
            //    {
            //        parms.Add(RfcConfigParameters.AppServerHost, "vhethqs4ci.sap.seas.sk");
            //        parms.Add(RfcConfigParameters.SystemNumber, "00");
            //        parms.Add(RfcConfigParameters.SystemID, "QS4");
            //        parms.Add(RfcConfigParameters.User, "SINTMIM");
            //        parms.Add(RfcConfigParameters.Password, "SEEnaL2121.!");
            //        parms.Add(RfcConfigParameters.Client, "100");
            //        parms.Add(RfcConfigParameters.Language, "EN");
            //        parms.Add(RfcConfigParameters.PoolSize, "100");
            //       // parms.Add(RfcConfigParameters.IdleTimeout, "600");
            //        //parms.Add(RfcConfigParameters.MaxPoolWaitTime, "300000");
            //    }
            //    else
            //    {
            //        parms.Add(RfcConfigParameters.AppServerHost, "sewsapw1p1v.seas.sk");
            //        parms.Add(RfcConfigParameters.SystemNumber, "00");
            //        parms.Add(RfcConfigParameters.SystemID, "W1P");
            //        parms.Add(RfcConfigParameters.User, "SINTMIM");
            //        parms.Add(RfcConfigParameters.Password, @"Qz>uXZrhVJGCzGwVnwew5wimzHoMHzupplSCHWQd");
            //        parms.Add(RfcConfigParameters.Client, "010");
            //        parms.Add(RfcConfigParameters.Language, "EN");
            //        parms.Add(RfcConfigParameters.PoolSize, "100");
            //       // parms.Add(RfcConfigParameters.IdleTimeout, "600");
            //        //parms.Add(RfcConfigParameters.MaxPoolWaitTime, "300000");
            //    }




            //    // parms.Add(RfcConfigParameters.AppServerHost, Properties.Settings.Default.AppServerHost);
            //    //parms.Add(RfcConfigParameters.SystemNumber, Properties.Settings.Default.SystemNumber);
            //    //parms.Add(RfcConfigParameters.SystemID, Properties.Settings.Default.SystemID);
            //    //parms.Add(RfcConfigParameters.User, Properties.Settings.Default.User);
            //    //parms.Add(RfcConfigParameters.Password, Properties.Settings.Default.Password);
            //    //parms.Add(RfcConfigParameters.Client, Properties.Settings.Default.Client);
            //    //parms.Add(RfcConfigParameters.Language, Properties.Settings.Default.Language);
            //    //parms.Add(RfcConfigParameters.PoolSize, Properties.Settings.Default.PoolSize);
            //    //parms.Add(RfcConfigParameters.IdleTimeout, Properties.Settings.Default.IdleTimeout);

            //}
            return parms;

        }
    }
}
