using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace SwiftExport.Core.Common
{
    public class BaseEntity
    {
        public int ID { get; set; }
        public short IsDelete { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime ChangeTime { get; set; }

    }
}
