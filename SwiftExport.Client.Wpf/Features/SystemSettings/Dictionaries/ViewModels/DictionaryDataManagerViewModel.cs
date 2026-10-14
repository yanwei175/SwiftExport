using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;

/// <summary>在按字典类型隔离的页签中维护明细，复用编辑器并保留各页私有草稿。</summary>
public sealed partial class DictionaryDataManagerViewModel : ViewModelBaseEx, IDocumentTabInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>由业务入口指定字典类型的公共路由，不另增侧边菜单。</summary>
    public const string RouteKey = "dictionary-data-manager";
    private bool _disposed;

    /// <summary>每个页签拥有独立明细工作区，类型在文档初始化时确定。</summary>
    public DictionaryDataManagerViewModel(DictionaryDataEditorViewModel values)
    {
        Values = values; Title = "字典明细"; values.ContextEditable = true;
        values.PropertyChanged += OnValuesChanged;
    }

    /// <summary>页面专属字典值编辑工作区。</summary>
    public DictionaryDataEditorViewModel Values { get; }
    /// <summary>当前字典类型的维护说明，由打开入口提供。</summary>
    [ObservableProperty] public partial string Description { get; private set; } = string.Empty;
    /// <inheritdoc />
    public string DocumentTitle => Title;
    /// <inheritdoc />
    public bool HasUnsavedChanges => Values.HasUnsavedChanges;
    /// <inheritdoc />
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not DictionaryDataDocumentOpenRequest request)
            throw new ArgumentException("打开字典明细需要指定字典类型。", nameof(parameter));
        // 一页只拥有一个类型，禁止重用同一编辑工作区替换另一类型的草稿。
        if (Values.DictCode is { } existingCode && !string.Equals(existingCode, request.DictCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("字典明细页不能切换类型，请打开对应类型的页签。");
        Title = request.DictionaryName;
        Description = request.Description;
        OnPropertyChanged(nameof(DocumentTitle));
        await Values.ActivateAsync(request.DictCode, request.DictionaryName).WaitAsync(cancellationToken);
    }
    /// <inheritdoc />
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Values.CanCloseAsync(cancellationToken);

    private void OnValuesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DictionaryDataEditorViewModel.HasUnsavedChanges))
        { IsDirty = HasUnsavedChanges; OnPropertyChanged(nameof(HasUnsavedChanges)); }
    }
    /// <summary>释放独立页签工作区，晚到读取或写入结果不能覆盖其他页面。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; Values.PropertyChanged -= OnValuesChanged; Values.Dispose(); }
}
