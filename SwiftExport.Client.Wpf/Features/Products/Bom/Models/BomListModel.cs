using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using Mapster;

namespace SwiftExportClient.Features.Products.Bom.Models;

/// <summary>维护 BOM 组成变更集、删除池及保存回执，供抽屉和管理页共用。</summary>
public sealed class BomListModel : BaseListModel<BomLineModel, BomDto>
{
    /// <summary>建立由基类跟踪状态的组成集合。</summary>
    public BomListModel() : base(_ => new BomLineModel()) { }

    /// <summary>为当前共享编号新增默认一比一组成，子件尚待选择。</summary>
    public BomLineModel AddNewItem(string bomNo) => AddNew(new BomDto
    {
        BomNo = bomNo,
        Numerator = 1,
        Denominator = 1,
    });

    /// <summary>按行身份、版本和客户端行键构造批量保存请求。</summary>
    public BomBatchSaveRequest CreateSaveRequest()
    {
        return new BomBatchSaveRequest
        {
            CreateItems = AddedItems.Select(line => new CreateItemRequest<BomDto> { ClientKey = line.ClientKey, Data = line.ToDto() }).ToList(),
            UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<BomDto> { ClientKey = line.ClientKey, Uid = line.Uid, Version = line.Version, Data = line.ToDto() }).ToList(),
            DeleteItems = DeletedItems.Where(line => line.Uid != Guid.Empty)
                .Select(line => new DeleteItemRequest { ClientKey = line.ClientKey, Uid = line.Uid, Version = line.Version, IsSoftDelete = true }).ToList(),
        };
    }

    /// <summary>核实完整成功回执后回填身份和版本；缺失或错配回执返回 false 并完整保留编辑。</summary>
    public bool ApplySaveResponse(SwiftExport.Contracts.DTOs.Products.Responses.BomBatchSaveResponse response)
    {
        var expected = AddedItems.Select(line => (Line: line, Operation: SaveOperation.Create))
            .Concat(ModifiedItems.Select(line => (Line: line, Operation: SaveOperation.Update)))
            .Concat(DeletedItems.Select(line => (Line: line, Operation: SaveOperation.Delete))).ToDictionary(item => item.Line.ClientKey);
        if (!response.AllSucceeded || response.Items.Count != expected.Count
            || response.Items.Select(item => item.ClientKey).Distinct().Count() != expected.Count) return false;
        foreach (var result in response.Items)
        {
            if (result.ClientKey is null || !expected.TryGetValue(result.ClientKey, out var item) || result.Operation != item.Operation
                || result.Uid is null || result.Uid == Guid.Empty
                || (item.Operation != SaveOperation.Create && result.Uid != item.Line.Uid)
                || (item.Operation != SaveOperation.Delete && (!result.NewVersion.HasValue || result.NewVersion <= item.Line.Version))) return false;
        }
        var byKey = expected.ToDictionary(item => item.Key, item => item.Value.Line, StringComparer.Ordinal);
        foreach (var result in response.Items.Where(item => item.Success && item.Operation != SaveOperation.Delete))
        {
            if (result.ClientKey is null || !byKey.TryGetValue(result.ClientKey, out var line)) continue;
            using var scope = line.SuppressChangeTracking();
            if (result.Uid.HasValue) line.Uid = result.Uid.Value;
            if (result.NewVersion.HasValue) line.Version = result.NewVersion.Value;
        }
        AcceptChanges();
        return true;
    }

    /// <summary>只把空目标的来源组成转成新增行；身份、版本和审计字段保持空值，来源对象不被修改。</summary>
    public void CopyIntoEmpty(string targetBomNo, IEnumerable<BomDto> source)
    {
        if (Items.Count > 0 || HasChanges) throw new InvalidOperationException("复制只允许用于没有未保存变更的空 BOM。");
        using var scope = SuppressListEvents();
        foreach (var dto in source)
        {
            var copied = dto.Adapt<BomDto>();
            copied.BomNo = targetBomNo;
            copied.Uid = Guid.Empty; copied.Version = 0; copied.IsDeleted = false;
            copied.CreatedBy = null; copied.CreatedDate = null; copied.UpdatedBy = null; copied.UpdatedDate = null;
            AddNew(copied);
        }
    }
}
