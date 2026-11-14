using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPLibrary
{
    public class Configuration : IDestinationConfiguration
    {
        public bool ChangeEventsSupported()
        {
            return false;
        }

        public event RfcDestinationManager.ConfigurationChangeHandler ConfigurationChanged;

        public RfcConfigParameters GetParameters(string destinationName)
        {

            RfcConfigParameters parms = new RfcConfigParameters();

            if (destinationName.Equals("mySAPdestination"))
            {
                // http://SEWSAPW1C1v.seas.sk:8000/sap/bc/srt/rfc/sap/zsksehr_service_mim/010/zsksehr_service_mim/zsksehr_service_mim
                parms.Add(RfcConfigParameters.AppServerHost, "sewsapw1c1v.seas.sk");
                parms.Add(RfcConfigParameters.SystemNumber, "00");
                parms.Add(RfcConfigParameters.SystemID, "W1C");
                parms.Add(RfcConfigParameters.User, "SINTMIM");
                parms.Add(RfcConfigParameters.Password, "asd.32SE");
                parms.Add(RfcConfigParameters.Client, "010");
                parms.Add(RfcConfigParameters.Language, "EN");
                parms.Add(RfcConfigParameters.PoolSize, "5");

            }
            return parms;

        }
    }
}
