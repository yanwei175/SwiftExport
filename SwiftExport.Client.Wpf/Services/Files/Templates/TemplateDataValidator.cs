using System.Collections;
using System.IO;
using System.Reflection;

namespace SwiftExportClient.Services.Files.Templates;

/// <summary>在交给 Report 前检查稳定模型树，阻止业务字符串被再次解释为 Report 标签。</summary>
internal static class TemplateDataValidator
{
    /// <summary>检查公开字段、属性和已物化列表；错误只包含字段路径，不返回业务值。</summary>
    internal static void Validate(ExcelTemplateData data, CancellationToken cancellationToken)
    {
        var activeObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var issues = new List<FileIssue>();
        var nodeCount = 0;

        foreach (var variable in data.Variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(variable.Key))
                AddIssue("InvalidTemplateVariable", "模板变量名不能为空。", variable.Key);
            Visit(variable.Value, variable.Key, 0);
        }

        if (issues.Count > 0)
            throw new FileOperationException("InvalidTemplateData", "模板数据校验失败。", issues: issues);

        // 深度和节点数限制用于约束反射与遍历成本，不形成业务对象克隆框架。
        void Visit(object? value, string path, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++nodeCount > FileReadLimits.Default.MaxCells || depth > 32)
                throw new FileOperationException("TemplateDataLimitExceeded", "模板数据树超过允许范围。",
                    issues: [new FileIssue("TemplateDataLimitExceeded", "模板数据树超过允许范围。", FieldKey: path, Severity: FileIssueSeverity.Error)]);
            if (value is null)
                return;
            if (value is string text)
            {
                if (text.Contains("<<", StringComparison.Ordinal) || text.Contains(">>", StringComparison.Ordinal))
                    AddIssue("TemplateTagInData", "业务文本不能包含 Report 标签分隔符。", path);
                return;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid)
                return;
            if (value is Delegate or Stream or Task || type.IsPointer)
            {
                AddIssue("InvalidTemplateValue", "模板变量须使用普通数据对象和已物化列表。", path);
                return;
            }
            if (activeObjects.Contains(value))
            {
                AddIssue("TemplateDataCycle", "模板数据不能包含循环引用。", path);
                return;
            }
            if (!visitedObjects.Add(value))
                return;

            activeObjects.Add(value);
            try
            {
                if (value is IDictionary dictionary)
                {
                    var dictionaryIndex = 0;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // 字典键也是数据，不直接拼入诊断路径，避免把用户内容暴露在错误摘要中。
                        Visit(entry.Key, $"{path}[{dictionaryIndex}].Key", depth + 1);
                        Visit(entry.Value, $"{path}[{dictionaryIndex++}].Value", depth + 1);
                    }
                    return;
                }
                if (value is IEnumerable enumerable)
                {
                    if (value is not ICollection && !type.GetInterfaces().Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))
                    {
                        AddIssue("TemplateDataNotSnapshot", "模板列表须提前物化为数组或只读集合。", path);
                        return;
                    }
                    var index = 0;
                    foreach (var item in enumerable)
                        Visit(item, $"{path}[{index++}]", depth + 1);
                    return;
                }

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    Visit(field.GetValue(value), $"{path}.{field.Name}", depth + 1);
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object? propertyValue;
                    try
                    {
                        propertyValue = property.GetValue(value);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        throw new FileOperationException("InvalidTemplateData", "无法读取模板数据字段。", exception,
                            [new FileIssue("TemplateDataGetterFailed", "无法读取模板数据字段。", FieldKey: $"{path}.{property.Name}", Severity: FileIssueSeverity.Error)]);
                    }
                    Visit(propertyValue, $"{path}.{property.Name}", depth + 1);
                }
            }
            finally
            {
                activeObjects.Remove(value);
            }
        }

        void AddIssue(string code, string message, string path)
        {
            // 控制错误摘要数量，避免恶意文本造成无界诊断集合。
            if (issues.Count < 100)
                issues.Add(new FileIssue(code, message, FieldKey: path, Severity: FileIssueSeverity.Error));
        }
    }
}
