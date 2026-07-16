using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Sales.Orders.Models;

public sealed class SalesOrderLineListModel
    : BaseListModel<SalesOrderLineModel, SalesOrderDocumentLineDto>
{
    public SalesOrderLineListModel()
        : base(CreateLine)
    {
    }

    public SalesOrderLineModel AddStandardLine(DateTime? expectedDeliveryDate)
    {
        return AddNew(CreateNewDto(false, expectedDeliveryDate));
    }

    public SalesOrderLineModel AddNonStandardLine(DateTime? expectedDeliveryDate)
    {
        var dto = CreateNewDto(true, expectedDeliveryDate);
        dto.Sku = NonStandardSkuGenerator.Create(dto.Uid);
        return AddNew(dto);
    }

    public SaveSalesOrderRequest CreateSaveRequest(SalesOrderHeaderEditModel header)
    {
        return new SaveSalesOrderRequest
        {
            Header = header.ToDto(),
            Lines = AllLines
                .Where(line => !line.IsDeleted)
                .Select(line => line.ToEditDto())
                .ToList(),
            DeletedLineIds = DeletedItems
                .Where(line => line.Uid != Guid.Empty)
                .Select(line => line.Uid)
                .ToList(),
        };
    }

    private static SalesOrderLineModel CreateLine(SalesOrderDocumentLineDto dto) => new();

    private SalesOrderDocumentLineDto CreateNewDto(bool isNonStandard, DateTime? expectedDeliveryDate)
    {
        return new SalesOrderDocumentLineDto
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            LineNumber = AllLines.Count + 1,
            IsNonStandard = isNonStandard,
            ExpectedDeliveryDate = expectedDeliveryDate,
        };
    }
}
