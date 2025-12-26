using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class PageRequestDto
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string Keyword { get; set; }
        public string OrderBy { get; set; }
    }
}
