namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
/// <summary>仅通知父级资料已变化，不传输工作簿或可变编辑数据。</summary>
public sealed record ProductMasterImportCompletedMessage(int ChangedCount);
