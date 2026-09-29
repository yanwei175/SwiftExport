using System;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>不可变的侧边窗格配置；注册只描述页面，不创建 UI 或触发网络加载。</summary>
    public sealed class TaskPaneConfig
    {
        /// <summary>稳定页面标识，用于同一 Excel 窗口内复用。</summary>
        public string Id { get; }
        /// <summary>Office 窗格标题。</summary>
        public string Title { get; }
        /// <summary>从页面 Scope 解析的 WPF 内容类型。</summary>
        public Type ViewType { get; }
        /// <summary>首次创建宽度，之后保留用户调整。</summary>
        public int DefaultWidth { get; }
        /// <summary>首次停靠侧边；当前只支持左侧或右侧。</summary>
        public Dock DockPosition { get; }
        /// <summary>验证注册参数，不实例化页面；水平窗格需另行定义高度策略。</summary>
        public TaskPaneConfig(string id, string title, Type viewType, int defaultWidth, Dock dockPosition = Dock.Right)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("页面标识不能为空", nameof(id));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("标题不能为空", nameof(title));
            if (viewType == null || !typeof(UIElement).IsAssignableFrom(viewType) || viewType.IsAbstract)
                throw new ArgumentException("页面必须是可实例化的 WPF 元素", nameof(viewType));
            if (defaultWidth <= 0) throw new ArgumentOutOfRangeException(nameof(defaultWidth));
            if (dockPosition != Dock.Left && dockPosition != Dock.Right)
                throw new ArgumentOutOfRangeException(nameof(dockPosition), "侧边窗格仅支持左侧或右侧");
            Id = id; Title = title; ViewType = viewType; DefaultWidth = defaultWidth; DockPosition = dockPosition;
        }
    }
}
