namespace EasyIDMMA.Export
{
    public enum ExportResultType
    {
        Success,
        ServerError,
        ConnectionError
    }
    
    public class ExportResponse
    {
        public ExportResultType ResultType { get; set; }
        public int StatusCode { get; set; }
        public string ErrorMessage { get; set; }
    }
}