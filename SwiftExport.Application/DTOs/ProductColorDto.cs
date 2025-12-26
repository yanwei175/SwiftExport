using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class ProductColorDto : BaseDto
    {
        public string Even { get; set; }
        public string 状态 { get; set; }
        public string 唯一颜色代码 { get; set; }
        public string 颜色代码 { get; set; }
        public string 中文颜色名称 { get; set; }
        public string 英文颜色名称 { get; set; }

        [Browsable(false)]
        public string SearchKey => $"{唯一颜色代码}|{中文颜色名称}|{英文颜色名称}".ToLower();
    }
}
