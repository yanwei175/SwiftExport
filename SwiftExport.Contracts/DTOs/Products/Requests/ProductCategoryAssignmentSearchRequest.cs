using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    public class ProductCategoryAssignmentSearchRequest : PagedRequestBase
    {
        public Guid TargetCategoryId { get; set; }
        public bool ExcludeTargetCategory { get; set; } = true;
    }
}
