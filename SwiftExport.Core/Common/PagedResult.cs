using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    /// <summary>
    /// 泛型分页查询结果（定义在 Core 层，供 Repository 使用）
    /// </summary>
    /// <typeparam name="T">领域实体类型</typeparam>
    public class PagedResult<T> where T : class
    {
        /// <summary>
        /// 当前页的数据集合
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页条数
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数（逻辑推算）
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
