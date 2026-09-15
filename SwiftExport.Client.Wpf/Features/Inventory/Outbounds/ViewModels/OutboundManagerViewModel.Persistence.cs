using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.Services;

namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;

/// <summary>整单保存、软删除和原命令恢复；待确认状态仅属于当前编辑会话。</summary>
public sealed partial class OutboundManagerViewModel
{
    private readonly IOutboundPersistenceService? _persistence;
    private readonly IOutboundDeleteDialogService? _deleteDialogs;
    private readonly Dictionary<OutboundHeaderModel, OutboundPendingWrite> _pendingWrites = [];

    /// <summary>请求 View 提交原生 Grid 编辑；转换或验证失败可设置 Cancel 阻止发送。</summary>
    public event CancelEventHandler? CommitEditsRequested;

    private bool CanSave() => CanEdit() && _persistence is not null && HasUnsavedChanges;

    /// <summary>发送前完整验证所有修改单据；逐单提交，首个失败后保留后续编辑。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !CommitGridEdits()) return;
        if (RequirePendingRecovery()) return;
        var rows = Headers.Items.Where(x => x.IsDirty || x.Lines.HasChanges).ToArray();
        ValidationSummary = null;
        foreach (var row in rows)
        {
            var error = row.ValidateRow();
            var badLine = row.Lines.Items.FirstOrDefault(x => x.ValidateRow() is not null);
            if (error is null && badLine is not null) error = $"第 {badLine.LineNumber} 行：{badLine.ValidateRow()}";
            if (error is null) continue;
            SelectedItem = row;
            if (badLine is not null) row.Lines.SelectedItem = badLine;
            ValidationSummary = $"{DisplayName(row)}：{error}";
            return;
        }
        await RunWriteAsync(async () =>
        {
            var saved = 0;
            foreach (var row in rows)
            {
                var draft = row.Adapt<OutboundDraftData>();
                var pending = row.IsNew
                    ? new OutboundPendingWrite { DocumentId = row.Uid, Create = new() { CommandId = Guid.NewGuid(), Draft = draft } }
                    : new OutboundPendingWrite { DocumentId = row.Uid, Update = new() { CommandId = Guid.NewGuid(), ExpectedVersion = row.Version, Draft = draft } };
                BeginPending(row, pending);
                if (!await CompletePendingAsync(row, pending))
                { if (!_disposed) { SelectedItem = row; ResultSummary = $"已完成 {saved} 张；{DisplayName(row)}：{ResultSummary}"; } return; }
                saved++;
            }
            if (!_disposed) ResultSummary = $"已保存 {saved} 张出库草稿，库存未变化。列表总数为上次查询快照。";
        }, "逐单保存出库草稿");
    }

    /// <summary>本地新增直接移除；已有草稿须填写共同原因，服务端明确成功才移除页面记录。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanEdit() || !CommitGridEdits() || RequirePendingRecovery()) return;
        var rows = (Headers.CheckedItems.Count > 0 ? Headers.CheckedItems : SelectedItem is null ? [] : new[] { SelectedItem }).ToArray();
        if (rows.Length == 0) return;
        var blocked = rows.FirstOrDefault(x => !x.CanEdit || x.HasVersionConflict);
        if (blocked is not null)
        { SelectedItem = blocked; ResultSummary = "所选单据尚未加载、只读或版本冲突，请先查看或刷新；本次未删除任何单据。"; return; }
        if (rows.Any(x => !x.IsNew) && (_persistence is null || _deleteDialogs is null))
        { ResultSummary = "持久化删除服务不可用。"; return; }
        // 整个确认动作拥有 Busy，避免弹窗等待期间重复进入或改变被确认的对象。
        await RunWriteAsync(async () =>
        {
            string? reason = null;
            if (rows.Any(x => !x.IsNew))
            {
                reason = await _deleteDialogs!.ConfirmAsync(rows.Length, _lifetime.Token);
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return;
            }
            else if (!await _dialogs.ConfirmAsync("删除本地出库草稿", $"确认移除 {rows.Length} 张未保存草稿及其明细？", cancellationToken: _lifetime.Token)) return;
            if (_disposed) return;
            var deleted = 0;
            foreach (var row in rows)
            {
                if (row.IsNew) Headers.RemoveCompletely(row);
                else
                {
                    var pending = new OutboundPendingWrite
                    {
                        DocumentId = row.Uid,
                        Delete = new() { CommandId = Guid.NewGuid(), ExpectedVersion = row.Version, Reason = reason! }
                    };
                    BeginPending(row, pending);
                    if (!await CompletePendingAsync(row, pending))
                    { if (!_disposed) { SelectedItem = row; ResultSummary = $"已删除 {deleted} 张；{DisplayName(row)}：{ResultSummary}"; } return; }
                }
                deleted++;
            }
            if (_disposed) return;
            if (SelectedItem is not null && !Headers.Items.Contains(SelectedItem)) SelectedItem = Headers.Items.FirstOrDefault();
            ResultSummary = $"已删除 {deleted} 张草稿；持久化单据保留软删除及审计记录。列表总数为上次查询快照。";
            NotifyEditState();
        }, "删除出库草稿");
    }

    /// <summary>刷新按钮显式恢复当前待确认命令；已收到成功结果时只重试详情。</summary>
    private async Task RecoverSelectedAsync()
    {
        if (!CanEdit() || _persistence is null || SelectedItem is not { } row || !_pendingWrites.TryGetValue(row, out var pending)) return;
        await RunWriteAsync(async () =>
        {
            if (pending.Delete is not null && pending.AcceptedResult is null
                && !await _dialogs.ConfirmAsync("重试待确认删除", "将使用原删除原因、版本和命令标识重试，确认是否继续？", cancellationToken: _lifetime.Token)) return;
            if (_disposed) return;
            if (await CompletePendingAsync(row, pending) && !_disposed)
            {
                if (!Headers.Items.Contains(row)) SelectedItem = Headers.Items.FirstOrDefault();
                ResultSummary = "原命令结果已确认；剩余未保存单据请继续点击保存。";
            }
        }, "确认原出库命令");
    }

    /// <summary>冲突后只重新读取当前单，用户拒绝或读取失败均保留原编辑和旧版本。</summary>
    private async Task ReloadConflictedAsync()
    {
        if (!CanEdit() || _persistence is null || SelectedItem is not { } row || !CommitGridEdits()) return;
        await RunBusyAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("重新加载冲突单据", "确认放弃当前单据的本地修改并读取服务器版本？其他单据的修改会保留。", cancellationToken: _lifetime.Token)) return;
            if (_disposed) return;
            var result = await _persistence.GetAsync(row.Uid, _lifetime.Token);
            if (_disposed) return;
            if (!result.Success || !ValidDetail(result.Data, row.Uid))
            { ResultSummary = result.Message + " 原编辑和版本已保留。"; return; }
            row.Load(result.Data.Header);
            row.Lines.LoadFromDtos(result.Data.Lines);
            row.IsDetailLoaded = true;
            row.HasVersionConflict = false;
            ValidationSummary = null;
            NotifyEditState();
            ResultSummary = "已重新加载当前单据；其他单据未保存修改已保留。";
        }, "重载冲突出库单");
    }

    /// <summary>发出请求前登记快照，即使未知异常也不能把已发送意图当作未发送。</summary>
    private void BeginPending(OutboundHeaderModel row, OutboundPendingWrite pending)
    {
        _pendingWrites.Add(row, pending);
        row.IsPersistencePending = true;
        NotifyEditState();
    }

    /// <summary>只按明确成功接纳版本；结果未知后的后续失败不能清除待确认状态。</summary>
    private async Task<bool> CompletePendingAsync(OutboundHeaderModel row, OutboundPendingWrite pending)
    {
        try
        {
            if (pending.AcceptedResult is null)
            {
                // 写入不使用查询取消源，也不因页面查询或选择变化而中断提交。
                var result = pending.Create is not null ? await _persistence!.CreateAsync(pending.Create, CancellationToken.None)
                    : pending.Update is not null ? await _persistence!.UpdateAsync(pending.DocumentId, pending.Update, CancellationToken.None)
                    : pending.Post is not null ? await SendPostAsync(pending)
                    : pending.Reverse is not null ? await SendReverseAsync(pending)
                    : await _persistence!.DeleteAsync(pending.DocumentId, pending.Delete!, CancellationToken.None);
                if (_disposed) return false;
                if (!result.Success)
                {
                    var code = result.Error?.Code;
                    var uncertain = code is null or OutboundErrorCodes.OutcomeUnknown or OutboundErrorCodes.CommandBusy
                        or ServiceErrorCodes.Unexpected or ServiceErrorCodes.CommandConflict;
                    pending.WasUncertain |= uncertain;
                    if (!pending.WasUncertain)
                    {
                        EndPending(row);
                        row.HasVersionConflict = pending.Create is null && code == ServiceErrorCodes.VersionConflict;
                    }
                    ResultSummary = result.Message + (pending.WasUncertain ? " 原命令已保留，请点击刷新重试；不能放弃后重新创建。" : " 编辑已保留。");
                    if (code == ServiceErrorCodes.Validation) ValidationSummary = $"{result.Error?.Field}：{result.Message}";
                    return false;
                }
                var accepted = result.Data;
                if (accepted is null || accepted.Uid == Guid.Empty || accepted.Version <= 0
                    || (pending.Create is null && accepted.Uid != pending.DocumentId) || accepted.IsDeleted != (pending.Delete is not null))
                { pending.WasUncertain = true; ResultSummary = "命令成功响应身份不完整或不匹配，请点击刷新确认原命令。"; return false; }
                pending.AcceptedResult = accepted;
                // 创建所得身份立即保留，但未取得服务器行身份前绝不清除编辑或再次创建。
                row.Uid = accepted.Uid;
                row.Version = accepted.Version;
                using (row.SuppressChangeTracking()) row.DocumentNo = accepted.DocumentNo;
                if (pending.Post is not null)
                    using (row.SuppressChangeTracking()) row.PostingStatus = SwiftExport.Contracts.SysEnums.Inventory.OutboundPostingStatusEnum.Posted;
                if (pending.ReverseResult is { } reversed)
                    using (row.SuppressChangeTracking())
                    {
                        row.PostingStatus = SwiftExport.Contracts.SysEnums.Inventory.OutboundPostingStatusEnum.Reversed;
                        row.ReversedDate = reversed.ReversedDate; row.ReversedBy = reversed.ReversedBy; row.ReverseReason = reversed.ReverseReason;
                    }
            }
            if (pending.Delete is not null)
            {
                EndPending(row);
                Headers.RemoveCompletely(row);
                return true;
            }
            var detailResult = await _persistence!.GetAsync(pending.AcceptedResult.Uid, CancellationToken.None);
            if (_disposed) return false;
            var detail = detailResult.Data;
            if (!detailResult.Success || !ValidDetail(detail, pending.AcceptedResult.Uid)
                || detail!.Header.Version < pending.AcceptedResult.Version
                || (pending.Post is not null && detail.Header.PostingStatus != SwiftExport.Contracts.SysEnums.Inventory.OutboundPostingStatusEnum.Posted)
                || (pending.Reverse is not null && detail.Header.PostingStatus != SwiftExport.Contracts.SysEnums.Inventory.OutboundPostingStatusEnum.Reversed))
            { ResultSummary = $"{(pending.Reverse is not null ? "已反过账" : pending.Post is null ? "已保存" : "已过账")}，但详情刷新失败：{detailResult.Message}。请点击刷新，仅重试详情。"; return false; }
            row.Load(detail.Header);
            row.Lines.LoadFromDtos(detail.Lines);
            row.IsDetailLoaded = true;
            row.HasVersionConflict = false;
            EndPending(row);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("出库命令接纳异常：{0}", ex.GetType().Name);
            pending.WasUncertain = true;
            if (!_disposed) ResultSummary = pending.AcceptedResult is null
                ? "结果未确认，原命令已保留，请点击刷新重试。" : "命令已成功，详情接纳失败，请点击刷新仅重试详情。";
            return false;
        }
        finally { if (!_disposed) NotifyEditState(); }
    }

    private void EndPending(OutboundHeaderModel row)
    { _pendingWrites.Remove(row); row.IsPersistencePending = false; NotifyEditState(); }

    /// <summary>待确认意图未解决前禁止用普通查询、删除或新保存替代该意图。</summary>
    private bool RequirePendingRecovery()
    {
        if (_pendingWrites.Count == 0) return false;
        SelectedItem = _pendingWrites.Keys.First();
        ResultSummary = "存在待确认命令或待回填详情，请选中该单并点击刷新；原请求已保留。";
        return true;
    }

    private bool CommitGridEdits()
    {
        var args = new CancelEventArgs();
        CommitEditsRequested?.Invoke(this, args);
        if (args.Cancel) ValidationSummary = "表格中存在未完成或无效的编辑，请先修正。";
        return !args.Cancel;
    }

    private static string DisplayName(OutboundHeaderModel row) => string.IsNullOrWhiteSpace(row.DocumentNo) ? "新增草稿" : row.DocumentNo;

    private static bool ValidDetail(OutboundManagerDetailDto? detail, Guid uid) => detail?.Header is not null
        && detail.Header.Uid == uid && detail.Lines is not null
        && detail.Lines.All(x => x.Uid != Guid.Empty && x.OutboundDocumentId == uid)
        && detail.Lines.Select(x => x.Uid).Distinct().Count() == detail.Lines.Count;
}
