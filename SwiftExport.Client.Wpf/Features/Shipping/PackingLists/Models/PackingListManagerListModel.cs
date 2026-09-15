using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>管理页摘要集合，复用公共加载、选择和状态通知机制。</summary>
public sealed partial class PackingListManagerListModel : BaseListModel<PackingListManagerRowModel, PackingListManagerListItemDto>
{
    /// <summary>创建只读摘要行集合；管理页不开放行内增删改。</summary>
    public PackingListManagerListModel() : base(_ => new PackingListManagerRowModel()) { }
}

