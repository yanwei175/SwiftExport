using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class SalesOrderHead : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string SoNo { get; set; }
        public DateTime SoDate { get; set; }
        public DateTime RePlyDate { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerPoNo { get; set; }
        public string SoFolder { get; set; }
        public string SoNote { get; set; }

    }
}
