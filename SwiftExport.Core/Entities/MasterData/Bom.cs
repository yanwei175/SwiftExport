using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities.MasterData
{
    public class Bom:BaseEntity
    {
        public string BomNo { get; set; } = default!;
        public int SkuId { get; set; }
        public string ProductSku { get; set; } = default!;
        public double Qty { get; set; }
        public string SubType { get; set; } = default!;
    }
}
