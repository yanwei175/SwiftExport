using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.Categories.Models;

public sealed partial class ProductCategoryTreeItemModel : ObservableObjectEx
{
    [ObservableProperty]
    private Guid uid;

    [ObservableProperty]
    private int version;

    [ObservableProperty]
    private bool isDeleted;

    [ObservableProperty]
    private DateTime? createdDate;

    [ObservableProperty]
    private string? createdBy;

    [ObservableProperty]
    private DateTime? updatedDate;

    [ObservableProperty]
    private string? updatedBy;

    [ObservableProperty]
    private Guid? parentId;

    [ObservableProperty]
    private string categoryName = string.Empty;

    [ObservableProperty]
    private string fakeName = string.Empty;

    [ObservableProperty]
    private string categoryCode = string.Empty;

    [ObservableProperty]
    private int sortNo;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private ProductCategoryNodeState state;

    public bool IsAdded => State == ProductCategoryNodeState.Added;

    public bool IsModified => State == ProductCategoryNodeState.Modified;

    public ObservableCollection<ProductCategoryTreeItemModel> Children { get; } = [];

    public static ProductCategoryTreeItemModel FromDto(ProductCategoryDto dto)
    {
        var item = new ProductCategoryTreeItemModel();
        using (item.SuppressScope())
        {
            item.Uid = dto.Uid;
            item.Version = dto.Version;
            item.IsDeleted = dto.IsDeleted;
            item.CreatedDate = dto.CreatedDate;
            item.CreatedBy = dto.CreatedBy;
            item.UpdatedDate = dto.UpdatedDate;
            item.UpdatedBy = dto.UpdatedBy;
            item.ParentId = dto.ParentId;
            item.CategoryName = dto.CategoryName ?? string.Empty;
            item.FakeName = dto.FakeName ?? string.Empty;
            item.CategoryCode = dto.CategoryCode ?? string.Empty;
            item.SortNo = dto.SortNo;
            item.State = ProductCategoryNodeState.Unchanged;
        }

        return item;
    }

    public ProductCategoryDto ToDto()
    {
        return new ProductCategoryDto
        {
            Uid = Uid,
            Version = Version,
            IsDeleted = IsDeleted,
            CreatedDate = CreatedDate,
            CreatedBy = CreatedBy,
            UpdatedDate = UpdatedDate,
            UpdatedBy = UpdatedBy,
            ParentId = ParentId == Guid.Empty ? null : ParentId,
            CategoryName = CategoryName.Trim(),
            FakeName = FakeName.Trim(),
            CategoryCode = CategoryCode.Trim(),
            SortNo = SortNo,
        };
    }

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
