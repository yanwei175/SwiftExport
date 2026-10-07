using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.QualityControl.Categories.Models;

/// <summary>分类树的编辑集合；复用通用行状态和删除池，集中处理层级、排序与提交投影。</summary>
public sealed class AfterSalesQualityCategoryTree
{
    /// <summary>唯一的编辑事实集合；删除节点保留在通用删除池中。</summary>
    public BaseListModel<AfterSalesQualityCategoryNode, AfterSalesQualityCategoryDto> Lines { get; } = new(_ => new());
    /// <summary>按层级展示同一批编辑节点。</summary>
    public ObservableCollection<AfterSalesQualityCategoryNode> Roots { get; } = [];

    /// <summary>替换服务端快照；非法树拒绝接纳，避免循环遍历或丢失节点。</summary>
    public void Load(IEnumerable<AfterSalesQualityCategoryDto> items)
    {
        var dtos = items.ToArray();
        var parents = dtos.ToDictionary(x => x.Uid, x => x.ParentId == Guid.Empty ? null : x.ParentId);
        foreach (var id in parents.Keys)
        {
            var visited = new HashSet<Guid> { id };
            var parent = parents[id];
            while (parent.HasValue)
            {
                if (!visited.Add(parent.Value) || !parents.TryGetValue(parent.Value, out parent))
                    throw new InvalidOperationException("分类层级无效，请联系管理员检查上级关系。");
            }
        }
        Lines.LoadFromDtos(dtos);
        Rebuild();
    }

    /// <summary>新增启用节点，父节点可为任意有效层级。</summary>
    public AfterSalesQualityCategoryNode Add(Guid? parentId)
    {
        var siblings = Siblings(parentId);
        var maximum = siblings.Count == 0 ? 0 : siblings.Max(x => x.SortNo);
        var node = Lines.AddNew(new AfterSalesQualityCategoryDto
        {
            Uid = Guid.NewGuid(), ParentId = parentId, CategoryName = "新建分类", IsEnabled = true,
            SortNo = maximum <= int.MaxValue - 10 ? maximum + 10 : int.MaxValue
        });
        siblings.Add(node);
        return node;
    }

    /// <summary>删除整棵子树，未保存节点直接移除，已保存节点进入删除池。</summary>
    public void Remove(AfterSalesQualityCategoryNode node)
    {
        if (!Lines.Items.Contains(node)) return;
        using var scope = Lines.SuppressListEvents();
        foreach (var item in Flatten([node]).ToArray()) Lines.Remove(item);
        Siblings(node.ParentId).Remove(node);
    }

    /// <summary>验证移动目标，禁止自身、后代和不属于当前树的节点。</summary>
    public bool CanMove(AfterSalesQualityCategoryNode node, Guid? parentId) => Lines.Items.Contains(node)
        && (parentId is null || Lines.Items.Any(x => x.Uid == parentId))
        && !Flatten([node]).Any(x => x.Uid == parentId);

    /// <summary>移动节点到新父级指定位置；index 指移除源节点前的插入位置。</summary>
    public bool Move(AfterSalesQualityCategoryNode node, Guid? parentId, int index)
    {
        if (!CanMove(node, parentId)) return false;
        using var scope = Lines.SuppressListEvents();
        var old = Siblings(node.ParentId);
        var target = Siblings(parentId);
        var oldIndex = old.IndexOf(node);
        if (ReferenceEquals(old, target) && oldIndex < index) index--;
        old.Remove(node);
        node.ParentId = parentId;
        target.Insert(Math.Clamp(index, 0, target.Count), node);
        Renumber(old);
        if (!ReferenceEquals(old, target)) Renumber(target);
        return true;
    }

    /// <summary>取得某一层的显示集合；上级身份必须来自当前树。</summary>
    public ObservableCollection<AfterSalesQualityCategoryNode> Siblings(Guid? parentId) => parentId is null || parentId == Guid.Empty
        ? Roots : Lines.Items.Single(x => x.Uid == parentId).Children;

    /// <summary>提交前定位第一个输入错误，编码在整棵树中忽略大小写唯一。</summary>
    public (AfterSalesQualityCategoryNode? Node, string? Message) Validate()
    {
        foreach (var node in Lines.Items)
            if (node.ValidateRow() is { } error) return (node, error);
        var duplicate = Lines.Items.Where(x => !string.IsNullOrWhiteSpace(x.CategoryCode))
            .GroupBy(x => x.CategoryCode.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        return duplicate is null ? (null, null) : (duplicate.First(), "分类编码不能重复。");
    }

    /// <summary>构造读写分离的批量请求，临时身份支持同批父子新增。</summary>
    public AfterSalesQualityCategoryBatchSaveRequest CreateRequest() => new()
    {
        CreateItems = Lines.AddedItems.Select(x => new CreateItemRequest<AfterSalesQualityCategoryEditDto>
            { ClientKey = x.Uid.ToString("N"), Data = x.ToEditDto() }).ToList(),
        UpdateItems = Lines.ModifiedItems.Select(x => new UpdateItemRequest<AfterSalesQualityCategoryEditDto>
            { ClientKey = x.Uid.ToString("N"), Uid = x.Uid, Version = x.Version, Data = x.ToEditDto() }).ToList(),
        DeleteItems = Lines.DeletedItems.Select(x => new DeleteItemRequest
            { ClientKey = x.Uid.ToString("N"), Uid = x.Uid, Version = x.Version, IsSoftDelete = true }).ToList()
    };

    /// <summary>从平面事实恢复有序树，不复制节点状态。</summary>
    public void Rebuild()
    {
        Roots.Clear();
        foreach (var node in Lines.Items) node.Children.Clear();
        foreach (var node in Lines.Items.OrderBy(x => x.SortNo).ThenBy(x => x.CategoryName)) Siblings(node.ParentId).Add(node);
    }

    /// <summary>前序展开节点，用于删除和后代检查。</summary>
    public static IEnumerable<AfterSalesQualityCategoryNode> Flatten(IEnumerable<AfterSalesQualityCategoryNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    /// <summary>移动后重排同级节点，变更由行模型自动跟踪。</summary>
    private static void Renumber(IList<AfterSalesQualityCategoryNode> nodes)
    {
        for (var index = 0; index < nodes.Count; index++) nodes[index].SortNo = index + 1;
    }
}
