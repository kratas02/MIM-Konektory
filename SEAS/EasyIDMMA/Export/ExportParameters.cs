using EasyIDMMA.Model.Parameters;

namespace EasyIDMMA.Export
{
    public class ExportParameters : SynchronizationParameters
    {
        public string ExportUrl { get; set; }
        public string Data { get; set; }
    }
    
    public class AsyncExportParameters : ExportParameters
    {
        public string ExportId { get; set; }
        public IDMObject IdmObject { get; set; }
    }
}