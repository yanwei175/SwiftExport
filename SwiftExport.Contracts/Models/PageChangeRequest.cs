using System;

namespace SwiftExport.Contracts.Models
{
    /// <summary>
    /// 分页更改请求参数
    /// </summary>
    public class PageChangeRequest
    {
        public int OldIndex { get; set; }
        public int NewIndex { get; set; }
        public int OldSize { get; set; }
        public int NewSize { get; set; }

        /// <summary>
        /// 是否仅仅是更改了页大小
        /// </summary>
        public bool IsPageSizeChanged => OldSize != NewSize;
    }
}