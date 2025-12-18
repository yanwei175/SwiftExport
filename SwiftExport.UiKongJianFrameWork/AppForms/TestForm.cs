using SwiftExport.AppLayer.DTOs;
using SwiftExport.UiKongJianFrameWork.BaseControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    public partial class TestForm : Form
    {
        public TestForm()
        {
            InitializeComponent();
            LoadData();

        }


        private void LoadData()
        {

            // 创建自定义DataGridView
            var dataGridView = new DataGridView()
            {
                Name = "dataGridView1",
                TabIndex = 0
            };


            InitializeBasicProperties(dataGridView);

            // 创建列
            dataGridView.Columns.Add("ID", "编号");
            dataGridView.Columns.Add("Name", "姓名");
            dataGridView.Columns.Add("Age", "年龄");
            dataGridView.Columns.Add("Department", "部门");

            // 设置列宽
            dataGridView.Columns["ID"].Width = 80;
            dataGridView.Columns["Name"].Width = 120;
            dataGridView.Columns["Age"].Width = 80;
            dataGridView.Columns["Department"].Width = 150;

            // 添加数据
            dataGridView.Rows.Add("001", "张三", "30", "技术部");
            dataGridView.Rows.Add("002", "李四", "28", "市场部");
            dataGridView.Rows.Add("003", "王五", "35", "人事部");
            dataGridView.Rows.Add("004", "赵六", "26", "财务部");

            dataGridView.Dock = DockStyle.Fill;
            this.panel1.Controls.Add(dataGridView);
            
        }


        public Font 全局字体 { get; set; } = new Font("微软雅黑", 8);
        public Font 标题字体 { get; set; } = new Font("微软雅黑", 9, FontStyle.Bold);
        public Color 网格线颜色 { get; set; } = Color.DarkGray;
        public Color 背景色 { get; set; } = Color.White;
        public Color 前景色 { get; set; } = Color.Black;
        public Color 行选中颜色 { get; set; } = Color.Honeydew;
        public Color 标题背景色 { get; set; } = Color.LightSkyBlue;
        public Color 交替背景色 { get; set; } = Color.FromArgb(248, 249, 250);
        public Color 交替前景色 { get; set; } = Color.FromArgb(33, 37, 41);
        private void InitializeBasicProperties(DataGridView dt)
        {
            dt.EnableHeadersVisualStyles = false; //禁用默认的视觉样式以获得更好的控制
            //设置默认行高
            //this.DoubleBuffered = true;// 启用双缓冲，减少闪烁
            dt.BackgroundColor = Color.White; // 设置背景颜色
            //this.VirtualMode = false;//// 启用虚拟模式（大数据量时性能更好）
            dt.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; // 设置列自动填充
            dt.BorderStyle = BorderStyle.FixedSingle;// 设置边框样式
            dt.GridColor = 网格线颜色;
            dt.RowHeadersVisible = true;// 启用行列头
            dt.ColumnHeadersVisible = true;// 启用行列头
            dt.RowHeadersWidth = 80;// 设置行头宽度
            dt.SelectionMode = DataGridViewSelectionMode.CellSelect;// 设置选择模式
            dt.MultiSelect = true;// 允许多选
            dt.AllowUserToAddRows = false;// 不允许添加行
            dt.AllowUserToDeleteRows = false;// 不允许删除行
            dt.AllowUserToResizeColumns = true;// 允许用户调整列宽
            dt.AllowUserToResizeRows = true; // 允许用户调整行高
            dt.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;// 编辑模式
            dt.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;// 启用复制功能

            // 设置默认单元格样式
            dt.DefaultCellStyle.BackColor = 背景色;
            dt.DefaultCellStyle.ForeColor = 前景色;
            dt.DefaultCellStyle.Font = 全局字体;
            dt.DefaultCellStyle.SelectionBackColor = Color.Transparent; // 透明，由行选择样式控制
            dt.DefaultCellStyle.SelectionForeColor = 前景色;
            dt.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dt.DefaultCellStyle.Padding = new Padding(5, 2, 5, 2);
            dt.AlternatingRowsDefaultCellStyle.BackColor = 交替背景色; // 设置交替行颜色（斑马线效果）
            dt.AlternatingRowsDefaultCellStyle.ForeColor = 交替前景色; // 设置交替行颜色（斑马线效果）
            dt.CellBorderStyle = DataGridViewCellBorderStyle.Single;// 设置单元格边框

            // 设置表头样式
            dt.ColumnHeadersDefaultCellStyle.BackColor = 标题背景色; // 浅蓝色背景
            dt.ColumnHeadersDefaultCellStyle.ForeColor = 前景色; // 黑色文字
            dt.ColumnHeadersDefaultCellStyle.Font = 标题字体;
            dt.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dt.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 5, 0, 5);
            dt.ColumnHeadersDefaultCellStyle.SelectionBackColor = 行选中颜色;
            dt.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;// 设置表头单元格边框样式
            dt.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;// 设置表头单元格边框样式

            // 设置行标题样式
            dt.RowHeadersDefaultCellStyle.BackColor = 背景色;
            dt.RowHeadersDefaultCellStyle.ForeColor = 前景色;
            dt.RowHeadersDefaultCellStyle.Font = 全局字体;
            dt.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            // 设置行选中样式
            dt.RowsDefaultCellStyle.SelectionBackColor = 行选中颜色;
            dt.RowsDefaultCellStyle.SelectionForeColor = 前景色;

            // 设置列选中样式（当选择模式为CellSelect时）
            dt.DefaultCellStyle.SelectionBackColor = 行选中颜色;
            dt.DefaultCellStyle.SelectionForeColor = 前景色;

            // 设置当前单元格的边框颜色
            dt.DefaultCellStyle.SelectionBackColor = 行选中颜色;
            dt.DefaultCellStyle.SelectionForeColor = 前景色;

            // 设置行高
            dt.RowTemplate.Height = 60;

            // 设置行最小高度
            dt.RowTemplate.MinimumHeight = 25;

            // 设置行标题显示行号
            dt.RowPostPaint += (sender, e) =>
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




    }
}
