using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsViewComboBoxBindingTests
{
    [TestMethod]
    public void ComboBoxSources_ShouldResolveCollectionViewsWithoutViewPathErrors()
    {
        StaTest.RunAsync(() =>
        {
            var materialTypesView = new CollectionViewSource { Source = new[] { "成品", "半成品" } };
            var supplyTypesView = new CollectionViewSource { Source = new[] { "采购", "生产" } };
            var materialTypeColumn = new DataGridComboBoxColumn();
            var supplyTypeColumn = new DataGridComboBoxColumn();
            var listener = new BindingErrorTraceListener();
            var originalLevel = PresentationTraceSources.DataBindingSource.Switch.Level;

            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            try
            {
                BindingOperations.SetBinding(
                    materialTypeColumn,
                    DataGridComboBoxColumn.ItemsSourceProperty,
                    new Binding { Source = materialTypesView });
                BindingOperations.SetBinding(
                    supplyTypeColumn,
                    DataGridComboBoxColumn.ItemsSourceProperty,
                    new Binding { Source = supplyTypesView });

                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                Assert.AreSame(materialTypesView.View, materialTypeColumn.ItemsSource);
                Assert.AreSame(supplyTypesView.View, supplyTypeColumn.ItemsSource);
                Assert.IsFalse(
                    listener.Messages.Any(message => message.Contains("View", StringComparison.Ordinal)),
                    "绑定日志不得再访问 ListCollectionView.View。\n" + string.Join(Environment.NewLine, listener.Messages));
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = originalLevel;
            }

            return Task.CompletedTask;
        });
    }

    private sealed class BindingErrorTraceListener : TraceListener
    {
        public List<string> Messages { get; } = [];

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Messages.Add(message);
            }
        }

        public override void WriteLine(string? message) => Write(message);
    }
}
