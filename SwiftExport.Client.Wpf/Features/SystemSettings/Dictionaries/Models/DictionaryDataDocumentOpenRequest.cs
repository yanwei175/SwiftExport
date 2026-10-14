namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

/// <summary>指定独立字典明细页的类型与展示文案；同类型使用稳定文档身份。</summary>
public sealed class DictionaryDataDocumentOpenRequest
{
    /// <summary>创建指定类型的打开参数，类型编码不承担 CLR 类型或页面路由职责。</summary>
    public DictionaryDataDocumentOpenRequest(string dictCode, string dictionaryName, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryName);
        DictCode = dictCode.Trim();
        DictionaryName = dictionaryName.Trim();
        Description = description ?? string.Empty;
    }

    /// <summary>现有字典类型的稳定编码。</summary>
    public string DictCode { get; }
    /// <summary>页签标题与编辑器中的字典名称。</summary>
    public string DictionaryName { get; }
    /// <summary>由业务入口提供的类型维护说明。</summary>
    public string Description { get; }
    /// <summary>按字典类型隔离页签；导航忽略编码大小写并复用已有草稿。</summary>
    public string DocumentKey => $"dictionary-data:{DictCode}";
}
