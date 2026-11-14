using Newtonsoft.Json.Linq;

namespace EasyIDMMA.Export
{
    public class AsyncExportItem
    {
        public string Id { get; set; }
    
        public JObject IDMObject { get; set; }
    }

    public class AsyncExportResponseItem
    {
        public string Id { get; set; }

        public string Url { get; set; }
    }
}