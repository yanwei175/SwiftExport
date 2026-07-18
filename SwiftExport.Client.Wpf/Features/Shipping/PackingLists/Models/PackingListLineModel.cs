using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

public sealed partial class PackingListLineModel : ObservableObject
{
    public Guid? Uid { get; set; }
    public int Version { get; set; }
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string? CustomerSku { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public Guid GrossDemandId { get; set; }
    public Guid NetDemandId { get; set; }
    public Guid NetDemandAllocationId { get; set; }
    public int SourceVersion { get; set; }
    public decimal ExpectedRemainingQty { get; set; }
    public string? SalesOrderNo { get; set; }
    public int? SalesOrderLineNumber { get; set; }
    public string? SourceSupplierName { get; set; }
    public string? WarehouseName { get; set; }
    public DateTime? RequiredDate { get; set; }
    [ObservableProperty] public partial decimal Quantity { get; set; }
    [ObservableProperty] public partial decimal? PackageCount { get; set; }
    [ObservableProperty] public partial decimal? NetWeight { get; set; }
    [ObservableProperty] public partial decimal? GrossWeight { get; set; }
    [ObservableProperty] public partial decimal? Volume { get; set; }
    [ObservableProperty] public partial string? Marks { get; set; }
    [ObservableProperty] public partial string? Note { get; set; }

    public static PackingListLineModel FromDetail(PackingListContributionDto dto) => new()
    {
        Uid = dto.Uid, Version = dto.Version, ProductId = dto.ProductId, Sku = dto.Sku, CustomerSku = dto.CustomerSku, Description = dto.Description, Unit = dto.Unit,
        GrossDemandId = dto.GrossDemandId, NetDemandId = dto.NetDemandId, NetDemandAllocationId = dto.NetDemandAllocationId, SourceVersion = dto.SourceVersion,
        ExpectedRemainingQty = dto.ShippableRemainingQty, SalesOrderNo = dto.SalesOrderNo, SalesOrderLineNumber = dto.SalesOrderLineNumber,
        SourceSupplierName = dto.SourceSupplierName, WarehouseName = dto.WarehouseName, RequiredDate = dto.RequiredDate,
        Quantity = dto.Quantity, PackageCount = dto.PackageCount, NetWeight = dto.NetWeight, GrossWeight = dto.GrossWeight, Volume = dto.Volume, Marks = dto.Marks, Note = dto.Note
    };

    public static PackingListLineModel FromSource(PackingListSourceDto dto) => new()
    {
        ProductId = dto.ProductId, Sku = dto.Sku, CustomerSku = dto.CustomerSku, Description = dto.Description, Unit = dto.Unit,
        GrossDemandId = dto.GrossDemandId, NetDemandId = dto.NetDemandId, NetDemandAllocationId = dto.NetDemandAllocationId, SourceVersion = dto.SourceVersion,
        ExpectedRemainingQty = dto.ShippableRemainingQty, SalesOrderNo = dto.SalesOrderNo, SalesOrderLineNumber = dto.SalesOrderLineNumber,
        SourceSupplierName = dto.SourceSupplierName, WarehouseName = dto.WarehouseName, RequiredDate = dto.RequiredDate, Quantity = dto.ShippableRemainingQty
    };

    public PackingListContributionCommand ToCommand() => new()
    {
        Uid = Uid, ExpectedVersion = Uid.HasValue ? Version : null, NetDemandAllocationId = NetDemandAllocationId,
        ExpectedSourceVersion = SourceVersion, ExpectedRemainingQty = ExpectedRemainingQty, Quantity = Quantity,
        PackageCount = PackageCount, NetWeight = NetWeight, GrossWeight = GrossWeight, Volume = Volume, Marks = Marks, Note = Note
    };
}
