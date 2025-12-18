using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class ExcelSheetFieldsMapping:BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string ShengXiaoDomain { get; set; }
        public string SourceFields { get; set; }
        public string TargetFields { get; set; }
    }
}
