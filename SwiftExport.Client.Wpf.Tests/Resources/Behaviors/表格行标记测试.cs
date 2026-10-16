using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Resources.Behaviors;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.Behaviors;

/// <summary>验证客户端行状态标记在模板子元素中继承。</summary>
[TestClass]
public sealed class GridRowPresentationTests
{
    /// <summary>状态的颜色与文本无需 Grid 协调器即可继承。</summary>
    [TestMethod]
    public void PresentationSlots_ShouldInheritWithoutBusinessStateContract()
    {
        StaTest.RunAsync(() =>
        {
            var rowVisual = new Grid();
            var child = new TextBlock();
            rowVisual.Children.Add(child);
            var marker = Brushes.ForestGreen;

            GridRowPresentation.SetMarkerBrush(rowVisual, marker);
            GridRowPresentation.SetMarkerText(rowVisual, "新增行");

            Assert.AreSame(marker, GridRowPresentation.GetMarkerBrush(child));
            Assert.AreEqual("新增行", GridRowPresentation.GetMarkerText(child));
            return Task.CompletedTask;
        });
    }
}
