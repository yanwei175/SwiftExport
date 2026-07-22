using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
[DoNotParallelize]
public sealed class ProductLookupTextBoxTests
{
    [TestMethod]
    public void ProductLookup_InitializationAndFocus_ShouldNotSearchOrOpenPopup()
    {
        StaTest.RunAsync(async () =>
        {
            var search = new RecordingCommand();
            var candidate = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "SKU-1" };
            var lookup = new ProductLookupTextBox
            {
                Text = "EXISTING-SKU",
                SearchCommand = search,
                ItemsSource = new[] { candidate },
                SearchDelay = TimeSpan.FromMilliseconds(20),
            };
            var window = new Window
            {
                Width = 500,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await WaitForDispatcherAsync(50);

                var editor = FindVisualChild<LookupTextBox>(lookup);
                Assert.IsNotNull(editor);
                Assert.AreEqual(LookupTextBoxSearchMode.Remote, editor.SearchMode);
                Assert.AreEqual(nameof(ProductLookupDto.Sku), editor.DisplayMemberPath);
                Assert.AreEqual(nameof(ProductLookupDto.Uid), editor.SelectedValuePath);
                Assert.IsTrue(editor.RequireItemMatch);
                Assert.IsFalse(editor.IsDropDownOpen);
                Assert.AreEqual(0, search.ExecutionCount);

                window.Activate();
                lookup.FocusEditor();
                await WaitForDispatcherAsync(40);

                Assert.IsFalse(editor.IsDropDownOpen, "获得焦点不得自动展开产品候选。");
                Assert.AreEqual(0, search.ExecutionCount, "初始化值和聚焦不得调用远程 API。");

                editor.SelectedItem = candidate;
                await WaitForDispatcherAsync(20);
                Assert.AreSame(candidate, lookup.SelectedProduct);
                Assert.AreEqual(candidate.Uid, lookup.SelectedProductId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void ProductLookup_ItemTemplate_ShouldUseOneSmallCachedImageAndWrappedChineseDescription()
    {
        StaTest.RunAsync(() =>
        {
            var lookup = new ProductLookupTextBox();
            var editor = (LookupTextBox?)lookup.FindName("LookupEditor");
            Assert.IsNotNull(editor);
            Assert.IsNotNull(editor.ItemTemplate);

            var content = (FrameworkElement)editor.ItemTemplate.LoadContent();
            content.DataContext = new ProductLookupDto
            {
                Sku = "SKU-1",
                DescriptionZh = "可以换行显示的中文描述",
                MainImage = string.Empty,
            };
            content.Measure(new Size(500, 200));
            content.Arrange(new Rect(content.DesiredSize));

            var images = FindVisualDescendants<CachedImage>(content).ToArray();
            var textBlocks = FindVisualDescendants<TextBlock>(content).ToArray();
            Assert.HasCount(1, images);
            Assert.AreEqual(96, images[0].DecodePixelWidth);
            Assert.AreEqual(48d, images[0].Width);
            Assert.AreEqual(48d, images[0].Height);
            Assert.IsNull(images[0].ToolTip, "候选缩略图不应触发额外的大图解码。");
            Assert.IsTrue(content.MinWidth >= 360);
            Assert.IsTrue(textBlocks.Any(text => text.TextWrapping == TextWrapping.Wrap));

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DataGridTemplateColumn_ShouldCreateLookupOnlyForEditingCell()
    {
        StaTest.RunAsync(() =>
        {
            var row = new TestRow { SubSku = "SKU-1" };
            var grid = new DataGrid
            {
                Width = 420,
                Height = 160,
                AutoGenerateColumns = false,
                ItemsSource = new[] { row },
            };
            var column = CreateLookupColumn();
            grid.Columns.Add(column);
            var window = new Window
            {
                Width = 500,
                Height = 220,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = grid,
            };

            try
            {
                window.Show();
                PumpDispatcher();
                Assert.AreEqual(0, FindVisualDescendants<ProductLookupTextBox>(grid).Count());

                grid.CurrentCell = new DataGridCellInfo(row, column);
                window.Activate();
                Assert.IsTrue(grid.BeginEdit());
                PumpDispatcher();
                var lookup = FindVisualDescendants<ProductLookupTextBox>(grid).Single();
                var editor = FindVisualChild<LookupTextBox>(lookup);
                var input = (TextBox?)editor?.FindName("InputTextBox");
                Assert.IsNotNull(input);
                Assert.AreEqual(Visibility.Visible, input.Visibility);
                Assert.IsTrue(input.IsKeyboardFocusWithin);

                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                PumpDispatcher();
                Assert.AreEqual(0, FindVisualDescendants<ProductLookupTextBox>(grid).Count());
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ProductLookup_EnterCommit_ShouldPublishSelectionBeforeBusinessCommand()
    {
        StaTest.RunAsync(async () =>
        {
            var candidate = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "CHILD" };
            var commandParameter = new object();
            ProductLookupTextBox? lookup = null;
            var command = new RecordingCommand(_ =>
            {
                Assert.IsNotNull(lookup);
                Assert.AreSame(candidate, lookup.SelectedProduct);
                Assert.AreEqual(candidate.Uid, lookup.SelectedProductId);
            });
            lookup = new ProductLookupTextBox
            {
                ItemsSource = new[] { candidate },
                SelectionCommittedCommand = command,
                SelectionCommittedCommandParameter = commandParameter,
            };
            var window = new Window
            {
                Width = 500,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await WaitForDispatcherAsync(30);
                var editor = FindVisualChild<LookupTextBox>(lookup);
                Assert.IsNotNull(editor);
                var input = (TextBox?)editor.FindName("InputTextBox");
                var results = (ListBox?)editor.FindName("ResultList");
                Assert.IsNotNull(input);
                Assert.IsNotNull(results);

                window.Activate();
                input.Focus();
                Keyboard.Focus(input);
                editor.IsDropDownOpen = true;
                results.SelectedItem = candidate;
                RaisePreviewKey(input, Key.Enter);
                await WaitForDispatcherAsync(20);

                Assert.AreEqual(1, command.ExecutionCount);
                Assert.AreSame(commandParameter, command.LastParameter);
                Assert.IsFalse(editor.IsDropDownOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void ProductLookup_ClearButton_ShouldForwardExplicitClearCommand()
    {
        StaTest.RunAsync(async () =>
        {
            var parameter = new object();
            var command = new RecordingCommand();
            var lookup = new ProductLookupTextBox
            {
                Text = "SKU-1",
                SelectionClearedCommand = command,
                SelectionClearedCommandParameter = parameter,
            };
            var window = new Window { Width = 400, Height = 120, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = lookup };
            try
            {
                window.Show();
                await WaitForDispatcherAsync(20);
                var editor = FindVisualChild<LookupTextBox>(lookup);
                Assert.IsNotNull(editor);
                var clear = (Button?)editor.FindName("BtnClear");
                Assert.IsNotNull(clear);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, command.ExecutionCount);
                Assert.AreSame(parameter, command.LastParameter);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DataGridTemplateColumn CreateLookupColumn()
    {
#pragma warning disable CS0618
        var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
        var displayTemplate = new DataTemplate { VisualTree = displayFactory };
        var editingFactory = new FrameworkElementFactory(typeof(ProductLookupTextBox));
        var editingTemplate = new DataTemplate { VisualTree = editingFactory };
#pragma warning restore CS0618
        return new DataGridTemplateColumn
        {
            Header = "子件 SKU",
            CellTemplate = displayTemplate,
            CellEditingTemplate = editingTemplate,
        };
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject =>
        parent is null ? null : FindVisualDescendants<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task WaitForDispatcherAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private sealed class TestRow
    {
        public string SubSku { get; set; } = string.Empty;
    }

    private sealed class RecordingCommand(Action<object?>? onExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecutionCount { get; private set; }

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecutionCount++;
            LastParameter = parameter;
            onExecute?.Invoke(parameter);
        }
    }
}
