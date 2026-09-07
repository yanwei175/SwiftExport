using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Sales.Orders.Models;

/// <summary>管理销售订单 Detail 行的新增、删除、排序和保存变更集。</summary>
public sealed class SalesOrderLineListModel
    : BaseListModel<SalesOrderLineModel, SalesOrderDetailLineDto>
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
            DeletedLines = DeletedItems
                .Where(line => line.Uid != Guid.Empty)
                .Select(line => new SalesOrderDeletedLineDto
                {
                    Uid = line.Uid,
                    Version = line.Version,
                })
                .ToList(),
        };
    }

    private static SalesOrderLineModel CreateLine(SalesOrderDetailLineDto dto) => new();

    private SalesOrderDetailLineDto CreateNewDto(bool isNonStandard, DateTime? expectedDeliveryDate)
    {
        return new SalesOrderDetailLineDto
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            LineNumber = AllLines.Count + 1,
            IsNonStandard = isNonStandard,
            ExpectedDeliveryDate = expectedDeliveryDate,
        };
    }
}
