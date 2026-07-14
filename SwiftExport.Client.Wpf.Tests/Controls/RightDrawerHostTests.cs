using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;

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

    [TestMethod]
    public void DrawerContentTemplate_ShouldUseDrawerContentAsDataContext()
    {
        StaTest.RunAsync(() =>
        {
            var context = new object();
#pragma warning disable CS0618
            var template = new DataTemplate
            {
                VisualTree = new FrameworkElementFactory(typeof(DrawerTestView)),
            };
#pragma warning restore CS0618
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                DrawerContent = context,
                DrawerContentTemplate = template,
            };
            var window = new Window
            {
                Width = 640,
                Height = 440,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host,
            };
            try
            {
                window.Show();
                host.UpdateLayout();
                var drawerView = FindVisualChild<DrawerTestView>(host);
                Assert.IsNotNull(drawerView);
                Assert.AreSame(context, drawerView.DataContext);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private sealed class DrawerTestView : UserControl
    {
    }
}
