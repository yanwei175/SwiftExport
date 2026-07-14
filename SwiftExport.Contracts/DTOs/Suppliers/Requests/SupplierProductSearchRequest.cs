using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.Suppliers.Requests
{
    public class SupplierProductSearchRequest : PagedRequestBase
    {
        public Guid SupplierId { get; set; }
    }
}
