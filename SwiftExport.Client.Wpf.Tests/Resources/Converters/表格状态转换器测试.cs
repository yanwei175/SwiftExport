using System.Globalization;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Resources.Converters;

namespace SwiftExport.Client.Wpf.Tests.Resources.Converters;

/// <summary>验证直接消费原生状态的无副作用转换器。</summary>
[TestClass]
public sealed class GridSelectionTests
{
    /// <summary>当前格必须同时匹配数据项和列，不依赖容器缓存。</summary>
    [TestMethod]
    public void CurrentCellStateConverter_ShouldMatchItemAndColumnWithoutCachingContainer()
    {
        var first = new TestRow("A");
        var second = new TestRow("B");
        var nameColumn = new DataGridTextColumn();
        var otherColumn = new DataGridTextColumn();
        var converter = new GridCurrentCellStateConverter();
        var current = new DataGridCellInfo(first, nameColumn);

        Assert.AreEqual(
            true,
            converter.Convert(
                [first, nameColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.Convert(
                [second, nameColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.Convert(
                [first, otherColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
    }

    /// <summary>活动行只由原生当前格的数据项决定。</summary>
    [TestMethod]
    public void CurrentRowStateConverter_ShouldMatchCurrentCellItemWithoutCachingContainer()
    {
        var first = new TestRow("A");
        var second = new TestRow("B");
        var column = new DataGridTextColumn();
        var converter = new GridCurrentRowStateConverter();
        var current = new DataGridCellInfo(second, column);

        Assert.AreEqual(
            false,
            converter.Convert([first, current], typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.Convert([second, current], typeof(bool), null!, CultureInfo.CurrentCulture));
    }

    /// <summary>布尔模式开关双向映射，并对空值或非法值回退整行模式。</summary>
    [TestMethod]
    public void SelectionUnitConverter_ShouldDefaultToFullRowAndRoundTripCellModes()
    {
        var converter = new GridSelectionUnitConverter();

        Assert.AreEqual(
            DataGridSelectionUnit.FullRow,
            converter.Convert(false, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            DataGridSelectionUnit.FullRow,
            converter.Convert(null!, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            DataGridSelectionUnit.CellOrRowHeader,
            converter.Convert(true, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.ConvertBack(DataGridSelectionUnit.Cell, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.ConvertBack(DataGridSelectionUnit.CellOrRowHeader, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.ConvertBack(DataGridSelectionUnit.FullRow, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.ConvertBack("invalid", typeof(bool), null!, CultureInfo.CurrentCulture));
    }


    private sealed record TestRow(string Name);
}
