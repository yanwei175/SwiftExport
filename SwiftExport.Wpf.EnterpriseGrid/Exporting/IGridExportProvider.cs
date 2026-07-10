using System.IO;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>导出格式提供器。</summary>
public interface IGridExportProvider
{
    /// <summary>将稳定快照写入目标流；提供器不得关闭调用方流。</summary>
    Task ExportAsync(
        GridExportRequest request,
        Stream target,
        IProgress<GridExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>模板列或业务显示值的显式访问器。</summary>
public interface IGridValueAccessor
{
    /// <summary>从数据项读取导出值；调用发生在创建 UI 快照的线程。</summary>
    object? GetValue(object item, GridExportValueContext context);
}
