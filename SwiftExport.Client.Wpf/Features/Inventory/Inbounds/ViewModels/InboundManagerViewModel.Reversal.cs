using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Services;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
/// <summary>当前手工单整单反过账，复用原命令未知结果扣减机制。</summary>
public sealed partial class InboundManagerViewModel
{
    private readonly IInboundReverseDialogService? _reverseDialogs;
    private bool CanReverse() => CanEdit() && _persistence is not null && _reverseDialogs is not null
        && _postingUser?.HasPermission(PermissionKeys.InventoryInboundReverse) == true
        && _postingUser.HasPermission("Inventory.Inbound.View")
        && SelectedItem is { IsNew: false, IsDetailLoaded: true, IsPersistencePending: false, HasVersionConflict: false,
            PostingStatus: InboundPostingStatusEnum.Posted, DocumentType: InboundDocumentTypeEnum.PurchaseReceipt,
            CreationMode: InboundCreationModeEnum.Manual, LifecycleStatus: InventoryDocumentLifecycleStatusEnum.Active, TriggerPackingListId: null, PersistedIsDeleted: false };

    /// <summary>预览和原因确认不写库存，确认后冻结单号、版本与原因，不能用新命令替代。</summary>
    [RelayCommand(CanExecute = nameof(CanReverse))]
    private async Task ReverseAsync()
    {
        if (!CanReverse() || !CommitGridEdits() || RequirePendingRecovery()) return;
        var row = SelectedItem!;
        if (row.IsDirty || row.Lines.HasChanges) { ValidationSummary = "当前单据有未保存修改，请先处理。"; return; }
        await RunWriteAsync(async () =>
        {
            var version = row.Version;
            var result = await _persistence!.PreviewReverseAsync(row.Uid, new() { ExpectedVersion = version }, _lifetime.Token);
            if (_disposed || !ReferenceEquals(SelectedItem, row)) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message ?? "反过账预览失败，未提交库存。"; return; }
            var preview = result.Data;
            if (preview.Uid != row.Uid || preview.Version != version || preview.Blockers is null || preview.StockImpacts is null)
            { ResultSummary = "反过账预览身份或版本不匹配，请重新加载。"; return; }
            var reason = await _reverseDialogs!.ConfirmAsync(preview, _lifetime.Token);
            if (_disposed || !ReferenceEquals(SelectedItem, row) || string.IsNullOrWhiteSpace(reason) || reason.Length > 500
                || !preview.CanReverse || preview.Blockers.Count > 0) return;
            if (row.IsDirty || row.Lines.HasChanges || row.Version != version || row.PostingStatus != InboundPostingStatusEnum.Posted)
            { ResultSummary = "确认期间单据已变化，请重新加载。"; return; }
            var pending = new InboundPendingWrite { DocumentId = row.Uid, Reverse = new() { CommandId = Guid.NewGuid(), ExpectedVersion = version, Reason = reason } };
            BeginPending(row, pending);
            if (await CompletePendingAsync(row, pending) && !_disposed) ResultSummary = $"{row.DocumentNo} 已整单反过账；原单只读，重新入库请新建单据。";
        }, "预览并提交整单反过账");
    }

    /// <summary>独立反向响应接入现有扣减协议，保留明确成功的状态及审计快照。</summary>
    private async Task<ServiceResult<InboundDraftCommandResult>> SendReverseAsync(InboundPendingWrite pending)
    {
        var result = await _persistence!.ReverseAsync(pending.DocumentId, pending.Reverse!, CancellationToken.None);
        if (!result.Success) return ServiceResult<InboundDraftCommandResult>.Fail(result.Error?.Code ?? ServiceErrorCodes.OutcomeUnknown, result.Message, result.Error?.Field);
        if (result.Data is null || result.Data.PostingStatus != InboundPostingStatusEnum.Reversed || result.Data.ReversedDate == default)
            return ServiceResult<InboundDraftCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "反过账响应不完整，请使用原命令确认。");
        pending.ReverseResult = result.Data;
        return ServiceResult<InboundDraftCommandResult>.Ok(new() { Uid = result.Data.Uid, Version = result.Data.Version, DocumentNo = result.Data.DocumentNo });
    }
}
