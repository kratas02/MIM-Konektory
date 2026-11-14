using EasyIDMMA.Model.Connector;

namespace EasyIDMMA.Model.Parameters
{
    public abstract class SynchronizationParameters : AuthorizationParameters
    {
        public ConnectorSchema ConnectorSchema { get; set; }
        public string BaseUrl { get; set; }
        public string ObjectTypeName { get; set; }
    }
}