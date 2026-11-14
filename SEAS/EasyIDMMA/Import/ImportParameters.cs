using EasyIDMMA.Model.Parameters;

namespace EasyIDMMA.Import
{
    public class ImportParameters : SynchronizationParameters
    {
        public string ImportUrl { get; set; }
        public int PageSize { get; set; }
    }
}