using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Bom.Models;

public sealed partial class BomLineModel : BaseLineModel<BomDto>
{
    [ObservableProperty] private Guid uid;
    [ObservableProperty] private int version;
    [ObservableProperty] private string bomNo = string.Empty;
    [ObservableProperty] private Guid subSkuId;
    [ObservableProperty] private string subSku = string.Empty;
    [ObservableProperty] private int numerator = 1;
    [ObservableProperty] private int denominator = 1;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string finish = string.Empty;
    [ObservableProperty] private string features = string.Empty;
    [ObservableProperty] private string mainImage = string.Empty;

    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(BomNo)) return "BOM 号不能为空。";
        if (SubSkuId == Guid.Empty || string.IsNullOrWhiteSpace(SubSku)) return "必须选择子件 SKU。";
        if (Numerator <= 0 || Denominator <= 0) return "用量分子和分母必须大于 0。";
        return null;
    }

    protected override void LoadFromDto(BomDto dto)
    {
        Uid = dto.Uid; Version = dto.Version; BomNo = dto.BomNo ?? string.Empty;
        SubSkuId = dto.SubSkuId; SubSku = dto.SubSku ?? string.Empty;
        Numerator = dto.Numerator; Denominator = dto.Denominator; Note = dto.Note ?? string.Empty;
        Description = dto.Description ?? string.Empty; Finish = dto.Finish ?? string.Empty;
        Features = dto.Features ?? string.Empty; MainImage = dto.MainImage ?? string.Empty;
    }

    public override BomDto ToDto() => new()
    {
        Uid = Uid, Version = Version, BomNo = BomNo, SubSkuId = SubSkuId, SubSku = SubSku,
        Numerator = Numerator, Denominator = Denominator, Note = Note,
        Description = Description, Finish = Finish, Features = Features, MainImage = MainImage,
    };
}
