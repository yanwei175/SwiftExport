using SwiftExport.Contracts.SysEnums.Inventory;
using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>入库页面可展示的业务类型，不将历史兼容值作为新单类型提供。</summary>
    public static class InboundDocumentTypes
    {
        /// <summary>采购、生产类型的只读列表；展示类型不表示对应后端功能已开放。</summary>
        public static IReadOnlyList<InboundDocumentTypeEnum> All { get; } =
            new List<InboundDocumentTypeEnum>
            {
                InboundDocumentTypeEnum.PurchaseReceipt,
                InboundDocumentTypeEnum.ProductionReceipt
            }.AsReadOnly();
    }

}
