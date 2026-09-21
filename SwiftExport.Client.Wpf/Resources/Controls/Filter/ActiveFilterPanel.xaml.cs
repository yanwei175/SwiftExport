using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExportClient.Resources.Controls.Filter;

public partial class ActiveFilterPanel : UserControl
{
    public ActiveFilterPanel()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(ActiveFilterPanel));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty RemoveCommandProperty =
        DependencyProperty.Register(
            nameof(RemoveCommand),
            typeof(ICommand),
            typeof(ActiveFilterPanel));

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public static readonly DependencyProperty ClearAllCommandProperty =
        DependencyProperty.Register(
            nameof(ClearAllCommand),
            typeof(ICommand),
            typeof(ActiveFilterPanel));

    public ICommand? ClearAllCommand
    {
        get => (ICommand?)GetValue(ClearAllCommandProperty);
        set => SetValue(ClearAllCommandProperty, value);
    }
}