using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

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

        /// <summary>使用缓存配置将已解析表头值映射到模型；复制启用配置后校验别名与目标，配置错误不修改模型。</summary>
        public bool TryAssignMappedValues(object model, IEnumerable<ExcelFieldMappingDto> mappings,
            IEnumerable<KeyValuePair<string, object>> sourceValues, CultureInfo dateCulture, out string error)
        {
            error = "";
            if (mappings == null || sourceValues == null) { error = "字段映射和已解析源值不能为空。"; return false; }
            var byHeader = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mapping in mappings)
            {
                if (mapping == null) { error = "字段映射包含空记录。"; return false; }
                if (!mapping.IsEnabled) continue;
                // 只复制当前调用所需的键，不修改缓存 DTO，不持有后续刷新会改变的列表。
                var header = ExcelHeaderNormalizer.Normalize(mapping.ExcelHeader);
                var target = mapping.TargetFieldKey;
                if (!string.Equals(mapping.GroupCode, GroupCode, StringComparison.Ordinal) || header.Length == 0 || !ContainsField(target))
                { error = "启用的映射不属于本组模型或目标不受支持：" + target; return false; }
                if (byHeader.ContainsKey(header)) { error = "存在重复的规范化 Excel 表头：" + header; return false; }
                byHeader.Add(header, target);
            }
            var assignments = new List<KeyValuePair<string, object>>();
            foreach (var source in sourceValues)
            {
                if (byHeader.TryGetValue(ExcelHeaderNormalizer.Normalize(source.Key), out var target))
                    assignments.Add(new KeyValuePair<string, object>(target, source.Value));
                // 模板可能含图片、统计、银行资料等未配置内容；这些内容由读取流程保留原文，不任意赋值。
            }
            return TryAssignValues(model, assignments, dateCulture, out error);
        }

        /// <summary>先检查全部键和存储类型再赋值；同次读取多个源列命中同一目标时拒绝覆盖。</summary>
        public bool TryAssignValues(object model, IEnumerable<KeyValuePair<string, object>> values, CultureInfo dateCulture, out string error)
        {
            error = "";
            if (model == null || values == null || dateCulture == null) { error = "模型、映射值和日期文化不能为空。"; return false; }
            if (model.GetType() != HeaderType && model.GetType() != LineType) { error = "目标模型不属于当前导入分组。"; return false; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var prepared = new List<KeyValuePair<ExcelImportField, object>>();
            foreach (var value in values)
            {
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
