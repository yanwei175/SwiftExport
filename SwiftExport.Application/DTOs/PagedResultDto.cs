using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    /// <summary>
    /// 分页传输对象（定义在 AppLayer 层，供 UI 展示使用）
    /// </summary>
    /// <typeparam name="T">Dto 类型</typeparam>
    public class PagedResultDto<T> where T : class
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }

        // UI 需要这些属性来控制按钮状态（启用/禁用）
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasPrevious => PageIndex > 1;
        public bool HasNext => PageIndex < TotalPages;
    }
}
