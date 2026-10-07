using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.QualityControl;

namespace SwiftExport.Core.Entities.Auditing
{
    [DbAppendOnly]
    [DbCheck("CK_BusinessOperationLog_Immutable", "\"is_deleted\" = FALSE")]
    [DbCheck("CK_BusinessOperationLog_ChangedJson", "\"changed_fields_json\" IS NULL OR (\"changed_fields_json\" IS JSON OBJECT OR \"changed_fields_json\" IS JSON ARRAY)")]
    [DbCheck("CK_BusinessOperationLog_RelatedJson", "\"related_objects_json\" IS NULL OR (\"related_objects_json\" IS JSON OBJECT OR \"related_objects_json\" IS JSON ARRAY)")]
    [DbIndex("UX_BusinessOperationLog_Command", nameof(BusinessOperationLog.Module), nameof(BusinessOperationLog.OperationType), nameof(BusinessOperationLog.CommandId), IsUnique = true)]
    [DbIndex("IX_BusinessOperationLog_Aggregate", nameof(BusinessOperationLog.AggregateType), nameof(BusinessOperationLog.AggregateUid), nameof(BusinessOperationLog.OccurredDate))]
    public sealed partial class BusinessOperationLog { }
}

namespace SwiftExport.Core.Entities.Auth
{
    [DbIndex("UX_SysRoles_RoleCode", nameof(SysRole.RoleCode), IsUnique = true)]
    public partial class SysRole { }

    [DbIndex("UX_SysPermissionDefinitions_Key", nameof(SysPermission.PermissionKey), IsUnique = true)]
    public partial class SysPermission { }

    [DbIndex("UX_SysRolePermissions_Role_Permission", nameof(SysRolePermission.RoleId), nameof(SysRolePermission.PermissionId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_SysRolePermissions_Permission", nameof(SysRolePermission.PermissionId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SysRolePermission.RoleId), typeof(SysRole), IsRequired = true)]
    [LogicalReference(nameof(SysRolePermission.PermissionId), typeof(SysPermission), IsRequired = true)]
    public partial class SysRolePermission { }

    [DbCheck("CK_SysUser_CredentialVersion", "\"credential_version\" >= 1")]
    [DbIndex("UX_SysUser_UserName", nameof(SysUser.UserName), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class SysUser { }

    [DbIndex("UX_SysUserRoles_User_Role", nameof(SysUserRole.UserId), nameof(SysUserRole.RoleId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_SysUserRoles_Role", nameof(SysUserRole.RoleId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SysUserRole.UserId), typeof(SysUser), IsRequired = true)]
    [LogicalReference(nameof(SysUserRole.RoleId), typeof(SysRole), IsRequired = true)]
    public partial class SysUserRole { }

}

namespace SwiftExport.Core.Entities.MasterData
{
    [DbCheck("CK_Bom_PositiveRatio", "\"numerator\" > 0 AND \"denominator\" > 0")]
    [DbCheck("CK_Bom_ParentDiffersFromChild", "\"component_product_id\" IS NULL OR \"parent_product_id\" <> \"component_product_id\"")]
    [DbIndex("UX_Bom_Number_Version_Line", nameof(Bom.BomNo), nameof(Bom.BomVersion), nameof(Bom.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_Bom_ParentProduct", nameof(Bom.ParentProductId), nameof(Bom.BomVersion), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(Bom.ParentProductId), typeof(Product))]
    [LogicalReference(nameof(Bom.ComponentProductId), typeof(Product))]
    public partial class Bom { }

    [DbIndex("UX_Customer_CustomerCode", nameof(Customer.CustomerCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class Customer { }

    [DbIndex("UX_Product_Sku", nameof(Product.Sku), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbFreezeColumnOnReference(nameof(Product.Unit), typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.ProductId))]
    [DbFreezeColumnOnReference(nameof(Product.Unit), typeof(SwiftExport.Core.Entities.Inventory.InventoryLedgerEntry), nameof(SwiftExport.Core.Entities.Inventory.InventoryLedgerEntry.ProductId))]
    [DbIndex("IX_Product_DefaultSupplierId", nameof(Product.DefaultSupplierId), Filter = "\"is_deleted\" = FALSE AND \"default_supplier_id\" IS NOT NULL")]
    [DbIndex("IX_Product_Master_Color", nameof(Product.ProductMasterId), nameof(Product.ColorId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(Product.ProductMasterId), typeof(ProductMaster))]
    [LogicalReference(nameof(Product.ColorId), typeof(ProductColor), IsRequired = true)]
    [LogicalReference(nameof(Product.DefaultSupplierId), typeof(Supplier))]
    [LogicalReference(nameof(Product.DefaultWarehouseSiteId), typeof(SwiftExport.Core.Entities.Inventory.WarehouseSite))]
    [LogicalReference(nameof(Product.ManufacturerId), typeof(Supplier))]
    public partial class Product { }

    [DbIndex("UX_ProductCategory_CategoryCode", nameof(ProductCategory.CategoryCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"category_code\" IS NOT NULL")]
    [LogicalReference(nameof(ProductCategory.ParentId), typeof(ProductCategory))]
    public partial class ProductCategory { }

    [DbIndex("UX_ProductColor_ColorCodeUnique", nameof(ProductColor.ColorCodeUnique), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class ProductColor { }

    [DbIndex("IX_ProductDetails_ProductId", nameof(ProductDetail.ProductId), Filter = "\"is_deleted\" = FALSE AND \"product_id\" IS NOT NULL")]
    [DbIndex("IX_ProductDetails_ProductSKU", nameof(ProductDetail.ProductSKU), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(ProductDetail.ProductId), typeof(Product))]
    public partial class ProductDetail { }

    [DbCheck("CK_ProductMaster_Pcs", "\"default_pcs_per_package\" > 0")]
    [DbCheck("CK_ProductMaster_Measures", "( \"default_net_weight\" IS NULL OR \"default_net_weight\" >= 0 ) AND ( \"default_gross_weight\" IS NULL OR \"default_gross_weight\" >= 0 ) AND ( \"default_cbm\" IS NULL OR \"default_cbm\" >= 0 )")]
    [DbIndex("UX_ProductMaster_MasterSku", nameof(ProductMaster.MasterSku), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_ProductMaster_Category", nameof(ProductMaster.CategoryId), Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_ProductMaster_DefaultSupplier", nameof(ProductMaster.DefaultSupplierId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(ProductMaster.CategoryId), typeof(ProductCategory), IsRequired = true)]
    [LogicalReference(nameof(ProductMaster.DefaultSupplierId), typeof(Supplier), IsRequired = true)]
    public partial class ProductMaster { }

    [DbCheck("CK_ProductNeutral_ExchangeRate", "\"default_exchange_rate\" IS NULL OR \"default_exchange_rate\" > 0")]
    [DbIndex("UX_ProductNeutral_ProductCode", nameof(ProductNeutral.ProductCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_ProductNeutral_Master_Color", nameof(ProductNeutral.ProductMasterId), nameof(ProductNeutral.ColorId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_ProductNeutral_ProductMaster", nameof(ProductNeutral.ProductMasterId), Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_ProductNeutral_Color", nameof(ProductNeutral.ColorId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(ProductNeutral.ProductMasterId), typeof(ProductMaster), IsRequired = true)]
    [LogicalReference(nameof(ProductNeutral.ColorId), typeof(ProductColor), IsRequired = true)]
    public partial class ProductNeutral { }

    [DbCheck("CK_ProductPackage_Pcs", "\"pcs_per_package\" > 0")]
    [DbCheck("CK_ProductPackage_Measures", "( \"length_cm\" IS NULL OR \"length_cm\" >= 0 ) AND ( \"width_cm\" IS NULL OR \"width_cm\" >= 0 ) AND ( \"height_cm\" IS NULL OR \"height_cm\" >= 0 ) AND ( \"nw_kg\" IS NULL OR \"nw_kg\" >= 0 ) AND ( \"gw_kg\" IS NULL OR \"gw_kg\" >= 0 ) AND ( \"cbm\" IS NULL OR \"cbm\" >= 0 )")]
    [DbIndex("UX_ProductPackage_Product_Type", nameof(ProductPackage.ProductId), nameof(ProductPackage.PackageType), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"product_id\" IS NOT NULL")]
    [LogicalReference(nameof(ProductPackage.ProductId), typeof(Product))]
    public partial class ProductPackage { }

    [DbIndex("UX_Supplier_SupplierCode", nameof(Supplier.SupplierCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class Supplier { }
}

namespace SwiftExport.Core.Entities.Relations
{
    [DbCheck("CK_CustomerPoConfig_DefaultDiscountRate", "\"default_discount_rate\" IS NULL OR \"default_discount_rate\" >= 0")]
    [DbIndex("UX_CustomerPoConfig_Codes", nameof(CustomerPoConfig.CustomerCode), nameof(CustomerPoConfig.SupplierCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(CustomerPoConfig.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(CustomerPoConfig.SupplierId), typeof(Supplier))]
    public partial class CustomerPoConfig { }

    [DbIndex("UX_CustomerProduct_Customer_CustomerSku", nameof(CustomerProduct.CustomerId), nameof(CustomerProduct.CustomerSku), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_CustomerProduct_SkuId", nameof(CustomerProduct.SkuId), Filter = "\"is_deleted\" = FALSE AND \"sku_id\" IS NOT NULL")]
    [LogicalReference(nameof(CustomerProduct.CustomerId), typeof(Customer), IsRequired = true)]
    [LogicalReference(nameof(CustomerProduct.SkuId), typeof(Product))]
    public partial class CustomerProduct { }

    [DbIndex("UX_SupplierProduct_Supplier_Product", nameof(SupplierProduct.SupplierId), nameof(SupplierProduct.SkuId), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"sku_id\" IS NOT NULL")]
    [DbIndex("IX_SupplierProduct_Product", nameof(SupplierProduct.SkuId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SupplierProduct.SupplierId), typeof(Supplier), IsRequired = true)]
    [LogicalReference(nameof(SupplierProduct.SkuId), typeof(Product), IsRequired = false)]
    public partial class SupplierProduct { }
}

namespace SwiftExport.Core.Entities.SalesOrders
{
    [DbCheck("CK_SalesOrderHeader_Status", "\"document_status\" IN ( 0 , 8 , 9 ) AND \"confirmation_status\" IN ( 0 , 1 )")]
    [DbIndex("UX_SalesOrderHeader_OrderNo", nameof(SalesOrderHeader.OrderNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_SalesOrderHeader_Customer_Date", nameof(SalesOrderHeader.CustomerId), nameof(SalesOrderHeader.OrderDate), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SalesOrderHeader.CustomerId), typeof(Customer))]
    public partial class SalesOrderHeader { }

    [DbCheck("CK_SalesOrderLine_LineNumber", "\"line_number\" > 0")]
    [DbCheck("CK_SalesOrderLine_Qty", "\"qty\" > 0")]
    [DbIndex("UX_SalesOrderLine_Head_Line", nameof(SalesOrderLine.SalesOrderId), nameof(SalesOrderLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SalesOrderLine.SalesOrderId), typeof(SalesOrderHeader), IsRequired = true)]
    [LogicalReference(nameof(SalesOrderLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(SalesOrderLine.CustomerProductId), typeof(Relations.CustomerProduct))]
    public partial class SalesOrderLine { }
}

namespace SwiftExport.Core.Entities.DemandPlanning
{
    [DbCheck("CK_GrossDemand_Lifecycle", "\"lifecycle_status\" IN ( 0 , 8 , 9 )")]
    [DbCheck("CK_GrossDemand_Quantity", "\"demand_qty\" > 0")]
    [DbCheck("CK_GrossDemand_StandardProduct", "( \"is_non_standard\" = FALSE AND \"product_id\" IS NOT NULL ) OR ( \"is_non_standard\" = TRUE AND \"product_id\" IS NULL )")]
    [DbIndex("UX_GrossDemand_SourceLine", nameof(GrossDemand.SourceType), nameof(GrossDemand.SourceUid), nameof(GrossDemand.SourceLineId), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"lifecycle_status\" = 0")]
    [DbIndex("IX_GrossDemand_Source", nameof(GrossDemand.SourceType), nameof(GrossDemand.SourceUid), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(GrossDemand.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(GrossDemand.ProductId), typeof(Product))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "SO", nameof(GrossDemand.SourceUid), typeof(SalesOrderHeader))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "SO", nameof(GrossDemand.SourceLineId), typeof(SalesOrderLine))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "CR", nameof(GrossDemand.SourceUid), typeof(AfterSalesHeader))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "CR", nameof(GrossDemand.SourceLineId), typeof(AfterSalesLine))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "MD", nameof(GrossDemand.SourceUid), typeof(GrossDemand))]
    [PolymorphicLogicalReference(nameof(GrossDemand.SourceType), "MD", nameof(GrossDemand.SourceLineId), typeof(GrossDemand))]
    public sealed partial class GrossDemand { }

    [DbCheck("CK_NetDemand_State", "\"lifecycle_status\" IN ( 0 , 8 , 9 ) AND \"decomposition_revision\" > 0 AND \"line_number\" > 0")]
    [DbCheck("CK_NetDemand_Quantities", "\"gross_qty\" > 0 AND \"net_qty\" >= 0")]
    [DbCheck("CK_NetDemand_Delivery", "( \"delivery_mode\" = 0 AND \"delivery_target_net_demand_id\" IS NULL ) OR ( \"delivery_mode\" = 1 AND \"parent_net_demand_id\" IS NOT NULL AND \"delivery_target_net_demand_id\" IS NOT NULL )")]
    [DbIndex("UX_NetDemand_Gross_Revision_Line", nameof(NetDemand.GrossDemandId), nameof(NetDemand.DecompositionRevision), nameof(NetDemand.LineNumber), IsUnique = true)]
    [DbIndex("IX_NetDemand_Gross_Active", nameof(NetDemand.GrossDemandId), nameof(NetDemand.LifecycleStatus), nameof(NetDemand.IsDeleted))]
    [DbIndex("IX_NetDemand_Root", nameof(NetDemand.RootNetDemandId), nameof(NetDemand.LifecycleStatus), nameof(NetDemand.IsDeleted))]
    [DbIndex("IX_NetDemand_DeliveryTarget", nameof(NetDemand.DeliveryTargetNetDemandId), Filter = "\"delivery_target_net_demand_id\" IS NOT NULL")]
    [LogicalReference(nameof(NetDemand.GrossDemandId), typeof(GrossDemand), IsRequired = true)]
    [LogicalReference(nameof(NetDemand.ParentNetDemandId), typeof(NetDemand))]
    [LogicalReference(nameof(NetDemand.RootNetDemandId), typeof(NetDemand), IsRequired = true)]
    [LogicalReference(nameof(NetDemand.DeliveryTargetNetDemandId), typeof(NetDemand))]
    [LogicalReference(nameof(NetDemand.ProductId), typeof(Product))]
    [LogicalReference(nameof(NetDemand.SuggestedPurchaseSupplierId), typeof(Supplier))]
    [LogicalReference(nameof(NetDemand.BomLineId), typeof(Bom))]
    public sealed partial class NetDemand { }

    [DbCheck("CK_DemandExecutionLink_Target", "\"target_type\" = 'PO'")]
    [DbCheck("CK_DemandExecutionLink_Quantity", "\"allocated_qty\" > 0 AND \"released_qty\" >= 0 AND \"released_qty\" <= \"allocated_qty\"")]
    [DbCheck("CK_DemandExecutionLink_State", "( \"link_status\" = 0 AND \"released_qty\" < \"allocated_qty\" ) OR ( \"link_status\" IN ( 8 , 9 ) AND \"released_qty\" = \"allocated_qty\" )")]
    [DbCheck("CK_DemandExecutionLink_NotDeleted", "\"is_deleted\" = FALSE")]
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
    [DbIndex("UX_PurchaseConfirmationLine_Command_Line", nameof(PurchaseConfirmationLine.CommandId), nameof(PurchaseConfirmationLine.PurchaseOrderLineId), IsUnique = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.PurchaseOrderId), typeof(PurchaseOrderHead), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.PurchaseOrderLineId), typeof(PurchaseOrderLine), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationLine.RootProductId), typeof(Product), IsRequired = true)]
    public sealed partial class PurchaseConfirmationLine { }

    [DbAppendOnly]
    [DbCheck("CK_PurchaseConfirmationNode_Ratio", "\"numerator\" > 0 AND \"denominator\" > 0 AND \"cumulative_ratio\" > 0")]
    [DbIndex("IX_PurchaseConfirmationNode_Line", nameof(PurchaseConfirmationNode.ConfirmationLineId))]
    [DbIndex("IX_PurchaseConfirmationNode_Product", nameof(PurchaseConfirmationNode.ComponentProductId))]
    [LogicalReference(nameof(PurchaseConfirmationNode.ConfirmationLineId), typeof(PurchaseConfirmationLine), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.RootProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ParentProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ComponentProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.BomLineId), typeof(Bom), IsRequired = true)]
    [LogicalReference(nameof(PurchaseConfirmationNode.ConsumptionSupplierId), typeof(Supplier))]
    public sealed partial class PurchaseConfirmationNode { }

    [DbCheck("CK_PurchaseOrderHead_Status", "\"confirmation_status\" IN ( 0 , 1 ) AND \"lifecycle_status\" IN ( 0 , 8 , 9 )")]
    [DbCheck("CK_PurchaseOrderHead_Discount", "\"discount_rate\" IS NULL OR \"discount_rate\" >= 0")]
    [DbIndex("UX_PurchaseOrderHead_OrderNo", nameof(PurchaseOrderHead.OrderNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_PurchaseOrderHead_ExecutionRequest", nameof(PurchaseOrderHead.ExecutionRequestId), nameof(PurchaseOrderHead.SupplierId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(PurchaseOrderHead.CustomerId), typeof(Customer))]
    [LogicalReference(nameof(PurchaseOrderHead.SupplierId), typeof(Supplier), IsRequired = true)]
    [DbImmutableColumns(nameof(PurchaseOrderHead.OriginKind), nameof(PurchaseOrderHead.ExecutionRequestId))]
    public sealed partial class PurchaseOrderHead { }

    [DbCheck("CK_PurchaseOrderLine_LineNumber", "\"line_number\" > 0")]
    [DbCheck("CK_PurchaseOrderLine_Quantity", "\"quantity\" > 0")]
    [DbIndex("UX_PurchaseOrderLine_Head_Line", nameof(PurchaseOrderLine.PurchaseOrderId), nameof(PurchaseOrderLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_PurchaseOrderLine_Product", nameof(PurchaseOrderLine.ProductId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(PurchaseOrderLine.PurchaseOrderId), typeof(PurchaseOrderHead), IsRequired = true)]
    [LogicalReference(nameof(PurchaseOrderLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(PurchaseOrderLine.DeliveryTargetSupplierId), typeof(Supplier))]
    public sealed partial class PurchaseOrderLine { }

    [DbCheck("CK_SupplierProgress_Quantity", "\"ready_qty\" IS NULL OR \"ready_qty\" >= 0")]
    [DbIndex("UX_SupplierProgress_Line_Sequence", nameof(SupplierProgressReport.PurchaseOrderLineId), nameof(SupplierProgressReport.Sequence), IsUnique = true)]
    [DbIndex("UX_SupplierProgress_Command", nameof(SupplierProgressReport.CommandId), IsUnique = true)]
    [LogicalReference(nameof(SupplierProgressReport.PurchaseOrderLineId), typeof(PurchaseOrderLine), IsRequired = true)]
    public sealed partial class SupplierProgressReport { }
}

namespace SwiftExport.Core.Entities.Inventory
{
    [DbCheck("CK_WarehouseSite_Type", "\"site_type\" IN ( 0 , 1 )")]
    [DbIndex("UX_WarehouseSite_Code", nameof(WarehouseSite.Code), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_WarehouseSite_ActiveSupplier", nameof(WarehouseSite.SupplierId), nameof(WarehouseSite.SiteType), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"supplier_id\" IS NOT NULL AND \"is_active\" = TRUE")]
    [LogicalReference(nameof(WarehouseSite.SupplierId), typeof(Supplier))]
    public sealed partial class WarehouseSite { }

    [DbCheck("CK_InventoryBalance_Sequence", "\"last_ledger_sequence\" >= 0")]
    [DbIndex("UX_InventoryBalance_Warehouse_Product", nameof(InventoryBalance.WarehouseSiteId), nameof(InventoryBalance.ProductId), IsUnique = true)]
    [LogicalReference(nameof(InventoryBalance.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    [LogicalReference(nameof(InventoryBalance.ProductId), typeof(Product), IsRequired = true)]
    public sealed partial class InventoryBalance { }

    [DbAppendOnly]
    [DbCheck("CK_InventoryLedger_Immutable", "\"is_deleted\" = FALSE")]
    [DbIndex("UX_InventoryLedger_CommandItem", nameof(InventoryLedgerEntry.CommandItemId), IsUnique = true)]
    [DbIndex("UX_InventoryLedger_Sequence", nameof(InventoryLedgerEntry.Sequence), IsUnique = true)]
    [DbIndex("UX_InventoryLedger_ReversalOf", nameof(InventoryLedgerEntry.ReversalOfLedgerId), IsUnique = true, Filter = "\"reversal_of_ledger_id\" IS NOT NULL")]
    [DbIndex("UX_InventoryLedger_ReplacementOf", nameof(InventoryLedgerEntry.ReplacementOfLedgerId), IsUnique = true, Filter = "\"replacement_of_ledger_id\" IS NOT NULL")]
    [DbIndex("IX_InventoryLedger_Warehouse_Product_Sequence", nameof(InventoryLedgerEntry.WarehouseSiteId), nameof(InventoryLedgerEntry.ProductId), nameof(InventoryLedgerEntry.Sequence))]
    [LogicalReference(nameof(InventoryLedgerEntry.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    [LogicalReference(nameof(InventoryLedgerEntry.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(InventoryLedgerEntry.ReversalOfLedgerId), typeof(InventoryLedgerEntry))]
    [LogicalReference(nameof(InventoryLedgerEntry.ReplacementOfLedgerId), typeof(InventoryLedgerEntry))]
    public sealed partial class InventoryLedgerEntry { }

    [DbCheck("CK_InboundDocument_Status", "\"lifecycle_status\" IN ( 0 , 8 , 9 ) AND \"posting_status\" IN ( 0 , 1 , 8 )")]
    [DbIndex("UX_InboundDocument_No", nameof(InboundDocument.DocumentNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_InboundDocument_Command", nameof(InboundDocument.CommandId), IsUnique = true, Filter = "\"command_id\" IS NOT NULL")]
    [LogicalReference(nameof(InboundDocument.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    public sealed partial class InboundDocument { }

    [DbCheck("CK_InboundLine_Line", "\"line_number\" > 0")]
    [DbCheck("CK_InboundLine_Quantity", "\"quantity\" > 0")]
    [DbIndex("UX_InboundLine_Head_Line", nameof(InboundDocumentLine.InboundDocumentId), nameof(InboundDocumentLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_InboundLine_Source", nameof(InboundDocumentLine.SourceLineId), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(InboundDocumentLine.InboundDocumentId), typeof(InboundDocument), IsRequired = true)]
    [LogicalReference(nameof(InboundDocumentLine.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(InboundDocumentLine.OriginalLineId), typeof(InboundDocumentLine))]
    public sealed partial class InboundDocumentLine { }

    // 自动流程先建未过账单再写流水，同事务更新为已过账；手工草稿也使用 0。
    [DbCheck("CK_OutboundDocument_Status", "\"lifecycle_status\" IN ( 0 , 8 , 9 ) AND \"posting_status\" IN ( 0 , 1 , 8 )")]
    [DbIndex("UX_OutboundDocument_No", nameof(OutboundDocument.DocumentNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_OutboundDocument_Command", nameof(OutboundDocument.CommandId), IsUnique = true)]
    [LogicalReference(nameof(OutboundDocument.WarehouseSiteId), typeof(WarehouseSite), IsRequired = true)]
    public sealed partial class OutboundDocument { }

    [DbCheck("CK_OutboundLine_Line", "\"line_number\" > 0")]
    [DbCheck("CK_OutboundLine_Quantity", "\"quantity\" > 0")]
    [DbIndex("UX_OutboundLine_Head_Line", nameof(OutboundDocumentLine.OutboundDocumentId), nameof(OutboundDocumentLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_OutboundLine_CommandItem", nameof(OutboundDocumentLine.CommandItemId), IsUnique = true)]
    [LogicalReference(nameof(OutboundDocumentLine.OutboundDocumentId), typeof(OutboundDocument), IsRequired = true)]
    [LogicalReference(nameof(OutboundDocumentLine.ProductId), typeof(Product), IsRequired = true)]
    [LogicalReference(nameof(OutboundDocumentLine.OriginalLineId), typeof(OutboundDocumentLine))]
    public sealed partial class OutboundDocumentLine { }
}

namespace SwiftExport.Core.Entities.Shipping
{
    [DbCheck("CK_PackingListHead_Status", "\"lifecycle_status\" IN ( 0 , 8 , 9 ) AND \"posting_status\" IN ( 0 , 1 , 8 )")]
    [DbIndex("UX_PackingListHead_ActiveNo", nameof(PackingListHead.PackingListNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE AND \"lifecycle_status\" = 0")]
    [DbIndex("UX_PackingListHead_CreateCommand", nameof(PackingListHead.CreateCommandId), IsUnique = true)]
    [DbIndex("IX_PackingListHead_CustomerDate", nameof(PackingListHead.CustomerId), nameof(PackingListHead.InvoiceDate), nameof(PackingListHead.Uid), Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(PackingListHead.CustomerId), typeof(Customer), IsRequired = true)]
    public sealed partial class PackingListHead { }

    [DbCheck("CK_PackingListLine_Line", "\"line_number\" > 0")]
    [DbCheck("CK_PackingListLine_Qty", "\"qty\" > 0")]
    [DbCheck("CK_PackingListLine_ProductMode", "( \"is_non_standard_snapshot\" = FALSE AND \"product_id\" IS NOT NULL ) OR ( \"is_non_standard_snapshot\" = TRUE AND \"product_id\" IS NULL )")]
    [DbCheck("CK_PackingListLine_Measures", "( \"pcs_per_package\" IS NULL OR \"pcs_per_package\" > 0 ) AND ( \"package_count\" IS NULL OR \"package_count\" >= 0 ) AND ( \"nw_per_package\" IS NULL OR \"nw_per_package\" >= 0 ) AND ( \"gw_per_package\" IS NULL OR \"gw_per_package\" >= 0 ) AND ( \"cbm_per_package\" IS NULL OR \"cbm_per_package\" >= 0 )")]
    [DbIndex("UX_PackingListLine_Head_Line", nameof(PackingListLine.PackingListHeadId), nameof(PackingListLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_PackingListLine_GrossDemand", nameof(PackingListLine.GrossDemandId), nameof(PackingListLine.PackingListHeadId), Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("IX_PackingListLine_PackingGroup", nameof(PackingListLine.PackingListHeadId), nameof(PackingListLine.PackingGroupId), IncludeColumns = new[] { nameof(PackingListLine.LineNumber) })]
    [LogicalReference(nameof(PackingListLine.PackingListHeadId), typeof(PackingListHead), IsRequired = true)]
    [LogicalReference(nameof(PackingListLine.GrossDemandId), typeof(DemandPlanning.GrossDemand))]
    [LogicalReference(nameof(PackingListLine.ProductId), typeof(Product))]
    public sealed partial class PackingListLine { }
}

namespace SwiftExport.Core.Entities.QualityControl
{
    [DbCheck("CK_AfterSalesHeader_Status", "\"document_status\" IN ( 0 , 8 , 9 ) AND \"confirmation_status\" IN ( 0 , 1 )")]
    [DbCheck("CK_AfterSalesHeader_Amount", "\"service_amount_cny\" IS NULL OR \"service_amount_cny\" >= 0")]
    [DbIndex("IX_AfterSalesHeader_Supplier", nameof(AfterSalesHeader.ResponsibleSupplierId), Filter = "\"is_deleted\" = FALSE")]
    [DbIndex("UX_AfterSalesHeader_No", nameof(AfterSalesHeader.AfterSalesNo), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(AfterSalesHeader.CustomerId), typeof(Customer), IsRequired = true)]
    [LogicalReference(nameof(AfterSalesHeader.OwnerId), typeof(SysUser))]
    [LogicalReference(nameof(AfterSalesHeader.OriginalSalesOrderLineId), typeof(SalesOrderLine))]
    [LogicalReference(nameof(AfterSalesHeader.ResponsibleSupplierId), typeof(Supplier))]
    [LogicalReference(nameof(AfterSalesHeader.IssueProductId), typeof(Product))]
    public sealed partial class AfterSalesHeader { }

    [DbCheck("CK_AfterSalesLine_Line", "\"line_number\" > 0")]
    [DbCheck("CK_AfterSalesLine_Quantity", "\"quantity\" IS NULL OR \"quantity\" >= 0")]
    [DbIndex("UX_AfterSalesLine_Head_Line", nameof(AfterSalesLine.HeaderId), nameof(AfterSalesLine.LineNumber), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(AfterSalesLine.HeaderId), typeof(AfterSalesHeader), IsRequired = true)]
    [LogicalReference(nameof(AfterSalesLine.ProductId), typeof(Product))]
    [LogicalReference(nameof(AfterSalesLine.ColorId), typeof(ProductColor))]
    public sealed partial class AfterSalesLine { }
}

namespace SwiftExport.Core.Entities.Systems.Numbering
{
    [DbIndex("UX_DocumentSequence_Type_Period", nameof(DocumentSequence.DocType), nameof(DocumentSequence.YearMonth), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class DocumentSequence { }
}

namespace SwiftExport.Core.Entities.Systems.Currencies
{
    [DbCheck("CK_SysDicCurrency_Precision", "\"precision\" >= 0 AND \"precision\" <= 8")]
    [DbIndex("UX_SysDicCurrency_Code", nameof(SysDicCurrency.CurrencyCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class SysDicCurrency { }
}

namespace SwiftExport.Core.Entities.Systems.ExchangeRates
{
    [DbCheck("CK_SysDicCurrencyExchange_Precision", "\"precision\" >= 0 AND \"precision\" <= 8")]
    [DbIndex("UX_SysDicCurrencyExchange_Pair", nameof(SysDicCurrencyExchange.FromCurrencyCode), nameof(SysDicCurrencyExchange.ToCurrencyCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    public partial class SysDicCurrencyExchange { }
}

namespace SwiftExport.Core.Entities.Systems.Dictionaries.Data
{
    [DbIndex("IX_SysDictData_DictCode", nameof(SysDictData.DictCode), nameof(SysDictData.SortOrder), Filter = "\"is_deleted\" = FALSE")]
    public partial class SysDictData { }
}

namespace SwiftExport.Core.Entities.Systems.Dictionaries.Types
{
    [DbIndex("UX_SysDictType_DictCode", nameof(SysDictType.DictCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
    [LogicalReference(nameof(SysDictType.ParentId), typeof(SysDictType))]
    public partial class SysDictType { }
}
