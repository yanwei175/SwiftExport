using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>草稿拼箱操作只改变包装归属，所有交付仍来自真实产品行。</summary>
public sealed partial class PackingListDetailViewModel
{
    /// <summary>添加来源和移入操作的目标组；为空时新增普通行。</summary>
    [ObservableProperty] public partial PackingListPackingGroupModel? SelectedPackingGroup { get; set; }

    /// <summary>建立只存在客户端的空组，保存前必须加入成员或删除。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddPackingGroup()
    {
        if (!IsEditable || CommitEdits?.Invoke() == false) return;
        SelectedPackingGroup = new PackingListPackingGroupModel(Guid.NewGuid(), true);
        Lines.PackingGroups.Add(SelectedPackingGroup);
        NotifyState();
    }

    /// <summary>清除目标选择，后续选源新增普通行。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void SelectOrdinaryPacking() { if (IsEditable) SelectedPackingGroup = null; }

    /// <summary>非空组不能隐式删除其成员。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void DeletePackingGroup()
    {
        if (!IsEditable || SelectedPackingGroup is null) return;
        if (SelectedPackingGroup.Members.Count > 0)
        { ReportValidationWarning("请先移出或删除拼箱成员，再删除空组。"); return; }
        Lines.PackingGroups.Remove(SelectedPackingGroup); SelectedPackingGroup = null; NotifyState();
    }

    /// <summary>整行移入目标组，保持来源、身份和数量；包装指标需重新填写。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private Task MoveIntoPackingGroupAsync() => ChangePackingAsync(false);

    /// <summary>整行移出成为普通单箱，不合并其他行。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private Task MoveOutOfPackingGroupAsync() => ChangePackingAsync(true);

    /// <summary>明确确认后在本地原子调整组归属，取消不修改现有产品行。</summary>
    private async Task ChangePackingAsync(bool remove)
    {
        if (!IsEditable || SelectedLine is not { } row || !row.GrossDemandId.HasValue || CommitEdits?.Invoke() == false) return;
        var target = remove ? null : SelectedPackingGroup;
        if ((!remove && target is null) || row.PackingGroupId == target?.Id) return;
        _dialogOpen = true; NotifyState();
        try
        {
            if (!await _dialogs.ConfirmAsync("调整拼箱", "将清空该行及受影响拼箱的净重、毛重、体积，请重新核对填写。数量和来源不变。") || _disposed) return;
            using (Lines.SuppressListEvents())
            {
                var oldGroup = row.PackingGroupId;
                if (oldGroup.HasValue) Lines.ClearMetrics(oldGroup.Value);
                row.PackingGroupId = target?.Id;
                row.PcsPerPackage = row.Qty;
                row.NwPerPackage = null; row.GwPerPackage = null; row.CbmPerPackage = null;
                if (target != null)
                {
                    row.Marks = target.Marks;
                    Lines.ClearMetrics(target.Id);
                    PlaceInGroup(row, target.Id);
                }
                else
                {
                    // 移出的普通行放到原组之后，不能打断原组成员连续性。
                    var last = Lines.Items.LastOrDefault(x => x.PackingGroupId == oldGroup);
                    if (last != null)
                    {
                        var index = Lines.Items.IndexOf(last);
                        if (Lines.Items.IndexOf(row) > index) index++;
                        Lines.Move(row, index);
                    }
                }
                Renumber();
            }
        }
        finally { _dialogOpen = false; NotifyState(); }
    }

    /// <summary>把新成员放到既有成员之后，保持组内行号连续。</summary>
    private void PlaceInGroup(PackingListLineModel row, Guid id)
    {
        var last = Lines.Items.LastOrDefault(x => x != row && x.PackingGroupId == id);
        if (last == null) return;
        var index = Lines.Items.IndexOf(last);
        if (Lines.Items.IndexOf(row) > index) index++;
        Lines.Move(row, index);
    }

    /// <summary>普通行与组按块移动；组内上下移动不能跨组。</summary>
    private bool MovePackingBlock(PackingListLineModel row, int offset)
    {
        if (row.PackingGroupId.HasValue)
        {
            var index = Lines.Items.IndexOf(row) + offset;
            if (index >= 0 && index < Lines.Items.Count && Lines.Items[index].PackingGroupId == row.PackingGroupId)
                Lines.Move(row, index);
            return true;
        }
        var blocks = Lines.Items.GroupBy(x => (x.PackingGroupId.HasValue, Id: x.PackingGroupId ?? x.Uid)).Select(x => x.ToList()).ToList();
        return MoveBlocks(blocks, blocks.FindIndex(x => x.Contains(row)), offset);
    }

    /// <summary>移动选中拼箱的完整成员块，不打散产品行。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void MovePackingGroupUp() => MoveSelectedGroup(-1);
    /// <summary>移动选中拼箱的完整成员块，不改变数量。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void MovePackingGroupDown() => MoveSelectedGroup(1);

    /// <summary>定位当前选中组并应用完整块移动。</summary>
    private void MoveSelectedGroup(int offset)
    {
        if (!IsEditable || SelectedPackingGroup is null || CommitEdits?.Invoke() == false) return;
        var blocks = Lines.Items.GroupBy(x => (x.PackingGroupId.HasValue, Id: x.PackingGroupId ?? x.Uid)).Select(x => x.ToList()).ToList();
        MoveBlocks(blocks, blocks.FindIndex(x => x[0].PackingGroupId == SelectedPackingGroup.Id), offset);
    }

    /// <summary>在基类批量通知作用域内重排实际行，随后统一生成连续行号。</summary>
    private bool MoveBlocks(List<List<PackingListLineModel>> blocks, int index, int offset)
    {
        if (index < 0 || index + offset < 0 || index + offset >= blocks.Count) return true;
        (blocks[index], blocks[index + offset]) = (blocks[index + offset], blocks[index]);
        using (Lines.SuppressListEvents())
        {
            var ordered = blocks.SelectMany(x => x).ToArray();
            for (var i = 0; i < ordered.Length; i++) Lines.Move(ordered[i], i);
            Renumber();
        }
        return true;
    }
}
