using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Models;

/// <summary>为设置列表提供分页读取、会话身份和迟到响应校验；不持有任何表单草稿。</summary>
public abstract partial class SettingsQueryViewModel : ViewModelBaseEx, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _query;
    private readonly string? _sessionUserId;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private long _queryRevision;

    /// <summary>本列表需要的服务端读取权限。</summary>
    protected abstract string ReadPermission { get; }
    /// <summary>当前会话与权限快照，写入仍以服务端授权为准。</summary>
    protected ICurrentUserService CurrentUser { get; }
    /// <summary>对 Refit 调用做统一错误归一化。</summary>
    protected IApiCallRunner Runner { get; }
    /// <summary>当前会话和权限允许接纳异步结果时为真。</summary>
    protected bool CanAccept => !_disposed && CurrentUser.IsAuthenticated
        && CurrentUser.CurrentUser?.UserId == _sessionUserId && CurrentUser.HasPermission(ReadPermission);
    /// <summary>页面已结束时为真。</summary>
    public bool IsDisposed => _disposed;
    /// <summary>当前列表读写状态文字。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; protected set; } = string.Empty;
    /// <summary>写命令进行中阻止重复操作和页签关闭。</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; protected set; }

    /// <summary>绑定会话事件，并在账号切换或撤权时清空列表。</summary>
    protected SettingsQueryViewModel(IApiCallRunner runner, ICurrentUserService currentUser)
    {
        Runner = runner;
        CurrentUser = currentUser;
        _sessionUserId = currentUser.CurrentUser?.UserId;
        currentUser.CurrentUserChanged += OnCurrentUserChanged;
    }

    /// <summary>加载所有服务端分页；每次新查询取消旧查询，并在提交前校验身份。</summary>
    protected async Task<List<T>?> ReadPagesAsync<T>(
        Func<ManagedPageRequest, Task<ServiceResult<PagedResponseBase<T>>>> query)
    {
        if (!CanAccept) return null;
        var revision = ++_queryRevision;
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var old = Interlocked.Exchange(ref _query, next);
        old?.Cancel();
        old?.Dispose();
        var items = new List<T>();
        try
        {
            for (var page = 1; ; page++)
            {
                var request = new ManagedPageRequest { PageIndex = page, PageSize = 100 };
                var result = await Runner.RunAsync(() => query(request).WaitAsync(next.Token), next.Token,
                    "读取管理列表失败");
                if (!CanAccept || next.IsCancellationRequested || revision != _queryRevision) return null;
                if (!result.Success || result.Data is null)
                {
                    StatusMessage = result.Message;
                    return null;
                }
                items.AddRange(result.Data.Items);
                if (items.Count >= result.Data.TotalCount || result.Data.Items.Count == 0) return items;
            }
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested) { return null; }
    }

    /// <summary>查询取消或会话失效后清除本列表；子类只处理自己的数据。</summary>
    protected abstract void ClearData();

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThreadId)
        { _context.Post(_ => OnCurrentUserChanged(sender, e), null); return; }
        if (_disposed) return;
        if (CurrentUser.CurrentUser?.UserId != _sessionUserId || !CurrentUser.IsAuthenticated)
        {
            Dispose();
            return;
        }
        if (CurrentUser.HasPermission(ReadPermission)) return;
        _query?.Cancel();
        ++_queryRevision;
        ClearData();
    }

    /// <summary>取消读取，清理数据及会话订阅；重复释放安全。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CurrentUser.CurrentUserChanged -= OnCurrentUserChanged;
        _lifetime.Cancel();
        _query?.Cancel();
        _query?.Dispose();
        _lifetime.Dispose();
        ClearData();
    }
}
