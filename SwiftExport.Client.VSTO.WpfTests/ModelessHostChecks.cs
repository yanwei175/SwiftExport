using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WpfUserControl = System.Windows.Controls.UserControl;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Windows;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>用真实 WinForms Form 和 ElementHost 验证非模态窗口复用及关闭释放。</summary>
    internal static class ModelessHostChecks
    {
        /// <summary>在独立 STA 打开、重复激活、关闭并重开页面，不依赖 Excel。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var large = ModelessElementHostForm.CalculateInitialSize(new Size(1280, 820), new Size(3840, 2160));
            var compact = ModelessElementHostForm.CalculateInitialSize(new Size(1920, 1080), new Size(1366, 768));
            var tiny = ModelessElementHostForm.CalculateInitialSize(new Size(1280, 820), new Size(640, 480));
            check(large.Width > 1280 && large.Height > 820, "高分屏初始窗体随可用区域增大");
            check(compact.Width < 1366 && compact.Height < 768, "小屏幕初始窗体保留可操作边缘");
            check(tiny.Width < 640 && tiny.Height < 480, "极小可用区域也不让首选尺寸越界");
            ModelessWindowContext context = null;
            var services = new ServiceCollection();
            services.AddSingleton(new AddInLog());
            services.AddScoped(provider => context = new ModelessWindowContext());
            services.AddScoped<TestView>();
            services.AddSingleton<ModelessElementHostWindowManager>();
            using (var provider = services.BuildServiceProvider())
            {
                var manager = provider.GetRequiredService<ModelessElementHostWindowManager>();
                manager.Show<TestView>("settings", "设置页", IntPtr.Zero, new Size(800, 600));
                var form = Application.OpenForms.Cast<Form>().Single(item => item.Text == "设置页");
                var host = form.Controls.OfType<ElementHost>().Single();
                check(form is ModelessElementHostForm && form.Visible && host.Child is TestView,
                    "非模态 WinForms 承载窗体装入 WPF 页面");
                check(form.AutoScaleMode == AutoScaleMode.Dpi && form.FormBorderStyle == FormBorderStyle.Sizable,
                    "承载窗体集中设置 DPI 缩放模式与可调整边框");
                check(context.Handle == form.Handle, "确认框归属设置窗体");
                manager.Show<TestView>("settings", "设置页", IntPtr.Zero, new Size(800, 600));
                check(ReferenceEquals(form, Application.OpenForms.Cast<Form>().Single(item => item.Text == "设置页")),
                    "重复打开只激活原窗体");
                form.Close();
                check(context.Handle == IntPtr.Zero, "关闭窗口释放页面 Scope 与 Owner 句柄");
                manager.Show<TestView>("settings", "设置页", IntPtr.Zero, new Size(800, 600));
                check(!ReferenceEquals(form, Application.OpenForms.Cast<Form>().Single(item => item.Text == "设置页")),
                    "关闭后可以创建新窗体");
                var allowClose = false;
                manager.Show<TestView>("sales-order-import", "销售订单导入", IntPtr.Zero,
                    new Size(1280, 820), view => allowClose);
                var salesForm = Application.OpenForms.Cast<Form>().Single(item => item.Text == "销售订单导入");
                salesForm.Close();
                check(salesForm.Visible && Application.OpenForms.Cast<Form>().Contains(salesForm),
                    "销售页取消关闭时保留原窗体和页面 Scope");
                allowClose = true;
                salesForm.Close();
                check(!Application.OpenForms.Cast<Form>().Contains(salesForm), "销售页确认后关闭窗体");
                allowClose = false;
                manager.Show<TestView>("sales-order-import", "销售订单导入", IntPtr.Zero,
                    new Size(1280, 820), view => allowClose);
                manager.Dispose();
                check(context.Handle == IntPtr.Zero && !Application.OpenForms.Cast<Form>().Any(item => item.Text == "设置页" || item.Text == "销售订单导入"),
                    "插件退出跳过销售关闭确认并释放全部窗体 Scope");
            }
            CheckPurchaseWindow(check);
        }

        /// <summary>用实际采购页面验证大尺寸非模态承载、重复激活和关闭释放。</summary>
        private static void CheckPurchaseWindow(Action<bool, string> check)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new AddInLog());
            services.AddScoped<ModelessWindowContext>();
            services.AddScoped<PurchaseOrderImportViewModel>();
            services.AddScoped<PurchaseOrderImportView>();
            services.AddSingleton<ModelessElementHostWindowManager>();
            using (var provider = services.BuildServiceProvider())
            {
                var manager = provider.GetRequiredService<ModelessElementHostWindowManager>();
                manager.Show<PurchaseOrderImportView>("purchase-order-import-preview", "采购订单导入预览", IntPtr.Zero, new Size(1280, 820));
                var form = Application.OpenForms.Cast<Form>().Single(item => item.Text == "采购订单导入预览");
                var host = form.Controls.OfType<ElementHost>().Single();
                var view = (PurchaseOrderImportView)host.Child;
                var model = (PurchaseOrderImportViewModel)view.DataContext;
                check(form is ModelessElementHostForm && form.Visible
                    && form.Width <= Screen.PrimaryScreen.WorkingArea.Width
                    && form.Height <= Screen.PrimaryScreen.WorkingArea.Height,
                    "采购预览使用适配屏幕的非模态 WinForms 承载窗体");
                manager.Show<PurchaseOrderImportView>("purchase-order-import-preview", "采购订单导入预览", IntPtr.Zero, new Size(1280, 820));
                check(ReferenceEquals(form, Application.OpenForms.Cast<Form>().Single(item => item.Text == "采购订单导入预览"))
                    && ReferenceEquals(view, host.Child), "重复打开采购预览保留原页面实例");
                form.Close();
                check(model.IsDisposed && !Application.OpenForms.Cast<Form>().Any(item => item.Text == "采购订单导入预览"),
                    "关闭采购窗体释放页面 Scope");
                manager.Dispose();
            }
        }

        /// <summary>供宿主测试解析的最小 WPF 页面。</summary>
        public sealed class TestView : WpfUserControl { }
    }
}
