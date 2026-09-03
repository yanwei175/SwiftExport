using Mapster;
using System.Runtime.CompilerServices;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.Models;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Mappings;

public sealed class MapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
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
