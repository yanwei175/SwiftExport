using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Core;

[TestClass]
public sealed class EnterpriseGridLifecycleTests
{
    [TestMethod]
    public void EnableLoadUnloadDisable_ShouldBeIdempotent()
    {
        StaTest.Run(() =>
        {
            var dataGrid = new DataGrid();

            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            var coordinator = EnterpriseGridCore.GetCoordinator(dataGrid);
            Assert.IsNotNull(coordinator);
            Assert.IsFalse(coordinator.IsActive);

            dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.IsTrue(coordinator.IsActive);

            dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.IsTrue(coordinator.IsActive);

            dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.IsFalse(coordinator.IsActive);

            EnterpriseGridCore.SetIsEnabled(dataGrid, false);
            Assert.IsNull(EnterpriseGridCore.GetCoordinator(dataGrid));
        });
    }
}
