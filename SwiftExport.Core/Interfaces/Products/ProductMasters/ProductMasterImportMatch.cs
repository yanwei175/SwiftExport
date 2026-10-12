using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces.Products.ProductMasters;
/// <summary>保留数据库编码比较口径的逐行匹配事实，仅用于应用层导入准备。</summary>
public sealed record ProductMasterImportMatch(int SourceRowNumber, bool DuplicateCode, ProductMaster? Existing);
