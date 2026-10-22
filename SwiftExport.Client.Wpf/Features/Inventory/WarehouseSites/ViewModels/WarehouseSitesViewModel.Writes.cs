using System.Text.Json;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.WarehouseSites.Models;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

/// <summary>仓库本页写命令恢复；只保留本实例内存中的原命令，不建立跨登录或磁盘队列。</summary>
public sealed partial class WarehouseSitesViewModel
{
    private FrozenWarehouseWrite? _pendingWrite;
    private int _userSession;
    private string _observedUser = string.Empty;
    private string CurrentUserKey => _currentUser.CurrentUser?.UserId ?? string.Empty;
    /// <summary>存在未消解的原请求时禁止替换输入或关闭页面。</summary>
    public bool HasPendingWrite => _pendingWrite is not null;
    /// <summary>保存中、结果未知或缺少管理权限时冻结抽屉输入。</summary>
    public bool CanEditInputs => _isAlive && HasManagePermission && IsNotBusy && !HasPendingWrite;
    /// <summary>沿用原按钮位置，明确未知结果只能恢复原命令。</summary>
    public string SaveActionText => HasPendingWrite ? "重试原命令" : "保存";
    /// <summary>抽屉外同样提供原命令恢复入口。</summary>
    public string RefreshActionText => HasPendingWrite ? "重试原命令" : "刷新";
    /// <summary>使用既有导航关闭保护保留编辑和未决请求。</summary>
    public bool HasUnsavedChanges => EditModel?.HasChanges == true || HasPendingWrite;
    /// <summary>固定页签标题。</summary>
    public string DocumentTitle => Title;
    /// <summary>正常导航关闭不能丢失尚未明确的写入上下文。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => IsBusy || HasPendingWrite ? Task.FromResult(false) : PrepareDrawerReplacementAsync();
    private bool CanRetryWrite() => _isAlive && IsNotBusy && HasManagePermission && _pendingWrite?.User == CurrentUserKey;
    private Task RetryWriteAsync() => CanRetryWrite() ? RunBusyAsync(SendFrozenWriteAsync, "核实原仓库命令...") : Task.CompletedTask;

    /// <summary>每次从冻结JSON还原原身份和输入；未知后任何拒绝都不能否认前一次可能已提交。</summary>
    private async Task SendFrozenWriteAsync()
    {
        if (_pendingWrite is not { } pending || pending.User != CurrentUserKey || !HasManagePermission || !_isAlive) return;
        var session = _userSession;
        ServiceResult<WarehouseSiteDto> result;
        try
        {
            result = await _runner.RunAsync(() => pending.Kind switch
            {
                "create" => _api.CreateSupplierVirtualAsync(JsonSerializer.Deserialize<CreateSupplierVirtualWarehouseCommand>(pending.Json)!),
                "update" => _api.UpdateAsync(pending.Target, JsonSerializer.Deserialize<UpdateWarehouseSiteCommand>(pending.Json)!),
                "activate" => _api.ActivateAsync(pending.Target, JsonSerializer.Deserialize<ActivateWarehouseSiteCommand>(pending.Json)!),
                "deactivate" => _api.DeactivateAsync(pending.Target, JsonSerializer.Deserialize<DeactivateWarehouseSiteCommand>(pending.Json)!),
                _ => _api.DeleteAsync(pending.Target, JsonSerializer.Deserialize<DeleteWarehouseSiteCommand>(pending.Json)!)
            }, "仓库命令结果未能确认");
        }
        catch { pending.WasUnknown = true; if (IsCurrentWrite(pending, session)) ResultSummary = "结果未知，请点击重试原命令；本页内存保留原请求，不能关闭或改写。"; return; }
        if (!IsCurrentWrite(pending, session)) { pending.WasUnknown = true; return; }
        var receipt = result.Data;
        var complete = result.Success && receipt is not null && receipt.Uid != Guid.Empty && receipt.Version > 0
            && (pending.Kind == "create" || receipt.Uid == pending.Target && receipt.Version == pending.Version + 1)
            && (pending.Kind != "activate" || receipt.IsActive) && (pending.Kind != "deactivate" || !receipt.IsActive);
        if (!complete)
        {
            var definitive = !result.Success && !pending.WasUnknown && result.Error?.Code is (ServiceErrorCodes.Validation or ServiceErrorCodes.BusinessBlocked
                or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.StateBlocked or ServiceErrorCodes.CommandConflict or ServiceErrorCodes.NotFound
                or ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated);
            if (definitive) { _pendingWrite = null; _messages.ShowError("仓库操作未完成", result.Message); ResultSummary = result.Message; }
            else { pending.WasUnknown = true; ResultSummary = "结果未知，请点击重试原命令；原身份和输入已冻结，本页不能关闭。"; _messages.ShowWarning("仓库结果待核实", ResultSummary); }
            NotifyWriteState(); return;
        }
        _pendingWrite = null;
        if (pending.IntentKey is not null) _writeIntents.Remove(pending.IntentKey);
        if (pending.Editor is { } editor && ReferenceEquals(EditModel, editor))
        {
            // 真实输入已禁用；仍防止外部赋值或迟到输入被成功接纳机械清掉。
            var changedName = editor.Name != pending.EditorName ? editor.Name : null;
            editor.Accept(receipt!);
            if (changedName is not null) { editor.Name = changedName; _messages.ShowWarning("原保存已成功", "提交后产生的新名称已保留，请复核后再保存。"); }
            else CloseDrawerCore();
        }
        NotifyWriteState();
        var refreshed = await LoadCoreAsync(pending.Kind == "delete" ? null : receipt!.Uid) == ListLoadResult.Succeeded;
        if (!IsCurrentWriteOwner(pending, session)) return;
        var action = pending.Kind switch { "create" => "已创建虚拟仓", "update" => "已保存虚拟仓", "activate" => "虚拟仓已启用", "deactivate" => "虚拟仓已停用", _ => "虚拟仓已删除" };
        _messages.ShowSuccess(action, receipt!.Code);
        if (!refreshed) _messages.ShowWarning(action + "，但列表刷新失败", ResultSummary ?? "请稍后刷新，不要重复提交。");
        // 旧仓库选择器缓存仍由原服务维护，缓存刷新失败不得否认成功，也不得向新账号提示旧结果。
        await RefreshWarehousePickerCacheAfterWriteAsync(pending.Kind switch { "activate" => "启用成功，缓存刷新失败", "deactivate" => "停用成功，缓存刷新失败", "delete" => "删除成功，缓存刷新失败", _ => "保存成功，缓存刷新失败" });
    }

    private bool IsCurrentWrite(FrozenWarehouseWrite pending, int session) => ReferenceEquals(_pendingWrite, pending) && IsCurrentWriteOwner(pending, session);
    private bool IsCurrentWriteOwner(FrozenWarehouseWrite pending, int session) => _isAlive && session == _userSession && pending.User == CurrentUserKey;
    /// <summary>同步抽屉、原操作恢复和导航关闭门禁，不因模型回填清除未决状态。</summary>
    private void NotifyWriteState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasPendingWrite)); OnPropertyChanged(nameof(CanEditInputs)); OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SaveActionText)); OnPropertyChanged(nameof(RefreshActionText));
        SaveCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); SearchCommand.NotifyCanExecuteChanged(); CloseDrawerCommand.NotifyCanExecuteChanged();
        OpenCreateCommand.NotifyCanExecuteChanged(); OpenEditCommand.NotifyCanExecuteChanged(); ActivateCommand.NotifyCanExecuteChanged(); DeactivateCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>冻结本页原请求及编辑锚点；状态位只描述恢复历史，不参与请求内容。</summary>
    private sealed class FrozenWarehouseWrite(string user, string kind, Guid target, int version, string json, WarehouseSiteEditModel? editor, string? intentKey)
    {
        public string User { get; } = user;
        public string Kind { get; } = kind;
        public Guid Target { get; } = target;
        public int Version { get; } = version;
        public string Json { get; } = json;
        public WarehouseSiteEditModel? Editor { get; } = editor;
        public string? EditorName { get; } = editor?.Name;
        public string? IntentKey { get; } = intentKey;
        public bool WasUnknown { get; set; }
    }
}
