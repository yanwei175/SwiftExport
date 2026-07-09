namespace SwiftExport.Contracts.Wrappers
{
    // 这个类用来告诉后端：我要第几页？每页多少条？搜什么关键字？
    public class PagedRequestBase
    {
        /// <summary>
        /// 模糊关键词
        /// </summary>
        public string Keyword { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;


    }
}
