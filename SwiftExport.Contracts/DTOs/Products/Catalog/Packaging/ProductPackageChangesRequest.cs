using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>当前物料的包装增删改；只处理显式行操作，省略的既有行保持，更新和删除须携带行版本。</summary>
    public class ProductPackageChangesRequest : BatchSaveRequestBase<ProductPackageWriteDto> { }
}
