using SwiftExport.Contracts.DTOs.Errors;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
public sealed partial class PackingListDetailViewModel
{
    private AssociatePackingListSourcesCommand? _pendingAssociation;
    private Guid _associationLineUid;
    private long _associationSessionEpoch;
    private bool _associationNeedsReload;
    private IReadOnlyList<PackingListSourcePickerSelection>? _lastAllocation;
    /// <summary>来源数量概览；不把草稿分配当作正式履约。</summary>
    public string AssociationSummary => $"待关联 {Lines.Items.Count(x => !x.GrossDemandId.HasValue)} 行 / {Lines.Items.Where(x => !x.GrossDemandId.HasValue).Sum(x => x.Qty):N4}；已关联 {Lines.Items.Where(x => x.GrossDemandId.HasValue).Sum(x => x.Qty):N4}。";
    private bool CanAssociateSources() => !_disposed && !IsBusy && !_dialogOpen && !HasPendingPosting && _postingUser?.HasPermission("Shipping.PackingList.Edit") == true
        && (_pendingAssociation != null || (IsEditable && !_isNew && !HasUnsavedChanges && SelectedLine is { GrossDemandId: null, PackingGroupId: null }));
    /// <summary>未保存编辑先由用户保存；不确定写入只重试原命令，关闭与普通编辑冻结。</summary>
    [RelayCommand(CanExecute = nameof(CanAssociateSources))]
    private async Task AssociateSourcesAsync()
    {
        if (!CanAssociateSources()) return;
        var sessionEpoch = _associationSessionEpoch;
        if (_pendingAssociation == null)
        {
            if (CommitEdits?.Invoke() == false || HasUnsavedChanges || SelectedLine is not { } row) { ReportValidationWarning("请先保存当前修改。 "); return; }
            if (_associationLineUid != row.Uid) _lastAllocation = null;
            _associationLineUid = row.Uid; _dialogOpen = true; NotifyState();
            try
            {
                var quantities = Lines.Items.Where(x => x.GrossDemandId.HasValue).GroupBy(x => x.GrossDemandId!.Value).ToDictionary(x => x.Key, x => x.Sum(y => y.Qty));
                var selected = await _sources.AllocateAsync(Header.CustomerId, quantities, row.ProductId, row.IsNonStandardSnapshot, row.InvoiceNo, row.Qty, _lastAllocation, _lifetime.Token);
                if (_disposed || sessionEpoch != _associationSessionEpoch || selected == null || _postingUser?.HasPermission("Shipping.PackingList.Edit") != true) return;
                _lastAllocation = selected;
                _pendingAssociation = new() { CommandId = Guid.NewGuid(), ExpectedVersion = Header.Version, ExpectedLineVersion = row.Version,
                    Allocations = selected.Select(x => new PackingListSourceAllocationInput { GrossDemandId = x.Source.GrossDemandId, ExpectedDemandVersion = x.Source.DemandVersion, ExpectedSourceVersion = x.Source.SourceVersion, Qty = x.Qty }).ToList() };
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception ex) { if (!_disposed) await HandleExceptionAsync(ex); return; }
            finally { _dialogOpen = false; NotifyState(); }
        }
        if (_pendingAssociation == null || _disposed || sessionEpoch != _associationSessionEpoch) return;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.AssociateSourcesAsync(_uid, _associationLineUid, _pendingAssociation, CancellationToken.None), "来源关联失败");
            if (_disposed || sessionEpoch != _associationSessionEpoch) return;
            if (!result.Success || result.Data?.Uid != _uid || !IsValidDetail(result.Data.Detail))
            {
                if (!result.Success && result.Error?.Code is not (null or ServiceErrorCodes.Unexpected or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.CommandConflict)) _pendingAssociation = null;
                ReportValidationWarning(_pendingAssociation == null ? result.Message : result.Message + " 结果未确认，请点击关联来源重试原命令。 "); return;
            }
            _pendingAssociation = null; _lastAllocation = null; ApplyDetail(result.Data.Detail);
            ResultSummary = "来源已关联；数量保持不变，拆分后的包装资料请重新核对。";
        }, "关联来源…");
        NotifyState();
    }
}
