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
using SwiftExportClient.Features.Suppliers.Components.Model;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.Models;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Features.SystemSettings.SystemParameters.Models;

namespace SwiftExportClient.Mappings;

/// <summary>集中注册客户端 DTO 与页面模型之间的结构映射。</summary>
public sealed class MapsterConfig : IRegister
{
    /// <summary>集中登记各业务 DTO 与私有页面模型映射，保留每个用例的身份和空值语义。</summary>
    public void Register(TypeAdapterConfig config)
    {
        new SwiftExportClient.Features.OrderTracking.Overview.Mappings.OrderTrackingOverviewMappings().Register(config);
        config.NewConfig<SystemParameterDto, SystemParameterLineModel>().Ignore(dest => dest.Status);
        new SwiftExportClient.Features.Shipping.PackingLists.Mappings.PackingListMappings().Register(config);
        new SwiftExportClient.Features.Inventory.Inbounds.Mappings.InboundManagerMappings().Register(config);
        new SwiftExportClient.Features.Inventory.Outbounds.Mappings.OutboundManagerMappings().Register(config);
        new SwiftExportClient.Features.Products.Bom.Mappings.BomManagementMappings().Register(config);
        new SwiftExportClient.Features.Products.Attributes.Mappings.ProductAttributeDefinitionMappings().Register(config);
        new SwiftExportClient.Features.QualityControl.FlowManagement.Mappings.FlowMappings().Register(config);
        config.NewConfig<SourceDemandCandidateGroupDto, SourceDemandCandidateGroupModel>()
            .Map(dest => dest.SourceType, src => src.SourceType ?? string.Empty)
            .Map(dest => dest.SourceNo, src => src.SourceNo ?? string.Empty);

        config.NewConfig<MaterialDemandManagerItemDto, MaterialDemandManagerRowModel>()
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


        config.NewConfig<PurchaseDraftCandidateDto, PurchaseCandidateItemModel>()
            .Map(dest => dest.RemainingQty, src => src.CreatableQty ?? 0m)
            .Map(dest => dest.DraftOrders, src => src.DraftOrders ?? new List<PurchaseDraftReferenceDto>());

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

        // 物料详情以嵌套可写数据采集草稿，空值必须接纳，不能沿用列表的忽略空值策略。
        config.NewConfig<ProductDetailDto, ProductEditModel>()
            .Map(dest => dest.RequiresBom, src => src.BomVersion > 0)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.BomNo, src => src.BomNo ?? string.Empty)
            .Map(dest => dest.Finish, src => src.Finish ?? string.Empty)
            .Map(dest => dest.DefaultSupplierCode, src => src.DefaultSupplierCode ?? string.Empty)
            .Map(dest => dest.SupplierName, src => src.SupplierName ?? string.Empty)
            .Map(dest => dest.ManufacturerCode, src => src.ManufacturerCode ?? string.Empty)
            .Map(dest => dest.ManufacturerName, src => src.ManufacturerName ?? string.Empty)
            .Map(dest => dest.DefaultWarehouseCode, src => src.DefaultWarehouseCode ?? string.Empty)
            .Map(dest => dest.DefaultWarehouseName, src => src.DefaultWarehouseName ?? string.Empty)
            .Map(dest => dest.Sku, src => src.Data.Sku ?? string.Empty)
            .Map(dest => dest.LinkSku, src => src.Data.LinkSku ?? string.Empty)
            .Map(dest => dest.MasterSku, src => src.MasterSku ?? string.Empty)
            .Map(dest => dest.ProductMasterId, src => src.Data.ProductMasterId)
            .Map(dest => dest.DescriptionEn, src => src.Data.DescriptionEn ?? string.Empty)
            .Map(dest => dest.DescriptionZh, src => src.Data.DescriptionZh ?? string.Empty)
            .Map(dest => dest.FeaturesEn, src => src.Data.FeaturesEn ?? string.Empty)
            .Map(dest => dest.FeaturesZh, src => src.Data.FeaturesZh ?? string.Empty)
            .Map(dest => dest.ColorId, src => src.Data.ColorId)
            .Map(dest => dest.MaterialType, src => src.Data.MaterialType ?? string.Empty)
            .Map(dest => dest.SupplyType, src => src.Data.SupplyType ?? string.Empty)
            .Map(dest => dest.Unit, src => src.Data.Unit ?? string.Empty)
            .Map(dest => dest.CnyPrice, src => src.Data.CNYPrice)
            .Map(dest => dest.UsdPrice, src => src.Data.USDPrice)
            .Map(dest => dest.UnitWeight, src => src.Data.UnitWeight)
            .Map(dest => dest.UnitCbm, src => src.Data.UnitCBM)
            .Map(dest => dest.DefaultSupplierId, src => src.Data.DefaultSupplierId)
            .Map(dest => dest.ManufacturerId, src => src.Data.ManufacturerId)
            .Map(dest => dest.ManufacturerModel, src => src.Data.ManufacturerModel ?? string.Empty)
            .Map(dest => dest.DefaultWarehouseSiteId, src => src.Data.DefaultWarehouseSiteId)
            .Map(dest => dest.MainImage, src => src.Data.MainImage ?? string.Empty)
            .Map(dest => dest.Note, src => src.Data.Note ?? string.Empty);
        config.NewConfig<ProductEditModel, ProductDetailWriteDto>()
            .Ignore(dest => dest.RequiresBom)
            .Map(dest => dest.CNYPrice, src => src.CnyPrice)
            .Map(dest => dest.USDPrice, src => src.UsdPrice)
            .Map(dest => dest.UnitCBM, src => src.UnitCbm);
        config.NewConfig<ProductEditModel, ProductDetailDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.Data, src => src);

        // 详情保存后的列表投影保留外层审计、包装、BOM和来源资料，旧列表价格与颜色按其契约空值规则转换。
        config.NewConfig<ProductDetailDto, ProductDto>()
            .Map(dest => dest.UnitWeight, src => src.Data.UnitWeight)
            .Map(dest => dest.Sku, src => src.Data.Sku)
            .Map(dest => dest.LinkSku, src => src.Data.LinkSku)
            .Map(dest => dest.ProductMasterId, src => src.Data.ProductMasterId)
            .Map(dest => dest.DescriptionEn, src => src.Data.DescriptionEn)
            .Map(dest => dest.DescriptionZh, src => src.Data.DescriptionZh)
            .Map(dest => dest.FeaturesEn, src => src.Data.FeaturesEn)
            .Map(dest => dest.FeaturesZh, src => src.Data.FeaturesZh)
            .Map(dest => dest.ColorId, src => src.Data.ColorId ?? Guid.Empty)
            .Map(dest => dest.MaterialType, src => src.Data.MaterialType)
            .Map(dest => dest.SupplyType, src => src.Data.SupplyType)
            .Map(dest => dest.Unit, src => src.Data.Unit)
            .Map(dest => dest.CNYPrice, src => src.Data.CNYPrice ?? 0)
            .Map(dest => dest.USDPrice, src => src.Data.USDPrice ?? 0)
            .Map(dest => dest.DefaultSupplierId, src => src.Data.DefaultSupplierId)
            .Map(dest => dest.ManufacturerId, src => src.Data.ManufacturerId)
            .Map(dest => dest.ManufacturerModel, src => src.Data.ManufacturerModel)
            .Map(dest => dest.DefaultWarehouseSiteId, src => src.Data.DefaultWarehouseSiteId)
            .Map(dest => dest.MainImage, src => src.Data.MainImage)
            .Map(dest => dest.Note, src => src.Data.Note);


        config.NewConfig<ProductDto, ProductLineModel>()
            .Map(dest => dest.RequiresBom, src => src.BomVersion > 0)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.Sku, src => src.Sku ?? string.Empty)
            .Map(dest => dest.MasterSku, src => src.MasterSku ?? string.Empty)
            .Map(dest => dest.DescriptionEn, src => src.DescriptionEn ?? string.Empty)
            .Map(dest => dest.DescriptionZh, src => src.DescriptionZh ?? string.Empty)
            .Map(dest => dest.FeaturesEn, src => src.FeaturesEn ?? string.Empty)
            .Map(dest => dest.FeaturesZh, src => src.FeaturesZh ?? string.Empty)
            .Map(dest => dest.Finish, src => src.Finish ?? string.Empty)
            .Map(dest => dest.MaterialType, src => src.MaterialType ?? string.Empty)
            .Map(dest => dest.Unit, src => src.Unit ?? string.Empty)
            .Map(dest => dest.SupplyType, src => src.SupplyType ?? string.Empty)
            .Map(dest => dest.BomNo, src => src.BomNo ?? string.Empty)
            .Map(dest => dest.Note, src => src.Note ?? string.Empty)
            .Map(dest => dest.LinkSku, src => src.LinkSku ?? string.Empty)
            .Map(dest => dest.CategoryName, src => src.CategoryName ?? string.Empty)
            .Map(dest => dest.SupplierName, src => src.SupplierName ?? string.Empty)
            .Map(dest => dest.DefaultWarehouseCode, src => src.DefaultWarehouseCode ?? string.Empty)
            .Map(dest => dest.DefaultWarehouseName, src => src.DefaultWarehouseName ?? string.Empty)
            .Map(dest => dest.ManufacturerCode, src => src.ManufacturerCode ?? string.Empty)
            .Map(dest => dest.ManufacturerName, src => src.ManufacturerName ?? string.Empty)
            .Map(dest => dest.ManufacturerModel, src => src.ManufacturerModel ?? string.Empty)
            .Map(dest => dest.DefaultSupplierCode, src => src.DefaultSupplierCode ?? string.Empty)
            .Map(dest => dest.MainImage, src => src.MainImage ?? string.Empty)
            .Map(dest => dest.CnyPrice, src => src.CNYPrice)
            .Map(dest => dest.UsdPrice, src => src.USDPrice);

        config.NewConfig<ProductLineModel, ProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.CNYPrice, src => src.CnyPrice)
            .Map(dest => dest.USDPrice, src => src.UsdPrice);

        config.NewConfig<ProductPackageDto, ProductPackageLineModel>()
            .Map(dest => dest.PackageType, src => src.Data.PackageType)
            .Map(dest => dest.PackagingMaterialId, src => src.Data.PackagingMaterialId)
            .Map(dest => dest.PcsPerPackage, src => src.Data.PcsPerPackage)
            .Map(dest => dest.Length_CM, src => src.Data.Length_CM)
            .Map(dest => dest.Width_CM, src => src.Data.Width_CM)
            .Map(dest => dest.Height_CM, src => src.Data.Height_CM)
            .Map(dest => dest.Nw_KG, src => src.Data.Nw_KG)
            .Map(dest => dest.Gw_KG, src => src.Data.Gw_KG)
            .Map(dest => dest.Cbm, src => src.Data.Cbm)
            .Map(dest => dest.Note, src => src.Data.Note);
        config.NewConfig<ProductPackageLineModel, ProductPackageDto>()
            .Map(dest => dest.Data, src => src.Adapt<ProductPackageWriteDto>());

        config.NewConfig<ProductMasterListItemDto, ProductMasterLineModel>()
            .IgnoreNullValues(true)
            // 装率、模板及系列空值是持久化事实，必须覆盖初始值与此前载入的数据。
            .AfterMapping((src, dest) => { dest.DefaultPcsPerPackage = src.Data.DefaultPcsPerPackage; dest.ProductAttributeProfileId = src.Data.ProductAttributeProfileId; dest.ProductSeriesId = src.Data.ProductSeriesId; })
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
            .Map(dest => dest.ProductAttributeProfileId, src => src.Data.ProductAttributeProfileId)
            .Map(dest => dest.ProductAttributeProfileName, src => src.ProductAttributeProfileName ?? string.Empty)
            .Map(dest => dest.ProductSeriesId, src => src.Data.ProductSeriesId)
            .Map(dest => dest.ProductSeriesName, src => src.ProductSeriesName ?? string.Empty)
            .Map(dest => dest.TagSummary, src => src.TagSummary ?? string.Empty)
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
            // 头部空装率、空模板及空系列同样原样接纳，不能受字符串空值兼容策略影响。
            .AfterMapping((src, dest) => { dest.DefaultPcsPerPackage = src.Master.DefaultPcsPerPackage; dest.ProductAttributeProfileId = src.Master.ProductAttributeProfileId; dest.ProductSeriesId = src.Master.ProductSeriesId; })
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
            .Map(dest => dest.ProductAttributeProfileId, src => src.Master.ProductAttributeProfileId)
            .Map(dest => dest.ProductAttributeProfileName, src => src.ProductAttributeProfileName ?? string.Empty)
            .Map(dest => dest.ProductSeriesId, src => src.Master.ProductSeriesId)
            .Map(dest => dest.ProductSeriesName, src => src.ProductSeriesName ?? string.Empty)
            .Map(dest => dest.CategoryId, src => src.Master.CategoryId)
            .Map(dest => dest.DefaultSupplierId, src => src.Master.DefaultSupplierId);

        // 详情保存后的单行刷新复用头部结构映射，摘要和默认主图由调用方按实时关联组装。
        config.NewConfig<ProductMasterDetailDto, ProductMasterListItemDto>()
            .Map(dest => dest.Data, src => src.Master);

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

        config.NewConfig<CustomerPoConfigDto, CustomerPoConfigLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Ignore(dest => dest.SelectedCustomer)
            .Ignore(dest => dest.SelectedSupplier);

        config.NewConfig<CustomerPoConfigLineModel, CustomerPoConfigDto>()
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

        config.NewConfig<SupplierDto, SupplierPickerModel>();
        config.NewConfig<SupplierPickerModel, SupplierDto>();

        config.NewConfig<SupplierProductDto, SupplierProductLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<SupplierProductLineModel, SupplierProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<PurchaseOrderManagerListItemDto, PurchaseOrderManagementRowModel>();

        config.NewConfig<PurchaseOrderManagerLineDto, PurchaseOrderManagementLineModel>()
            .Map(dest => dest.SourceSummary, src => src.SourceSummary ?? string.Empty)
            .Map(dest => dest.AssociationReviewReason, src => src.AssociationReviewReason ?? string.Empty);

        config.NewConfig<PurchaseOrderLineDetailDto, PurchaseOrderLineEditModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<PurchaseOrderLineEditModel, PurchaseOrderLineDetailDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<PurchaseOrderLineEditModel, SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto.UpdatePurchaseOrderDraftLineCommand>()
            .Map(dest => dest.PurchaseOrderLineId, src => src.Uid)
            .Map(dest => dest.ExpectedVersion, src => src.Version);
        // 手工选品以 ProductId 为准；不把候选显示的客户型号误当作 Excel 导入匹配意图。
        config.NewConfig<PurchaseOrderLineEditModel, SwiftExport.Contracts.DTOs.Procurement.OrderManagement.CreateIndependentPurchaseOrderLineCommand>()
            .Ignore(dest => dest.CustomerSku)
            .Ignore(dest => dest.SelectedCustomerProductId);

        config.NewConfig<AfterSalesListItemDto, AfterSalesManagerRowModel>()
            .Map(dest => dest.CustomerDisplay, src => string.Concat(src.CustomerCodeSnapshot, " · ", src.CustomerNameSnapshot))
            .Map(dest => dest.DocumentStatusText, src => src.DocumentStatus == SwiftExport.Contracts.SysEnums.DocumentStatusEnum.Closed ? "已结案" : src.DocumentStatus == SwiftExport.Contracts.SysEnums.DocumentStatusEnum.Voided ? "已作废" : "开放")
            .Map(dest => dest.ConfirmationStatusText, src => src.ConfirmationStatus == SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认 ? "已确认" : "待确认")
            .Map(dest => dest.AmountText, src => src.HasUndeterminedAmount ? "待定" : src.KnownAmountCny == 0 ? "免费" : string.Concat("¥", src.KnownAmountCny.ToString("N2")));
        config.NewConfig<AfterSalesHeaderDto, AfterSalesHeaderEditModel>();
        config.NewConfig<AfterSalesHeaderEditModel, AfterSalesEditHeaderDto>();
        config.NewConfig<AfterSalesLineDto, AfterSalesLineModel>();
        config.NewConfig<AfterSalesLineModel, AfterSalesEditLineDto>();
        config.NewConfig<AfterSalesLineModel, AfterSalesLineDto>();
        // 产品选择只带入显示快照，图片不属于售后保存契约。
        config.NewConfig<ProductLookupDto, AfterSalesHeaderEditModel>()
            .IgnoreNonMapped(true)
            .Map(d => d.IssueProductId, s => s.Uid)
            .Map(d => d.IssueSkuSnapshot, s => s.Sku ?? string.Empty)
            .Map(d => d.IssueLinkSkuSnapshot, s => s.LinkSku ?? string.Empty)
            .Map(d => d.IssueCustomerSkuSnapshot, s => string.Empty)
            .Map(d => d.ImagePath, s => s.MainImage);
        config.NewConfig<ProductLookupDto, AfterSalesLineModel>()
            .IgnoreNonMapped(true)
            .Map(d => d.ProductId, s => s.Uid)
            .Map(d => d.SkuSnapshot, s => s.Sku ?? string.Empty)
            .Map(d => d.LinkSkuSnapshot, s => s.LinkSku ?? string.Empty)
            .Map(d => d.CustomerSkuSnapshot, s => string.Empty)
            .Map(d => d.DescriptionSnapshot, s => s.DescriptionEn ?? s.DescriptionZh ?? string.Empty)
            .Map(d => d.ColorId, s => (Guid?)null)
            .Map(d => d.ColorSnapshot, s => s.Finish ?? string.Empty)
            .Map(d => d.SpecificationSnapshot, s => s.FeaturesEn ?? s.FeaturesZh ?? string.Empty)
            .Map(d => d.ImagePath, s => s.MainImage);
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
