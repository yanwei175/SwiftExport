using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Bom.Models;

public sealed class BomListModel : BaseListModel<BomLineModel, BomDto>
{
    public BomListModel() : base(_ => new BomLineModel()) { }

    public BomLineModel AddNewItem(string bomNo) => AddNew(new BomDto
    {
        BomNo = bomNo,
        Numerator = 1,
        Denominator = 1,
    });

    public BomBatchSaveRequest CreateSaveRequest()
    {
        var changes = GetChangeSet();
        return new BomBatchSaveRequest
        {
            CreateItems = changes.Added.Select(dto => new CreateItemRequest<BomDto> { Data = dto }).ToList(),
            UpdateItems = changes.Modified.Select(dto => new UpdateItemRequest<BomDto> { Uid = dto.Uid, Version = dto.Version, Data = dto }).ToList(),
            DeleteItems = changes.Deleted.Where(dto => dto.Uid != Guid.Empty)
                .Select(dto => new DeleteItemRequest { Uid = dto.Uid, Version = dto.Version, IsSoftDelete = true }).ToList(),
        };
    }
}
