using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    public class AssignProductsToCategoryRequest
    {
        public Guid TargetCategoryId { get; set; }
        public List<AssignProductToCategoryItem> Items { get; set; } = new List<AssignProductToCategoryItem>();
    }

    public class AssignProductToCategoryItem
    {
        public Guid ProductId { get; set; }
        public int ExpectedVersion { get; set; }
    }
}
