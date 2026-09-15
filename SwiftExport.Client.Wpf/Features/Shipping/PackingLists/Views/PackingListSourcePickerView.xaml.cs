using System.Windows.Controls;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views;

/// <summary>待交付来源展示视图，业务选择由 ViewModel 负责。</summary>
public partial class PackingListSourcePickerView : UserControl
{
    /// <summary>初始化选源布局。</summary>
    public PackingListSourcePickerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.PackingListSourcePickerViewModel vm)
                vm.ValidateEditors = () => !PackingListInputValidation.HasError(this);
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is ViewModels.PackingListSourcePickerViewModel vm) vm.ValidateEditors = null;
        };
    }
}
