using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

[TestClass]
public sealed class RightDrawerHostTests
{
    [TestMethod]
    public void OverlayDrawer_ShouldResizeWithinConfiguredBounds()
    {
        StaTest.RunAsync(() =>
        {
            var host = new RightDrawerHost
            {
                Width = 900,
                Height = 500,
                DisplayMode = DrawerDisplayMode.Overlay,
                IsOpen = true,
                IsResizable = true,
                MinDrawerWidth = 360,
                MaxDrawerWidth = 700,
                DrawerWidth = 520
            };
            var window = new Window
            {
                Width = 940,
                Height = 540,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };

            try
            {
                window.Show();
                host.UpdateLayout();
                var thumb = (Thumb?)host.FindName("ResizeThumb");
                Assert.IsNotNull(thumb);

                thumb.RaiseEvent(new DragDeltaEventArgs(-80, 0)
                {
                    RoutedEvent = Thumb.DragDeltaEvent
                });
                Assert.AreEqual(600, host.DrawerWidth);

                host.DrawerWidth = 900;
                Assert.AreEqual(700, host.DrawerWidth);
                host.DrawerWidth = 100;
                Assert.AreEqual(360, host.DrawerWidth);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }
}
