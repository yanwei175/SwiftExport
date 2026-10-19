using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;

/// <summary>仅供跟踪明细四个只读区域复用读取生命周期；各实例独立保存数据、权限、错误及取消状态。</summary>
public sealed partial class DemandTrackingSectionViewModel<T> : ObservableObjectEx, IDisposable where T : class
{
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly string[] _permissions;
    private readonly Func<Guid, CancellationToken, Task<ServiceResult<T>>> _load;
    private readonly Func<T, bool> _isEmpty;
    private readonly Func<T, string?> _review;
    private readonly Func<T, Guid?>? _identity;
    private CancellationTokenSource? _cancellation;
    private Guid _demandId;
    private long _version;
    private bool _attempted;
    private bool _disposed;

    /// <summary>指定实际区域读取、必要权限和结果语义，不向业务层暴露通用加载框架。</summary>
    public DemandTrackingSectionViewModel(IApiCallRunner runner, ICurrentUserService user, string[] permissions,
        Func<Guid, CancellationToken, Task<ServiceResult<T>>> load, Func<T, bool> isEmpty,
        Func<T, string?> review, Func<T, Guid?>? identity = null)
    { _runner = runner; _user = user; _permissions = permissions; _load = load; _isEmpty = isEmpty; _review = review; _identity = identity; }

    [ObservableProperty] public partial T? Data { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "未加载";
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }
    [ObservableProperty] public partial string? ReviewReason { get; private set; }
    /// <summary>区域需要跟踪查看权限以及自身所有读取权限，缺任一项都不发请求。</summary>
    public bool CanRead => !_disposed && _demandId != Guid.Empty && _user.HasPermission(PermissionKeys.OrderTrackingView)
        && _permissions.All(_user.HasPermission);

    /// <summary>切需求、会话或撤权时清除旧区域事实，旧请求按版本失效。</summary>
    public void Reset(Guid demandId)
    {
        ++_version; _cancellation?.Cancel(); _demandId = demandId;
        Data = null; IsLoaded = false; IsLoading = false; _attempted = false; ErrorMessage = ReviewReason = null;
        StatusText = demandId == Guid.Empty ? "未加载" : CanRead ? "未加载" : "无权查看";
        OnPropertyChanged(nameof(CanRead)); ReloadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>首次激活区域时才加载，失败后由用户明确重试。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => _attempted || IsLoading
        ? Task.CompletedTask : LoadAsync(cancellationToken);

    /// <summary>只刷新当前区域；失败、无权和空数据保持不同状态。</summary>
    [RelayCommand(CanExecute = nameof(CanRead), AllowConcurrentExecutions = true)]
    private Task ReloadAsync() => LoadAsync(CancellationToken.None);

    /// <summary>独立执行可取消读取，未知证据不会转成空列表或零数量。</summary>
    private async Task LoadAsync(CancellationToken externalToken)
    {
        if (!CanRead) { if (!_disposed && _demandId != Guid.Empty) StatusText = "无权查看"; return; }
        var version = ++_version; _cancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken); _cancellation = cancellation;
        var token = cancellation.Token; var user = _user.CurrentUser; var id = _demandId;
        _attempted = true; IsLoading = true; ErrorMessage = ReviewReason = null; Data = null; IsLoaded = false; StatusText = "加载中…";
        try
        {
            var result = await _runner.RunAsync(() => _load(id, token), token, "跟踪区域加载失败");
            if (token.IsCancellationRequested || version != _version || id != _demandId || !CanRead || !ReferenceEquals(user, _user.CurrentUser)) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Error?.Code is ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated ? "无权查看" : "加载失败，可重试";
                ErrorMessage = result.Message; return;
            }
            if (_identity is not null && _identity(result.Data) != id)
            { StatusText = "加载失败，可重试"; ErrorMessage = "区域返回的成品需求身份不一致。"; return; }
            Data = result.Data; IsLoaded = true; ReviewReason = _review(result.Data);
            StatusText = !string.IsNullOrWhiteSpace(ReviewReason) ? "待核实" : _isEmpty(result.Data) ? "暂无记录" : "已加载";
        }
        finally
        {
            if (version == _version) IsLoading = false;
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        }
    }

    /// <summary>由所属明细释放当前区域读取，其他页签不受影响。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; ++_version; _cancellation?.Cancel(); }
}
