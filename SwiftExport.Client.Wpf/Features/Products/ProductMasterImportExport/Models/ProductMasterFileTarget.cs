namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
/// <summary>保存对话框明确选择的路径与覆盖许可，工作簿服务默认拒绝覆盖。</summary>
public sealed record ProductMasterFileTarget(string Path, bool Overwrite = false);
