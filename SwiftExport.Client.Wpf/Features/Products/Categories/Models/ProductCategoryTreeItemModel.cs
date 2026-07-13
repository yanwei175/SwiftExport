using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.Categories.Models;

public sealed partial class ProductCategoryTreeItemModel : ObservableObjectEx
{
    [ObservableProperty]
    public partial Guid Uid { get; set; }

    [ObservableProperty]
    public partial int Version { get; set; }

    [ObservableProperty]
    public partial bool IsDeleted { get; set; }

    [ObservableProperty]
    public partial DateTime? CreatedDate { get; set; }

    [ObservableProperty]
    public partial string? CreatedBy { get; set; }

    [ObservableProperty]
    public partial DateTime? UpdatedDate { get; set; }

    [ObservableProperty]
    public partial string? UpdatedBy { get; set; }

    [ObservableProperty]
    public partial Guid? ParentId { get; set; }

    [ObservableProperty]
    public partial string CategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FakeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SortNo { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    public partial ProductCategoryNodeState State { get; set; }

    public bool IsAdded => State == ProductCategoryNodeState.Added;

    public bool IsModified => State == ProductCategoryNodeState.Modified;

    public ObservableCollection<ProductCategoryTreeItemModel> Children { get; } = [];

    public static ProductCategoryTreeItemModel FromDto(ProductCategoryDto dto)
    {
        var item = new ProductCategoryTreeItemModel();
        using (item.SuppressScope())
        {
            dto.Adapt(item);
            item.State = ProductCategoryNodeState.Unchanged;
        }

        return item;
    }

    public ProductCategoryDto ToDto() => this.Adapt<ProductCategoryDto>();

    partial void OnParentIdChanged(Guid? value) => MarkModified();

    partial void OnCategoryNameChanged(string value) => MarkModified();

    partial void OnFakeNameChanged(string value) => MarkModified();

    partial void OnCategoryCodeChanged(string value) => MarkModified();

    partial void OnSortNoChanged(int value) => MarkModified();

    private void MarkModified()
    {
        if (!IsSuppressed && State == ProductCategoryNodeState.Unchanged)
        {
            State = ProductCategoryNodeState.Modified;
        }
    }
}
