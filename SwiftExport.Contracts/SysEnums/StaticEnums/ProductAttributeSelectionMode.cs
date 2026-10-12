namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>分类标签组允许的选择数量规则。</summary>
    public enum ProductAttributeSelectionMode
    {
        /// <summary>同一主型号在本组最多选择一个值。</summary>
        Single = 0,
        /// <summary>同一主型号在本组允许选择多个值。</summary>
        Multiple = 1
    }
}
