using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>草稿拼箱操作只改变包装归属，所有交付仍来自真实产品行。</summary>
public sealed partial class PackingListDetailViewModel
{
    private bool _syncingPackageSelection;
    /// <summary>上表当前包装条目；拼箱行不是可删除的真实产品。</summary>
    [ObservableProperty] public partial PackingListPackingGroupModel? SelectedPackage { get; set; }
    /// <summary>上表多选快照，仅用于显式拼箱，不扩大手柄拖动范围。</summary>
    public ObservableCollection<PackingListPackingGroupModel> SelectedPackages { get; } = [];

    /// <summary>上表选择决定下表内容，普通产品不保留隐藏的拼箱目标。</summary>
    partial void OnSelectedPackageChanged(PackingListPackingGroupModel? value)
    {
        if (_syncingPackageSelection) return;
        _syncingPackageSelection = true;
        try
        {
            SelectedPackingGroup = value?.IsMixed == true ? value : null;
            SelectedPackingLine = null;
            SelectedLine = value?.Product;
        }
        finally { _syncingPackageSelection = false; }
        NotifyState();
    }

    /// <summary>视图仅桥接选择集合，命令资格仍由页面统一刷新。</summary>
    public void UpdatePackageSelection(IEnumerable<PackingListPackingGroupModel> selection)
    {
        SelectedPackages.Clear();
        foreach (var item in selection) SelectedPackages.Add(item);
        PackProductsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>包装级拖动按完整真实成员块移动，空箱先处理后再排序。</summary>
    public bool CanReorderPackages => IsEditable && RowReorderRequest.IsViewInSourceOrder(Lines.PackageView);
    /// <summary>箱内排序仅允许当前箱成员，额外筛选或排序时禁用。</summary>
    public bool CanReorderMembers => CanReorder && SelectedPackingGroup is { Members.Count: > 0 } group
        && RowReorderRequest.IsViewInSourceOrder(CollectionViewSource.GetDefaultView(group.Members));

    /// <summary>将上表单个包装条目移动为完整块，不改变成员内部顺序或事实。</summary>
    [RelayCommand(CanExecute = nameof(CanReorderPackage))]
    private void ReorderPackage(RowReorderRequest? request)
    {
        if (!CanReorderPackage(request) || CommitEdits?.Invoke() == false || !CanReorderPackage(request)) return;
        request!.TryResolve(Lines.Packages, out var package, out var index);
        var ordered = Lines.Packages.ToList(); ordered.Remove(package); ordered.Insert(index, package);
        using (Lines.SuppressListEvents())
        {
            var rows = ordered.SelectMany(x => x.Members).ToArray();
            for (var i = 0; i < rows.Length; i++) Lines.Move(rows[i], i);
            Renumber();
        }
        SelectedPackage = package;
        NotifyState();
    }

    /// <summary>只接纳当前上表对象和有成员的落点，拒绝旧快照及跨表载荷。</summary>
    private bool CanReorderPackage(RowReorderRequest? request) => CanReorderPackages
        && request is not null && request.TryResolve(Lines.Packages, out var source, out _)
        && source.Members.Count > 0 && (request.TargetItem is null || request.TargetItem is PackingListPackingGroupModel { Members.Count: > 0 });

    /// <summary>下表落点以本箱成员解析，再映射为全单行号；末尾落点也不能离开当前箱。</summary>
    [RelayCommand(CanExecute = nameof(CanReorderMember))]
    private void ReorderMember(RowReorderRequest? request)
    {
        if (!CanReorderMember(request) || CommitEdits?.Invoke() == false || !CanReorderMember(request)) return;
        var members = SelectedPackingGroup!.Members.ToList();
        request!.TryResolve(members, out var row, out var index);
        var first = Lines.Items.IndexOf(members[0]);
        members.Remove(row); members.Insert(index, row);
        using (Lines.SuppressListEvents())
        {
            for (var i = 0; i < members.Count; i++) Lines.Move(members[i], first + i);
            Renumber();
        }
        SelectedPackingLine = row; SelectedLine = row;
        NotifyState();
    }

    /// <summary>命令本身复核同箱成员身份，不仅依赖拖拽控件的跨表保护。</summary>
    private bool CanReorderMember(RowReorderRequest? request) => CanReorderMembers && request is not null
        && request.TryResolve(SelectedPackingGroup!.Members, out _, out _);

    /// <summary>当前选择必须全部是仍有效的普通产品；目标为空表示新建拼箱。</summary>
    private bool CanPackProducts(PackingListPackingGroupModel? target) => IsEditable && SelectedPackages.Count > 0
        && SelectedPackages.All(x => !x.IsMixed && x.Product is { GrossDemandId: not null } row
            && row.GrossDemandId != Guid.Empty && Lines.Items.Contains(row) && !row.IsMixedPacking)
        && (target is null || Lines.PackingGroups.Contains(target));

    /// <summary>冻结多选产品，确认后原子加入目标箱；不自动拆数量，不向保存集合添加汇总行。</summary>
    [RelayCommand(CanExecute = nameof(CanPackProducts))]
    private async Task PackProductsAsync(PackingListPackingGroupModel? target)
    {
        if (!CanPackProducts(target) || CommitEdits?.Invoke() == false) return;
        var rows = Lines.Items.Where(x => SelectedPackages.Any(p => ReferenceEquals(p.Product, x))).ToArray();
        _dialogOpen = true; NotifyState();
        try
        {
            if (!await _dialogs.ConfirmAsync("拼箱", $"将选中的 {rows.Length} 行产品整行加入{(target is null ? "新拼箱" : target.Title)}。数量和来源不变，整箱重量、体积需重新填写；需要拆尾箱时请先取消。") || _disposed) return;
            if (rows.Any(x => !Lines.Items.Contains(x) || x.IsMixedPacking) || (target is not null && !Lines.PackingGroups.Contains(target))) return;
            var group = target ?? new PackingListPackingGroupModel(Guid.NewGuid(), true);
            using (Lines.SuppressListEvents())
            {
                if (target is null) Lines.PackingGroups.Add(group);
                foreach (var row in rows)
                {
                    row.PackingGroupId = group.Id; row.PcsPerPackage = row.Qty; row.Marks = group.Marks;
                    row.NwPerPackage = null; row.GwPerPackage = null; row.CbmPerPackage = null;
                    PlaceInGroup(row, group.Id);
                }
                Lines.ClearMetrics(group.Id); Renumber();
            }
            SelectedPackage = group; SelectedPackingLine = rows[0];
        }
        finally { _dialogOpen = false; NotifyState(); }
    }

    /// <summary>解散只取消包装归属，所有真实产品留在原箱位置，未知单箱指标不能沿用原整箱值。</summary>
    [RelayCommand(CanExecute = nameof(CanDissolvePackage))]
    private async Task DissolvePackageAsync()
    {
        if (!CanDissolvePackage() || CommitEdits?.Invoke() == false) return;
        var group = SelectedPackage!; var rows = group.Members.ToArray();
        var previous = Math.Max(0, Lines.Packages.IndexOf(group) - 1);
        _dialogOpen = true; NotifyState();
        try
        {
            if (!await _dialogs.ConfirmAsync(rows.Length == 0 ? "删除空拼箱" : "解散拼箱",
                rows.Length == 0 ? "删除当前空拼箱？" : "解散后产品保留为普通行，数量和来源不变，单箱重量、体积需重新填写。") || _disposed) return;
            if (!Lines.PackingGroups.Contains(group)) return;
            using (Lines.SuppressListEvents())
            {
                foreach (var row in rows)
                {
                    row.PackingGroupId = null; row.PcsPerPackage = row.Qty;
                    row.NwPerPackage = null; row.GwPerPackage = null; row.CbmPerPackage = null;
                }
                Lines.PackingGroups.Remove(group); Renumber();
            }
            SelectedPackage = rows.FirstOrDefault()?.DisplayGroup ?? Lines.Packages.ElementAtOrDefault(previous);
        }
        finally { _dialogOpen = false; NotifyState(); }
    }

    /// <summary>拼箱汇总行只能解散或删除空箱，不能通过产品删除命令删掉全部成员。</summary>
    private bool CanDissolvePackage() => IsEditable && SelectedPackage is { IsMixed: true } group && Lines.PackingGroups.Contains(group);

    /// <summary>上表删除只作用于一个普通产品，选择在删除后回到上一包装条目。</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePackageProduct))]
    private void DeletePackageProduct()
    {
        if (!CanDeletePackageProduct() || CommitEdits?.Invoke() == false) return;
        var previous = Math.Max(0, Lines.Packages.IndexOf(SelectedPackage!) - 1);
        SelectedLine = SelectedPackage!.Product;
        DeleteLine();
        SelectedPackage = Lines.Packages.ElementAtOrDefault(previous);
    }
    /// <summary>删除普通产品的真实命令门禁，不允许把箱汇总当产品。</summary>
    private bool CanDeletePackageProduct() => IsEditable && SelectedPackage?.Product is { } row && Lines.Items.Contains(row);

    /// <summary>下表删除只针对当前箱中选中的产品，删除后回选上一成员，空箱继续显式展示。</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePackingMember))]
    private void DeletePackingMember()
    {
        if (!CanDeletePackingMember() || CommitEdits?.Invoke() == false) return;
        var group = SelectedPackingGroup!;
        var previous = Math.Max(0, group.Members.ToList().IndexOf(SelectedPackingLine!) - 1);
        SelectedLine = SelectedPackingLine;
        DeleteLine();
        SelectedPackage = group;
        SelectedPackingLine = group.Members.ElementAtOrDefault(previous);
    }
    /// <summary>下表菜单不能删除其他箱或上表残留的选择。</summary>
    private bool CanDeletePackingMember() => IsEditable && SelectedPackingLine is { } row
        && SelectedPackingGroup?.Members.Contains(row) == true && Lines.Items.Contains(row);

    /// <summary>下表当前显示的拼箱；获取来源始终新增普通行。</summary>
    [ObservableProperty] public partial PackingListPackingGroupModel? SelectedPackingGroup { get; set; }

    /// <summary>下表的成员选择独立维护，清空或切换下表不能清掉上表选中行。</summary>
    [ObservableProperty] public partial PackingListLineModel? SelectedPackingLine { get; set; }

    /// <summary>只允许在可编辑草稿的当前拼箱上录入共享指标。</summary>
    public bool CanEditSelectedPackingGroup => IsEditable && SelectedPackingGroup is not null;

    /// <summary>下表选择定位到同一个真实产品行，不复制成员或改变包装归属。</summary>
    partial void OnSelectedPackingLineChanged(PackingListLineModel? value)
    {
        if (value is not null && SelectedPackingGroup?.Members.Contains(value) == true)
            SelectedLine = value;
        DeletePackingMemberCommand.NotifyCanExecuteChanged();
    }

    /// <summary>切箱时清理不属于该箱的下表选择，保留上表待加入的产品行。</summary>
    partial void OnSelectedPackingGroupChanged(PackingListPackingGroupModel? value)
    {
        SelectedPackingLine = SelectedLine is not null && value?.Members.Contains(SelectedLine) == true ? SelectedLine : null;
        NotifyState();
    }

    /// <summary>建立只存在客户端的空组，保存前必须加入成员或删除。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddPackingGroup()
    {
        if (!IsEditable || CommitEdits?.Invoke() == false) return;
        var group = new PackingListPackingGroupModel(Guid.NewGuid(), true);
        Lines.PackingGroups.Add(group);
        SelectedPackingGroup = group;
        NotifyState();
    }

    /// <summary>清除目标选择，后续选源新增普通行。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void SelectOrdinaryPacking() { if (IsEditable && CommitEdits?.Invoke() != false) SelectedPackingGroup = null; }

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
    [RelayCommand(CanExecute = nameof(CanMoveOutOfPackingGroup))]
    private Task MoveOutOfPackingGroupAsync() => CanMoveOutOfPackingGroup() ? ChangePackingAsync(true) : Task.CompletedTask;

    /// <summary>只能移出当前拼箱中选中的成员，切箱后不能误操作上表残留的旧成员选择。</summary>
    private bool CanMoveOutOfPackingGroup() => IsEditable && SelectedLine?.PackingGroupId is { } id
        && SelectedPackingGroup?.Id == id;

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
