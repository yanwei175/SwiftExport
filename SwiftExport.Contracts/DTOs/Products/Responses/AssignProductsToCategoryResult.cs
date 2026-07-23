using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.Responses
{
    public class AssignProductsToCategoryResult
    {
        public Guid TargetCategoryId { get; set; }
        public int UpdatedCount { get; set; }
        public List<AssignedProductVersionResult> Items { get; set; } = new List<AssignedProductVersionResult>();
    }

    public class AssignedProductVersionResult
    {
        public Guid ProductId { get; set; }
        public int OldVersion { get; set; }
        public int NewVersion { get; set; }
    }
}
