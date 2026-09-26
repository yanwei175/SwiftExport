using System;

namespace SwiftExport.Contracts.DTOs.Suppliers.Requests
{
    /// <summary>通过有效供应商产品关系设置产品默认供应商。</summary>
    public class SetProductDefaultSupplierCommand
    {
        public Guid SupplierProductId { get; set; }
        public Guid ProductId { get; set; }
        public Guid SupplierId { get; set; }
        public int ExpectedProductVersion { get; set; }
    }

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
