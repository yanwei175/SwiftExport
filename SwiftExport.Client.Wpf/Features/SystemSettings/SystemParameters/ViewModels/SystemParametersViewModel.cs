using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.SystemParameters.Models;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels;

/// <summary>按注册参数维护表格草稿，逐行条件保存；刷新保留草稿，页签拥有并发和关闭保护。</summary>
public sealed partial class SystemParametersViewModel : ObservableObject, IDisposable
{
    private readonly ISystemParameterApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ISystemParameterCacheService _cache;
    private readonly ICurrentUserService _user;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<SystemParameterLineModel> _rows = [];
    private readonly string? _userId;
    private bool _initialized, _disposed;

    /// <summary>创建管理员页签，每个参数键拥有独立输入和版本，不新增任意参数定义。</summary>
    public SystemParametersViewModel(ISystemParameterApi api, IApiCallRunner runner, ISystemParameterCacheService cache,
        ICurrentUserService user, IDialogService dialogs)
    {
        _api = api; _runner = runner; _cache = cache; _user = user; _dialogs = dialogs;
        _userId = user.CurrentUser?.UserId;
        Rows = new(_rows);
    }
    /// <summary>服务端注册的只读行集合；参数值在对应行中编辑。</summary>
    public ReadOnlyObservableCollection<SystemParameterLineModel> Rows { get; }
    /// <summary>请求期间冻结所有行及刷新，防止并发保存或关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInteractionEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RefreshCommand), nameof(RejectChangesCommand))]
    public partial bool IsBusy { get; private set; }
    /// <summary>页面级读取反馈，具体保存结果显示在对应参数行。</summary>
    [ObservableProperty] public partial string Status { get; private set; } = "尚未读取系统参数。";
    /// <summary>任意一行有输入差异时保护整个页签。</summary>
    public bool HasUnsavedChanges => _rows.Any(row => row.HasUnsavedChanges);
    /// <summary>编辑必须属于当前管理员会话且没有在途请求。</summary>
    public bool IsInteractionEnabled => IsCurrent() && !IsBusy;
    private bool CanSave(SystemParameterLineModel? row) => IsInteractionEnabled && row is not null && _rows.Contains(row)
        && (row.HasUnsavedChanges || !row.IsConfigured) && row.ValidationMessage.Length == 0;
    private bool CanRefresh() => IsInteractionEnabled;
    private bool CanReject(SystemParameterLineModel? row) => IsInteractionEnabled && row is not null
        && _rows.Contains(row) && row.HasUnsavedChanges;

    /// <summary>按需加载所有注册参数，重复激活不重新读取或丢弃草稿。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        IsBusy = true;
        try
        {
            var result = await _cache.EnsureLoadedAsync();
            if (!IsCurrent()) return;
            if (result.Success) MergeCache();
            else Status = result.Message;
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>仅保存当前行的冻结值及版本，成功后接纳该行，其他行草稿保持原值。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(SystemParameterLineModel? row)
    {
        if (!CanSave(row)) return;
        var key = row!.Key;
        var request = new UpdateSystemParameterRequest { Value = row.Value, ExpectedVersion = row.Version };
        IsBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.UpdateAsync(key, request, _lifetime.Token), _lifetime.Token, "保存系统参数失败");
            if (!IsCurrent()) return;
            if (!result.Success || result.Data is null)
            {
                row.Status = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "参数已由其他人修改。输入已保留；刷新后撤销本行编辑以读取最新值，再重新修改。"
                    : result.Message + " 输入已保留；保存结果不明时请先刷新核对。";
                return;
            }
            if (result.Data.Key != key)
            { row.Status = "保存回执的参数键不匹配，输入已保留；请刷新核对。"; return; }
            _cache.ApplySaved(result.Data);
            row.Accept(result.Data);
            row.Status = $"已保存，版本 {row.Version}。本客户端立即生效，其他客户端可手动刷新。";
            NotifyRowState();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>刷新缓存并接纳没有草稿的行；正在编辑的行保留输入和原版本。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        IsBusy = true;
        try
        {
            var result = await _cache.RefreshAsync(_lifetime.Token);
            if (!IsCurrent()) return;
            if (result.Success) MergeCache();
            else Status = result.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>显式撤销一行输入，采用缓存中该键的最新值和版本。</summary>
    [RelayCommand(CanExecute = nameof(CanReject))]
    private void RejectChanges(SystemParameterLineModel? row)
    {
        if (CanReject(row)) RejectRow(row!);
    }

    /// <summary>离开或关闭前保护全部行输入，在途读取或保存不允许离开。</summary>
    public async Task<bool> CanLeaveAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (!HasUnsavedChanges) return true;
        if (!await _dialogs.ConfirmAsync("系统参数尚未保存", "是否丢弃本次系统参数编辑？", "丢弃", cancellationToken: cancellationToken)) return false;
        if (!_disposed) foreach (var row in _rows.Where(item => item.HasUnsavedChanges).ToArray()) RejectRow(row);
        return true;
    }
    private bool IsCurrent() => !_disposed && _user.IsAuthenticated && _user.IsAdmin && _user.CurrentUser?.UserId == _userId;

    /// <summary>按键合并注册定义，增加新参数不会要求为它新增页面；未提交行始终保留。</summary>
    private void MergeCache()
    {
        foreach (var parameter in _cache.Parameters)
        {
            var row = _rows.FirstOrDefault(item => item.Key == parameter.Key);
            if (row is null)
            {
                row = new SystemParameterLineModel();
                row.Accept(parameter);
                row.PropertyChanged += OnRowChanged;
                _rows.Add(row);
            }
            else if (!row.HasUnsavedChanges) row.Accept(parameter);
            else row.Status = "缓存已刷新，本行输入和原版本已保留；撤销编辑可读取最新值。";
        }
        Status = $"已读取 {_rows.Count} 个参数。每行独立保存，未保存输入会保留。";
        NotifyRowState();
    }
    /// <summary>缓存没有该键时也能丢弃输入，恢复本行读取基线。</summary>
    private void RejectRow(SystemParameterLineModel row)
    {
        var parameter = _cache.Parameters.FirstOrDefault(item => item.Key == row.Key);
        if (parameter is not null) row.Accept(parameter);
        else { row.RejectToBaseline(); row.Status = "未能读取最新参数，请刷新后重试。"; }
        NotifyRowState();
    }
    /// <summary>行输入变化只通知草稿和命令资格，不隐式执行 API。</summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameterLineModel.Value) or nameof(SystemParameterLineModel.HasUnsavedChanges)) NotifyRowState();
    }
    /// <summary>将各行的草稿变化通知页签及行操作按钮。</summary>
    private void NotifyRowState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged(); RejectChangesCommand.NotifyCanExecuteChanged();
    }
    /// <summary>销毁页签时取消请求并解除行订阅，迟到回执不能应用到新会话。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        foreach (var row in _rows) row.PropertyChanged -= OnRowChanged;
    }
}
