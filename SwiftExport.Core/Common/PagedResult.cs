using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        /// <summary>
        /// 总页数（逻辑推算）
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
