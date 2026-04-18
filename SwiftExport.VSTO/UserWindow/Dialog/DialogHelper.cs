using Microsoft.WindowsAPICodePack.Dialogs;

namespace SwiftExport.VSTO.UserWindow.Dialog
{
    public static class DialogHelper
    {
        /// <summary>
        /// 弹出文件夹选择框
        /// </summary>
        /// <param name="title">弹窗标题</param>
        /// <param name="initialDirectory">初始目录(可选)</param>
        /// <returns>返回选择的路径，如果取消则返回 null</returns>
        public static string PickFolder(string title = "请选择文件夹", string initialDirectory = "")
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true; // 🔥 关键模式
                dialog.Title = title;

                // 如果没传路径，默认打开“我的文档”或者 C盘
                if (!string.IsNullOrEmpty(initialDirectory))
                {
                    dialog.InitialDirectory = initialDirectory;
                }

                // 标准配置
                dialog.AddToMostRecentlyUsedList = false;
                dialog.AllowNonFileSystemItems = false;
                dialog.EnsurePathExists = true;
                dialog.EnsureReadOnly = false;
                dialog.EnsureValidNames = true;
                dialog.Multiselect = false;
                dialog.ShowPlacesList = true;

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    return dialog.FileName;
                }
            }
            return null; // 用户点了取消
        }

        /// <summary>
        /// 弹出文件选择框
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="filter">过滤器，例如 "*.xlsx;*.xls"</param>
        /// <returns>返回文件路径，取消返回 null</returns>
        public static string PickFile(string title = "请选择文件", string filter = "*.*")
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = false; // 文件模式
                dialog.Title = title;
                dialog.EnsureFileExists = true;

                // 解析过滤器 (简单处理)
                if (!string.IsNullOrEmpty(filter) && filter != "*.*")
                {
                    // WindowsAPICodePack 的过滤器添加方式稍微有点不同
                    // 这里简单处理，如果需要复杂过滤可以扩展
                    dialog.Filters.Add(new CommonFileDialogFilter("Excel 文件", filter));
                    dialog.Filters.Add(new CommonFileDialogFilter("所有文件", "*.*"));
                }

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    return dialog.FileName;
                }
            }
            return null;
        }
    }
}