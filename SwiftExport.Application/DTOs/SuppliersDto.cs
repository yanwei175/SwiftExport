using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class SuppliersDto : BaseDto
    {
        public string Even { get; set; }
        public string 状态 { get; set; }
        public string 供应商代码 { get; set; } 
        public string 供应商名称 { get; set; }
        public decimal? 折扣 { get; set; }
        public string 折扣备注 { get; set; }
    }
}
