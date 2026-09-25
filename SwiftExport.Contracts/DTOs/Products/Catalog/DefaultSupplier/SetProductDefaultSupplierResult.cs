using System;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExport.Contracts.DTOs.Suppliers.Requests
{
    /// <summary>设置 Product 默认供应商后的产品版本与供应商资料回显。</summary>
    public class SetProductDefaultSupplierResult
    {
        public Guid ProductId { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public int OldProductVersion { get; set; }
        public int NewProductVersion { get; set; }
        public SupplierProductDto SupplierProduct { get; set; }
    }
}
