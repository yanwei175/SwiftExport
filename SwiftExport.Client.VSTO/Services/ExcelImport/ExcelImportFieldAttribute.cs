using System;

namespace SwiftExport.Client.VSTO.Services.ExcelImport
{
    /// <summary>声明一个允许从 Excel 导入的编辑属性；稳定键与 CLR 属性名分离。</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ExcelImportFieldAttribute : Attribute
    {
        /// <summary>声明配置键和中文显示名；只有标记过的公共实例属性进入目录。</summary>
        public ExcelImportFieldAttribute(string fieldKey, string displayName)
        {
            FieldKey = fieldKey; DisplayName = displayName;
        }
        public string FieldKey { get; }
        public string DisplayName { get; }
        /// <summary>配置页显示顺序，数字小者靠前；未指定时排最后，不参与读取或赋值。</summary>
        public int DisplayOrder { get; set; } = int.MaxValue;
        public bool SupportsCreate { get; set; } = true;
        public bool SupportsUpdate { get; set; } = true;
    }
}
