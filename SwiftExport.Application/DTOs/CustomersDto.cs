using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class CustomersDto : BaseDto
    {
        public string Even { get; set; }
        public string 状态 { get; set; }
        public string 客户代码 { get; set; }
        public string 客户名称 { get; set; }
        public string 地址 { get; set; }
        public string 联系方式 { get; set; }
        public string 联系人 { get; set; }
        public string 标贴文件夹 { get; set; }
        public string 说明书文件夹 { get; set; }
        public string 尺寸图文件夹 { get; set; }
        public string 线框图文件夹 { get; set; }
        public string 条码文件夹 { get; set; }
        public string 规格书文件夹 { get; set; }
        public string 单据关联 { get; set; }

        [Browsable(false)]
        public string SearchKey => $"{客户代码}|{客户名称}|{地址}".ToLower();

    }
}
