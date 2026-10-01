using System;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>后端检查的不可变SalesOrderImportMatchCandidate快照，防止共享 DTO 后续修改页面结果。</summary>
    public sealed class SalesOrderImportMatchCandidate
    {
        /// <summary>复制已接纳的检查反馈；不执行网络或业务字段赋值。</summary>
        public SalesOrderImportMatchCandidate(SalesOrderImportProductCandidateDto value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            CustomerProductId = value.CustomerProductId;
            ProductId = value.ProductId;
            CustomerSku = value.CustomerSku ?? "";
            Sku = value.Sku ?? "";
            LinkSku = value.LinkSku ?? "";
            Unit = value.Unit ?? "";
            MainImage = value.MainImage ?? "";
            IsUsable = value.IsUsable;
        }
        public Guid CustomerProductId { get; }
        public Guid? ProductId { get; }
        public string CustomerSku { get; }
        public string Sku { get; }
        public string LinkSku { get; }
        public string Unit { get; }
        public string MainImage { get; }
        public bool IsUsable { get; }
    }
}
