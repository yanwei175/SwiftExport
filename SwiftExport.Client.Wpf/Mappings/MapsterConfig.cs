using Mapster;
using System.Runtime.CompilerServices;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.Models;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExportClient.Mappings;

public sealed class MapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SourceDemandCandidateGroupDto, SourceDemandCandidateGroupModel>()
            .Map(dest => dest.SourceType, src => src.SourceType ?? string.Empty)
            .Map(dest => dest.SourceNo, src => src.SourceNo ?? string.Empty);

        config.NewConfig<MaterialDemandWorkspaceItemDto, MaterialDemandWorkspaceRowModel>()
            .Ignore(dest => dest.IsChecked)
            .Ignore(dest => dest.SuggestedPurchaseSupplierSelection)
            .Map(dest => dest.SuggestedPurchaseSupplierCode, src => src.SuggestedPurchaseSupplierCode ?? string.Empty)
            .Map(dest => dest.SuggestedPurchaseSupplierName, src => src.SuggestedPurchaseSupplierName ?? string.Empty)
            .Map(dest => dest.SuggestedDeliverySupplierCode, src => src.SuggestedDeliverySupplierCode ?? string.Empty)
            .Map(dest => dest.SuggestedDeliverySupplierName, src => src.SuggestedDeliverySupplierName ?? string.Empty)
            .Map(dest => dest.SuggestedDeliverySupplierHint, src => src.SuggestedDeliverySupplierHint ?? string.Empty)
            .Map(dest => dest.SourceType, src => src.SourceType ?? string.Empty)
            .Map(dest => dest.SourceNo, src => src.SourceNo ?? string.Empty)
            .Map(dest => dest.RootSku, src => src.RootSku ?? string.Empty)
            .Map(dest => dest.Sku, src => src.Sku ?? string.Empty)
            .Map(dest => dest.LinkSku, src => src.LinkSku ?? string.Empty)
            .Map(dest => dest.CustomerSku, src => src.CustomerSku ?? string.Empty)
            .Map(dest => dest.Description, src => src.Description ?? string.Empty)
            .Map(dest => dest.Color, src => src.Color ?? string.Empty)
            .Map(dest => dest.Specification, src => src.Specification ?? string.Empty)
            .Map(dest => dest.ImagePath, src => src.ImagePath ?? string.Empty)
            .Map(dest => dest.MaterialType, src => src.MaterialType ?? string.Empty)
            .Map(dest => dest.Unit, src => src.Unit ?? string.Empty)
            .Map(dest => dest.SupplyType, src => src.SupplyType ?? string.Empty)
            .Map(dest => dest.PurchaseOrderNos, src => src.PurchaseOrderNos ?? string.Empty);

        config.NewConfig<PurchaseDraftCandidateSourceGroupDto, PurchaseCandidateSourceGroupModel>()
            .Map(dest => dest.SourceType, src => src.SourceType ?? string.Empty)
            .Map(dest => dest.SourceNo, src => src.SourceNo ?? string.Empty)
            .Map(dest => dest.SourceUid, src => (Guid?)src.SourceUid)
            .Ignore(dest => dest.IsAllSources);

        config.NewConfig<PurchaseDraftPreviewLineDto, PurchaseDraftPreviewLineModel>();

        config.NewConfig<ProductColorDto, ProductColorFilterOption>()
            .Ignore(dest => dest.IsChecked);

        config.NewConfig<ProductCategoryDto, ProductCategoryTreeItemModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.CategoryName, src => src.CategoryName ?? string.Empty)
            .Map(dest => dest.FakeName, src => src.FakeName ?? string.Empty)
            .Map(dest => dest.CategoryCode, src => src.CategoryCode ?? string.Empty);

        config.NewConfig<ProductCategoryTreeItemModel, ProductCategoryDto>()
            .Map(dest => dest.ParentId, src => src.ParentId == Guid.Empty ? null : src.ParentId)
            .Map(dest => dest.CategoryName, src => src.CategoryName.Trim())
            .Map(dest => dest.FakeName, src => src.FakeName.Trim())
            .Map(dest => dest.CategoryCode, src => src.CategoryCode.Trim());

        config.NewConfig<ProductDto, ProductLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.CnyPrice, src => src.CNYPrice)
            .Map(dest => dest.UsdPrice, src => src.USDPrice);

        config.NewConfig<ProductLineModel, ProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.CNYPrice, src => src.CnyPrice)
            .Map(dest => dest.USDPrice, src => src.UsdPrice);

        config.NewConfig<ProductMasterListItemDto, ProductMasterLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.MasterSku, src => src.Data.MasterSku)
            .Map(dest => dest.DefaultDescriptionEn, src => src.Data.DefaultDescriptionEn)
            .Map(dest => dest.DefaultDescriptionZh, src => src.Data.DefaultDescriptionZh)
            .Map(dest => dest.DefaultFeaturesEn, src => src.Data.DefaultFeaturesEn)
            .Map(dest => dest.DefaultFeaturesZh, src => src.Data.DefaultFeaturesZh)
            .Map(dest => dest.DefaultPcsPerPackage, src => src.Data.DefaultPcsPerPackage)
            .Map(dest => dest.DefaultNetWeight, src => src.Data.DefaultNetWeight)
            .Map(dest => dest.DefaultGrossWeight, src => src.Data.DefaultGrossWeight)
            .Map(dest => dest.DefaultCbm, src => src.Data.DefaultCbm)
            .Map(dest => dest.ProductType, src => src.Data.ProductType)
            .Map(dest => dest.ProductTag, src => src.Data.ProductTag)
            .Map(dest => dest.CategoryId, src => src.Data.CategoryId)
            .Map(dest => dest.DefaultSupplierId, src => src.Data.DefaultSupplierId)
            .Map(dest => dest.MainImagePath, src => src.MainImagePath ?? string.Empty)
            .Map(dest => dest.DefaultSalesPriceUsd, src => src.DefaultSalesPriceUsd)
            .Map(dest => dest.DefaultPurchasePriceCny, src => src.DefaultPurchasePriceCny);

        config.NewConfig<ProductMasterListItemDto, ProductCategoryAssignmentLineModel>()
            .Map(dest => dest.MainImagePath, src => src.MainImagePath)
            .Map(dest => dest.MasterSku, src => src.Data.MasterSku)
            .Map(dest => dest.DefaultDescriptionZh, src => src.Data.DefaultDescriptionZh)
            .Map(dest => dest.OriginalCategoryId, src => src.Data.CategoryId)
            .Map(dest => dest.OriginalCategoryName, src => string.IsNullOrWhiteSpace(src.CategoryName) ? "未分类" : src.CategoryName)
            .Ignore(dest => dest.IsChecked);

        config.NewConfig<ProductMasterLineModel, ProductMasterWriteDto>();
        config.NewConfig<ProductMasterLineModel, ProductMasterListItemDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.Data, src => src);

        config.NewConfig<ProductMasterDetailDto, ProductMasterEditModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.MasterSku, src => src.Master.MasterSku)
            .Map(dest => dest.DefaultDescriptionEn, src => src.Master.DefaultDescriptionEn)
            .Map(dest => dest.DefaultDescriptionZh, src => src.Master.DefaultDescriptionZh)
            .Map(dest => dest.DefaultFeaturesEn, src => src.Master.DefaultFeaturesEn)
            .Map(dest => dest.DefaultFeaturesZh, src => src.Master.DefaultFeaturesZh)
            .Map(dest => dest.DefaultPcsPerPackage, src => src.Master.DefaultPcsPerPackage)
            .Map(dest => dest.DefaultNetWeight, src => src.Master.DefaultNetWeight)
            .Map(dest => dest.DefaultGrossWeight, src => src.Master.DefaultGrossWeight)
            .Map(dest => dest.DefaultCbm, src => src.Master.DefaultCbm)
            .Map(dest => dest.ProductType, src => src.Master.ProductType)
            .Map(dest => dest.ProductTag, src => src.Master.ProductTag)
            .Map(dest => dest.CategoryId, src => src.Master.CategoryId)
            .Map(dest => dest.DefaultSupplierId, src => src.Master.DefaultSupplierId);

        config.NewConfig<ProductMasterEditModel, ProductMasterWriteDto>();
        config.NewConfig<ProductMasterEditModel, ProductMasterDetailDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.Master, src => src)
            .Ignore(dest => dest.Neutrals);

        config.NewConfig<ProductNeutralDto, ProductNeutralLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.ProductCode, src => src.Data.ProductCode)
            .Map(dest => dest.ColorId, src => src.Data.ColorId)
            .Map(dest => dest.DefaultSalesPriceUsd, src => src.Data.DefaultSalesPriceUsd)
            .Map(dest => dest.DefaultExchangeRate, src => src.Data.DefaultExchangeRate)
            .Map(dest => dest.ProfitMargin, src => src.Data.ProfitMargin)
            .Map(dest => dest.DefaultPurchasePriceCny, src => src.Data.DefaultPurchasePriceCny)
            .Map(dest => dest.ImagePath, src => src.Data.ImagePath)
            .Map(dest => dest.IsMainImage, src => src.Data.IsMainImage);

        config.NewConfig<ProductNeutralLineModel, ProductNeutralWriteDto>();
        config.NewConfig<ProductNeutralLineModel, ProductNeutralDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.Data, src => src);

        config.NewConfig<ProductNeutralManagerListItemDto, ProductNeutralManagerLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.ProductMasterId, src => src.Data.ProductMasterId)
            .Map(dest => dest.ProductCode, src => src.Data.ProductCode)
            .Map(dest => dest.ColorId, src => src.Data.ColorId)
            .Map(dest => dest.DefaultSalesPriceUsd, src => src.Data.DefaultSalesPriceUsd)
            .Map(dest => dest.DefaultExchangeRate, src => src.Data.DefaultExchangeRate)
            .Map(dest => dest.ProfitMargin, src => src.Data.ProfitMargin)
            .Map(dest => dest.DefaultPurchasePriceCny, src => src.Data.DefaultPurchasePriceCny)
            .Map(dest => dest.ImagePath, src => src.Data.ImagePath);

        config.NewConfig<ProductNeutralManagerLineModel, ProductNeutralManagerWriteDto>();
        config.NewConfig<ProductNeutralManagerLineModel, ProductNeutralManagerListItemDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.Data, src => src);

        config.NewConfig<ProductColorDto, ProductColorLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<ProductColorLineModel, ProductColorDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<BomDto, BomLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<BomLineModel, BomDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<CustomerDto, CustomerLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<CustomerLineModel, CustomerDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<CustomerProductDto, CustomerProductLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.UsdPrice, src => src.USDPrice)
            .Map(dest => dest.CnyPrice, src => src.CNYPrice)
            .Map(dest => dest.Moq, src => src.MOQ);

        config.NewConfig<CustomerProductLineModel, CustomerProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.USDPrice, src => src.UsdPrice)
            .Map(dest => dest.CNYPrice, src => src.CnyPrice)
            .Map(dest => dest.MOQ, src => src.Moq);

        config.NewConfig<SupplierDto, SupplierLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.Discount, src => src.ZheKou)
            .Map(dest => dest.DiscountRemark, src => src.ZheKouBeiZhu)
            .Map(dest => dest.DefaultLeadTimeDays, src => src.DefaultLeadTime);

        config.NewConfig<SupplierLineModel, SupplierDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.ZheKou, src => src.Discount)
            .Map(dest => dest.ZheKouBeiZhu, src => src.DiscountRemark)
            .Map(dest => dest.DefaultLeadTime, src => src.DefaultLeadTimeDays);

        config.NewConfig<SupplierProductDto, SupplierProductLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<SupplierProductLineModel, SupplierProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<PurchaseOrderManagerListItemDto, PurchaseOrderManagementRowModel>()
            .Map(dest => dest.LatestReadyQty, src => src.ReadyQty)
            .Map(dest => dest.ReceiptProgress, src => src.ReceiptProgress.ToString("P0"));

        config.NewConfig<PurchaseOrderManagerLineDto, PurchaseOrderManagementLineModel>()
            .Map(dest => dest.LatestReadyQtyText, src => src.ReadyQty.ToString("N1"))
            .Map(dest => dest.ExpectedCompletionDateText, src => src.ExpectedCompletionDate.HasValue ? src.ExpectedCompletionDate.Value.ToString("yyyy-MM-dd") : string.Empty)
            .Map(dest => dest.LatestFollowUpDateText, src => src.LatestFollowUpDate.HasValue ? src.LatestFollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty)
            .Map(dest => dest.LatestFollowUpUserText, src => src.FollowUpUserName ?? string.Empty)
            .Map(dest => dest.SourceSummary, src => string.IsNullOrEmpty(src.SourceNo)
                ? src.SourceType ?? string.Empty
                : string.Concat(src.SourceType, " · ", src.SourceNo,
                    src.SourceLineNumber.HasValue ? string.Concat(" / ", src.SourceLineNumber.Value.ToString()) : string.Empty));

        config.NewConfig<PurchaseOrderLineDetailDto, PurchaseOrderLineEditModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<PurchaseOrderLineEditModel, PurchaseOrderLineDetailDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<AfterSalesListItemDto, AfterSalesManagerRowModel>()
            .Map(dest => dest.CustomerDisplay, src => string.Concat(src.CustomerCodeSnapshot, " · ", src.CustomerNameSnapshot))
            .Map(dest => dest.DocumentStatusText, src => src.DocumentStatus == SwiftExport.Contracts.SysEnums.DocumentStatusEnum.Closed ? "已结案" : src.DocumentStatus == SwiftExport.Contracts.SysEnums.DocumentStatusEnum.Voided ? "已作废" : "开放")
            .Map(dest => dest.ConfirmationStatusText, src => src.ConfirmationStatus == SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认 ? "已确认" : "待确认")
            .Map(dest => dest.AmountText, src => string.Concat("¥", src.KnownAmountCny.ToString("N2"), src.HasUndeterminedAmount ? " + 待定" : string.Empty));
        config.NewConfig<AfterSalesHeaderDto, AfterSalesHeaderEditModel>();
        config.NewConfig<AfterSalesHeaderEditModel, AfterSalesEditHeaderDto>();
        config.NewConfig<AfterSalesLineDto, AfterSalesLineModel>();
        config.NewConfig<AfterSalesLineModel, AfterSalesEditLineDto>();
    }
}

internal static class MapsterInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        new MapsterConfig().Register(TypeAdapterConfig.GlobalSettings);
    }
}
