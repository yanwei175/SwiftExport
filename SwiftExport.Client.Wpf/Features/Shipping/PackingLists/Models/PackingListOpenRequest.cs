namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>装箱单详情打开参数；新建使用稳定临时身份，保存后仍使用同一页签。</summary>
public sealed class PackingListOpenRequest
{
    public Guid Uid { get; init; }
    public bool IsNew { get; init; }
}
