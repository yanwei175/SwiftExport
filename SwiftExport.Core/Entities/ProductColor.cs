using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class ProductColor : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string ColorCodeUnique { get; set; }
        public string ColorCode { get; set; }
        public string ColorNameEn { get; set; }
        public string ColorNameZh { get; set; }

    }
}
