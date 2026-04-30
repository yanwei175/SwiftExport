namespace SwiftExport.Contracts.Wrappers
{
    // 这个类用来告诉后端：我要第几页？每页多少条？搜什么关键字？
    public class PagedRequestDto
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // 模糊搜索的关键字
        public string SearchText { get; set; }
        public string BatchSearchTexts { get; set; }//p批量搜索

        // 以后还可以扩充各种高级过滤条件，比如：
        // public int? Status { get; set; }
        // public DateTime? StartDate { get; set; }
    }
}
