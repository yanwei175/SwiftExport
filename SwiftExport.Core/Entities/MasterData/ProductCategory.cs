using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductCategory : BaseEntity
{
    public string CategoryName { get; set; } = default!; // 对应 [CategoryName]
    public int? ParentID { get; set; }

    // 注意：这是数据冗余字段。
    // 优点：查询列表显示时不需要连表。
    // 缺点：修改父级名字时，需要批量更新所有子级的 ParentName。
    public string? ParentName { get; set; }

    public string? CategoryNo { get; set; }
    public int SortNo { get; set; } = default!;
}
