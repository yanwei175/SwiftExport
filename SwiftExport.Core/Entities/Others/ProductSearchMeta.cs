using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Others;

/// <summary>产品搜索索引实体，用于汇总可检索的产品关键词。</summary>
public class ProductSearchMeta : BaseEntity
{
    /// <summary>唯一标识，用户选中后用它去查库。</summary>
    public string? SKU { get; set; }      // 唯一标识，用户选中后用它去查库

    // 搜索文本：包含 SKU、描述、颜色、客户型号等所有能搜的字
    // 格式建议："AT010011 | 红色铝合金把手 | C-001"
    /// <summary>该实体保存的产品综合检索文本。</summary>
    public string? SearchText { get; set; }

    //以此重写 ToString，ComboBox 显示的就是这个
    public override string? ToString() => SearchText;


}
