using System;
using System.Collections.Concurrent;
using SAP.Middleware.Connector;

namespace SAPBAPI_Direct
{
    /// <summary>
    /// Process-wide NCo registrace. Stejny vzor jako OmniSync: kazda instance MA
    /// ma vlastni destination name, aby slo mit vic SAP systemu v jednom miiserver.exe.
    /// </summary>
    internal sealed class SapDestination : IDestinationConfiguration
    {
        private static readonly SapDestination Instance = new SapDestination();
        private static readonly ConcurrentDictionary<string, RfcConfigParameters> Destinations =
            new ConcurrentDictionary<string, RfcConfigParameters>(StringComparer.OrdinalIgnoreCase);
        private static readonly object RegistrationLock = new object();
        private static bool _registered;

        private SapDestination()
        {
        }

        internal static string Register(SapSettings settings)
        {
            string name = "SAPBAPI_Direct_" + (settings.SystemId ?? "SAP") + "_" + (settings.Client ?? "000");
            var parameters = new RfcConfigParameters();
            parameters.Add(RfcConfigParameters.Name, name);
            parameters.Add(RfcConfigParameters.AppServerHost, settings.AppServerHost);
            parameters.Add(RfcConfigParameters.SystemNumber, settings.SystemNumber);
            if (!string.IsNullOrWhiteSpace(settings.SystemId))
                parameters.Add(RfcConfigParameters.SystemID, settings.SystemId);
            parameters.Add(RfcConfigParameters.Client, settings.Client);
            parameters.Add(RfcConfigParameters.Language, settings.Language);
            parameters.Add(RfcConfigParameters.User, settings.User);
            parameters.Add(RfcConfigParameters.Password, settings.Password);
            parameters.Add(RfcConfigParameters.PoolSize, settings.PoolSize.ToString());

            Destinations[name] = parameters;

            lock (RegistrationLock)
            {
                if (!_registered)
                {
                    try
                    {
                        RfcDestinationManager.RegisterDestinationConfiguration(Instance);
                    }
                    catch (RfcInvalidStateException)
                    {
                    }
                    _registered = true;
                }
            }

            return name;
        }

        public RfcConfigParameters GetParameters(string destinationName)
        {
            RfcConfigParameters p;
            if (Destinations.TryGetValue(destinationName, out p))
                return p;
            throw new InvalidOperationException("SAP destination '" + destinationName + "' neni registrovana.");
        }

        public bool ChangeEventsSupported()
        {
            return false;
        }

#pragma warning disable 0067
        public event RfcDestinationManager.ConfigurationChangeHandler ConfigurationChanged;
#pragma warning restore 0067
    }

    internal sealed class SapSettings
    {
        public string AppServerHost;
        public string SystemNumber = "00";
        public string SystemId;
        public string Client = "010";
        public string Language = "EN";
        public string User;
        public string Password;
        public int PoolSize = 5;
        public string[] DependantRoles = new string[0];
    }
}
