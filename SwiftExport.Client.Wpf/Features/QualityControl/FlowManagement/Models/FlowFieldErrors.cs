using System.Collections;
using System.ComponentModel;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Models;

/// <summary>本功能的字段错误容器；供头行模型实现统一错误通知，不承担业务输入或脏跟踪。</summary>
public sealed class FlowFieldErrors : INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    /// <summary>字段错误新增或移除时通知绑定。</summary>
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    /// <summary>当前是否有阻断输入的字段错误。</summary>
    public bool HasErrors => _errors.Count > 0;
    /// <summary>读取指定字段错误，空字段名返回所有错误。</summary>
    public IEnumerable GetErrors(string? propertyName) => string.IsNullOrEmpty(propertyName)
        ? _errors.Values.ToArray() : _errors.TryGetValue(propertyName, out var error) ? new[] { error } : Array.Empty<string>();
    /// <summary>设置一条明确字段键的错误并通知视图。</summary>
    public string Set(string property, string message)
    {
        _errors[property] = message;
        ErrorsChanged?.Invoke(this, new(property));
        return message;
    }
    /// <summary>字段重新编辑时清除旧错误。</summary>
    public void Clear(string property)
    {
        if (_errors.Remove(property)) ErrorsChanged?.Invoke(this, new(property));
    }
    /// <summary>重新验证或装载服务端基线前清除上一轮错误。</summary>
    public void ClearAll()
    {
        var fields = _errors.Keys.ToArray(); _errors.Clear();
        foreach (var field in fields) ErrorsChanged?.Invoke(this, new(field));
    }
}

