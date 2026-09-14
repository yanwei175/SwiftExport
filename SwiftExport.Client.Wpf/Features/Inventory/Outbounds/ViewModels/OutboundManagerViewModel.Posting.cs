using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.Services;

namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;

/// <summary>当前单正向过账：先预览确认，再冻结命令并复用持久化恢复协议。</summary>
public sealed partial class OutboundManagerViewModel
{
    private readonly IOutboundPostDialogService? _postDialogs;
    private readonly ICurrentUserService? _postingUser;

    private bool CanPost() => CanEdit() && _persistence is not null && _postDialogs is not null
        && _postingUser?.HasPermission(PermissionKeys.InventoryOutboundPost) == true
        && _postingUser.HasPermission(PermissionKeys.ModuleInventoryAccess)
        && SelectedItem is { CanEdit: true, IsNew: false, HasVersionConflict: false, DocumentType: OutboundDocumentTypeEnum.StockIssue };

    /// <summary>只操作当前已保存且无修改的单，不自动保存，不用预览替代正式重验。</summary>
    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        if (!CanPost() || !CommitGridEdits() || RequirePendingRecovery()) return;
        var row = SelectedItem!;
        if (row.IsDirty || row.Lines.HasChanges)
        { ValidationSummary = "当前出库单有未保存修改，请先保存，再预览过账。"; return; }
        await RunWriteAsync(async () =>
        {
            var version = row.Version;
            var result = await _persistence!.PreviewPostAsync(row.Uid, new() { ExpectedVersion = version }, _lifetime.Token);
            if (_disposed) return;
            if (!result.Success || result.Data is null)
            { ResultSummary = result.Message ?? "过账预览失败，没有提交库存。"; return; }
            var preview = result.Data;
            if (preview.Uid != row.Uid || preview.Version != version || preview.Blockers is null || preview.StockImpacts is null)
            { ResultSummary = "过账预览版本或身份不匹配，请重新加载。"; return; }
            if (!await _postDialogs!.ConfirmAsync(preview, _lifetime.Token) || !preview.CanPost || preview.Blockers.Count > 0 || _disposed) return;
            if (row.IsDirty || row.Lines.HasChanges || row.Version != version || !row.CanEdit)
            { ResultSummary = "确认期间单据已变化，请重新保存并预览。"; return; }
            var pending = new OutboundPendingWrite { DocumentId = row.Uid, Post = new() { CommandId = Guid.NewGuid(), ExpectedVersion = version } };
            BeginPending(row, pending);
            if (await CompletePendingAsync(row, pending) && !_disposed)
                ResultSummary = $"{row.DocumentNo} 已过账，单据只读；暂不支持修改或撤销。";
        }, "预览并提交当前出库单");
    }

    /// <summary>将独立过账响应接入现有单据身份回填，不扩大草稿命令的传输契约。</summary>
    private async Task<ServiceResult<OutboundDraftCommandResult>> SendPostAsync(OutboundPendingWrite pending)
    {
        var result = await _persistence!.PostAsync(pending.DocumentId, pending.Post!, CancellationToken.None);
        if (!result.Success) return ServiceResult<OutboundDraftCommandResult>.Fail(result.Error?.Code ?? OutboundErrorCodes.OutcomeUnknown, result.Message, result.Error?.Field);
        if (result.Data is null || result.Data.PostingStatus != OutboundPostingStatusEnum.Posted)
            return ServiceResult<OutboundDraftCommandResult>.Fail(OutboundErrorCodes.OutcomeUnknown, "过账响应状态不完整，请以原命令确认。");
        return ServiceResult<OutboundDraftCommandResult>.Ok(new() { Uid = result.Data.Uid, Version = result.Data.Version, DocumentNo = result.Data.DocumentNo });
    }

    private void OnPostingUserChanged(object? sender, EventArgs e) => PostCommand.NotifyCanExecuteChanged();
}
