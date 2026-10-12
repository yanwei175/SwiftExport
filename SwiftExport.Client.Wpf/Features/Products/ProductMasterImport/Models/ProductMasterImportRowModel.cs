using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;

namespace SwiftExportClient.Features.Products.ProductMasterImport.Models;

/// <summary>客户端队列行状态；结果未知的行不能自动重试。</summary>
public enum ProductMasterImportStatus
{
    /// <summary>此行尚未发送。</summary>
    Pending,
    /// <summary>此行所在批次正在等待完整回执。</summary>
    Submitting,
    /// <summary>已收到确定的保存成功回执。</summary>
    Success,
    /// <summary>本地校验或服务器行保存失败，原因保留。</summary>
    Failed,
    /// <summary>请求已发送但保存结果无法确认，需核对后重新导入。</summary>
    OutcomeUnknown
}

/// <summary>保存原始输入和行回执，不支持在 DataGrid 中编辑或维护服务器预览。</summary>
public sealed partial class ProductMasterImportRowModel(ProductMasterImportRowDto input) : ObservableObject
{
    /// <summary>原文件十一字段，错误回写仍使用这些原值。</summary>
    public ProductMasterImportRowDto Input { get; } = input;
    /// <summary>原文件一基行号。</summary>
    public int SourceRowNumber => Input.SourceRowNumber;
    /// <summary>本地校验和保存回执的可读原因。</summary>
    [ObservableProperty] public partial string Reason { get; set; } = string.Empty;
    /// <summary>队列状态由当前批次的完整回执推进。</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ProductMasterImportStatus Status { get; set; }
    /// <summary>页面状态文案。</summary>
    public string StatusText => Status switch
    {
        ProductMasterImportStatus.Submitting => "提交中",
        ProductMasterImportStatus.Success => "成功",
        ProductMasterImportStatus.Failed => "失败",
        ProductMasterImportStatus.OutcomeUnknown => "结果未知",
        _ => "待提交"
    };
}
