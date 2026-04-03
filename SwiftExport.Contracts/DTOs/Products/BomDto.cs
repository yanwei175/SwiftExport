using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class BomDto : BaseDto
    {
        public string BomNo { get; set; }
        public int SkuId { get; set; }
        public string ProductSku { get; set; }
        public double Qty { get; set; }
        public string SubType { get; set; }
    }
}
