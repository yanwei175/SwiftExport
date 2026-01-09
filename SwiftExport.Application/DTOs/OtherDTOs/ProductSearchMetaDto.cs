namespace SwiftExport.AppLayer.DTOs.OtherDTOs
{
    public class ProductSearchMetaDto
    {
        public string SKU { get; set; }      // 唯一标识，用户选中后用它去查库

        // 搜索文本：包含 SKU、描述、颜色、客户型号等所有能搜的字
        // 格式建议："AT010011 | 红色铝合金把手 | C-001"
        public string SearchText { get; set; }

        //以此重写 ToString，ComboBox 显示的就是这个
        public override string ToString() => SearchText;
    }
}
