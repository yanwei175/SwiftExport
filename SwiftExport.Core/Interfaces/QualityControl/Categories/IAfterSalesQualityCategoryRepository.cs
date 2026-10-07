using SwiftExport.Core.Entities.QualityControl;

namespace SwiftExport.Core.Interfaces.QualityControl.Categories;

/// <summary>售后分类持久化端口；单实体写入仅供树批量服务在其事务内调用。</summary>
public interface IAfterSalesQualityCategoryRepository : IRepository<AfterSalesQualityCategory>
{
    /// <summary>读取全部未删节点，可按身份顺序锁行；锁定读取要求调用方事务。</summary>
    Task<IReadOnlyList<AfterSalesQualityCategory>> ReadAllAsync(bool forUpdate, CancellationToken cancellationToken);
    /// <summary>返回仍被未删售后单引用的分类，供整批删除保护；不锁售后头以避免反向锁序。</summary>
    Task<IReadOnlyList<Guid>> GetReferencedCategoryIdsAsync(IEnumerable<Guid> categoryIds, CancellationToken cancellationToken);
}
