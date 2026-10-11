namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>定义采购订单正常导入检查响应中的问题与确认警告代码，不代表请求执行失败。</summary>
    public static class PurchaseOrderImportIssueCodes
    {
        /// <summary>非空 BONO 已被未删除的采购单占用，不能通过确认绕过。</summary>
        public const string BoNoAlreadyExists = "BoNoAlreadyExists";
        /// <summary>客户不可用或代码存在歧义。</summary>
        public const string CustomerUnavailable = "CustomerUnavailable";
        /// <summary>未选择有效实际供应商。</summary>
        public const string SupplierUnavailable = "SupplierUnavailable";
        /// <summary>非标单位未启用或有歧义。</summary>
        public const string UnitUnavailable = "UnitUnavailable";
        /// <summary>检查产品前必须定位客户。</summary>
        public const string CustomerRequired = "CustomerRequired";
        /// <summary>导入中性型号与系统资料不同，需要确认。</summary>
        public const string LinkSkuMismatch = "LinkSkuMismatch";
        /// <summary>客供件未选择有效送达供应商。</summary>
        public const string DeliverySupplierRequired = "DeliverySupplierRequired";
    }
}
