using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.State.Presentation;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.State;

[TestClass]
public sealed class GridRowPresentationTests
{
    [TestMethod]
    public void PresentationSlots_ShouldInheritWithoutBusinessStateContract()
    {
        StaTest.Run(() =>
        {
            var rowVisual = new Grid();
            var child = new TextBlock();
            rowVisual.Children.Add(child);
            var marker = Brushes.ForestGreen;

            GridRowPresentation.SetMarkerBrush(rowVisual, marker);
            GridRowPresentation.SetMarkerText(rowVisual, "新增行");
            GridRowPresentation.SetMarkerGlyph(rowVisual, "+");

            Assert.AreSame(marker, GridRowPresentation.GetMarkerBrush(child));
            Assert.AreEqual("新增行", GridRowPresentation.GetMarkerText(child));
            Assert.AreEqual("+", GridRowPresentation.GetMarkerGlyph(child));
            Assert.IsFalse(typeof(GridRowPresentation).Assembly.GetReferencedAssemblies()
                .Any(assembly => assembly.Name?.StartsWith("SwiftExport.Client", StringComparison.Ordinal) == true));
        });
    }
}
