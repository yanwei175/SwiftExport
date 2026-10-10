using System;
using System.Globalization;
using System.Reflection;

namespace SwiftExport.Client.VSTO.Services.ExcelImport
{
    /// <summary>一次反射得到的允许导入字段；目录展示和赋值使用同一属性描述。</summary>
    public sealed class ExcelImportField
    {
        private readonly PropertyInfo property;
        internal ExcelImportField(Type modelType, PropertyInfo property, ExcelImportFieldAttribute attribute)
        {
            ModelType = modelType; this.property = property;
            FieldKey = attribute.FieldKey; DisplayName = attribute.DisplayName;
            DisplayOrder = attribute.DisplayOrder;
            SupportsCreate = attribute.SupportsCreate; SupportsUpdate = attribute.SupportsUpdate;
        }
        public Type ModelType { get; }
        public string FieldKey { get; }
        public string DisplayName { get; }
        /// <summary>模型特性指定的配置页显示顺序，不改变字段键或受控赋值规则。</summary>
        public int DisplayOrder { get; }
        public string PropertyName => property.Name;
        public bool SupportsCreate { get; }
        public bool SupportsUpdate { get; }

        // 转换只处理属性存储类型，不校验客户、产品、数量或价格等订单业务。
        internal bool TryConvert(object value, CultureInfo dateCulture, out object converted, out string error)
        {
            converted = null; error = "";
            if (property.PropertyType == typeof(string))
            {
                if (value == null || value is string) { converted = value ?? ""; return true; }
                if (IsNumber(value))
                {
                    // 数值写入文本模型时采用模型解析所使用的当前 UI 文化；原文由 Source 单独保留。
                    converted = Convert.ToString(value, CultureInfo.CurrentCulture); return true;
                }
            }
            else if (property.PropertyType == typeof(DateTime?))
            {
                if (value == null || value is string blank && string.IsNullOrWhiteSpace(blank)) return true;
                if (value is DateTime date) { converted = date; return true; }
                if (value is string text && DateTime.TryParse(text, dateCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
                { converted = parsed; return true; }
                // Excel 序号可能来自 1900 或 1904 日期系统；此处不能猜，读取层须先转为 DateTime。
            }
            error = DisplayName + "的值无法转换为模型属性 " + PropertyName + "；请检查原值或日期格式。";
            return false;
        }

        // 注册时已检查公共 setter，调用时仍由分组定义确认模型身份，不接受任意反射目标。
        internal void Assign(object model, object value) => property.SetValue(model, value);

        private static bool IsNumber(object value) => value is byte || value is sbyte || value is short || value is ushort
            || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal;
    }
}
