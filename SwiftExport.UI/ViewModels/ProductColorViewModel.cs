using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.UI.ViewModels.Base;
using System.Collections.ObjectModel;

namespace SwiftExport.UI.ViewModels;

public class ProductColorViewModel : ViewModelBase
{
    public ObservableCollection<ProductColorDto> Colors { get; }
        = new ObservableCollection<ProductColorDto>();

    private ProductColorDto _selected;
    public ProductColorDto Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Raise(nameof(Selected));
            RaiseCommands();
        }
    }

    public RelayCommand LoadCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public ProductColorViewModel()
    {
        LoadCommand = new RelayCommand(Load);
        NewCommand = new RelayCommand(NewItem);
        SaveCommand = new RelayCommand(Save, () => Selected != null);
        DeleteCommand = new RelayCommand(Delete, () => Selected?.Id > 0);
    }

    private void Load()
    {
        // 现在先写死，下一步接 WebAPI
        Colors.Clear();
        Colors.Add(new ProductColorDto { Id = 1, ColorName = "Red", ColorCode = "#FF0000" });
        Colors.Add(new ProductColorDto { Id = 2, ColorName = "Blue", ColorCode = "#0000FF" });
    }

    private void NewItem()
    {
        var item = new ProductColorDto();
        Colors.Add(item);
        Selected = item;
    }

    private void Save()
    {
        // 下一步改成 WebAPI
    }

    private void Delete()
    {
        Colors.Remove(Selected);
        Selected = null;
    }

    private void RaiseCommands()
    {
        SaveCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}
