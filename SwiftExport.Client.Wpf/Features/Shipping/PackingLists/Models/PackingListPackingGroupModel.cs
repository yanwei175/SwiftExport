using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>客户端包装组投影；普通组只是视图容器，拼箱共享值同步到真实行，不持久化此对象。</summary>
public sealed partial class PackingListPackingGroupModel : ObservableObject
{
    private bool _loading;
    /// <summary>稳定视图身份，拼箱时等于成员的 PackingGroupId。</summary>
    public Guid Id { get; }
    /// <summary>是否为一只拼箱，普通行容器不展示组头。</summary>
    public bool IsMixed { get; }
    /// <summary>仅在客户端维护的真实成员引用。</summary>
    public IReadOnlyList<PackingListLineModel> Members { get; private set; } = [];
    /// <summary>可理解的组显示名。</summary>
    public string Title => IsMixed ? $"拼箱 {Id.ToString("N")[..6]} · 1 箱" : "普通包装";
    /// <summary>客户端展开状态，不持久化。</summary>
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    /// <summary>整箱净重，未知为空。</summary>
    [ObservableProperty] public partial decimal? NetWeight { get; set; }
    /// <summary>整箱毛重，未知为空。</summary>
    [ObservableProperty] public partial decimal? GrossWeight { get; set; }
    /// <summary>整箱体积，未知为空。</summary>
    [ObservableProperty] public partial decimal? Volume { get; set; }
    /// <summary>共享箱唛。</summary>
    [ObservableProperty] public partial string? Marks { get; set; }

    /// <summary>创建纯 UI 容器，不创建产品行。</summary>
    public PackingListPackingGroupModel(Guid id, bool isMixed) { Id = id; IsMixed = isMixed; }

    /// <summary>读取已有成员快照，不通过属性通知改写持久化行。</summary>
    public void BindMembers(IReadOnlyList<PackingListLineModel> members)
    {
        Members = members;
        if (members.Count == 0) return;
        _loading = true;
        try
        {
            NetWeight = members[0].NwPerPackage; GrossWeight = members[0].GwPerPackage;
            Volume = members[0].CbmPerPackage; Marks = members[0].Marks;
        }
        finally { _loading = false; }
    }

    partial void OnNetWeightChanged(decimal? value) { if (!_loading) foreach (var row in Members) row.NwPerPackage = value; }
    partial void OnGrossWeightChanged(decimal? value) { if (!_loading) foreach (var row in Members) row.GwPerPackage = value; }
    partial void OnVolumeChanged(decimal? value) { if (!_loading) foreach (var row in Members) row.CbmPerPackage = value; }
    partial void OnMarksChanged(string? value) { if (!_loading) foreach (var row in Members) row.Marks = value; }
}
