using System.ComponentModel;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Navigation;

/// <summary>表示一个页签的路由状态，并统一拥有内容订阅、取消令牌与依赖注入作用域句柄。</summary>
public sealed class NavigationTabItem : ObservableObjectEx, IDisposable
{
    private string _documentKey;
    private string _title;
    private bool _isDirty;
    private INotifyPropertyChanged? _documentNotifier;
    private INavigationTabLease? _contentLease;
    private CancellationTokenSource? _lifetimeCancellation = new();
    private object? _contentViewModel;
    private bool _isInitializing;
    private bool _isDisposed;

    /// <summary>创建直接拥有指定 ViewModel 的兼容页签；释放页签时会幂等释放该 ViewModel。</summary>
    public NavigationTabItem(
        NavigationRoute route,
        object contentViewModel,
        string? documentKey = null)
        : this(route, documentKey, isInitializing: false)
    {
        AttachContent(new DirectNavigationTabLease(contentViewModel));
    }

    internal NavigationTabItem(
        NavigationRoute route,
        string? documentKey,
        bool isInitializing)
    {
        RouteKey = route.Key;
        _documentKey = string.IsNullOrWhiteSpace(documentKey) ? route.Key : documentKey;
        _title = route.Title;
        SupportsClose = route.CanClose;
        _isInitializing = isInitializing;
    }

    /// <summary>获取此页签是否处于初始化阶段；初始化期间拒绝普通关闭。</summary>
    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    /// <summary>获取路由是否允许用户关闭此页签。</summary>
    public bool SupportsClose { get; }

    /// <summary>获取当前是否可以关闭；初始化中的可关闭路由也会暂时拒绝关闭。</summary>
    public bool CanClose => SupportsClose && !IsInitializing;

    /// <summary>获取页签生命周期取消标记，Reset、失败或释放时会触发取消。</summary>
    internal CancellationToken LifetimeToken => _lifetimeCancellation?.Token ?? new CancellationToken(canceled: true);

    /// <summary>获取页签是否已被 Reset、失败收尾或关闭使其失效。</summary>
    internal bool IsLifetimeCancellationRequested => _lifetimeCancellation?.IsCancellationRequested ?? true;

    /// <summary>撤销页面读取权限时取消在途加载；未保存编辑仍留在非活动页签等待用户决定。</summary>
    internal void SuspendForRevokedPermission() => _lifetimeCancellation?.Cancel();

    /// <summary>获取供 ViewModel-first DataTemplate 呈现的 ViewModel；占位阶段可能为空。</summary>
    public object? ContentViewModel
    {
        get => _contentViewModel;
        private set => SetProperty(ref _contentViewModel, value);
    }

    /// <summary>把工厂创建的唯一内容句柄接入已登记的初始化占位页签。</summary>
    internal void AttachContent(INavigationTabLease contentLease)
    {
        ArgumentNullException.ThrowIfNull(contentLease);
        if (_isDisposed || _contentLease is not null)
        {
            contentLease.Dispose();
            throw new InvalidOperationException("页签内容已释放或已完成绑定。");
        }

        _contentLease = contentLease;
        ContentViewModel = contentLease.ViewModel;

        if (ContentViewModel is IDocumentTabViewModel document)
        {
            SynchronizeDocumentState(document);
            _documentNotifier = ContentViewModel as INotifyPropertyChanged;
            if (_documentNotifier is not null)
            {
                _documentNotifier.PropertyChanged += OnDocumentPropertyChanged;
            }
        }
    }

    /// <summary>标记初始化成功，并恢复路由原有的关闭能力。</summary>
    internal void CompleteInitialization()
    {
        if (!_isDisposed)
        {
            IsInitializing = false;
        }
    }

    public string RouteKey { get; }

    public string DocumentKey
    {
        get => _documentKey;
        internal set => SetProperty(ref _documentKey, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    /// <summary>幂等取消页签生命周期、解除文档订阅并释放唯一内容句柄。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;

        if (_documentNotifier is not null)
        {
            _documentNotifier.PropertyChanged -= OnDocumentPropertyChanged;
            _documentNotifier = null;
        }

        ContentViewModel = null;
        Interlocked.Exchange(ref _contentLease, null)?.Dispose();
    }

    private sealed class DirectNavigationTabLease(object viewModel) : INavigationTabLease
    {
        private object? _viewModel = viewModel;

        public object ViewModel => _viewModel ?? throw new ObjectDisposedException(nameof(DirectNavigationTabLease));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _viewModel, null) is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ContentViewModel is not IDocumentTabViewModel document)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(IDocumentTabViewModel.DocumentTitle)
                or nameof(IDocumentTabViewModel.HasUnsavedChanges))
        {
            SynchronizeDocumentState(document);
        }
    }

    private void SynchronizeDocumentState(IDocumentTabViewModel document)
    {
        Title = string.IsNullOrWhiteSpace(document.DocumentTitle)
            ? Title
            : document.DocumentTitle;
        IsDirty = document.HasUnsavedChanges;
    }
}
