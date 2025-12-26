using SwiftExport.AppLayer.DTOs;
using System;
using System.Collections.Generic;
using System.Drawing;
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
            AttachEvents(dgv);
            return newConfig;
        }


        private static void AttachEvents(DataGridView dgv)
        {
            // 1. 处理单元格验证逻辑
            dgv.CellValidating += (s, e) =>
            {
                // e.FormattedValue 就是用户还没按下回车、还在编辑框里的那个“新鲜”字符串
                string currentInput = e.FormattedValue?.ToString()?.Trim() ?? string.Empty;

                // 传入当前值进行校验
                dgv.CheckCell(e.RowIndex, e.ColumnIndex, currentInput);
            };
            // 2. 处理编辑时的实时交互逻辑 (如大写转换)
            dgv.EditingControlShowing += (s, e) =>
            {
                var config = GetConfig(dgv);
                if (e.Control is TextBox textBox)
                {
                    // 注意：这里使用 DataPropertyName 进行匹配，确保 DTO 映射一致
                    string currentPropName = dgv.CurrentCell?.OwningColumn?.DataPropertyName;

                    if (!string.IsNullOrEmpty(currentPropName) && config.UcaseColumns.Contains(currentPropName))
                    {
                        textBox.CharacterCasing = CharacterCasing.Upper;
                    }
                    else
                    {
                        textBox.CharacterCasing = CharacterCasing.Normal;
                    }
                }

            };


        }

        // 核心验证引擎：检查单个单元格并设置其 ErrorText
        // 返回值：true 表示合法，false 表示有错
        public static bool CheckCell(this DataGridView dgv, int rowIndex, int colIndex, string proposedValue = null)
        {
            var config = GetConfig(dgv);
            var cell = dgv.Rows[rowIndex].Cells[colIndex];
            string colName = dgv.Columns[colIndex].DataPropertyName;
            if (string.IsNullOrEmpty(colName)) colName = dgv.Columns[colIndex].Name; // 兜底：如果没有绑定，则取 Name
            // 获取值并处理空值情况
            string value = proposedValue ?? cell.Value?.ToString()?.Trim() ?? string.Empty;

            // 1. 必填验证
            if (config.NotNullColumns.Contains(colName) && string.IsNullOrWhiteSpace(value))
            {
                cell.ErrorText = "此项必填";
                return false;
            }

            // 2. 唯一性验证 (表内查重)
            if (config.UniqueColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
            {
                bool isDuplicate = dgv.Rows.Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow && row.Index != rowIndex)
                    .Any(row => row.Cells[colIndex].Value?.ToString()?.Trim() == value);

                if (isDuplicate)
                {
                    cell.ErrorText = $"值 '{value}' 重复";
                    return false;
                }
            }

            // 3. 数字验证 (针对非空的情况)
            if (config.NumericColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
            {
                if (!decimal.TryParse(value, out _))
                {
                    cell.ErrorText = "必须为数字";
                    return false;
                }
            }
            // --- 【新增】4. 日期验证 ---
            if (config.DateColumns.Contains(colName) && !string.IsNullOrWhiteSpace(value))
            {
                // 尝试解析日期，如果解析失败则报错
                if (!DateTime.TryParse(value, out _))
                {
                    cell.ErrorText = "日期格式不正确";
                    return false;
                }
            }
            // 校验通过，清空错误
            cell.ErrorText = string.Empty;
            return true;
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
        /// 注册需要自动转大写的列
        /// </summary>
        public static DataGridView SetAutoUcaseColumn(this DataGridView dgv, params string[] cols)
        {
            var config = GetConfig(dgv);
            foreach (var col in cols) config.UcaseColumns.Add(col);
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
      /// 验证行
      /// </summary>
      /// <param name="dgv"></param>
      /// <param name="rowIndex"></param>
      /// <returns></returns>
        public static bool ValidateRow(this DataGridView dgv, int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgv.RowCount) return true;

            bool isValid = true;
            // 遍历该行的所有列进行检查
            for (int i = 0; i < dgv.ColumnCount; i++)
            {
                if (!dgv.CheckCell(rowIndex, i))
                {
                    isValid = false; // 只要有一列没过，整行就不合法
                }
            }
            return isValid;
        }

        /// <summary>
        /// 验证所有,在Save之前验证
        /// </summary>
        /// <param name="dgv"></param>
        /// <returns></returns>
        public static bool ValidateAll(this DataGridView dgv)
        {
            
            // 强制结束当前的编辑状态，确保值已写入 DataSource
            if (dgv.IsCurrentCellInEditMode)
            {
                dgv.EndEdit();
            }

            bool hasError = false;
            for (int r = 0; r < dgv.RowCount; r++)
            {
                if (dgv.Rows[r].IsNewRow) continue;

                if (!dgv.ValidateRow(r))
                {
                    hasError = true;
                }
            }

            //if (hasError)
            //{
            //    MessageBox.Show("表格中存在输入错误或未填项，请根据红叉提示修正。", "提示",
            //                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            return !hasError;
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
                            if (cell.Value == null)
                            {
                                // 2. 【核心】手动标记选中的行
                                var selectedDtos = dgv.SelectedCells
                                                      .Cast<DataGridViewCell>()
                                                      .Select(c => c.OwningRow.DataBoundItem)
                                                      .Distinct()
                                                      .Cast<BaseDto>();

                                foreach (var dto in selectedDtos)
                                {
                                    if (dto.视图状态 == RowState.正常)
                                        dto.视图状态 = RowState.修改;
                                }
                            }

                            //cell.Value = DBNull.Value; // 或者使用 null，取决于你的数据绑定类型
                            cell.Value = null; // 或者使用 null，取决于你的数据绑定类型
                        }

                    }

                    // 4. 手动触发一次验证，让红叉（如必填项）在删除后立刻出现
                    dgv.EndEdit();
                    dgv.ValidateAll();
                    dgv.Invalidate();
                    e.Handled = true; // 表示该按键已处理，防止触发系统其他默认行为
                }
            };
            return dgv;
        }

        public static DataGridView EnableEnterAsTab(this DataGridView dgv)
        {
            dgv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    // 阻止系统默认的换行行为
                    e.Handled = true;

                    int col = dgv.CurrentCell.ColumnIndex;
                    int row = dgv.CurrentCell.RowIndex;

                    if (col < dgv.ColumnCount - 1)
                    {
                        dgv.CurrentCell = dgv.Rows[row].Cells[col + 1];
                    }
                    else if (row < dgv.RowCount - 1)
                    {
                        dgv.CurrentCell = dgv.Rows[row + 1].Cells[0];
                    }
                }
            };
            return dgv;
        }

        /// <summary>
        /// 为 DGV 绑定右键菜单，并实现“右键即选中”逻辑
        /// </summary>
        public static void BindContextMenu(this DataGridView dgv, ContextMenuStrip menu)
        {
            dgv.ContextMenuStrip = menu;

            // 处理右键点击自动选中行
            dgv.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                {
                    if (!dgv.Rows[e.RowIndex].Selected)
                    {
                        dgv.ClearSelection();
                        dgv.Rows[e.RowIndex].Selected = true;
                    }

                    // 如果绑定了 BindingSource，自动同步 Position
                    if (dgv.DataSource is BindingSource bs)
                    {
                        bs.Position = e.RowIndex;
                    }
                }
            };
        }


    }
}
