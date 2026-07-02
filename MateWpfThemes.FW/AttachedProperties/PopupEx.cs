using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Interop;
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// PopupEx 是一个“增强版 Popup”，主要用于验证提示、错误提示、Tooltip 等场景。
    /// 它解决了 WPF 原生 Popup 的几个痛点：
    ///
    /// 1. 当窗口移动、缩放、最大化/还原时，Popup 会自动重新定位，不会漂移。
    /// 2. 当窗口激活/失活时，Popup 会自动调整 TopMost 状态，避免被其他窗口遮挡。
    /// 3. 当 PlacementTarget 尺寸变化时，Popup 会自动刷新位置。
    /// 4. 适合 Validation.ErrorTemplate、提示框、悬浮提示等“静态提示”场景。
    ///
    /// ⚠ 不适合用于自动补全（AutoComplete）或 DataGrid 单元格跟随输入框的动态 Popup。
    ///   因为 PopupEx 的定位刷新机制较重，不适合频繁开关和频繁移动的场景。
    /// </summary>

    public class PopupEx : Popup
    {
        public static readonly DependencyProperty CloseOnMouseLeftButtonDownProperty
            = DependencyProperty.Register(nameof(CloseOnMouseLeftButtonDown),
                                          typeof(bool),
                                          typeof(PopupEx),
                                          new PropertyMetadata(false));

        /// <summary>
        /// Gets/sets if the popup can be closed by left mouse button down.
        /// </summary>
        public bool CloseOnMouseLeftButtonDown
        {
            get => (bool)GetValue(CloseOnMouseLeftButtonDownProperty);
            set => SetValue(CloseOnMouseLeftButtonDownProperty, value);
        }

        public static readonly DependencyProperty AllowTopMostProperty
            = DependencyProperty.Register(nameof(AllowTopMost),
                                          typeof(bool),
                                          typeof(PopupEx),
                                          new PropertyMetadata(true));

        public bool AllowTopMost
        {
            get => (bool)GetValue(AllowTopMostProperty);
            set => SetValue(AllowTopMostProperty, value);
        }

        /// <summary>
        /// 构造函数：订阅 Loaded 与 Opened 事件
        /// </summary>
        public PopupEx()
        {
            // 窗口加载完成后我们需要初始化与宿主窗口的关联事件
            Loaded += PopupEx_Loaded;
            // 弹出时根据宿主窗口状态设置 TopMost
            Opened += PopupEx_Opened;
        }

        /// <summary>
        /// Causes the popup to update it's position according to it's current settings.
        /// </summary>
        public void RefreshPosition()
        {
            var offset = HorizontalOffset;
            // "bump" the offset to cause the popup to reposition itself on its own
            SetCurrentValue(HorizontalOffsetProperty, offset + 1);
            SetCurrentValue(HorizontalOffsetProperty, offset);
        }

        // 当 Popup 被加载到视觉树时，绑定宿主窗口相关事件以便在窗口移动/缩放时重新定位
        private void PopupEx_Loaded(object sender, RoutedEventArgs e)
        {
            var target = PlacementTarget as FrameworkElement;
            if (target == null)
            {
                return;
            }

            // 获取当前 PlacementTarget 所属的窗口（宿主窗口）
            _hostWindow = Window.GetWindow(target);
            if (_hostWindow == null)
            {
                return;
            }

            // 先移除再添加，避免重复订阅
            _hostWindow.LocationChanged -= HostWindow_SizeOrLocationChanged;
            _hostWindow.LocationChanged += HostWindow_SizeOrLocationChanged;
            _hostWindow.SizeChanged -= HostWindow_SizeOrLocationChanged;
            _hostWindow.SizeChanged += HostWindow_SizeOrLocationChanged;
            target.SizeChanged -= HostWindow_SizeOrLocationChanged;
            target.SizeChanged += HostWindow_SizeOrLocationChanged;
            _hostWindow.StateChanged -= HostWindow_StateChanged;
            _hostWindow.StateChanged += HostWindow_StateChanged;
            _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow.Activated += HostWindow_Activated;
            _hostWindow.Deactivated -= HostWindow_Deactivated;
            _hostWindow.Deactivated += HostWindow_Deactivated;

            Unloaded -= PopupEx_Unloaded;
            Unloaded += PopupEx_Unloaded;
        }

        // 弹出窗口打开时，设置弹出层是否置顶（仅在宿主窗口激活或宿主窗口本身置顶时置顶）
        private void PopupEx_Opened(object sender, EventArgs e)
        {
            bool topMost = true;
            if (_hostWindow == null)
            {
                topMost = true;
            }
            else
            {
                // 如果宿主窗口存在，只有宿主窗口激活或宿主窗口本身为 Topmost 时，Popup 才设为 Topmost
                topMost = _hostWindow.IsActive || _hostWindow.Topmost;
            }

            SetTopmostState(topMost);
        }

        // 宿主窗口被激活时，将 Popup 设置为置顶
        private void HostWindow_Activated(object sender, EventArgs e)
        {
            SetTopmostState(true);
        }

        // 宿主窗口失去激活时，根据宿主窗口的 Topmost 属性决定 Popup 是否继续置顶
        private void HostWindow_Deactivated(object sender, EventArgs e)
        {
            SetTopmostState(_hostWindow != null ? _hostWindow.Topmost : false);
        }

        // Popup 从视觉树卸载时，解除对宿主窗口的事件订阅，避免内存泄露
        private void PopupEx_Unloaded(object sender, RoutedEventArgs e)
        {
            if (PlacementTarget is FrameworkElement target)
            {
                target.SizeChanged -= HostWindow_SizeOrLocationChanged;
            }
            if (_hostWindow != null)
            {
                _hostWindow.LocationChanged -= HostWindow_SizeOrLocationChanged;
                _hostWindow.SizeChanged -= HostWindow_SizeOrLocationChanged;
                _hostWindow.StateChanged -= HostWindow_StateChanged;
                _hostWindow.Activated -= HostWindow_Activated;
                _hostWindow.Deactivated -= HostWindow_Deactivated;
            }
            Unloaded -= PopupEx_Unloaded;
            Opened -= PopupEx_Opened;
            _hostWindow = null;
        }

        // 宿主窗口状态变化（例如最大化/最小化）时的处理，特别针对验证弹窗的特殊处理
        private void HostWindow_StateChanged(object sender, EventArgs e)
        {
            if (_hostWindow != null && _hostWindow.WindowState != WindowState.Minimized)
            {
                // special handling for validation popup
                var target = PlacementTarget as FrameworkElement;
                var holder = target != null ? target.DataContext as AdornedElementPlaceholder : null;
                if (holder != null && holder.AdornedElement != null)
                {
                    PopupAnimation = PopupAnimation.None;
                    IsOpen = false;
                    var errorTemplate = holder.AdornedElement.GetValue(Validation.ErrorTemplateProperty);
                    holder.AdornedElement.SetValue(Validation.ErrorTemplateProperty, null);
                    holder.AdornedElement.SetValue(Validation.ErrorTemplateProperty, errorTemplate);
                }
            }
        }

        // 宿主窗口尺寸或位置改变时，刷新 Popup 的显示位置
        private void HostWindow_SizeOrLocationChanged(object sender, EventArgs e)
        {
            RefreshPosition();
        }

        private void SetTopmostState(bool isTop)
        {
            isTop &= AllowTopMost;
            // Don抰 apply state if it抯 the same as incoming state
            if (appliedTopMost.HasValue && appliedTopMost == isTop)
            {
                return;
            }

            if (Child is null)
            {
                return;
            }

            var hwndSource = (PresentationSource.FromVisual(Child)) as HwndSource;
            if (hwndSource is null)
            {
                return;
            }
            var hwnd = hwndSource.Handle;

            RECT rect;
            if (!GetWindowRect(hwnd, out rect))
            {
                return;
            }
            //Debug.WriteLine("setting z-order " + isTop);

            var left = rect.Left;
            var top = rect.Top;
            var width = rect.Width;
            var height = rect.Height;
            if (isTop)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP.TOPMOST);
            }
            else
            {
                // Z-Order would only get refreshed/reflected if clicking the
                // the titlebar (as opposed to other parts of the external
                // window) unless I first set the popup to HWND_BOTTOM
                // then HWND_TOP before HWND_NOTOPMOST
                SetWindowPos(hwnd, HWND_BOTTOM, left, top, width, height, SWP.TOPMOST);
                SetWindowPos(hwnd, HWND_TOP, left, top, width, height, SWP.TOPMOST);
                SetWindowPos(hwnd, HWND_NOTOPMOST, left, top, width, height, SWP.TOPMOST);
            }

            appliedTopMost = isTop;
        }

        /// <summary>
        /// 鼠标左键按下时，如果配置了 CloseOnMouseLeftButtonDown，则关闭弹窗
        /// </summary>
        protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CloseOnMouseLeftButtonDown)
            {
                IsOpen = false;
            }
        }
        // 宿主窗口引用（可能为空）
        private Window _hostWindow;
        private bool? appliedTopMost;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        static readonly IntPtr HWND_TOP = new IntPtr(0);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        /// <summary>
        /// SetWindowPos options
        /// </summary>
        [Flags]
        internal enum SWP
        {
            ASYNCWINDOWPOS = 0x4000,
            DEFERERASE = 0x2000,
            DRAWFRAME = 0x0020,
            FRAMECHANGED = 0x0020,
            HIDEWINDOW = 0x0080,
            NOACTIVATE = 0x0010,
            NOCOPYBITS = 0x0100,
            NOMOVE = 0x0002,
            NOOWNERZORDER = 0x0200,
            NOREDRAW = 0x0008,
            NOREPOSITION = 0x0200,
            NOSENDCHANGING = 0x0400,
            NOSIZE = 0x0001,
            NOZORDER = 0x0004,
            SHOWWINDOW = 0x0040,
            TOPMOST = SWP.NOACTIVATE | SWP.NOOWNERZORDER | SWP.NOSIZE | SWP.NOMOVE | SWP.NOREDRAW | SWP.NOSENDCHANGING,
        }

        internal static int LOWORD(int i)
        {
            return (short)(i & 0xFFFF);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            private int _left;
            private int _top;
            private int _right;
            private int _bottom;

            public void Offset(int dx, int dy)
            {
                _left += dx;
                _top += dy;
                _right += dx;
                _bottom += dy;
            }

            public int Left
            {
                get => _left;
                set => _left = value;
            }

            public int Right
            {
                get => _right;
                set => _right = value;
            }

            public int Top
            {
                get => _top;
                set => _top = value;
            }

            public int Bottom
            {
                get => _bottom;
                set => _bottom = value;
            }

            public int Width => _right - _left;

            public int Height => _bottom - _top;

            public POINT Position => new POINT { x = _left, y = _top };

            public SIZE Size => new SIZE { cx = Width, cy = Height };

            public static RECT Union(RECT rect1, RECT rect2)
            {
                return new RECT
                {
                    Left = Math.Min(rect1.Left, rect2.Left),
                    Top = Math.Min(rect1.Top, rect2.Top),
                    Right = Math.Max(rect1.Right, rect2.Right),
                    Bottom = Math.Max(rect1.Bottom, rect2.Bottom),
                };
            }

            public override bool Equals(object obj)
            {
                try
                {
                    var rc = (RECT)obj;
                    return rc._bottom == _bottom
                        && rc._left == _left
                        && rc._right == _right
                        && rc._top == _top;
                }
                catch (InvalidCastException)
                {
                    return false;
                }
                catch (NullReferenceException)
                {
                    return false;
                }
            }

            public override int GetHashCode()
                => (_left << 16 | LOWORD(_right)) ^ (_top << 16 | LOWORD(_bottom));
        }

        [SecurityCritical]
        [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [SecurityCritical]
        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool _SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SWP uFlags);

        [SecurityCritical]
        private static bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SWP uFlags)
        {
            if (!_SetWindowPos(hWnd, hWndInsertAfter, x, y, cx, cy, uFlags))
            {
                // If this fails it's never worth taking down the process.  Let the caller deal with the error if they want.
                return false;
            }

            return true;
        }
    }
}
