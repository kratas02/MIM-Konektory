namespace EasyIDMMA.Import
{
    public class ImportEntriesPagination
    {
        public int LastObjectIndex { get; set; } = 0;
        public int LastObjectTypeIndex { get; set; } = 0;
        public int PagesCount { get; set; } = 0;
        public int PageSize { get; set; } = 40;


        public void Reset()
        {
            this.LastObjectIndex = 0;
            this.LastObjectTypeIndex = 0;
            this.PagesCount = 0;
        }

        public void NextPage()
        {
            this.PagesCount++;
        }

        public void NextObjectType()
        {
            this.LastObjectIndex = 0;
            this.LastObjectTypeIndex++;
            this.PagesCount = 0;
        }
    }
}