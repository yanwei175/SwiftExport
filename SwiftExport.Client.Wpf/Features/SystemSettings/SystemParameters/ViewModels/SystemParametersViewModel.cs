using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels;

/// <summary>管理员编辑注册图片参数；草稿独立于缓存，失败及版本冲突保留输入。</summary>
public sealed partial class SystemParametersViewModel : ObservableObject, IDisposable
{
    private readonly ISystemParameterApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ISystemParameterCacheService _cache;
    private readonly ICurrentUserService _user;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _userId;
    private string _baseline = string.Empty;
    private int _version;
    private bool _initialized, _disposed;

    /// <summary>创建当前页签独立草稿，保存后接纳服务端规范值。</summary>
    public SystemParametersViewModel(ISystemParameterApi api, IApiCallRunner runner, ISystemParameterCacheService cache,
        ICurrentUserService user, IDialogService dialogs)
    {
        _api = api; _runner = runner; _cache = cache; _user = user; _dialogs = dialogs;
        _userId = user.CurrentUser?.UserId;
    }
    /// <summary>唯一注册参数键，界面不允许改名。</summary>
    public string Key => SystemParameterKeys.ProductImagesBaseUrl;
    /// <summary>参数显示名。</summary>
    [ObservableProperty] public partial string DisplayName { get; private set; } = "产品图片基础地址";
    /// <summary>服务端参数说明。</summary>
    [ObservableProperty] public partial string Description { get; private set; } = string.Empty;
    /// <summary>当前编辑输入，与已生效缓存隔离。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RejectChangesCommand))]
    public partial string Value { get; set; } = string.Empty;
    /// <summary>请求期间冻结编辑与页签关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInteractionEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RefreshCommand), nameof(RejectChangesCommand))]
    public partial bool IsBusy { get; private set; }
    /// <summary>配置版本、冲突或刷新反馈。</summary>
    [ObservableProperty] public partial string Status { get; private set; } = "尚未读取系统参数。";
    /// <summary>当前草稿是否区别于读取基线。</summary>
    public bool HasUnsavedChanges => Value != _baseline;
    /// <summary>页面仍属于当前管理员会话且未在途请求。</summary>
    public bool IsInteractionEnabled => !_disposed && !IsBusy && _user.IsAdmin && _user.CurrentUser?.UserId == _userId;
    /// <summary>与后端一致的格式提示，最终仍由服务器校验。</summary>
    public string ValidationMessage => ProductImageAddress.TryNormalizeBaseUrl(Value, out _) ? string.Empty
        : "请输入 HTTP/HTTPS 原图目录地址；不能含凭据、查询、片段或 Thumbnails 目录，最多 2048 字符。";
    private bool CanSave() => IsInteractionEnabled && HasUnsavedChanges && ValidationMessage.Length == 0;
    private bool CanRefresh() => IsInteractionEnabled;
    private bool CanReject() => IsInteractionEnabled && HasUnsavedChanges;

    /// <summary>按需读取缓存，不覆盖已经初始化的草稿。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        IsBusy = true;
        try
        {
            var result = await _cache.EnsureLoadedAsync();
            if (!IsCurrent()) return;
            if (result.Success) AcceptCache();
            else Status = result.Message;
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>保存一次冻结请求，未知结果和冲突不自动重试。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        var request = new UpdateSystemParameterRequest { Value = Value, ExpectedVersion = _version };
        IsBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.UpdateAsync(Key, request, _lifetime.Token), _lifetime.Token, "保存系统参数失败");
            if (!IsCurrent()) return;
            if (!result.Success || result.Data is null)
            {
                Status = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "参数已由其他人修改。输入已保留；刷新后点击撤销编辑以读取最新值，再重新修改。"
                    : result.Message + " 输入已保留；保存结果不明时请先刷新核对。";
                return;
            }
            _cache.ApplySaved(result.Data);
            Accept(result.Data);
            Status = $"已保存，版本 {_version}。本客户端立即生效，其他客户端可在个人设置刷新系统参数。";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>刷新全局快照；已有草稿保持原值和原版本，不能静默覆盖用户输入。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        IsBusy = true;
        try
        {
            var result = await _cache.RefreshAsync(_lifetime.Token);
            if (!IsCurrent()) return;
            if (!result.Success) { Status = result.Message; return; }
            if (!HasUnsavedChanges) AcceptCache();
            else Status = "缓存已刷新，当前未保存输入和原版本已保留；点击撤销编辑可读取最新值。";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>显式丢弃草稿，采用当前最新缓存快照。</summary>
    [RelayCommand(CanExecute = nameof(CanReject))]
    private void RejectChanges() { if (CanReject()) AcceptCache(); }

    /// <summary>离开或关闭前保护草稿，保存进行中拒绝离开。</summary>
    public async Task<bool> CanLeaveAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (!HasUnsavedChanges) return true;
        if (!await _dialogs.ConfirmAsync("系统参数尚未保存", "是否丢弃本次系统参数编辑？", "丢弃", cancellationToken: cancellationToken)) return false;
        if (!_disposed) AcceptCache();
        return true;
    }
    private bool IsCurrent() => !_disposed && _user.IsAuthenticated && _user.IsAdmin && _user.CurrentUser?.UserId == _userId;
    /// <summary>显式接纳最新快照；无快照时仍恢复原草稿基线，避免确认丢弃后残留脏状态。</summary>
    private void AcceptCache()
    {
        var dto = _cache.Parameters.FirstOrDefault(parameter => parameter.Key == Key);
        if (dto is null) { Value = _baseline; Status = "未能读取参数定义，请刷新后重试。"; return; }
        Accept(dto);
        Status = dto.IsConfigured ? $"当前版本 {_version}。" : "尚未配置，首次保存将建立版本 1。";
    }
    /// <summary>接纳权威值和版本，更新草稿基线及关闭保护。</summary>
    private void Accept(SystemParameterDto dto)
    {
        _baseline = dto.Value; _version = dto.Version;
        DisplayName = dto.DisplayName; Description = dto.Description; Value = dto.Value;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged(); RejectChangesCommand.NotifyCanExecuteChanged();
    }
    /// <summary>页签释放或权限撤销时取消在途请求，迟到回执不能应用到新会话。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
