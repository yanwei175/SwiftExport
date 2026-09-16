using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.QualityControl;

namespace SwiftExport.Core.Entities.Auditing
{
    [DbAppendOnly]
    [DbCheck("CK_BusinessOperationLog_Immutable", "[IsDeleted] = 0")]
    [DbCheck("CK_BusinessOperationLog_ChangedJson", "[ChangedFieldsJson] IS NULL OR ISJSON([ChangedFieldsJson]) = 1")]
    [DbCheck("CK_BusinessOperationLog_RelatedJson", "[RelatedObjectsJson] IS NULL OR ISJSON([RelatedObjectsJson]) = 1")]
    [DbIndex("UX_BusinessOperationLog_Command", nameof(BusinessOperationLog.Module), nameof(BusinessOperationLog.OperationType), nameof(BusinessOperationLog.CommandId), IsUnique = true)]
    [DbIndex("IX_BusinessOperationLog_Aggregate", nameof(BusinessOperationLog.AggregateType), nameof(BusinessOperationLog.AggregateUid), nameof(BusinessOperationLog.OccurredDate))]
    public sealed partial class BusinessOperationLog { }
}

namespace SwiftExport.Core.Entities.Auth
{
    [DbIndex("UX_SysRoles_RoleName", nameof(SysRole.RoleName), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SysRole.ParentRoleId), typeof(SysRole))]
    public partial class SysRole { }

    [DbCheck("CK_SysUser_CredentialVersion", "[CredentialVersion] >= 1")]
    [DbIndex("UX_SysUser_UserName", nameof(SysUser.UserName), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class SysUser { }

    [DbIndex("UX_SysUserRoles_User_Role", nameof(SysUserRole.UserId), nameof(SysUserRole.RoleId), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SysUserRole.UserId), typeof(SysUser), IsRequired = true)]
    [LogicalReference(nameof(SysUserRole.RoleId), typeof(SysRole), IsRequired = true)]
    public partial class SysUserRole { }

    [DbIndex("UX_SysUserModulePermissions_User_Key", nameof(SysUserModulePermission.UserId), nameof(SysUserModulePermission.PermissionKey), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_SysUserModulePermissions_Key", nameof(SysUserModulePermission.PermissionKey), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SysUserModulePermission.UserId), typeof(SysUser), IsRequired = true)]
    public sealed partial class SysUserModulePermission { }
}

namespace SwiftExport.Core.Entities.MasterData
{
    [DbCheck("CK_Bom_PositiveRatio", "[Numerator] > 0 AND [Denominator] > 0")]
    [DbCheck("CK_Bom_ParentDiffersFromChild", "[ComponentProductId] IS NULL OR [ParentProductId] <> [ComponentProductId]")]
    [DbIndex("UX_Bom_Number_Version_Line", nameof(Bom.BomNo), nameof(Bom.BomVersion), nameof(Bom.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_Bom_ParentProduct", nameof(Bom.ParentProductId), nameof(Bom.BomVersion), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(Bom.ParentProductId), typeof(Product))]
    [LogicalReference(nameof(Bom.ComponentProductId), typeof(Product))]
    public partial class Bom { }

    [DbIndex("UX_Customer_CustomerCode", nameof(Customer.CustomerCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class Customer { }

    [DbIndex("UX_Product_Sku", nameof(Product.Sku), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_Product_DefaultSupplierId", nameof(Product.DefaultSupplierId), Filter = "[IsDeleted] = 0 AND [DefaultSupplierId] IS NOT NULL")]
    [DbIndex("IX_Product_Master_Color", nameof(Product.ProductMasterId), nameof(Product.ColorId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(Product.ProductMasterId), typeof(ProductMaster))]
    [LogicalReference(nameof(Product.ColorId), typeof(ProductColor), IsRequired = true)]
    [LogicalReference(nameof(Product.DefaultSupplierId), typeof(Supplier))]
    public partial class Product { }

    [DbIndex("UX_ProductCategory_CategoryCode", nameof(ProductCategory.CategoryCode), IsUnique = true, Filter = "[IsDeleted] = 0 AND [CategoryCode] IS NOT NULL")]
    [LogicalReference(nameof(ProductCategory.ParentId), typeof(ProductCategory))]
    public partial class ProductCategory { }

    [DbIndex("UX_ProductColor_ColorCodeUnique", nameof(ProductColor.ColorCodeUnique), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class ProductColor { }

    [DbIndex("IX_ProductDetails_ProductId", nameof(ProductDetail.ProductId), Filter = "[IsDeleted] = 0 AND [ProductId] IS NOT NULL")]
    [DbIndex("IX_ProductDetails_ProductSKU", nameof(ProductDetail.ProductSKU), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(ProductDetail.ProductId), typeof(Product))]
    public partial class ProductDetail { }

    [DbCheck("CK_ProductMaster_Pcs", "[DefaultPcsPerPackage] > 0")]
    [DbCheck("CK_ProductMaster_Measures", "([DefaultNetWeight] IS NULL OR [DefaultNetWeight] >= 0) AND ([DefaultGrossWeight] IS NULL OR [DefaultGrossWeight] >= 0) AND ([DefaultCbm] IS NULL OR [DefaultCbm] >= 0)")]
    [DbIndex("UX_ProductMaster_MasterSku", nameof(ProductMaster.MasterSku), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_ProductMaster_Category", nameof(ProductMaster.CategoryId), Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_ProductMaster_DefaultSupplier", nameof(ProductMaster.DefaultSupplierId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(ProductMaster.CategoryId), typeof(ProductCategory), IsRequired = true)]
    [LogicalReference(nameof(ProductMaster.DefaultSupplierId), typeof(Supplier), IsRequired = true)]
    public partial class ProductMaster { }

    [DbCheck("CK_ProductNeutral_ExchangeRate", "[DefaultExchangeRate] IS NULL OR [DefaultExchangeRate] > 0")]
    [DbIndex("UX_ProductNeutral_ProductCode", nameof(ProductNeutral.ProductCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_ProductNeutral_Master_Color", nameof(ProductNeutral.ProductMasterId), nameof(ProductNeutral.ColorId), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_ProductNeutral_ProductMaster", nameof(ProductNeutral.ProductMasterId), Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_ProductNeutral_Color", nameof(ProductNeutral.ColorId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(ProductNeutral.ProductMasterId), typeof(ProductMaster), IsRequired = true)]
    [LogicalReference(nameof(ProductNeutral.ColorId), typeof(ProductColor), IsRequired = true)]
    public partial class ProductNeutral { }

    [DbCheck("CK_ProductPackage_Pcs", "[PcsPerPackage] > 0")]
    [DbCheck("CK_ProductPackage_Measures", "([Length_CM] IS NULL OR [Length_CM] >= 0) AND ([Width_CM] IS NULL OR [Width_CM] >= 0) AND ([Height_CM] IS NULL OR [Height_CM] >= 0) AND ([Nw_KG] IS NULL OR [Nw_KG] >= 0) AND ([Gw_KG] IS NULL OR [Gw_KG] >= 0) AND ([Cbm] IS NULL OR [Cbm] >= 0)")]
    [DbIndex("UX_ProductPackage_Product_Type", nameof(ProductPackage.ProductId), nameof(ProductPackage.PackageType), IsUnique = true, Filter = "[IsDeleted] = 0 AND [ProductId] IS NOT NULL")]
    [LogicalReference(nameof(ProductPackage.ProductId), typeof(Product))]
    public partial class ProductPackage { }

    [DbIndex("UX_Supplier_SupplierCode", nameof(Supplier.SupplierCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class Supplier { }
}

namespace SwiftExport.Core.Entities.Relations
{
    [DbCheck("CK_CustomerPoConfig_DefaultDiscountRate", "[DefaultDiscountRate] IS NULL OR [DefaultDiscountRate] >= 0")]
    [DbIndex("UX_CustomerPoConfig_Codes", nameof(CustomerPoConfig.CustomerCode), nameof(CustomerPoConfig.SupplierCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(CustomerPoConfig.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(CustomerPoConfig.SupplierId), typeof(Supplier))]
    public partial class CustomerPoConfig { }

    [DbIndex("UX_CustomerProduct_Customer_CustomerSku", nameof(CustomerProduct.CustomerId), nameof(CustomerProduct.CustomerSku), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_CustomerProduct_SkuId", nameof(CustomerProduct.SkuId), Filter = "[IsDeleted] = 0 AND [SkuId] IS NOT NULL")]
    [LogicalReference(nameof(CustomerProduct.CustomerId), typeof(Customer), IsRequired = true)]
    [LogicalReference(nameof(CustomerProduct.SkuId), typeof(Product))]
    public partial class CustomerProduct { }

    [DbIndex("UX_SupplierProduct_Supplier_Product", nameof(SupplierProduct.SupplierId), nameof(SupplierProduct.SkuId), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_SupplierProduct_Product", nameof(SupplierProduct.SkuId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SupplierProduct.SupplierId), typeof(Supplier), IsRequired = true)]
    [LogicalReference(nameof(SupplierProduct.SkuId), typeof(Product), IsRequired = true)]
    public partial class SupplierProduct { }
}

namespace SwiftExport.Core.Entities.Orders
{
    [DbCheck("CK_SalesOrderHeader_Status", "[DocumentStatus] IN (0, 8, 9) AND [ConfirmationStatus] IN (0, 1)")]
    [DbIndex("UX_SalesOrderHeader_OrderNo", nameof(SalesOrderHeader.OrderNo), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_SalesOrderHeader_Customer_Date", nameof(SalesOrderHeader.CustomerId), nameof(SalesOrderHeader.OrderDate), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SalesOrderHeader.CustomerId), typeof(Customer))]
    public partial class SalesOrderHeader { }

    [DbCheck("CK_SalesOrderLine_LineNumber", "[LineNumber] > 0")]
    [DbCheck("CK_SalesOrderLine_Qty", "[Qty] > 0")]
    [DbIndex("UX_SalesOrderLine_Head_Line", nameof(SalesOrderLine.SalesOrderId), nameof(SalesOrderLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SalesOrderLine.SalesOrderId), typeof(SalesOrderHeader), IsRequired = true)]
    [LogicalReference(nameof(SalesOrderLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(SalesOrderLine.CustomerProductId), typeof(Relations.CustomerProduct))]
    public partial class SalesOrderLine { }
}

namespace SwiftExport.Core.Entities.DemandPlanning
{
    [DbCheck("CK_GrossDemand_Lifecycle", "[LifecycleStatus] IN (0, 8, 9)")]
    [DbCheck("CK_GrossDemand_Quantity", "[DemandQty] > 0")]
    [DbCheck("CK_GrossDemand_StandardProduct", "([IsNonStandard] = 0 AND [ProductId] IS NOT NULL) OR ([IsNonStandard] = 1 AND [ProductId] IS NULL)")]
    [DbIndex("UX_GrossDemand_SourceLine", nameof(GrossDemand.SourceType), nameof(GrossDemand.SourceUid), nameof(GrossDemand.SourceLineId), IsUnique = true, Filter = "[IsDeleted] = 0 AND [LifecycleStatus] = 0")]
    [DbIndex("IX_GrossDemand_Source", nameof(GrossDemand.SourceType), nameof(GrossDemand.SourceUid), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(GrossDemand.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(GrossDemand.ProductId), typeof(Product))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "SO", nameof(GrossDemand.SourceUid), typeof(SalesOrderHeader))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "SO", nameof(GrossDemand.SourceLineId), typeof(SalesOrderLine))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "CR", nameof(GrossDemand.SourceUid), typeof(AfterSalesHeader))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "CR", nameof(GrossDemand.SourceLineId), typeof(AfterSalesLine))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "MD", nameof(GrossDemand.SourceUid), typeof(GrossDemand))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "MD", nameof(GrossDemand.SourceLineId), typeof(GrossDemand))]
    public sealed partial class GrossDemand { }

    [DbCheck("CK_NetDemand_State", "[LifecycleStatus] IN (0, 8, 9) AND [DecompositionRevision] > 0 AND [LineNumber] > 0")]
    [DbCheck("CK_NetDemand_Quantities", "[GrossQty] > 0 AND [NetQty] >= 0")]
    [DbCheck("CK_NetDemand_Delivery", "([DeliveryMode] = 0 AND [DeliveryTargetNetDemandId] IS NULL) OR ([DeliveryMode] = 1 AND [ParentNetDemandId] IS NOT NULL AND [DeliveryTargetNetDemandId] IS NOT NULL)")]
    [DbIndex("UX_NetDemand_Gross_Revision_Line", nameof(NetDemand.GrossDemandId), nameof(NetDemand.DecompositionRevision), nameof(NetDemand.LineNumber), IsUnique = true)]
    [DbIndex("IX_NetDemand_Gross_Active", nameof(NetDemand.GrossDemandId), nameof(NetDemand.LifecycleStatus), nameof(NetDemand.IsDeleted))]
    [DbIndex("IX_NetDemand_Root", nameof(NetDemand.RootNetDemandId), nameof(NetDemand.LifecycleStatus), nameof(NetDemand.IsDeleted))]
    [DbIndex("IX_NetDemand_DeliveryTarget", nameof(NetDemand.DeliveryTargetNetDemandId), Filter = "[DeliveryTargetNetDemandId] IS NOT NULL")]
    [LogicalReference(nameof(NetDemand.GrossDemandId), typeof(GrossDemand), IsRequired = true)]
    [LogicalReference(nameof(NetDemand.ParentNetDemandId), typeof(NetDemand))]
    [LogicalReference(nameof(NetDemand.RootNetDemandId), typeof(NetDemand), IsRequired = true)]
    [LogicalReference(nameof(NetDemand.DeliveryTargetNetDemandId), typeof(NetDemand))]
    [LogicalReference(nameof(NetDemand.ProductId), typeof(Product))]
    [LogicalReference(nameof(NetDemand.SuggestedPurchaseSupplierId), typeof(Supplier))]
    [LogicalReference(nameof(NetDemand.BomLineId), typeof(Bom))]
    public sealed partial class NetDemand { }

    [DbCheck("CK_DemandExecutionLink_Target", "[TargetType] = N'PO'")]
    [DbCheck("CK_DemandExecutionLink_Quantity", "[AllocatedQty] > 0 AND [ReleasedQty] >= 0 AND [ReleasedQty] <= [AllocatedQty]")]
    [DbCheck("CK_DemandExecutionLink_State", "([LinkStatus] = 0 AND [ReleasedQty] < [AllocatedQty]) OR ([LinkStatus] IN (8, 9) AND [ReleasedQty] = [AllocatedQty])")]
    [DbCheck("CK_DemandExecutionLink_NotDeleted", "[IsDeleted] = 0")]
    [DbIndex("UX_DemandExecutionLink_CommandItem", nameof(DemandExecutionLink.CommandItemId), IsUnique = true)]
    [DbIndex("IX_DemandExecutionLink_NetDemand_Remaining", nameof(DemandExecutionLink.NetDemandId), nameof(DemandExecutionLink.ReleasedQty), nameof(DemandExecutionLink.AllocatedQty))]
    [DbIndex("IX_DemandExecutionLink_Target", nameof(DemandExecutionLink.TargetType), nameof(DemandExecutionLink.TargetLineId))]
    [DbImmutableColumns(nameof(DemandExecutionLink.NetDemandId), nameof(DemandExecutionLink.ExecutionRequestId), nameof(DemandExecutionLink.CommandItemId), nameof(DemandExecutionLink.TargetType), nameof(DemandExecutionLink.TargetHeadId), nameof(DemandExecutionLink.TargetNo), nameof(DemandExecutionLink.TargetLineId), nameof(DemandExecutionLink.AllocatedQty))]
    [LogicalReference(nameof(DemandExecutionLink.NetDemandId), typeof(NetDemand), IsRequired = true)]
    [PolymorphicLogicalReference(nameof(DemandExecutionLink.TargetType), "PO", nameof(DemandExecutionLink.TargetHeadId), typeof(PurchaseOrderHead))]
    [PolymorphicLogicalReference(nameof(DemandExecutionLink.TargetType), "PO", nameof(DemandExecutionLink.TargetLineId), typeof(PurchaseOrderLine))]
    public sealed partial class DemandExecutionLink { }
}

namespace SwiftExport.Core.Entities.Procurement
{
    [DbAppendOnly]
    [DbCheck("CK_ProcurementCommandReceipt_Result", "ISJSON([ResultJson]) = 1")]
    [DbIndex("UX_ProcurementCommandReceipt_Command", nameof(ProcurementCommandReceipt.CommandId), nameof(ProcurementCommandReceipt.Operation), IsUnique = true)]
    [LogicalReference(nameof(ProcurementCommandReceipt.ActorUid), typeof(SysUser), IsRequired = true)]
    [LogicalReference(nameof(ProcurementCommandReceipt.PurchaseOrderId), typeof(PurchaseOrderHead), IsRequired = true)]
    public sealed partial class ProcurementCommandReceipt { }

    [DbAppendOnly]
    [DbIndex("UX_PurchaseConfirmationLine_Command_Line", nameof(PurchaseConfirmationLine.CommandId), nameof(PurchaseConfirmationLine.PurchaseOrderLineId), IsUnique = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.PurchaseOrderId), typeof(PurchaseOrderHead), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.PurchaseOrderLineId), typeof(PurchaseOrderLine), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.RootProductId), typeof(Product), IsRequired = true)]
    public sealed partial class PurchaseConfirmationLine { }

    [DbAppendOnly]
    [DbCheck("CK_PurchaseConfirmationNode_Ratio", "[Numerator] > 0 AND [Denominator] > 0 AND [CumulativeRatio] > 0")]
    [DbIndex("IX_PurchaseConfirmationNode_Line", nameof(PurchaseConfirmationNode.ConfirmationLineId))]
    [DbIndex("IX_PurchaseConfirmationNode_Product", nameof(PurchaseConfirmationNode.ComponentProductId))]
    [LogicalReference(nameof(PurchaseConfirmationNode.ConfirmationLineId), typeof(PurchaseConfirmationLine), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.RootProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ParentProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ComponentProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.BomLineId), typeof(Bom), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ConsumptionSupplierId), typeof(Supplier))]
    public sealed partial class PurchaseConfirmationNode { }

    [DbCheck("CK_PurchaseOrderHead_Status", "[ConfirmationStatus] IN (0, 1) AND [LifecycleStatus] IN (0, 8, 9)")]
    [DbCheck("CK_PurchaseOrderHead_Discount", "[DiscountRate] IS NULL OR [DiscountRate] >= 0")]
    [DbIndex("UX_PurchaseOrderHead_OrderNo", nameof(PurchaseOrderHead.OrderNo), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_PurchaseOrderHead_ExecutionRequest", nameof(PurchaseOrderHead.ExecutionRequestId), nameof(PurchaseOrderHead.SupplierId), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(PurchaseOrderHead.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(PurchaseOrderHead.SupplierId), typeof(Supplier), IsRequired = true)]
    [LogicalReference(nameof(PurchaseOrderHead.ActiveConfirmationId), typeof(PurchaseConfirmationLine))]
    [DbImmutableColumns(nameof(PurchaseOrderHead.OriginKind), nameof(PurchaseOrderHead.ExecutionRequestId))]
    public sealed partial class PurchaseOrderHead { }

    [DbCheck("CK_PurchaseOrderLine_LineNumber", "[LineNumber] > 0")]
    [DbCheck("CK_PurchaseOrderLine_Quantity", "[QTY] > 0")]
    [DbIndex("UX_PurchaseOrderLine_Head_Line", nameof(PurchaseOrderLine.PurchaseOrderId), nameof(PurchaseOrderLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_PurchaseOrderLine_Product", nameof(PurchaseOrderLine.ProductId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(PurchaseOrderLine.PurchaseOrderId), typeof(PurchaseOrderHead), IsRequired = true)]
    [LogicalReference(nameof(PurchaseOrderLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(PurchaseOrderLine.DeliveryTargetSupplierId), typeof(Supplier))]
    public sealed partial class PurchaseOrderLine { }

    [DbCheck("CK_SupplierProgress_Quantity", "[ReadyQty] IS NULL OR [ReadyQty] >= 0")]
    [DbIndex("UX_SupplierProgress_Line_Sequence", nameof(SupplierProgressReport.PurchaseOrderLineId), nameof(SupplierProgressReport.Sequence), IsUnique = true)]
    [DbIndex("UX_SupplierProgress_Command", nameof(SupplierProgressReport.CommandId), IsUnique = true)]
    [LogicalReference(nameof(SupplierProgressReport.PurchaseOrderLineId), typeof(PurchaseOrderLine), IsRequired = true)]
    public sealed partial class SupplierProgressReport { }
}

namespace SwiftExport.Core.Entities.Inventory
{
    [DbCheck("CK_WarehouseSite_Type", "[SiteType] IN (0, 1)")]
    [DbIndex("UX_WarehouseSite_Code", nameof(WarehouseSite.Code), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_WarehouseSite_ActiveSupplier", nameof(WarehouseSite.SupplierId), nameof(WarehouseSite.SiteType), IsUnique = true, Filter = "[IsDeleted] = 0 AND [SupplierId] IS NOT NULL AND [IsActive] = 1")]
    [LogicalReference(nameof(WarehouseSite.SupplierId), typeof(Supplier))]
    public sealed partial class WarehouseSite { }

    [DbCheck("CK_InventoryBalance_Sequence", "[LastLedgerSequence] >= 0")]
    [DbIndex("UX_InventoryBalance_Warehouse_Product", nameof(InventoryBalance.WarehouseSiteId), nameof(InventoryBalance.ProductId), IsUnique = true)]
    [LogicalReference(nameof(InventoryBalance.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    [LogicalReference(nameof(InventoryBalance.ProductId), typeof(Product), IsRequired = true)]
    public sealed partial class InventoryBalance { }

    [DbAppendOnly]
    [DbCheck("CK_InventoryLedger_Immutable", "[IsDeleted] = 0")]
    [DbIndex("UX_InventoryLedger_CommandItem", nameof(InventoryLedgerEntry.CommandItemId), IsUnique = true)]
    [DbIndex("UX_InventoryLedger_Sequence", nameof(InventoryLedgerEntry.Sequence), IsUnique = true)]
    [DbIndex("UX_InventoryLedger_ReversalOf", nameof(InventoryLedgerEntry.ReversalOfLedgerId), IsUnique = true, Filter = "[ReversalOfLedgerId] IS NOT NULL")]
    [DbIndex("UX_InventoryLedger_ReplacementOf", nameof(InventoryLedgerEntry.ReplacementOfLedgerId), IsUnique = true, Filter = "[ReplacementOfLedgerId] IS NOT NULL")]
    [DbIndex("IX_InventoryLedger_Warehouse_Product_Sequence", nameof(InventoryLedgerEntry.WarehouseSiteId), nameof(InventoryLedgerEntry.ProductId), nameof(InventoryLedgerEntry.Sequence))]
    [LogicalReference(nameof(InventoryLedgerEntry.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    [LogicalReference(nameof(InventoryLedgerEntry.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(InventoryLedgerEntry.ReversalOfLedgerId), typeof(InventoryLedgerEntry))]
    [LogicalReference(nameof(InventoryLedgerEntry.ReplacementOfLedgerId), typeof(InventoryLedgerEntry))]
    public sealed partial class InventoryLedgerEntry { }

    [DbCheck("CK_InboundDocument_Status", "[LifecycleStatus] IN (0, 8, 9) AND [PostingStatus] IN (0, 1, 8)")]
    [DbIndex("UX_InboundDocument_No", nameof(InboundDocument.DocumentNo), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_InboundDocument_Command", nameof(InboundDocument.CommandId), IsUnique = true, Filter = "[CommandId] IS NOT NULL")]
    [LogicalReference(nameof(InboundDocument.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    public sealed partial class InboundDocument { }

    [DbCheck("CK_InboundLine_Line", "[LineNumber] > 0")]
    [DbCheck("CK_InboundLine_Quantity", "[Quantity] > 0")]
    [DbIndex("UX_InboundLine_Head_Line", nameof(InboundDocumentLine.InboundDocumentId), nameof(InboundDocumentLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_InboundLine_Source", nameof(InboundDocumentLine.SourceLineId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(InboundDocumentLine.InboundDocumentId), typeof(InboundDocument), IsRequired = true)]
    [LogicalReference(nameof(InboundDocumentLine.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(InboundDocumentLine.OriginalLineId), typeof(InboundDocumentLine))]
    public sealed partial class InboundDocumentLine { }

    [DbCheck("CK_OutboundDocument_Status", "[LifecycleStatus] IN (0, 8, 9) AND [PostingStatus] IN (1, 8)")]
    [DbIndex("UX_OutboundDocument_No", nameof(OutboundDocument.DocumentNo), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_OutboundDocument_Command", nameof(OutboundDocument.CommandId), IsUnique = true)]
    [LogicalReference(nameof(OutboundDocument.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    public sealed partial class OutboundDocument { }

    [DbCheck("CK_OutboundLine_Line", "[LineNumber] > 0")]
    [DbCheck("CK_OutboundLine_Quantity", "[Quantity] > 0")]
    [DbIndex("UX_OutboundLine_Head_Line", nameof(OutboundDocumentLine.OutboundDocumentId), nameof(OutboundDocumentLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("UX_OutboundLine_CommandItem", nameof(OutboundDocumentLine.CommandItemId), IsUnique = true)]
    [LogicalReference(nameof(OutboundDocumentLine.OutboundDocumentId), typeof(OutboundDocument), IsRequired = true)]
    [LogicalReference(nameof(OutboundDocumentLine.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(OutboundDocumentLine.OriginalLineId), typeof(OutboundDocumentLine))]
    public sealed partial class OutboundDocumentLine { }
}

namespace SwiftExport.Core.Entities.Shipping
{
    [DbCheck("CK_PackingListHead_Status", "[LifecycleStatus] IN (0, 8, 9) AND [PostingStatus] IN (0, 1, 8)")]
    [DbIndex("UX_PackingListHead_ActiveNo", nameof(PackingListHead.PackingListNo), IsUnique = true, Filter = "[IsDeleted] = 0 AND [LifecycleStatus] = 0")]
    [DbIndex("UX_PackingListHead_CreateCommand", nameof(PackingListHead.CreateCommandId), IsUnique = true)]
    [DbIndex("IX_PackingListHead_CustomerDate", nameof(PackingListHead.CustomerId), nameof(PackingListHead.InvoiceDate), nameof(PackingListHead.Uid), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(PackingListHead.CustomerId), typeof(Customer), IsRequired = true)]
    public sealed partial class PackingListHead { }

    [DbCheck("CK_PackingListLine_Line", "[LineNumber] > 0")]
    [DbCheck("CK_PackingListLine_Qty", "[Qty] > 0")]
    [DbCheck("CK_PackingListLine_ProductMode", "([IsNonStandardSnapshot] = 0 AND [ProductId] IS NOT NULL) OR ([IsNonStandardSnapshot] = 1 AND [ProductId] IS NULL AND [WarehouseSiteId] IS NULL)")]
    [DbCheck("CK_PackingListLine_Measures", "([PcsPerPackage] IS NULL OR [PcsPerPackage] > 0) AND ([PackageCount] IS NULL OR [PackageCount] >= 0) AND ([NwPerPackage] IS NULL OR [NwPerPackage] >= 0) AND ([GwPerPackage] IS NULL OR [GwPerPackage] >= 0) AND ([CbmPerPackage] IS NULL OR [CbmPerPackage] >= 0)")]
    [DbIndex("UX_PackingListLine_Head_Line", nameof(PackingListLine.PackingListHeadId), nameof(PackingListLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [DbIndex("IX_PackingListLine_GrossDemand", nameof(PackingListLine.GrossDemandId), nameof(PackingListLine.PackingListHeadId), Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(PackingListLine.PackingListHeadId), typeof(PackingListHead), IsRequired = true)]
    [LogicalReference(nameof(PackingListLine.GrossDemandId), typeof(DemandPlanning.GrossDemand), IsRequired = true)]
    [LogicalReference(nameof(PackingListLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(PackingListLine.WarehouseSiteId), typeof(Inventory.WarehouseSite))]
    public sealed partial class PackingListLine { }
}

namespace SwiftExport.Core.Entities.QualityControl
{
    [DbCheck("CK_AfterSalesHeader_Status", "[DocumentStatus] IN (0, 8, 9) AND [ConfirmationStatus] IN (0, 1)")]
    [DbIndex("UX_AfterSalesHeader_No", nameof(AfterSalesHeader.AfterSalesNo), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(AfterSalesHeader.CustomerId), typeof(Customer), IsRequired = true)]
    [LogicalReference(nameof(AfterSalesHeader.OwnerId), typeof(SysUser))]
    public sealed partial class AfterSalesHeader { }

    [DbCheck("CK_AfterSalesLine_Line", "[LineNumber] > 0")]
    [DbCheck("CK_AfterSalesLine_Quantity", "[Quantity] IS NULL OR [Quantity] > 0")]
    [DbIndex("UX_AfterSalesLine_Head_Line", nameof(AfterSalesLine.HeaderId), nameof(AfterSalesLine.LineNumber), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(AfterSalesLine.HeaderId), typeof(AfterSalesHeader), IsRequired = true)]
    [LogicalReference(nameof(AfterSalesLine.OriginalSalesOrderLineId), typeof(SalesOrderLine))]
    [LogicalReference(nameof(AfterSalesLine.ResponsibleSupplierId), typeof(Supplier))]
    [LogicalReference(nameof(AfterSalesLine.IssueProductId), typeof(Product))]
    [LogicalReference(nameof(AfterSalesLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(AfterSalesLine.IssueColorId), typeof(ProductColor))]
    [LogicalReference(nameof(AfterSalesLine.ColorId), typeof(ProductColor))]
    public sealed partial class AfterSalesLine { }
}

namespace SwiftExport.Core.Entities.Systems
{
    [DbIndex("UX_DocumentSequence_Type_Period", nameof(DocumentSequence.DocType), nameof(DocumentSequence.YearMonth), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class DocumentSequence { }

    [DbCheck("CK_SysDicCurrency_Precision", "[Precision] >= 0 AND [Precision] <= 8")]
    [DbIndex("UX_SysDicCurrency_Code", nameof(SysDicCurrency.CurrencyCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class SysDicCurrency { }

    [DbCheck("CK_SysDicCurrencyExchange_Precision", "[Precision] >= 0 AND [Precision] <= 8")]
    [DbIndex("UX_SysDicCurrencyExchange_Pair", nameof(SysDicCurrencyExchange.FromCurrencyCode), nameof(SysDicCurrencyExchange.ToCurrencyCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class SysDicCurrencyExchange { }

    [DbIndex("IX_SysDictData_DictCode", nameof(SysDictData.DictCode), nameof(SysDictData.SortOrder), Filter = "[IsDeleted] = 0")]
    public partial class SysDictData { }

    [DbIndex("UX_SysDictType_DictCode", nameof(SysDictType.DictCode), IsUnique = true, Filter = "[IsDeleted] = 0")]
    [LogicalReference(nameof(SysDictType.ParentId), typeof(SysDictType))]
    public partial class SysDictType { }
}

namespace SwiftExport.Core.Entities.SystemEntities
{
    [DbIndex("UX_SysViewRoute_TargetViewKey", nameof(SysViewRoute.TargetViewKey), IsUnique = true, Filter = "[IsDeleted] = 0")]
    public partial class SysViewRoute { }
}
