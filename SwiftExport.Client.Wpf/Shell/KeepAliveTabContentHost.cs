using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Shell;

/// <summary>为每个打开页签保留唯一内容呈现器，仅切换可见性并在移除时主动断开视觉与数据引用。</summary>
public sealed class KeepAliveTabContentHost : Grid, IWeakEventListener
{
    private readonly Dictionary<NavigationTabItem, ContentPresenter> _presenters = [];
    private INotifyCollectionChanged? _observableItems;

    /// <summary>标识页签集合依赖属性。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(KeepAliveTabContentHost),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>标识当前页签依赖属性。</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(NavigationTabItem),
        typeof(KeepAliveTabContentHost),
        new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>获取或设置需要保活呈现的页签集合。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>获取或设置当前可见页签，其他页签仍保留在视觉树中。</summary>
    public NavigationTabItem? SelectedItem
    {
        get => (NavigationTabItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (KeepAliveTabContentHost)d;
        host.DetachItemsSource();
        host.AttachItemsSource(e.NewValue as IEnumerable);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((KeepAliveTabContentHost)d).UpdateVisibility();
    }

    /// <summary>订阅集合变化并按当前顺序建立每个页签唯一的内容呈现器。</summary>
    private void AttachItemsSource(IEnumerable? itemsSource)
    {
        _observableItems = itemsSource as INotifyCollectionChanged;
        if (_observableItems is not null)
        {
            CollectionChangedEventManager.AddListener(_observableItems, this);
        }

        Rebuild(itemsSource);
    }

    /// <summary>解除旧集合订阅并清除所有呈现器引用。</summary>
    private void DetachItemsSource()
    {
        if (_observableItems is not null)
        {
            CollectionChangedEventManager.RemoveListener(_observableItems, this);
            _observableItems = null;
        }

        RemoveAllPresenters();
    }

    /// <summary>通过 WPF 弱事件接收集合变化，避免导航单例集合延长已关闭 Shell 的生命周期。</summary>
    public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
    {
        if (managerType != typeof(CollectionChangedEventManager)
            || e is not NotifyCollectionChangedEventArgs collectionChanged)
        {
            return false;
        }

        OnItemsCollectionChanged(collectionChanged);
        return true;
    }

    private void OnItemsCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Rebuild(ItemsSource);
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var tab in e.OldItems.OfType<NavigationTabItem>())
            {
                RemovePresenter(tab);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var tab in e.NewItems.OfType<NavigationTabItem>())
            {
                AddPresenter(tab);
            }
        }

        UpdateVisibility();
    }

    /// <summary>重建集合只用于 ItemsSource 更换或 Reset，确保旧 presenter 先断开再创建新项。</summary>
    private void Rebuild(IEnumerable? itemsSource)
    {
        RemoveAllPresenters();
        if (itemsSource is not null)
        {
            foreach (var tab in itemsSource.OfType<NavigationTabItem>())
            {
                AddPresenter(tab);
            }
        }

        UpdateVisibility();
    }

    private void AddPresenter(NavigationTabItem tab)
    {
        if (_presenters.ContainsKey(tab))
        {
            return;
        }

        var presenter = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        BindingOperations.SetBinding(
            presenter,
            ContentPresenter.ContentProperty,
            new Binding(nameof(NavigationTabItem.ContentViewModel)) { Source = tab });
        _presenters.Add(tab, presenter);
        Children.Add(presenter);
    }

    /// <summary>先从视觉树移除并清除绑定，使 Tab 随后释放 Scope 时不再被宿主持有。</summary>
    private void RemovePresenter(NavigationTabItem tab)
    {
        if (!_presenters.Remove(tab, out var presenter))
        {
            return;
        }

        Children.Remove(presenter);
        BindingOperations.ClearBinding(presenter, ContentPresenter.ContentProperty);
        presenter.Content = null;
    }

    private void RemoveAllPresenters()
    {
        foreach (var tab in _presenters.Keys.ToArray())
        {
            RemovePresenter(tab);
        }
    }

    /// <summary>只切换呈现器可见性；隐藏内容继续留在视觉树且不会触发 Unloaded。</summary>
    private void UpdateVisibility()
    {
        foreach (var pair in _presenters)
        {
            var shouldShow = ReferenceEquals(pair.Key, SelectedItem);
            pair.Value.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
