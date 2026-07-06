using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Categories.Models;

public sealed class ProductCategoryTreeItemModel : ObservableObjectEx
{
    private Guid _uid;
    private Guid? _parentId;
    private string _categoryName = string.Empty;
    private string _fakeName = string.Empty;
    private string _categoryCode = string.Empty;
    private int _sortNo;
    private bool _isExpanded;
    private bool _isSelected;
    private ProductCategoryNodeState _state;

    public Guid Uid
    {
        get => _uid;
        set => SetProperty(ref _uid, value);
    }

    public Guid? ParentId
    {
        get => _parentId;
        set
        {
            if (SetProperty(ref _parentId, value))
            {
                MarkModified();
            }
        }
    }

    public string CategoryName
    {
        get => _categoryName;
        set
        {
            if (SetProperty(ref _categoryName, value))
            {
                MarkModified();
            }
        }
    }

    public string FakeName
    {
        get => _fakeName;
        set
        {
            if (SetProperty(ref _fakeName, value))
            {
                MarkModified();
            }
        }
    }

    public string CategoryCode
    {
        get => _categoryCode;
        set
        {
            if (SetProperty(ref _categoryCode, value))
            {
                MarkModified();
            }
        }
    }

    public int SortNo
    {
        get => _sortNo;
        set
        {
            if (SetProperty(ref _sortNo, value))
            {
                MarkModified();
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ProductCategoryNodeState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsAdded));
                OnPropertyChanged(nameof(IsModified));
            }
        }
    }

    public bool IsAdded => State == ProductCategoryNodeState.Added;

    public bool IsModified => State == ProductCategoryNodeState.Modified;

    public ObservableCollection<ProductCategoryTreeItemModel> Children { get; } = [];

    public static ProductCategoryTreeItemModel FromDto(ProductCategoryDto dto)
    {
        var item = new ProductCategoryTreeItemModel();
        using (item.SuppressScope())
        {
            item.Uid = dto.Uid;
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
            ParentId = ParentId == Guid.Empty ? null : ParentId,
            CategoryName = CategoryName.Trim(),
            FakeName = FakeName.Trim(),
            CategoryCode = CategoryCode.Trim(),
            SortNo = SortNo
        };
    }

    private void MarkModified()
    {
        if (!IsSuppressed && State == ProductCategoryNodeState.Unchanged)
        {
            State = ProductCategoryNodeState.Modified;
        }
    }
}
