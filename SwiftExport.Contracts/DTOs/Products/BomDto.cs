using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class BomDto : BaseDto
    {
        public string BomNo { get; set; } 
        public Guid SubSkuId { get; set; }
        public string SubSku { get; set; }
        public int Numerator { get; set; } 
        public int Denominator { get; set; }
        public string Note { get; set; }



        public string Description { get; set; }
        public string Finish { get; set; }
        public string Features { get; set; }
        public string MainImage { get; set; }
    }
}
