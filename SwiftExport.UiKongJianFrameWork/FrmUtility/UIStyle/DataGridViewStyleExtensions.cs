using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.FrmUtility.UIStyle
{
    public static class DataGridViewStyleExtensions
        {
        /// <summary>
        /// 启用双缓冲
        /// </summary>
        /// <param name="dgv"></param>
        public static DataGridView EnableDoubleBuffered(this DataGridView dgv)
        {
            // 获取 DataGridView 类型的属性信息
            var dgvType = dgv.GetType();
            var pi = dgvType.GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // 设置为 true
            pi?.SetValue(dgv, true, null);
            return dgv;
        }

        /// <summary>
        /// 应用现代化 UI 样式
        /// </summary>
        public static DataGridView ApplyModernStyle(this DataGridView dgv)
            {
            // 1. 基础设置
                dgv.BackgroundColor = Color.White;
                dgv.BorderStyle = BorderStyle.None;
                dgv.GridColor = Color.FromArgb(230, 230, 230); // 浅灰色网格线
                dgv.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
                dgv.MultiSelect = true;
                dgv.RowHeadersVisible = false; // 隐藏最左边的行头，看起来更像 Web 表格
                dgv.AllowUserToResizeRows = false;
                dgv.EnableHeadersVisualStyles = false; // 必须设为 false 才能自定义列头

                // 2. 列头样式 (Header)
                dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
                dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.LightBlue, 
                    ForeColor = Color.Black,
                    Font = new Font("Microsoft YaHei UI", 8, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    Padding = new Padding(5, 0, 0, 0)
                };
                dgv.ColumnHeadersHeight = 50;
                // 3. 单元格样式 (Rows)
                dgv.DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = Color.Black,
                    Font = new Font("Microsoft YaHei UI", 8),
                    SelectionBackColor = Color.FromArgb(235, 243, 255), // 浅蓝色选中
                    SelectionForeColor = Color.FromArgb(0, 120, 215),
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                };
                dgv.RowTemplate.Height = 50; // 增加行高，不显得拥挤
                dgv.RowTemplate.MinimumHeight = 25; //最小高度

                // 4. 隔行换色（可选）
                dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);

                return dgv; // 支持链式调用
            }


        #region 笔刷重绘行号
        // 预创建并缓存绘图对象，避免在 Paint 事件中频繁创建
        private static readonly StringFormat _centerFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        /// <summary>
        /// 启用自动显示行号
        /// </summary>
        public static DataGridView EnableRowNumbers(this DataGridView dgv)
        {
            dgv.RowHeadersVisible = true;
            dgv.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;

            // 设置一次性样式，而不是在 Paint 里设置
            dgv.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 247);
            dgv.RowHeadersDefaultCellStyle.ForeColor = Color.FromArgb(64, 64, 64);

            dgv.RowPostPaint += (sender, e) =>
            {
                // 性能优化点 1: 仅在行头区域有效时绘制
                if (e.RowIndex < 0) return;

                var grid = (DataGridView)sender;
                string rowIdx = (e.RowIndex + 1).ToString();

                // 性能优化点 2: 严格限制绘制矩形区域
                // 不需要每次 new Rectangle，直接利用 e.RowBounds
                var headerBounds = new Rectangle(
                    e.RowBounds.Left,
                    e.RowBounds.Top,
                    grid.RowHeadersWidth,
                    e.RowBounds.Height);

                // 性能优化点 3: 使用缓存的 _centerFormat 和系统内置画刷
                // SystemBrushes 不会被 GC 回收，速度极快
                e.Graphics.DrawString(rowIdx, grid.Font, SystemBrushes.ControlText, headerBounds, _centerFormat);
            };

            // 3. 自动调整宽度
            dgv.RowsAdded += (s, e) => { UpdateRowHeaderWidth(dgv); };
            dgv.RowsRemoved += (s, e) => { UpdateRowHeaderWidth(dgv); };

            return dgv;
        }

        private static void UpdateRowHeaderWidth(DataGridView dgv)
        {
            // 根据行数动态调整宽度（防止行号被遮挡）
            int rowCount = dgv.RowCount;
            int width = 25;
            if (rowCount >= 10) width = 35;
            if (rowCount >= 100) width = 45;
            if (rowCount >= 1000) width = 60;

            if (dgv.RowHeadersWidth < width)
                dgv.RowHeadersWidth = width;
        }

        #endregion



        #region 专用行号列
        private const string LineNumColName = "ColLineNumber";
        /// <summary>
        /// 添加专用行号列（替代 RowHeader）
        /// </summary>
        public static DataGridView AddLineNumberColumn(this DataGridView dgv, string headerText = "No.", int width = 60)
        {
            // 1. 隐藏原始行头
            dgv.RowHeadersVisible = false;

            // 2. 创建并配置行号列
            if (!dgv.Columns.Contains(LineNumColName))
            {
                var lineCol = new DataGridViewTextBoxColumn
                {
                    Name = LineNumColName,
                    HeaderText = headerText,
                    ReadOnly = true,
                    Resizable = DataGridViewTriState.False,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Frozen = true,
                    // --- 核心优化逻辑：适配 Fill 模式 ---

                    // 1. 设置宽度和最小宽度一致
                    Width = width,
                    MinimumWidth = width,

                    // 2. 将 FillWeight 设为极小值（例如 1）
                    // 在 Fill 模式下，列会按 FillWeight 的比例瓜分剩余空间。
                    // 设置为 1 且配合 MinimumWidth，可以保证它只占用固定像素。
                    FillWeight = 1,

                    // 3. 明确指定 AutoSizeMode 为 None 或 MinimumWidth
                    // 这样它就不会被 Fill 逻辑拉伸
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,

                    // --- 样式部分保持不变 ---
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = DataGridViewContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(248, 248, 248),
                        ForeColor = Color.DimGray,
                        SelectionBackColor = Color.FromArgb(248, 248, 248),
                        SelectionForeColor = Color.DimGray
                    }
                };

                dgv.Columns.Insert(0, lineCol);
            }

            // 3. 核心：处理行号的自动更新
            // 使用 CellFormatting 事件是最高效的，因为它只在显示时计算，不需要手动遍历所有行
            dgv.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex >= 0 && dgv.Columns[e.ColumnIndex].Name == LineNumColName)
                {
                    e.Value = (e.RowIndex + 1).ToString();
                    e.FormattingApplied = true;
                }
            };

            return dgv;
        }
        #endregion 

        public static DataGridView EnableSilentDataError(this DataGridView dgv)
        {
            // 拦截 DataError 事件
            dgv.DataError += (s, e) =>
            {
                // 1. 阻止弹出系统默认的错误对话框
                e.ThrowException = false;

                // 2. 准备友好的提示消息
                string colName = dgv.Columns[e.ColumnIndex].HeaderText;
                string errorMsg = $"[{colName}] 输入的数据格式不正确！\n\n" +
                                  $"详细错误: {e.Exception.Message}\n" +
                                  $"请检查是否在数字列输入了文字，或日期格式有误。";

                // 3. 直接弹窗提醒
                MessageBox.Show(errorMsg, "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // 4. 取消本次编辑，回滚到输入前的值
                dgv.CancelEdit();

                // 5. 标记错误已处理，且不强制用户留在该单元格（因为已经回滚了，数据现在是合法的）
                e.Cancel = false;
            };

            return dgv;
        }



    }
}
