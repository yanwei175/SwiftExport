using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.Wrappers
{
    public class PagedResultDto<T>
    {
        public IReadOnlyList<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        /// <summary>
        /// 总页数（逻辑推算）
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}