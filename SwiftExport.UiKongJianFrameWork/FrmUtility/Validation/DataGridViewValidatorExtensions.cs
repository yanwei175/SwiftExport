using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.FrmUtility
{


    public static class DataGridViewValidatorExtensions
    {// 获取或初始化配置对象
        private static DataGridViewValidationConfig GetConfig(DataGridView dgv)
        {
            if (dgv.Tag is DataGridViewValidationConfig config) return config;
            var newConfig = new DataGridViewValidationConfig();
            dgv.Tag = newConfig;

            // 只需要挂载一次事件
            AttachEvents(dgv, newConfig);
            return newConfig;
        }

        private static void AttachEvents(DataGridView dgv, DataGridViewValidationConfig config)
        {
            dgv.CellValidating += (s, e) =>
            {
                string colName = dgv.Columns[e.ColumnIndex].Name;
                string value = e.FormattedValue?.ToString();
                var cell = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex];

                // 1. 必填验证
                if (config.NotNullColumns.Contains(colName) && string.IsNullOrWhiteSpace(value))
                {
                    cell.ErrorText = "此项必填";
                    return;
                }

                // 2. 数字验证 (如果不为空，则检查格式)
                if (config.NumericColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
                {
                    if (!decimal.TryParse(value, out _))
                    {
                        cell.ErrorText = "请输入有效的数字";
                        return;
                    }
                }

                // 3. 日期验证
                if (config.DateColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
                {
                    if (!DateTime.TryParse(value, out _))
                    {
                        cell.ErrorText = "请输入有效的日期格式";
                        return;
                    }
                }

                // 4. 唯一性验证 (表内查重)
                if (config.UniqueColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
                {
                    // 利用 LINQ 查找当前列是否有重复值（排除掉当前正在编辑的这一行）
                    bool isDuplicate = dgv.Rows.Cast<DataGridViewRow>()
                        .Where(row => !row.IsNewRow && row.Index != e.RowIndex)
                        .Any(row => row.Cells[colName].Value?.ToString()?.Trim() == value);

                    if (isDuplicate)
                    {
                        cell.ErrorText = $"值 '{value}' 已在表中存在，请勿重复输入";
                        return;
                    }
                }

                // 通过所有验证，清空错误
                cell.ErrorText = string.Empty;
            };
        }

        // --- 扩展方法接口 ---
        /// <summary>
        /// 设置不为空的列
        /// </summary>
        /// <param name="dgv"></param>
        /// <param name="cols">
        /// 数组中包含列名
        /// </param>
        /// <returns></returns>
        public static DataGridView SetNotNullColumnsColumns(this DataGridView dgv, params string[] cols)
        {
            var config = GetConfig(dgv);
            foreach (var col in cols) config.NotNullColumns.Add(col);
            return dgv;
        }
        /// <summary>
        /// 设置数字列
        /// </summary>
        /// <param name="dgv"></param>
        /// <param name="cols">
        /// 数组中包含列名
        /// </param>
        /// <returns></returns>
        public static DataGridView SetNumericColumns(this DataGridView dgv, params string[] cols)
        {
            var config = GetConfig(dgv);
            foreach (var col in cols) config.NumericColumns.Add(col);
            return dgv;
        }
        /// <summary>
        /// 设置日期列
        /// </summary>
        /// <param name="dgv"></param>
        /// <param name="cols">
        /// 数组中包含列名
        /// </param>
        /// <returns></returns>
        public static DataGridView SetDateColumns(this DataGridView dgv, params string[] cols)
        {
            var config = GetConfig(dgv);
            foreach (var col in cols) config.DateColumns.Add(col);
            return dgv;
        }
        /// <summary>
        /// 启用自动Trim
        /// </summary>
        /// <param name="dgv"></param>
        /// <returns></returns>
        public static DataGridView EnableAutoTrim(this DataGridView dgv)
        {
            dgv.CellParsing += (s, e) => {
                if (e.Value is string str) { e.Value = str.Trim(); e.ParsingApplied = true; }
            };
            return dgv;
        }

        /// <summary>
        /// 验证所有,在Save之前验证
        /// </summary>
        /// <param name="dgv"></param>
        /// <returns></returns>
        public static (bool IsValid, List<string> Errors) ValidateAll(this DataGridView dgv)
        {
            var errors = new List<string>();
            DataGridViewCell firstErrorCell = null;

            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (!string.IsNullOrEmpty(cell.ErrorText))
                    {
                        errors.Add($"行{row.Index + 1} {dgv.Columns[cell.ColumnIndex].HeaderText}: {cell.ErrorText}");
                        if (firstErrorCell == null) firstErrorCell = cell;
                    }
                }
            }

            if (firstErrorCell != null)
            {
                dgv.CurrentCell = firstErrorCell; // 自动定位到第一个错误
                dgv.BeginEdit(true);             // 开启编辑状态
            }

            return (!errors.Any(), errors);
        }

        /// <summary>
        /// 设置唯一列
        /// </summary>
        /// <param name="dgv"></param>
        /// <param name="cols"></param>
        /// <returns></returns>
        public static DataGridView SetUniqueColumns(this DataGridView dgv, params string[] cols)
        {
            var config = GetConfig(dgv);
            foreach (var col in cols) config.UniqueColumns.Add(col);
            return dgv;
        }

        /// <summary>
        /// /启用删除键
        /// </summary>
        /// <param name="dgv"></param>
        /// <returns></returns>
        public static DataGridView EnableDeleteKey(this DataGridView dgv)
        {
            dgv.KeyDown += (s, e) =>
            {
                // 1. 判断是否按下 Delete 键，且当前没有进入单元格编辑状态
                if (e.KeyCode == Keys.Delete && dgv.IsCurrentCellInEditMode == false)
                {
                    // 2. 如果没有选中的单元格，直接返回
                    if (dgv.SelectedCells.Count == 0) return;

                    // 3. 批量清除（支持跨行跨列的框选）
                    foreach (DataGridViewCell cell in dgv.SelectedCells)
                    {
                        // 排除只读列（比如我们刚才加的行号列）
                        if (!cell.ReadOnly)
                        {
                            //cell.Value = DBNull.Value; // 或者使用 null，取决于你的数据绑定类型
                            cell.Value = null; // 或者使用 null，取决于你的数据绑定类型
                        }
                    }

                    // 4. 手动触发一次验证，让红叉（如必填项）在删除后立刻出现
                    dgv.EndEdit();

                    e.Handled = true; // 表示该按键已处理，防止触发系统其他默认行为
                }
            };
            return dgv;
        }






    }
}
