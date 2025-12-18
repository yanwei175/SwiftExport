namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    partial class FrmCustomersManager
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.ToolStripButton_Add = new System.Windows.Forms.ToolStripButton();
            this.ToolStripButton_Save = new System.Windows.Forms.ToolStripButton();
            this.ToolStripButton_Delete = new System.Windows.Forms.ToolStripButton();
            this.ToolStripButton_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.ToolStripComboBox_QueryField = new System.Windows.Forms.ToolStripComboBox();
            this.ToolStripTextBox_QueryResult = new System.Windows.Forms.ToolStripTextBox();
            this.ToolStripButton_Query = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel_Find = new System.Windows.Forms.ToolStripLabel();
            this.ToolStripTextBox_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.customersDtoBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.操作提示 = new System.Windows.Forms.ToolStripLabel();
            this.DataGridView1 = new SwiftExport.UiKongJianFrameWork.BaseControls.BaseDataGridView();
            this.iDDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.evenDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.状态DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.视图状态 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.客户代码DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.客户名称DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.地址DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.联系方式DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.联系人DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.标贴文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.说明书文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.尺寸图文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.线框图文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.条码文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.规格书文件夹DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.是否删除DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.创建时间DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.修改时间DataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataSplitUserControl1 = new SwiftExport.UiKongJianFrameWork.AppUserControls.DataSplitUserControl();
            this.toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.customersDtoBindingSource)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // toolStrip1
            // 
            this.toolStrip1.BackColor = System.Drawing.SystemColors.Window;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripButton_Add,
            this.ToolStripButton_Save,
            this.ToolStripButton_Delete,
            this.ToolStripButton_Refresh,
            this.toolStripSeparator2,
            this.ToolStripComboBox_QueryField,
            this.ToolStripTextBox_QueryResult,
            this.ToolStripButton_Query,
            this.toolStripSeparator1,
            this.toolStripLabel_Find,
            this.ToolStripTextBox_FindResult,
            this.操作提示});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(2717, 66);
            this.toolStrip1.TabIndex = 7;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // ToolStripButton_Add
            // 
            this.ToolStripButton_Add.AutoSize = false;
            this.ToolStripButton_Add.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Add.Name = "ToolStripButton_Add";
            this.ToolStripButton_Add.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Add.Text = "新建";
            this.ToolStripButton_Add.Click += new System.EventHandler(this.ToolStripButton_Add_Click);
            // 
            // ToolStripButton_Save
            // 
            this.ToolStripButton_Save.AutoSize = false;
            this.ToolStripButton_Save.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Save.Name = "ToolStripButton_Save";
            this.ToolStripButton_Save.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Save.Text = "保存";
            this.ToolStripButton_Save.Click += new System.EventHandler(this.ToolStripButton_Save_Click);
            // 
            // ToolStripButton_Delete
            // 
            this.ToolStripButton_Delete.AutoSize = false;
            this.ToolStripButton_Delete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Delete.Name = "ToolStripButton_Delete";
            this.ToolStripButton_Delete.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Delete.Text = "删除";
            this.ToolStripButton_Delete.Click += new System.EventHandler(this.ToolStripButton_Delete_Click);
            // 
            // ToolStripButton_Refresh
            // 
            this.ToolStripButton_Refresh.AutoSize = false;
            this.ToolStripButton_Refresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Refresh.Name = "ToolStripButton_Refresh";
            this.ToolStripButton_Refresh.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Refresh.Text = "刷新";
            this.ToolStripButton_Refresh.Click += new System.EventHandler(this.ToolStripButton_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 66);
            // 
            // ToolStripComboBox_QueryField
            // 
            this.ToolStripComboBox_QueryField.Name = "ToolStripComboBox_QueryField";
            this.ToolStripComboBox_QueryField.Size = new System.Drawing.Size(233, 66);
            this.ToolStripComboBox_QueryField.Text = "--请选择--";
            // 
            // ToolStripTextBox_QueryResult
            // 
            this.ToolStripTextBox_QueryResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ToolStripTextBox_QueryResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.ToolStripTextBox_QueryResult.Name = "ToolStripTextBox_QueryResult";
            this.ToolStripTextBox_QueryResult.Size = new System.Drawing.Size(233, 66);
            // 
            // ToolStripButton_Query
            // 
            this.ToolStripButton_Query.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Query.Name = "ToolStripButton_Query";
            this.ToolStripButton_Query.Size = new System.Drawing.Size(66, 60);
            this.ToolStripButton_Query.Text = "查询";
            this.ToolStripButton_Query.Click += new System.EventHandler(this.ToolStripButton_Query_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 66);
            // 
            // toolStripLabel_Find
            // 
            this.toolStripLabel_Find.Name = "toolStripLabel_Find";
            this.toolStripLabel_Find.Size = new System.Drawing.Size(116, 60);
            this.toolStripLabel_Find.Text = "结果筛选:";
            // 
            // ToolStripTextBox_FindResult
            // 
            this.ToolStripTextBox_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ToolStripTextBox_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.ToolStripTextBox_FindResult.Name = "ToolStripTextBox_FindResult";
            this.ToolStripTextBox_FindResult.Size = new System.Drawing.Size(233, 66);
            this.ToolStripTextBox_FindResult.TextChanged += new System.EventHandler(this.ToolStripTextBox_FindResult_TextChanged);
            // 
            // customersDtoBindingSource
            // 
            this.customersDtoBindingSource.DataSource = typeof(SwiftExport.AppLayer.DTOs.CustomersDto);
            // 
            // 操作提示
            // 
            this.操作提示.ForeColor = System.Drawing.Color.Red;
            this.操作提示.Name = "操作提示";
            this.操作提示.Size = new System.Drawing.Size(0, 60);
            // 
            // DataGridView1
            // 
            this.DataGridView1.AllowUserToAddRows = false;
            this.DataGridView1.AllowUserToDeleteRows = false;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(249)))), ((int)(((byte)(250)))));
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(37)))), ((int)(((byte)(41)))));
            this.DataGridView1.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.DataGridView1.AutoGenerateColumns = false;
            this.DataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.DataGridView1.BackgroundColor = System.Drawing.Color.White;
            this.DataGridView1.ClipboardCopyMode = System.Windows.Forms.DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            this.DataGridView1.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.SkyBlue;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("微软雅黑", 9F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle2.Padding = new System.Windows.Forms.Padding(0, 5, 0, 5);
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.Color.Honeydew;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.DataGridView1.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            this.DataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.iDDataGridViewTextBoxColumn,
            this.evenDataGridViewTextBoxColumn,
            this.状态DataGridViewTextBoxColumn,
            this.视图状态,
            this.客户代码DataGridViewTextBoxColumn,
            this.客户名称DataGridViewTextBoxColumn,
            this.地址DataGridViewTextBoxColumn,
            this.联系方式DataGridViewTextBoxColumn,
            this.联系人DataGridViewTextBoxColumn,
            this.标贴文件夹DataGridViewTextBoxColumn,
            this.说明书文件夹DataGridViewTextBoxColumn,
            this.尺寸图文件夹DataGridViewTextBoxColumn,
            this.线框图文件夹DataGridViewTextBoxColumn,
            this.条码文件夹DataGridViewTextBoxColumn,
            this.规格书文件夹DataGridViewTextBoxColumn,
            this.是否删除DataGridViewTextBoxColumn,
            this.创建时间DataGridViewTextBoxColumn,
            this.修改时间DataGridViewTextBoxColumn});
            this.DataGridView1.DataSource = this.customersDtoBindingSource;
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.White;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("微软雅黑", 8F);
            dataGridViewCellStyle3.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle3.Padding = new System.Windows.Forms.Padding(5, 2, 5, 2);
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.Honeydew;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.DataGridView1.DefaultCellStyle = dataGridViewCellStyle3;
            this.DataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataGridView1.EnableHeadersVisualStyles = false;
            this.DataGridView1.Font = new System.Drawing.Font("微软雅黑", 8F);
            this.DataGridView1.GridColor = System.Drawing.Color.DarkGray;
            this.DataGridView1.Location = new System.Drawing.Point(0, 66);
            this.DataGridView1.Name = "DataGridView1";
            this.DataGridView1.RowHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle4.BackColor = System.Drawing.Color.White;
            dataGridViewCellStyle4.Font = new System.Drawing.Font("微软雅黑", 8F);
            dataGridViewCellStyle4.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle4.SelectionBackColor = System.Drawing.Color.Honeydew;
            dataGridViewCellStyle4.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.DataGridView1.RowHeadersDefaultCellStyle = dataGridViewCellStyle4;
            this.DataGridView1.RowHeadersWidth = 60;
            this.DataGridView1.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            dataGridViewCellStyle5.SelectionBackColor = System.Drawing.Color.Honeydew;
            dataGridViewCellStyle5.SelectionForeColor = System.Drawing.Color.Black;
            this.DataGridView1.RowsDefaultCellStyle = dataGridViewCellStyle5;
            this.DataGridView1.RowTemplate.Height = 60;
            this.DataGridView1.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGridView1.Size = new System.Drawing.Size(2717, 1309);
            this.DataGridView1.TabIndex = 3;
            this.DataGridView1.交替前景色 = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(37)))), ((int)(((byte)(41)))));
            this.DataGridView1.交替背景色 = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(249)))), ((int)(((byte)(250)))));
            this.DataGridView1.全局字体 = new System.Drawing.Font("微软雅黑", 8F);
            this.DataGridView1.前景色 = System.Drawing.Color.Black;
            this.DataGridView1.标题字体 = new System.Drawing.Font("微软雅黑", 9F);
            this.DataGridView1.标题背景色 = System.Drawing.Color.Cyan;
            this.DataGridView1.网格线颜色 = System.Drawing.Color.DarkGray;
            this.DataGridView1.背景色 = System.Drawing.Color.White;
            this.DataGridView1.行选中颜色 = System.Drawing.Color.Honeydew;
            this.DataGridView1.CellBeginEdit += new System.Windows.Forms.DataGridViewCellCancelEventHandler(this.DataGridView1_CellBeginEdit);
            this.DataGridView1.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGridView1_CellEndEdit);
            this.DataGridView1.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGridView1_CellValueChanged);
            this.DataGridView1.DataBindingComplete += new System.Windows.Forms.DataGridViewBindingCompleteEventHandler(this.DataGridView1_DataBindingComplete);
            // 
            // iDDataGridViewTextBoxColumn
            // 
            this.iDDataGridViewTextBoxColumn.DataPropertyName = "ID";
            this.iDDataGridViewTextBoxColumn.HeaderText = "ID";
            this.iDDataGridViewTextBoxColumn.MinimumWidth = 10;
            this.iDDataGridViewTextBoxColumn.Name = "iDDataGridViewTextBoxColumn";
            this.iDDataGridViewTextBoxColumn.ReadOnly = true;
            this.iDDataGridViewTextBoxColumn.Visible = false;
            // 
            // evenDataGridViewTextBoxColumn
            // 
            this.evenDataGridViewTextBoxColumn.DataPropertyName = "Even";
            this.evenDataGridViewTextBoxColumn.HeaderText = "Even";
            this.evenDataGridViewTextBoxColumn.MinimumWidth = 10;
            this.evenDataGridViewTextBoxColumn.Name = "evenDataGridViewTextBoxColumn";
            this.evenDataGridViewTextBoxColumn.ReadOnly = true;
            this.evenDataGridViewTextBoxColumn.Visible = false;
            // 
            // 状态DataGridViewTextBoxColumn
            // 
            this.状态DataGridViewTextBoxColumn.DataPropertyName = "状态";
            this.状态DataGridViewTextBoxColumn.HeaderText = "状态";
            this.状态DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.状态DataGridViewTextBoxColumn.Name = "状态DataGridViewTextBoxColumn";
            this.状态DataGridViewTextBoxColumn.ReadOnly = true;
            this.状态DataGridViewTextBoxColumn.Visible = false;
            // 
            // 视图状态
            // 
            this.视图状态.DataPropertyName = "视图状态";
            this.视图状态.FillWeight = 50F;
            this.视图状态.HeaderText = "操作";
            this.视图状态.MinimumWidth = 10;
            this.视图状态.Name = "视图状态";
            this.视图状态.ReadOnly = true;
            this.视图状态.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // 客户代码DataGridViewTextBoxColumn
            // 
            this.客户代码DataGridViewTextBoxColumn.DataPropertyName = "客户代码";
            this.客户代码DataGridViewTextBoxColumn.HeaderText = "客户代码";
            this.客户代码DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.客户代码DataGridViewTextBoxColumn.Name = "客户代码DataGridViewTextBoxColumn";
            // 
            // 客户名称DataGridViewTextBoxColumn
            // 
            this.客户名称DataGridViewTextBoxColumn.DataPropertyName = "客户名称";
            this.客户名称DataGridViewTextBoxColumn.HeaderText = "客户名称";
            this.客户名称DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.客户名称DataGridViewTextBoxColumn.Name = "客户名称DataGridViewTextBoxColumn";
            // 
            // 地址DataGridViewTextBoxColumn
            // 
            this.地址DataGridViewTextBoxColumn.DataPropertyName = "地址";
            this.地址DataGridViewTextBoxColumn.HeaderText = "地址";
            this.地址DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.地址DataGridViewTextBoxColumn.Name = "地址DataGridViewTextBoxColumn";
            // 
            // 联系方式DataGridViewTextBoxColumn
            // 
            this.联系方式DataGridViewTextBoxColumn.DataPropertyName = "联系方式";
            this.联系方式DataGridViewTextBoxColumn.HeaderText = "联系方式";
            this.联系方式DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.联系方式DataGridViewTextBoxColumn.Name = "联系方式DataGridViewTextBoxColumn";
            // 
            // 联系人DataGridViewTextBoxColumn
            // 
            this.联系人DataGridViewTextBoxColumn.DataPropertyName = "联系人";
            this.联系人DataGridViewTextBoxColumn.HeaderText = "联系人";
            this.联系人DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.联系人DataGridViewTextBoxColumn.Name = "联系人DataGridViewTextBoxColumn";
            // 
            // 标贴文件夹DataGridViewTextBoxColumn
            // 
            this.标贴文件夹DataGridViewTextBoxColumn.DataPropertyName = "标贴文件夹";
            this.标贴文件夹DataGridViewTextBoxColumn.HeaderText = "标贴文件夹";
            this.标贴文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.标贴文件夹DataGridViewTextBoxColumn.Name = "标贴文件夹DataGridViewTextBoxColumn";
            // 
            // 说明书文件夹DataGridViewTextBoxColumn
            // 
            this.说明书文件夹DataGridViewTextBoxColumn.DataPropertyName = "说明书文件夹";
            this.说明书文件夹DataGridViewTextBoxColumn.HeaderText = "说明书文件夹";
            this.说明书文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.说明书文件夹DataGridViewTextBoxColumn.Name = "说明书文件夹DataGridViewTextBoxColumn";
            // 
            // 尺寸图文件夹DataGridViewTextBoxColumn
            // 
            this.尺寸图文件夹DataGridViewTextBoxColumn.DataPropertyName = "尺寸图文件夹";
            this.尺寸图文件夹DataGridViewTextBoxColumn.HeaderText = "尺寸图文件夹";
            this.尺寸图文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.尺寸图文件夹DataGridViewTextBoxColumn.Name = "尺寸图文件夹DataGridViewTextBoxColumn";
            // 
            // 线框图文件夹DataGridViewTextBoxColumn
            // 
            this.线框图文件夹DataGridViewTextBoxColumn.DataPropertyName = "线框图文件夹";
            this.线框图文件夹DataGridViewTextBoxColumn.HeaderText = "线框图文件夹";
            this.线框图文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.线框图文件夹DataGridViewTextBoxColumn.Name = "线框图文件夹DataGridViewTextBoxColumn";
            // 
            // 条码文件夹DataGridViewTextBoxColumn
            // 
            this.条码文件夹DataGridViewTextBoxColumn.DataPropertyName = "条码文件夹";
            this.条码文件夹DataGridViewTextBoxColumn.HeaderText = "条码文件夹";
            this.条码文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.条码文件夹DataGridViewTextBoxColumn.Name = "条码文件夹DataGridViewTextBoxColumn";
            // 
            // 规格书文件夹DataGridViewTextBoxColumn
            // 
            this.规格书文件夹DataGridViewTextBoxColumn.DataPropertyName = "规格书文件夹";
            this.规格书文件夹DataGridViewTextBoxColumn.HeaderText = "规格书文件夹";
            this.规格书文件夹DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.规格书文件夹DataGridViewTextBoxColumn.Name = "规格书文件夹DataGridViewTextBoxColumn";
            // 
            // 是否删除DataGridViewTextBoxColumn
            // 
            this.是否删除DataGridViewTextBoxColumn.DataPropertyName = "是否删除";
            this.是否删除DataGridViewTextBoxColumn.HeaderText = "是否删除";
            this.是否删除DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.是否删除DataGridViewTextBoxColumn.Name = "是否删除DataGridViewTextBoxColumn";
            this.是否删除DataGridViewTextBoxColumn.ReadOnly = true;
            this.是否删除DataGridViewTextBoxColumn.Visible = false;
            // 
            // 创建时间DataGridViewTextBoxColumn
            // 
            this.创建时间DataGridViewTextBoxColumn.DataPropertyName = "创建时间";
            this.创建时间DataGridViewTextBoxColumn.HeaderText = "创建时间";
            this.创建时间DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.创建时间DataGridViewTextBoxColumn.Name = "创建时间DataGridViewTextBoxColumn";
            this.创建时间DataGridViewTextBoxColumn.ReadOnly = true;
            this.创建时间DataGridViewTextBoxColumn.Visible = false;
            // 
            // 修改时间DataGridViewTextBoxColumn
            // 
            this.修改时间DataGridViewTextBoxColumn.DataPropertyName = "修改时间";
            this.修改时间DataGridViewTextBoxColumn.HeaderText = "修改时间";
            this.修改时间DataGridViewTextBoxColumn.MinimumWidth = 10;
            this.修改时间DataGridViewTextBoxColumn.Name = "修改时间DataGridViewTextBoxColumn";
            this.修改时间DataGridViewTextBoxColumn.ReadOnly = true;
            this.修改时间DataGridViewTextBoxColumn.Visible = false;
            // 
            // dataSplitUserControl1
            // 
            this.dataSplitUserControl1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.dataSplitUserControl1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.dataSplitUserControl1.Location = new System.Drawing.Point(0, 1375);
            this.dataSplitUserControl1.Margin = new System.Windows.Forms.Padding(4);
            this.dataSplitUserControl1.Name = "dataSplitUserControl1";
            this.dataSplitUserControl1.PageCount = 0;
            this.dataSplitUserControl1.PageIndex = 0;
            this.dataSplitUserControl1.PageSize = 20;
            this.dataSplitUserControl1.Size = new System.Drawing.Size(2717, 66);
            this.dataSplitUserControl1.TabIndex = 4;
            this.dataSplitUserControl1.TotalRecords = 0;
            // 
            // FrmCustomersManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(2717, 1441);
            this.Controls.Add(this.DataGridView1);
            this.Controls.Add(this.dataSplitUserControl1);
            this.Controls.Add(this.toolStrip1);
            this.Font = new System.Drawing.Font("微软雅黑", 7.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "FrmCustomersManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "客户管理";
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.customersDtoBindingSource)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataGridView1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.BindingSource customersDtoBindingSource;
        private BaseControls.BaseDataGridView DataGridView1;
        private AppUserControls.DataSplitUserControl dataSplitUserControl1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Add;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Save;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Delete;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripComboBox ToolStripComboBox_QueryField;
        private System.Windows.Forms.ToolStripTextBox ToolStripTextBox_QueryResult;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Query;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel toolStripLabel_Find;
        private System.Windows.Forms.ToolStripTextBox ToolStripTextBox_FindResult;
        private System.Windows.Forms.DataGridViewTextBoxColumn iDDataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn evenDataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 状态DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 视图状态;
        private System.Windows.Forms.DataGridViewTextBoxColumn 客户代码DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 客户名称DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 地址DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 联系方式DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 联系人DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 标贴文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 说明书文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 尺寸图文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 线框图文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 条码文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 规格书文件夹DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 是否删除DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 创建时间DataGridViewTextBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn 修改时间DataGridViewTextBoxColumn;
        private System.Windows.Forms.ToolStripLabel 操作提示;
    }
}