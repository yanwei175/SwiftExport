using System.Collections.ObjectModel;

namespace SwiftExportClient.Services.Files.Templates;

/// <summary>保存一次 Excel 模板生成的命名变量；变量内的对象和列表须由调用方提前形成稳定快照。</summary>
public sealed class ExcelTemplateData
{
    /// <summary>复制变量字典，避免调用方增删变量影响生成；不克隆变量内的 POCO 和列表。</summary>
    public ExcelTemplateData(IReadOnlyDictionary<string, object?> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        Variables = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(variables, StringComparer.Ordinal));
    }

    /// <summary>模板可访问的命名变量；生成期间调用方不得并发修改其对象和列表。</summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }
}
