using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.VSTO.UserWindow.Converters // 换成你实际的命名空间
{
    // ★ 职责：把 UI 的 DataGridCellInfo 集合，扒掉外衣，提取出纯洁的数据 Model 集合
    public class SelectedCellsToModelsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IList<DataGridCellInfo> cells)
            {
                // 提取实体，去重（防止同一行选中了多个格子），返回纯数据集合
                return cells.Select(c => c.Item)
                            .Where(i => i != null)
                            .Distinct()
                            .ToList();
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}