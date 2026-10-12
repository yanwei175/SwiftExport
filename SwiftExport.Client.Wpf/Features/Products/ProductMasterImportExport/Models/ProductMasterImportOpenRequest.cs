using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
/// <summary>携带来源页已应用筛选的独立值快照，不引用来源 ViewModel。</summary>
public sealed record ProductMasterImportOpenRequest(ProductMasterExportRequest Filter);
