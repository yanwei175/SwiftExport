using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace SwiftExport.Core.Common
{
    /// <summary>
    /// 分页工具类
    /// </summary>
    public class Pagination
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int PageCount { get; set; }
    }
}
