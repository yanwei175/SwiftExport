using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using UIElement = System.Windows.UIElement;

namespace SwiftExport.Client.VSTO.Hosting.Windows
{
    /// <summary>统一承载非模态 WPF 页面；按所属显示器可用区域设置窗体尺寸和外观。</summary>
    public sealed class ModelessElementHostForm : Form
    {
        private readonly ElementHost host;
        private bool contentDetached;

        /// <summary>创建 WinForms 外壳；首选尺寸是窗口外框尺寸，实际尺寸不会超出所属显示器的可用区域。</summary>
        public ModelessElementHostForm(string title, Size preferredSize, IntPtr owner)
        {
            var screen = owner == IntPtr.Zero ? Screen.PrimaryScreen : Screen.FromHandle(owner);
            var area = screen.WorkingArea;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = owner == IntPtr.Zero ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Text = title;
            BackColor = Color.White;
            Padding = new Padding(1);
            var initialSize = CalculateInitialSize(preferredSize, area.Size);
            MinimumSize = new Size(Math.Min(640, initialSize.Width), Math.Min(420, initialSize.Height));
            Size = initialSize;
            // 宿主和页面留白统一为白色，避免透明内容暴露 WinForms 的底色。
            host = new ElementHost { Dock = DockStyle.Fill, BackColor = Color.White, BackColorTransparent = false };
            Controls.Add(host);
        }

        /// <summary>当前承载的 WPF 页面；关闭确认只读取该实例，不重新解析依赖。</summary>
        public UIElement Content { get { return host.Child; } }

        /// <summary>为页面绑定统一字体，再将已从 Scope 创建的 WPF 内容装入窗体。</summary>
        public void LoadWpfUI(UIElement content)
        {
            if (IsDisposed || Disposing) throw new ObjectDisposedException(nameof(ModelessElementHostForm));
            // 只统一字体族，保留页面的字号、字重及 WPF-UI 控件模板。
            if (content is System.Windows.FrameworkElement element)
                element.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "SwiftExportFont");
            host.Child = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>根据可用屏幕计算初始外框；至少占约四分之三，最多保留边缘操作空间。</summary>
        internal static Size CalculateInitialSize(Size preferred, Size workingArea)
        {
            if (preferred.Width <= 0 || preferred.Height <= 0) throw new ArgumentOutOfRangeException(nameof(preferred));
            if (workingArea.Width <= 0 || workingArea.Height <= 0) throw new ArgumentOutOfRangeException(nameof(workingArea));
            var width = Math.Min(Math.Max(preferred.Width, (int)(workingArea.Width * 0.78)), (int)(workingArea.Width * 0.92));
            var height = Math.Min(Math.Max(preferred.Height, (int)(workingArea.Height * 0.78)), (int)(workingArea.Height * 0.90));
            return new Size(Math.Max(1, width), Math.Max(1, height));
        }

        /// <summary>释放 WinForms 桥接前断开 WPF 可视树，避免保留已关闭页面。</summary>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && !contentDetached)
                {
                    contentDetached = true;
                    if (host != null && !host.IsDisposed && !host.Disposing) host.Child = null;
                }
            }
            finally { base.Dispose(disposing); }
        }
    }
}
