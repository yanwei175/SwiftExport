using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup.Internal;

internal sealed class GridLookupDisplayTextBlock : TextBlock
{
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue),
        typeof(object),
        typeof(GridLookupDisplayTextBlock),
        new PropertyMetadata(null, OnLookupChanged));

    public static readonly DependencyProperty LookupItemsSourceProperty = DependencyProperty.Register(
        nameof(LookupItemsSource),
        typeof(IEnumerable),
        typeof(GridLookupDisplayTextBlock),
        new PropertyMetadata(null, OnLookupChanged));

    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(
        nameof(Provider),
        typeof(IGridLookupProvider),
        typeof(GridLookupDisplayTextBlock),
        new PropertyMetadata(null, OnLookupChanged));

    private CancellationTokenSource? _cancellation;

    public GridLookupDisplayTextBlock()
    {
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => CancelPendingResolve();
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public IEnumerable? LookupItemsSource
    {
        get => (IEnumerable?)GetValue(LookupItemsSourceProperty);
        set => SetValue(LookupItemsSourceProperty, value);
    }

    public IGridLookupProvider? Provider
    {
        get => (IGridLookupProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    public string? SelectedValuePath { get; set; }

    public string? DisplayMemberPath { get; set; }

    private static void OnLookupChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((GridLookupDisplayTextBlock)dependencyObject).Refresh();
    }

    private void Refresh()
    {
        CancelPendingResolve();
        _cancellation = new CancellationTokenSource();

        if (SelectedValue is null)
        {
            Text = string.Empty;
            return;
        }

        if (LookupValueAccessor.FindBySelectedValue(LookupItemsSource, SelectedValue, SelectedValuePath) is { } localItem)
        {
            Text = LookupValueAccessor.GetValue(localItem, DisplayMemberPath)?.ToString() ?? string.Empty;
            return;
        }

        if (Provider is not null)
        {
            _ = ResolveAsync(_cancellation.Token);
            return;
        }

        Text = SelectedValue.ToString() ?? string.Empty;
    }

    private void CancelPendingResolve()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var item = await Provider!.ResolveAsync(SelectedValue!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Text = LookupValueAccessor.GetValue(item, DisplayMemberPath)?.ToString() ?? string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            Text = SelectedValue?.ToString() ?? string.Empty;
        }
    }
}
