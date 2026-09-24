using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Shipping.PackingLists.Services;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>过账交互会话；冻结不确定命令，只有服务端明确结果才能释放编辑。</summary>
public sealed partial class PackingListDetailViewModel
{
    private readonly IPackingListPostingDialogService? _postingDialogs;
    private readonly ICurrentUserService? _postingUser;
    private PostPackingListCommand? _pendingPost;
    private ReversePackingListCommand? _pendingReverse;
    private bool HasPendingPosting => _pendingPost != null || _pendingReverse != null;
    /// <summary>权限快照改变时刷新操作按钮，不改变当前编辑内容。</summary>
    private void OnPostingUserChanged(object? sender, EventArgs args) => NotifyState();

    private bool CanPost() => PostingAvailable(PermissionKeys.PackingListPost) && _pendingReverse == null
        && Header.PostingStatus == PackingListPostingStatusEnum.Unposted && !ReverseCommand.IsRunning;
    private bool CanReverse() => PostingAvailable(PermissionKeys.PackingListReverse) && _pendingPost == null && !_isNew
        && Header.PostingStatus == PackingListPostingStatusEnum.Posted && !PostCommand.IsRunning;
    private bool PostingAvailable(string permission) => !_disposed && _loaded && !IsBusy && !_dialogOpen
        && _postingDialogs != null && _postingUser?.HasPermission(permission) == true
        && _postingUser.HasPermission(PermissionKeys.ModuleShippingAccess);

    /// <summary>先保存最新草稿；已发出但结果不确定的请求直接重试原命令，不重新预览或保存。</summary>
    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        if (!CanPost()) return;
        if (_pendingPost != null) { await SubmitPostingAsync(false); return; }
        await SaveAsync();
        if (!_lastSaveSucceeded || _disposed || _isNew || HasUnsavedChanges || !CanPost()) return;
        await PreparePostingAsync(false);
    }

    /// <summary>反向基于原执行证据，原因必填；不改变当前箱单业务日期。</summary>
    [RelayCommand(CanExecute = nameof(CanReverse))]
    private Task ReverseAsync() => !CanReverse() ? Task.CompletedTask :
        _pendingReverse != null ? SubmitPostingAsync(true) : PreparePostingAsync(true);

    /// <summary>弹窗期间冻结编辑，预览与确认使用相同日期、原因和头版本。</summary>
    private async Task PreparePostingAsync(bool reverse)
    {
        _dialogOpen = true; NotifyState();
        try
        {
            var options = await _postingDialogs!.GetOptionsAsync(reverse, _lifetime.Token);
            if (_disposed || options == null) return;
            if (options.BusinessDate is not { } date || date == default
                || reverse && (string.IsNullOrWhiteSpace(options.Reason) || options.Reason.Length > 2000))
            { ReportValidationWarning("请填写库存业务日期；反过账原因必填且不能超过 2000 字。"); return; }
            var version = Header.Version;
            var reason = options.Reason?.Trim();
            var response = reverse
                ? await _runner.RunAsync(() => _api.ReversePreviewAsync(_uid, new() { ExpectedVersion = version, BusinessDate = date.Date, Reason = reason }, _lifetime.Token), _lifetime.Token, "反向预览失败")
                : await _runner.RunAsync(() => _api.PostPreviewAsync(_uid, new() { ExpectedVersion = version, BusinessDate = date.Date }, _lifetime.Token), _lifetime.Token, "过账预览失败");
            if (_disposed || _lifetime.IsCancellationRequested) return;
            var preview = response.Data;
            if (!response.Success || preview == null) { ReportValidationWarning(response.Message); return; }
            if (preview.PackingListId != _uid || preview.ExpectedVersion != version || preview.BusinessDate.Date != date.Date
                || preview.IsReverse != reverse || preview.Actions == null || preview.Blockers == null)
            { ReportValidationWarning("预览响应身份或日期不一致，请重新预览。"); return; }
            if (preview.Blockers.Count > 0) ReportValidationWarning(string.Join(Environment.NewLine, preview.Blockers.Select(x => x.Message)));
            var confirmed = await _postingDialogs.ConfirmAsync(preview, _lifetime.Token);
            if (!confirmed || _disposed || preview.Blockers.Count != 0 || string.IsNullOrWhiteSpace(preview.PreviewToken)) return;
            if (Header.Version != version || HasUnsavedChanges) { ReportValidationWarning("草稿已变化，请保存后重新预览。"); return; }
            // 弹窗期间会话权限可能被刷新，不能沿用打开弹窗时的客户端授权快照。
            if (_postingUser?.HasPermission(PermissionKeys.ModuleShippingAccess) != true
                || !_postingUser.HasPermission(reverse ? PermissionKeys.PackingListReverse : PermissionKeys.PackingListPost))
            { ReportValidationWarning("操作权限已变化，未提交过账命令。"); return; }
            if (reverse) _pendingReverse = new() { CommandId = Guid.NewGuid(), ExpectedVersion = version, BusinessDate = date.Date, Reason = reason, PreviewToken = preview.PreviewToken };
            else _pendingPost = new() { CommandId = Guid.NewGuid(), ExpectedVersion = version, BusinessDate = date.Date, PreviewToken = preview.PreviewToken };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) await HandleExceptionAsync(error); }
        finally { _dialogOpen = false; NotifyState(); }
        if (!_disposed && HasPendingPosting) await SubmitPostingAsync(reverse);
    }

    /// <summary>写请求不受查询取消影响；不确定失败保留完整原命令并冻结编辑、刷新和关闭。</summary>
    private async Task SubmitPostingAsync(bool reverse)
    {
        if (_disposed || IsBusy || _dialogOpen) return;
        await RunBusyAsync(async () =>
        {
            var result = reverse
                ? await _runner.RunAsync(() => _api.ReverseAsync(_uid, _pendingReverse!, CancellationToken.None), "反过账失败")
                : await _runner.RunAsync(() => _api.PostAsync(_uid, _pendingPost!, CancellationToken.None), "过账失败");
            if (_disposed) return;
            var expectedStatus = reverse ? PackingListPostingStatusEnum.Reversed : PackingListPostingStatusEnum.Posted;
            if (!result.Success || result.Data?.Status is not ("Success" or "AlreadyApplied") || !IsValidDetail(result.Data.Detail)
                || result.Data.Detail.Header.PostingStatus != expectedStatus)
            {
                // 只有服务端明确的未执行结果才能解除冻结；没有结构化结果时不猜测网络提交状态。
                if (result.Data?.Status is "VersionConflict" or "PreviewExpired"
                    || result.Data?.Status == "Blocked" && result.Data.ErrorCode is not ("COMMAND_BUSY" or "IDEMPOTENCY_CONFLICT" or "EXECUTION_INVALID" or "RESULT_MISSING"))
                { _pendingPost = null; _pendingReverse = null; }
                ReportValidationWarning(HasPendingPosting ? $"{result.Message} 结果尚未确认，请再次点击原操作以重试同一命令；当前编辑已冻结。" : result.Message);
                return;
            }
            _pendingPost = null; _pendingReverse = null;
            ApplyDetail(result.Data.Detail); ValidationSummary = null;
            ResultSummary = reverse ? "整单反过账成功，交付剩余已恢复。" : "过账成功，交付与库存执行记录已保存。";
            _messages.ShowSuccess("PackingList", ResultSummary);
            foreach (var manager in _navigation.OpenTabs.Select(x => x.ContentViewModel).OfType<PackingListManagerViewModel>())
                await manager.RefreshCommand.ExecuteAsync(null);
        }, reverse ? "反过账中…" : "过账中…");
        NotifyState();
    }

    /// <summary>只读查询实际执行，取消或页面释放后不展示旧结果。</summary>
    [RelayCommand]
    private async Task TraceAsync()
    {
        if (_disposed || !_loaded || _isNew || IsBusy || _dialogOpen || HasPendingPosting || _postingDialogs == null) return;
        _dialogOpen = true; NotifyState();
        try
        {
            var result = await _runner.RunAsync(() => _api.TraceAsync(_uid, _lifetime.Token), _lifetime.Token, "执行追踪读取失败");
            if (_disposed || _lifetime.IsCancellationRequested) return;
            if (!result.Success || result.Data?.PackingListId != _uid) { ReportValidationWarning(result.Message); return; }
            await _postingDialogs.ShowTraceAsync(result.Data, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) await HandleExceptionAsync(error); }
        finally { _dialogOpen = false; NotifyState(); }
    }
}
