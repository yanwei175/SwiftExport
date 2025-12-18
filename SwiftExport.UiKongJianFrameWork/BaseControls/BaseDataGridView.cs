using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.BaseControls
{
    [ToolboxItem(true)]
    public class BaseDataGridView : DataGridView
    {

        public BaseDataGridView()
        {
            // 设置基本属性
            InitializeBasicProperties();
            // 禁用字体继承
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, false);
            this.Font=全局字体;
        }

        public Font 全局字体 { get; set; } = new Font("微软雅黑", 8);
        public Font 标题字体 { get; set; } = new Font("微软雅黑", 9);
        public Color 网格线颜色 { get; set; } = Color.DarkGray; 
        public Color 背景色 { get; set; } = Color.White;
        public Color 前景色 { get; set; } = Color.Black;
        public Color 行选中颜色 { get; set; } = Color.Honeydew;
        public Color 标题背景色 { get; set; } = Color.LightSkyBlue;
        public Color 交替背景色 { get; set; } = Color.FromArgb(248, 249, 250);
        public Color 交替前景色 { get; set; } = Color.FromArgb(33, 37, 41);
        private void InitializeBasicProperties()
        {
            this.EnableHeadersVisualStyles = false; //禁用默认的视觉样式以获得更好的控制

            this.DoubleBuffered = true;// 启用双缓冲，减少闪烁
            this.BackgroundColor = Color.White; // 设置背景颜色
            //this.VirtualMode = false;//// 启用虚拟模式（大数据量时性能更好）
            this.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; // 设置列自动填充
            this.BorderStyle = BorderStyle.FixedSingle;// 设置边框样式
            this.GridColor = 网格线颜色;
            this.RowHeadersVisible = true;// 启用行列头
            this.ColumnHeadersVisible = true;
            this.RowHeadersWidth = 80;// 设置行头宽度
            this.SelectionMode = DataGridViewSelectionMode.CellSelect;// 设置选择模式
            this.MultiSelect = true;// 允许多选
            this.AllowUserToAddRows = false;// 不允许添加行
            this.AllowUserToDeleteRows = false;// 不允许删除行
            this.AllowUserToResizeColumns = true;// 允许用户调整列宽
            this.AllowUserToResizeRows = true; // 允许用户调整行高
            this.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;// 编辑模式
            this.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;// 启用复制功能

            // 设置默认单元格样式
            this.DefaultCellStyle.BackColor = 背景色;
            this.DefaultCellStyle.ForeColor = 前景色;
            this.DefaultCellStyle.Font = 全局字体;
            this.DefaultCellStyle.SelectionBackColor = Color.Transparent; // 透明，由行选择样式控制
            this.DefaultCellStyle.SelectionForeColor = 前景色;
            this.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            this.DefaultCellStyle.Padding = new Padding(5, 2, 5, 2);
            this.AlternatingRowsDefaultCellStyle.BackColor = 交替背景色; // 设置交替行颜色（斑马线效果）
            this.AlternatingRowsDefaultCellStyle.ForeColor = 交替前景色; // 设置交替行颜色（斑马线效果）
            this.CellBorderStyle = DataGridViewCellBorderStyle.Single;// 设置单元格边框


            // 设置表头样式
            this.ColumnHeadersDefaultCellStyle.BackColor = 标题背景色; // 浅蓝色背景
            this.ColumnHeadersDefaultCellStyle.ForeColor = 前景色; // 黑色文字
            this.ColumnHeadersDefaultCellStyle.Font = 标题字体;
            this.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 5, 0, 5);
            this.ColumnHeadersDefaultCellStyle.SelectionBackColor = 行选中颜色;
            this.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;// 设置表头单元格边框样式
            this.ColumnHeadersHeight = 60;
            this.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;


            // 设置行标题样式
            this.RowHeadersDefaultCellStyle.BackColor = 背景色;
            this.RowHeadersDefaultCellStyle.ForeColor = 前景色;
            this.RowHeadersDefaultCellStyle.Font = 全局字体;
            this.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;// 设置表头单元格边框样式

            // 设置行选中样式
            this.RowsDefaultCellStyle.SelectionBackColor = 行选中颜色;
            this.RowsDefaultCellStyle.SelectionForeColor = 前景色;

            // 设置列选中样式（当选择模式为CellSelect时）
            this.DefaultCellStyle.SelectionBackColor = 行选中颜色;
            this.DefaultCellStyle.SelectionForeColor = 前景色;

            // 设置当前单元格的边框颜色
            this.DefaultCellStyle.SelectionBackColor = 行选中颜色;
            this.DefaultCellStyle.SelectionForeColor = 前景色;

            // 设置行高
            this.RowTemplate.Height = 60;

            // 设置行最小高度
            this.RowTemplate.MinimumHeight = 25;

            // 设置行标题显示行号
            this.RowPostPaint += (sender, e) =>
            {
                if (sender is DataGridView grid && grid.RowHeadersVisible)
                {
                    var rowIdx = (e.RowIndex + 1).ToString();
                    var centerFormat = new StringFormat()
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    var headerBounds = new Rectangle(
                        e.RowBounds.Left, e.RowBounds.Top,
                        grid.RowHeadersWidth, e.RowBounds.Height);

                    e.Graphics.DrawString(rowIdx, grid.Font,
                        Brushes.Black, headerBounds, centerFormat);
                }
            };

        }

        // 可选：添加自定义方法
        public void ApplyTheme(Color headerBackColor, Color headerForeColor,
                              Color cellBackColor, Color cellForeColor)
        {
            // 表头颜色
            this.ColumnHeadersDefaultCellStyle.BackColor = headerBackColor;
            this.ColumnHeadersDefaultCellStyle.ForeColor = headerForeColor;

            // 单元格颜色
            this.DefaultCellStyle.BackColor = cellBackColor;
            this.DefaultCellStyle.ForeColor = cellForeColor;
            this.AlternatingRowsDefaultCellStyle.BackColor =
                Color.FromArgb(
                    Math.Min(cellBackColor.R + 10, 255),
                    Math.Min(cellBackColor.G + 10, 255),
                    Math.Min(cellBackColor.B + 10, 255)
                );
        }





    }
}
