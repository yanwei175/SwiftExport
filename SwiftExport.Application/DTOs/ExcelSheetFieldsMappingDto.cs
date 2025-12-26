using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class ExcelSheetFieldsMappingDto : BaseDto

    {
        public string Even { get; set; }
        public string 状态 { get; set; }
        public string 生效表 { get; set; }
        public string 源字段 { get; set; }
        public string 目标字段 { get; set; }

        [Browsable(false)]
        public string SearchKey => $"{生效表}|{源字段}".ToLower();

    }
}
