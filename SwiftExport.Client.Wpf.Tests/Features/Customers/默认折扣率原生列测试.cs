using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证原生文本列保留折扣输入、错误阻断和修正后的提交能力。</summary>
[TestClass]
public sealed class DiscountRateColumnTests
{
    /// <summary>原生提交必须拒绝负数、非法文本和超长小数，修正后原位提交，取消保留旧值。</summary>
    [TestMethod]
    public void Commit_ShouldValidateFormatRangeAndDecimalPlacesWithoutFilteringInput()
    {
        StaTest.RunAsync(() =>
        {
            var row = new NumericRow { DefaultDiscountRate = 1m };
            var column = LoadDiscountRateColumn();
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { row },
                Language = XmlLanguage.GetLanguage("en-US")
            };
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, row, column);

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = FindCurrentEditor(dataGrid);
            editor.Text = "-1";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(editor));

            editor.Text = "10.1234567";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));

            Assert.AreEqual("10.1234567", editor.Text);
            Assert.AreEqual(1m, row.DefaultDiscountRate);
            editor.Text = "invalid";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            editor.Text = "10.12";
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.AreEqual(10.12m, row.DefaultDiscountRate);
            Assert.IsFalse(System.Windows.Controls.Validation.GetHasError(editor));
            Assert.IsTrue(dataGrid.BeginEdit());
            editor = FindCurrentEditor(dataGrid);
            editor.Text = "-2";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.IsTrue(dataGrid.CancelEdit(DataGridEditingUnit.Cell));
            Assert.AreEqual(10.12m, row.DefaultDiscountRate);

            foreach (var (input, expected) in new (string, decimal?)[] { ("", null), ("0", 0m), ("10.123456", 10.123456m) })
            {
                Assert.IsTrue(dataGrid.BeginEdit());
                editor = FindCurrentEditor(dataGrid);
                editor.Text = input;
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true), $"输入 '{input}'：{string.Join("; ", System.Windows.Controls.Validation.GetErrors(editor).Select(error => error.ErrorContent))}");
                Assert.AreEqual(expected, row.DefaultDiscountRate);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>提取实际页面的原生 Binding 和验证规则，确保回归覆盖真实空值映射和精度约束。</summary>
    private static DataGridTextColumn LoadDiscountRateColumn()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var document = XDocument.Load(Path.Combine(root.FullName, "SwiftExport.Client.Wpf", "Features", "Customers",
            "CustomerPoConfigs", "Views", "CustomerPoConfigManagerView.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var binding = document.Descendants(ns + "Binding").Single(element => (string?)element.Attribute("Path") == "DefaultDiscountRate");
        var column = new XElement(ns + "DataGridTextColumn",
            new XAttribute("xmlns", ns.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "validation", "clr-namespace:SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;assembly=SwiftExport.Client.Wpf"),
            new XElement(ns + "DataGridTextColumn.Binding", new XElement(binding)));
        foreach (var element in column.Descendants().Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)))
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=SwiftExport.Client.Wpf");
        return (DataGridTextColumn)XamlReader.Parse(column.ToString());
    }

    /// <summary>创建可布局的原生宿主，让测试经过真实编辑提交路径。</summary>
    private static IDisposable Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 400,
            Height = 200,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        return new WindowScope(window);
    }

    /// <summary>显式定位目标格并生成编辑容器，代替已退出的活动行自动滚动。</summary>
    private static void SetCurrentCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.SelectedItem = item;
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
    }

    /// <summary>从测试可视树中查找原生编辑框，保持断言不依赖主题模板层级。</summary>
    private static TextBox FindCurrentEditor(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBox textBox)
            {
                return textBox;
            }

            try
            {
                return FindCurrentEditor(child);
            }
            catch (AssertFailedException)
            {
            }
        }

        throw new AssertFailedException("未找到原生数字文本编辑器。");
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class NumericRow
    {
        public decimal? DefaultDiscountRate { get; set; }
    }
}
