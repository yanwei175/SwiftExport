using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Services;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.SysEnums.Inventory;
using System.Windows.Data;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>整单保存、软删除和原命令恢复；待确认状态仅属于当前编辑会话。</summary>
public sealed partial class InboundManagerViewModel
{
    private readonly IInboundPersistenceService? _persistence;
    private readonly IInboundDeleteDialogService? _deleteDialogs;
    private readonly Dictionary<InboundHeaderModel, InboundPendingWrite> _pendingWrites = [];

    /// <summary>请求 View 提交原生 Grid 编辑；转换或验证失败可设置 Cancel 阻止发送。</summary>
    public event CancelEventHandler? CommitEditsRequested;

    private bool CanSave() => CanEdit() && _persistence is not null && _pendingWrites.Count == 0
        && Headers.Items.Any(x => x.IsDirty || x.Lines.HasChanges)
        && Headers.Items.Where(x => x.IsDirty || x.Lines.HasChanges).All(HasDraftPermission);

    /// <summary>删除本地新单需要创建权限，删除持久化草稿需要删除权限，整组选中先验后执行。</summary>
    private bool CanDeleteSelection() => CanEdit() && _pendingWrites.Count == 0 && DeletionRows() is { Length: > 0 } rows
        && rows.All(row => row.IsNew ? HasDraftPermission(row) : _postingUser?.HasPermission(PermissionKeys.InventoryInboundDelete) == true);
    private InboundHeaderModel[] DeletionRows() => (Headers.CheckedItems.Count > 0 ? Headers.CheckedItems
        : SelectedItem is null ? [] : new[] { SelectedItem }).ToArray();

    /// <summary>已知成功只需查看权限读取当前详情；未知写入重试仍要求原操作细权限及原账号。</summary>
    private bool HasRecoveryPermission(InboundPendingWrite pending) => !_disposed && HasViewPermission && pending.UserKey == CurrentUserKey
        && (pending.AcceptedResult is not null || _postingUser?.HasPermission(pending.Create is not null ? PermissionKeys.InventoryInboundCreate
            : pending.Update is not null ? PermissionKeys.InventoryInboundEdit : pending.Delete is not null ? PermissionKeys.InventoryInboundDelete
            : pending.Post is not null ? PermissionKeys.InventoryInboundPost : PermissionKeys.InventoryInboundReverse) == true);
    private bool CanRecover(InboundPendingWrite pending) => IsNotBusy && _persistence is not null && HasRecoveryPermission(pending);

    /// <summary>发送前完整验证所有修改单据；逐单提交，首个失败后保留后续编辑。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !CommitGridEdits()) return;
        if (RequirePendingRecovery()) return;
        var rows = Headers.Items.Where(x => x.IsDirty || x.Lines.HasChanges).ToArray();
        var session = _userSession;
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
                if (_disposed || session != _userSession || !HasViewPermission || !HasDraftPermission(row) || IsReadOnly) return;
                var draft = row.Adapt<InboundDraftData>();
                var pending = row.IsNew
                    ? new InboundPendingWrite { DocumentId = row.Uid, Create = new() { CommandId = Guid.NewGuid(), Draft = draft } }
                    : new InboundPendingWrite { DocumentId = row.Uid, Update = new() { CommandId = Guid.NewGuid(), ExpectedVersion = row.Version, Draft = draft } };
                BeginPending(row, pending);
                if (!await CompletePendingAsync(row, pending))
                { if (IsCurrentWriteContext(pending, session)) { SelectedItem = row; ResultSummary = $"已完成 {saved} 张；{DisplayName(row)}：{ResultSummary}"; } return; }
                saved++;
                if (pending.HasLaterDetail) { ResultSummary = LaterDetailMessage(row); return; }
            }
            if (!_disposed) ResultSummary = $"已保存 {saved} 张入库草稿，库存未变化。列表总数为上次查询快照。";
        }, "逐单保存入库草稿");
    }

    /// <summary>本地新增直接移除；已有草稿须填写共同原因，服务端明确成功才移除页面记录。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDeleteSelection() || !CommitGridEdits() || RequirePendingRecovery()) return;
        var selected = DeletionRows().ToHashSet();
        var rows = CollectionViewSource.GetDefaultView(Headers.Items).Cast<InboundHeaderModel>().Where(selected.Contains).ToArray();
        var session = _userSession;
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
            else if (!await _dialogs.ConfirmAsync("删除本地入库草稿", $"确认移除 {rows.Length} 张未保存草稿及其明细？", cancellationToken: _lifetime.Token)) return;
            if (_disposed || session != _userSession || !CanDeletePermissions(rows)) return;
            var deleted = 0;
            foreach (var row in rows)
            {
                if (_disposed || session != _userSession || !CanDeletePermissions(rows)) return;
                if (row.IsNew) RemoveHeaderAndSelectPrevious(row);
                else
                {
                    var pending = new InboundPendingWrite
                    {
                        DocumentId = row.Uid,
                        Delete = new() { CommandId = Guid.NewGuid(), ExpectedVersion = row.Version, Reason = reason! }
                    };
                    BeginPending(row, pending);
                    if (!await CompletePendingAsync(row, pending))
                    { if (IsCurrentWriteContext(pending, session)) { SelectedItem = row; ResultSummary = $"已删除 {deleted} 张；{DisplayName(row)}：{ResultSummary}"; } return; }
                }
                deleted++;
            }
            if (_disposed) return;
            ResultSummary = $"已删除 {deleted} 张草稿；持久化单据保留软删除及审计记录。列表总数为上次查询快照。";
            NotifyEditState();
        }, "删除入库草稿");
        // 写入期间选择事件不发起读取；退出 Busy 后补载删除后定位的摘要行。
        await LoadSelectedDetailAsync();
    }

    /// <summary>刷新按钮显式恢复当前待确认命令；已收到成功结果时只重试详情。</summary>
    private async Task RecoverSelectedAsync()
    {
        if (SelectedItem is not { } row || !_pendingWrites.TryGetValue(row, out var pending) || !CanRecover(pending)) return;
        await RunBusyAsync(async () =>
        {
            if (pending.Delete is not null && pending.AcceptedResult is null
                && !await _dialogs.ConfirmAsync("重试待确认删除", "将使用原删除原因、版本和命令标识重试，确认是否继续？", cancellationToken: _lifetime.Token)) return;
            if (!HasRecoveryPermission(pending)) return;
            if (await CompletePendingAsync(row, pending) && !_disposed)
            {
                ResultSummary = pending.HasLaterDetail ? LaterDetailMessage(row) : "原命令结果已确认；剩余未保存单据请继续点击保存。";
            }
        }, "确认原入库命令");
        await LoadSelectedDetailAsync();
    }

    /// <summary>冲突后只重新读取当前单，用户拒绝或读取失败均保留原编辑和旧版本。</summary>
    private async Task ReloadConflictedAsync()
    {
        if (!CanEdit() || _persistence is null || SelectedItem is not { } row || !CommitGridEdits()) return;
        var session = _userSession;
        await RunBusyAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("重新加载冲突单据", "确认放弃当前单据的本地修改并读取服务器版本？其他单据的修改会保留。", cancellationToken: _lifetime.Token)) return;
            if (_disposed || session != _userSession || !HasViewPermission) return;
            var revision = _editRevision;
            var result = await _persistence.GetAsync(row.Uid, _lifetime.Token);
            if (_disposed || session != _userSession || !HasViewPermission || revision != _editRevision) return;
            if (!result.Success || !ValidDetail(result.Data, row.Uid))
            { ResultSummary = result.Message + " 原编辑和版本已保留。"; return; }
            row.Load(result.Data.Header);
            row.Lines.LoadFromDtos(result.Data.Lines);
            row.IsDetailLoaded = true;
            row.HasVersionConflict = false;
            ValidationSummary = null;
            NotifyEditState();
            ResultSummary = "已重新加载当前单据；其他单据未保存修改已保留。";
        }, "重载冲突入库单");
    }

    /// <summary>发出请求前登记快照，即使未知异常也不能把已发送意图当作未发送。</summary>
    private void BeginPending(InboundHeaderModel row, InboundPendingWrite pending)
    {
        pending.UserKey = CurrentUserKey;
        _pendingWrites.Add(row, pending);
        row.IsPersistencePending = true;
        NotifyEditState();
    }

    /// <summary>只按明确成功接纳版本；结果未知后的后续失败不能清除待确认状态。</summary>
    private async Task<bool> CompletePendingAsync(InboundHeaderModel row, InboundPendingWrite pending)
    {
        var session = _userSession;
        if (!HasRecoveryPermission(pending)) return false;
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
                if (!result.Success)
                {
                    var code = result.Error?.Code;
                    var uncertain = code is null or ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy
                        or ServiceErrorCodes.Unexpected or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.CommandConflict;
                    pending.WasUncertain |= uncertain;
                    if (_disposed || session != _userSession || pending.UserKey != CurrentUserKey) return false;
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
                var expectedVersion = pending.Update?.ExpectedVersion ?? pending.Delete?.ExpectedVersion ?? pending.Post?.ExpectedVersion ?? pending.Reverse?.ExpectedVersion;
                if (accepted is null || accepted.Uid == Guid.Empty || accepted.Version <= 0
                    || (pending.Create is null && (accepted.Uid != pending.DocumentId || accepted.Version != expectedVersion + 1)) || accepted.IsDeleted != (pending.Delete is not null))
                { pending.WasUncertain = true; if (IsCurrentWriteContext(pending, session)) ResultSummary = "命令成功响应身份不完整或不匹配，请点击刷新确认原命令。"; return false; }
                pending.AcceptedResult = accepted;
                if (_disposed || session != _userSession || pending.UserKey != CurrentUserKey) return false;
                // 创建所得身份立即保留，但未取得服务器行身份前绝不清除编辑或再次创建。
                row.Uid = accepted.Uid;
                row.Version = accepted.Version;
                using (row.SuppressChangeTracking()) row.DocumentNo = accepted.DocumentNo;
                if (pending.Post is not null)
                    using (row.SuppressChangeTracking()) row.PostingStatus = SwiftExport.Contracts.SysEnums.Inventory.InboundPostingStatusEnum.Posted;
            }
            if (_disposed || session != _userSession || pending.UserKey != CurrentUserKey || !HasViewPermission) return false;
            if (pending.ReverseResult is { } reversed)
                using (row.SuppressChangeTracking())
                {
                    row.PostingStatus = SwiftExport.Contracts.SysEnums.Inventory.InboundPostingStatusEnum.Reversed;
                    row.ReversedDate = reversed.ReversedDate; row.ReversedBy = reversed.ReversedBy; row.ReverseReason = reversed.ReverseReason;
                }
            if (pending.Delete is not null)
            {
                EndPending(row);
                RemoveHeaderAndSelectPrevious(row);
                return true;
            }
            var detailResult = await _persistence!.GetAsync(pending.AcceptedResult.Uid, CancellationToken.None);
            if (_disposed || session != _userSession || pending.UserKey != CurrentUserKey || !HasViewPermission) return false;
            var detail = detailResult.Data;
            if (!detailResult.Success || !ValidDetail(detail, pending.AcceptedResult.Uid)
                || detail!.Header.Version < pending.AcceptedResult.Version
                || (pending.Post is not null && detail.Header.PostingStatus is not (InboundPostingStatusEnum.Posted or InboundPostingStatusEnum.Reversed))
                || (pending.Reverse is not null && detail.Header.PostingStatus != InboundPostingStatusEnum.Reversed)
                || (detail.Header.Version == pending.AcceptedResult.Version
                    && (pending.Post is not null && detail.Header.PostingStatus != InboundPostingStatusEnum.Posted
                        || pending.Reverse is not null && detail.Header.PostingStatus != InboundPostingStatusEnum.Reversed)))
            { ResultSummary = $"{(pending.Reverse is not null ? "已反过账" : pending.Post is null ? "已保存" : "已过账")}，原命令已成功，但当前详情不可读或版本未核实：{detailResult.Message}。请点击刷新，仅重试详情，不再发送写命令。"; return false; }
            pending.HasLaterDetail = detail.Header.Version > pending.AcceptedResult.Version;
            row.Load(detail.Header);
            row.Lines.LoadFromDtos(detail.Lines);
            row.IsDetailLoaded = true;
            row.HasVersionConflict = false;
            EndPending(row);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("入库命令接纳异常：{0}", ex.GetType().Name);
            pending.WasUncertain = true;
            if (IsCurrentWriteContext(pending, session)) ResultSummary = pending.AcceptedResult is null
                ? "结果未确认，原命令已保留，请点击刷新重试。" : $"{(pending.Reverse is not null ? "已反过账" : pending.Post is null ? "已保存" : "已过账")}，详情接纳失败，请点击刷新重试详情。";
            return false;
        }
        finally { if (!_disposed) NotifyEditState(); }
    }

    private void EndPending(InboundHeaderModel row)
    { _pendingWrites.Remove(row); row.IsPersistencePending = false; NotifyEditState(); }

    /// <summary>仅在真实删除成功后定位前一幸存行；首行回退到后继，空集合不访问负索引。</summary>
    private void RemoveHeaderAndSelectPrevious(InboundHeaderModel row)
    {
        var displayed = CollectionViewSource.GetDefaultView(Headers.Items).Cast<InboundHeaderModel>().ToArray();
        var index = Array.IndexOf(displayed, row);
        if (index < 0) return;
        Headers.RemoveCompletely(row);
        SelectedItem = displayed.Take(index).LastOrDefault(Headers.Items.Contains)
            ?? displayed.Skip(index + 1).FirstOrDefault(Headers.Items.Contains);
    }

    /// <summary>晚到写入仍保留回执事实，但反馈和选择仅属于原账号当前会话。</summary>
    private bool IsCurrentWriteContext(InboundPendingWrite pending, int session) => !_disposed && session == _userSession && pending.UserKey == CurrentUserKey;

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

    private static string DisplayName(InboundHeaderModel row) => string.IsNullOrWhiteSpace(row.DocumentNo) ? "新增草稿" : row.DocumentNo;

    /// <summary>确认等待期间或逐单删除前重新核对整组权限，不在已进入 Busy 后复用按钮准入。</summary>
    private bool CanDeletePermissions(IEnumerable<InboundHeaderModel> rows) => !IsReadOnly && HasViewPermission
        && rows.All(row => row.IsNew ? HasDraftPermission(row) : _postingUser?.HasPermission(PermissionKeys.InventoryInboundDelete) == true);

    /// <summary>原命令成功与后续状态变化分别说明，避免把新详情误称为原过账状态。</summary>
    private static string LaterDetailMessage(InboundHeaderModel row) => $"原命令已成功；单据随后已变化，现已接纳版本 {row.Version}，当前状态 {row.PostingStatus}。";

    private static bool ValidDetail(InboundManagerDetailDto? detail, Guid uid) => detail?.Header is not null
        && detail.Header.Uid == uid && detail.Header.Version > 0 && Enum.IsDefined(detail.Header.PostingStatus)
        && Enum.IsDefined(detail.Header.LifecycleStatus) && !detail.Header.IsDeleted && detail.Lines is not null
        && detail.Lines.All(x => x.Uid != Guid.Empty && x.InboundDocumentId == uid)
        && detail.Lines.Select(x => x.Uid).Distinct().Count() == detail.Lines.Count;
}
