using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.Common
{
    public static class IconProvider
    {
        private static readonly ConcurrentDictionary<string, Image> _cache = new ConcurrentDictionary<string, Image>();

        private static readonly string _iconBasePath =
            Path.Combine(AppContext.BaseDirectory, "Assets");

        /// <summary>
        /// 获取图标（不带扩展名）
        /// </summary>
        public static Image GetToolBarIcons(string name, string extednName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // 设计期直接返回 null，避免 VS 设计器炸
            if (IsDesignMode())
                return null;
            string _iconPath = Path.Combine(_iconBasePath, "ToolBarIcons");

            // 假设默认扩展名为 "png"，如需其他扩展名请调整
            return _cache.GetOrAdd(name, key => LoadImage(_iconPath, key, extednName));
        }

        public static Image GetStatusIcons(string name, string extednName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // 设计期直接返回 null，避免 VS 设计器炸
            if (IsDesignMode())
                return null;
            string _iconPath = Path.Combine(_iconBasePath, "StatusIcons");

            return _cache.GetOrAdd(name, key => LoadImage(_iconPath, key, extednName));
        }

        private static Image LoadImage(string _iconPath, string name, string _extednName)
        {
            string path = Path.Combine(_iconPath, name + "." + _extednName);

            if (!File.Exists(path))
            {
                Image _NotFoundImg = new Bitmap(100, 100);
                using (var g = Graphics.FromImage(_NotFoundImg))
                {
                    g.Clear(Color.White); g.DrawString("Nothing", new Font(name = "微软哑黑", 6), Brushes.Red, 15, 15);
                    return _NotFoundImg;
                }
            }


            // 用 FileStream 避免文件锁
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return Image.FromStream(fs);
            }
            ;

        }

        private static bool IsDesignMode()
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
                return true;

            if (Application.OpenForms.Count == 0)
                return true;

            return false;
        }
    }
}
