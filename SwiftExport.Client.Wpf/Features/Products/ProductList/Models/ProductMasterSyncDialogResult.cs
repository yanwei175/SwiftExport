namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号保存确认结果；只有确认结果可以把同步选项提交给服务端。</summary>
public sealed partial class ProductMasterSyncDialogResult
{
    /// <summary>是否明确确认保存；取消、Esc 和关闭均为 false。</summary>
    public bool Confirmed { get; init; }
    /// <summary>是否覆盖关联物料的描述与配置。</summary>
    public bool UpdateProductDescriptions { get; init; }
    /// <summary>是否覆盖关联物料的默认供应商。</summary>
    public bool UpdateProductDefaultSupplier { get; init; }
}
