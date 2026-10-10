using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SwiftExport.Client.VSTO.Services.ExcelImport
{
    /// <summary>一组导入业务的模型组合，反射目录也是其唯一的可赋值属性表。</summary>
    public sealed class ExcelImportDefinition
    {
        private readonly Dictionary<string, ExcelImportField> byKey;
        /// <summary>只注册真实单头和明细模型；重复键、非公共 setter 或不支持的类型立即报配置错误。</summary>
        public ExcelImportDefinition(string groupCode, string displayName, Type headerType, Type lineType)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("分组编码和名称不能为空。");
            GroupCode = groupCode.Trim(); DisplayName = displayName;
            if (GroupCode.Length > 40 || !Regex.IsMatch(GroupCode, @"\A[A-Za-z_][A-Za-z0-9_]*\z")) throw new ArgumentException("分组编码必须符合数据库配置的 ASCII 标识格式。");
            HeaderType = headerType ?? throw new ArgumentNullException(nameof(headerType));
            LineType = lineType ?? throw new ArgumentNullException(nameof(lineType));
            byKey = new Dictionary<string, ExcelImportField>(StringComparer.Ordinal);
            AddFields(HeaderType, "Header."); AddFields(LineType, "Line.");
            Fields = Array.AsReadOnly(byKey.Values.OrderBy(field => field.FieldKey, StringComparer.Ordinal).ToArray());
        }
        public string GroupCode { get; }
        public string DisplayName { get; }
        public Type HeaderType { get; }
        public Type LineType { get; }
        public IReadOnlyList<ExcelImportField> Fields { get; }

        /// <summary>判断稳定字段键是否在此模型组合中显式声明，不能根据公共 setter 推断可导入性。</summary>
        public bool ContainsField(string fieldKey) => fieldKey != null && byKey.ContainsKey(fieldKey);

        /// <summary>赋值单个已映射值，使用调用方明确指定的日期文化；错误不写目标属性。</summary>
        public bool TryAssign(object model, string fieldKey, object value, CultureInfo dateCulture, out string error)
            => TryAssignValues(model, new[] { new KeyValuePair<string, object>(fieldKey, value) }, dateCulture, out error);

        /// <summary>先检查活动键和类型再赋值；销售旧汇率输入忽略，其他重复目标仍拒绝覆盖。</summary>
        public bool TryAssignValues(object model, IEnumerable<KeyValuePair<string, object>> values, CultureInfo dateCulture, out string error)
        {
            error = "";
            if (model == null || values == null || dateCulture == null) { error = "模型、映射值和日期文化不能为空。"; return false; }
            if (model.GetType() != HeaderType && model.GetType() != LineType) { error = "目标模型不属于当前导入分组。"; return false; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var prepared = new List<KeyValuePair<ExcelImportField, object>>();
            foreach (var value in values)
            {
                // 旧快照可能仍携带汇率键；不转换其原值，也不把已停用输入当成未知字段错误。
                if (model.GetType() == HeaderType && ExcelAddressRules.IsDeprecatedField(GroupCode, value.Key)) continue;
                if (value.Key == null || !byKey.TryGetValue(value.Key, out var field) || field.ModelType != model.GetType())
                { error = "目标字段未在当前模型声明为可导入：" + value.Key; return false; }
                if (!seen.Add(value.Key)) { error = "多个源列同时命中目标字段：" + value.Key; return false; }
                if (!field.TryConvert(value.Value, dateCulture, out var converted, out error)) return false;
                prepared.Add(new KeyValuePair<ExcelImportField, object>(field, converted));
            }
            // 所有转换成功后才写入；属性通知与业务输入校验仍由编辑模型承担。
            foreach (var value in prepared) value.Key.Assign(model, value.Value);
            return true;
        }

        // 只扫描显式注册类型的实例属性，禁止扫描程序集猜业务组或递归进入身份／命令对象。
        private void AddFields(Type modelType, string prefix)
        {
            foreach (var property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = property.GetCustomAttribute<ExcelImportFieldAttribute>();
                if (attribute == null) continue;
                if (string.IsNullOrWhiteSpace(attribute.FieldKey) || !attribute.FieldKey.StartsWith(prefix, StringComparison.Ordinal)
                    || attribute.FieldKey.Length == prefix.Length || attribute.FieldKey.Length > 100
                    || !Regex.IsMatch(attribute.FieldKey, @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z")
                    || string.IsNullOrWhiteSpace(attribute.DisplayName)
                    || property.GetIndexParameters().Length != 0 || property.GetSetMethod() == null
                    || property.PropertyType != typeof(string) && property.PropertyType != typeof(DateTime?))
                    throw new InvalidOperationException("导入字段声明无效：" + modelType.Name + "." + property.Name);
                if (byKey.ContainsKey(attribute.FieldKey)) throw new InvalidOperationException("导入字段键重复：" + attribute.FieldKey);
                byKey.Add(attribute.FieldKey, new ExcelImportField(modelType, property, attribute));
            }
        }
    }
}
