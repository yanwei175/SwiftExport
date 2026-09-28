using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;

/// <summary>只读权限定义目录，独立于用户和角色查询。</summary>
public sealed partial class PermissionsQueryViewModel : SettingsQueryViewModel
{
    private readonly ISystemRbacApi _api;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _query;
    private int _queryRevision;
    private bool _loaded;
    private bool _cleared;

    /// <inheritdoc />
    protected override string ReadPermission => "System.Permissions.View";
    /// <summary>服务端只读权限定义集合。</summary>
    public ObservableCollection<ManagedPermissionDto> Permissions { get; } = [];

    /// <summary>注入权限定义 API 与会话。</summary>
    public PermissionsQueryViewModel(ISystemRbacApi api, IApiCallRunner runner, ICurrentUserService currentUser)
        : base(runner, currentUser) => _api = api;

    /// <summary>首次进入时读取目录，重复呈现不请求用户或角色接口。</summary>
    public Task InitializeAsync() => _loaded ? Task.CompletedTask : RefreshPermissionsPageAsync();

    /// <summary>显式刷新目录；新请求取消旧请求，迟到结果不能回写。</summary>
    [RelayCommand]
    public async Task RefreshPermissionsPageAsync()
    {
        if (!CanAccept || _cleared) return;
        var revision = ++_queryRevision;
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var old = Interlocked.Exchange(ref _query, next);
        old?.Cancel(); old?.Dispose();
        await RunBusyAsync(async () =>
        {
            var result = await Runner.RunAsync(() => _api.GetPermissionsAsync().WaitAsync(next.Token),
                next.Token, "读取权限目录失败");
            if (!CanAccept || next.IsCancellationRequested || revision != _queryRevision) return;
            if (!result.Success || result.Data is null) { StatusMessage = result.Message; return; }
            Permissions.Clear();
            foreach (var item in result.Data) Permissions.Add(item);
            _loaded = true;
            StatusMessage = $"已载入 {Permissions.Count} 项权限定义。";
        }, "正在加载权限目录...", skipIfBusy: false);
    }

    /// <inheritdoc />
    protected override void ClearData()
    {
        _loaded = false; Permissions.Clear(); ++_queryRevision;
        if (_cleared) return;
        _cleared = true;
        _lifetime.Cancel(); _query?.Cancel();
        _query?.Dispose(); _lifetime.Dispose();
    }
}
