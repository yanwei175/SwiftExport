using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class ProductDetails : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string ProductSKU { get; set; }
        public string LabelPath { get; set; }
        public string IMPath { get; set; }
        public string DrawingPath { get; set; }
        public string LinedrawingPath { get; set; }
        public string SpecSheetPath { get; set; }
        public string Barcode1Path { get; set; }
        public string Barcode2Path { get; set; }

    }
}
