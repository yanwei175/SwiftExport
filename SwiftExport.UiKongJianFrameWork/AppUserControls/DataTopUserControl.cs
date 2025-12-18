using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    public partial class DataTopUserControl : UserControl
    {
        // 字段列表
        private List<string> ZiDuanLieBiao { get; set;}
        // 新建
        public event EventHandler AddClicked;
        // 保存
        public event EventHandler SaveClicked;
        // 删除
        public event EventHandler DeleteClicked;
        // 刷新
        public event EventHandler RefreshClicked;
        // 查询
        public event EventHandler<QueryEventArgs> QueryClicked;
        //过滤
        public event EventHandler<FilterEventArgs> FilterChanged;
        public DataTopUserControl()
        {
            InitializeComponent();
            // 绑定按钮事件
            ToolStripButton_Add.Click += (s, e) => AddClicked?.Invoke(this, EventArgs.Empty);
            ToolStripButton_Save.Click += (s, e) => SaveClicked?.Invoke(this, EventArgs.Empty);
            ToolStripButton_Delete.Click += (s, e) => DeleteClicked?.Invoke(this, EventArgs.Empty);
            ToolStripButton_Refresh.Click += (s, e) => RefreshClicked?.Invoke(this, EventArgs.Empty);

            ToolStripButton_Query.Click += ToolStripButton_Query_Click;

            ToolStripComboBox_QueryField.DropDownStyle = ComboBoxStyle.DropDownList;
            // 绑定筛选框事件
            toolStripTextBox_FindResult.TextChanged += ToolStripTextBox_FindResult_TextChanged;
        }

        public void BindZiDuanLieBiao(List<string> ziDuanLieBiao)
        {
            ZiDuanLieBiao = ziDuanLieBiao;
            // 绑定字段列表
            ToolStripComboBox_QueryField.Items.Clear();
            ToolStripComboBox_QueryField.Items.AddRange(ziDuanLieBiao.ToArray());
        }

        private void ToolStripButton_Query_Click(object sender, EventArgs e)
        {
            var args = new QueryEventArgs
            {
                Field = ToolStripComboBox_QueryField.Text,
                Value = ToolStripTextBox_QueryResult.Text
            };
            QueryClicked?.Invoke(this, args);
        }

        private void ToolStripTextBox_FindResult_TextChanged(object sender, EventArgs e)
        {
            var args = new FilterEventArgs
            {
                FilterText = toolStripTextBox_FindResult.Text
            };
            FilterChanged?.Invoke(this, args);
        }


    }

    public class QueryEventArgs : EventArgs
    {
        public string Field { get; set; }   // 查询字段
        public string Value { get; set; }   // 查询值
    }
    // 筛选事件参数
    public class FilterEventArgs : EventArgs
    {
        public string FilterText { get; set; }
    }
}
