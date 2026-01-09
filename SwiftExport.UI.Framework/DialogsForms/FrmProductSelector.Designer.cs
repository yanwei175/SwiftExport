namespace SwiftExport.UI.Framework.DialogsForms
{
    partial class FrmProductSelector
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            this.Dgv = new System.Windows.Forms.DataGridView();
            this.SelectedDgv = new System.Windows.Forms.DataGridView();
            this.Btn_ShiftAll = new System.Windows.Forms.Button();
            this.Btn_ShiftSelect = new System.Windows.Forms.Button();
            this.Btn_RemoveSelect = new System.Windows.Forms.Button();
            this.Btn_RemoveAll = new System.Windows.Forms.Button();
            this.CmbLimitCustomer = new System.Windows.Forms.ComboBox();
            this.TxtSearch = new System.Windows.Forms.TextBox();
            this.TableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.Panel5 = new System.Windows.Forms.Panel();
            this.Panel4 = new System.Windows.Forms.Panel();
            this.Panel1 = new System.Windows.Forms.Panel();
            this.Panel2 = new System.Windows.Forms.Panel();
            this.ToolStrip1 = new System.Windows.Forms.ToolStrip();
            this.LblStatue = new System.Windows.Forms.ToolStripLabel();
            this.BtnCancel = new System.Windows.Forms.ToolStripButton();
            this.BtnOK = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Panel3 = new System.Windows.Forms.Panel();
            this.TxtBatchSearch = new System.Windows.Forms.TextBox();
            this.图片 = new System.Windows.Forms.DataGridViewImageColumn();
            this.产品型号 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.描述 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.颜色 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.客户代码 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.客户型号 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.配置 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.主图地址 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选图片 = new System.Windows.Forms.DataGridViewImageColumn();
            this.已选产品型号 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选描述 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选颜色 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选客户代码 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选客户型号 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选配置 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.已选主图地址 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.Dgv)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SelectedDgv)).BeginInit();
            this.TableLayoutPanel1.SuspendLayout();
            this.Panel5.SuspendLayout();
            this.Panel4.SuspendLayout();
            this.Panel1.SuspendLayout();
            this.Panel2.SuspendLayout();
            this.ToolStrip1.SuspendLayout();
            this.Panel3.SuspendLayout();
            this.SuspendLayout();
            // 
            // Dgv
            // 
            this.Dgv.AllowUserToAddRows = false;
            this.Dgv.AllowUserToDeleteRows = false;
            this.Dgv.BackgroundColor = System.Drawing.SystemColors.Window;
            this.Dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.Dgv.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.图片,
            this.产品型号,
            this.描述,
            this.颜色,
            this.客户代码,
            this.客户型号,
            this.配置,
            this.主图地址});
            this.Dgv.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Dgv.Location = new System.Drawing.Point(0, 0);
            this.Dgv.Margin = new System.Windows.Forms.Padding(0);
            this.Dgv.Name = "Dgv";
            this.Dgv.ReadOnly = true;
            this.Dgv.RowHeadersVisible = false;
            this.Dgv.RowHeadersWidth = 82;
            this.Dgv.RowTemplate.Height = 37;
            this.Dgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.Dgv.Size = new System.Drawing.Size(1373, 1036);
            this.Dgv.TabIndex = 11;
            this.Dgv.VirtualMode = true;
            this.Dgv.ColumnHeaderMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.Dgv_ColumnHeaderMouseClick);
            // 
            // SelectedDgv
            // 
            this.SelectedDgv.AllowUserToAddRows = false;
            this.SelectedDgv.AllowUserToDeleteRows = false;
            this.SelectedDgv.BackgroundColor = System.Drawing.SystemColors.Window;
            this.SelectedDgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.SelectedDgv.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.已选图片,
            this.已选产品型号,
            this.已选描述,
            this.已选颜色,
            this.已选客户代码,
            this.已选客户型号,
            this.已选配置,
            this.已选主图地址});
            this.SelectedDgv.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SelectedDgv.Location = new System.Drawing.Point(0, 0);
            this.SelectedDgv.Margin = new System.Windows.Forms.Padding(0);
            this.SelectedDgv.Name = "SelectedDgv";
            this.SelectedDgv.ReadOnly = true;
            this.SelectedDgv.RowHeadersVisible = false;
            this.SelectedDgv.RowHeadersWidth = 82;
            this.SelectedDgv.RowTemplate.Height = 37;
            this.SelectedDgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.SelectedDgv.Size = new System.Drawing.Size(1373, 1036);
            this.SelectedDgv.TabIndex = 11;
            // 
            // Btn_ShiftAll
            // 
            this.Btn_ShiftAll.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Btn_ShiftAll.Location = new System.Drawing.Point(16, 119);
            this.Btn_ShiftAll.Name = "Btn_ShiftAll";
            this.Btn_ShiftAll.Size = new System.Drawing.Size(166, 63);
            this.Btn_ShiftAll.TabIndex = 12;
            this.Btn_ShiftAll.Text = "> 全选";
            this.Btn_ShiftAll.UseVisualStyleBackColor = true;
            // 
            // Btn_ShiftSelect
            // 
            this.Btn_ShiftSelect.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Btn_ShiftSelect.Location = new System.Drawing.Point(16, 311);
            this.Btn_ShiftSelect.Name = "Btn_ShiftSelect";
            this.Btn_ShiftSelect.Size = new System.Drawing.Size(166, 63);
            this.Btn_ShiftSelect.TabIndex = 12;
            this.Btn_ShiftSelect.Text = "> 选择";
            this.Btn_ShiftSelect.UseVisualStyleBackColor = true;
            // 
            // Btn_RemoveSelect
            // 
            this.Btn_RemoveSelect.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Btn_RemoveSelect.Location = new System.Drawing.Point(16, 215);
            this.Btn_RemoveSelect.Name = "Btn_RemoveSelect";
            this.Btn_RemoveSelect.Size = new System.Drawing.Size(166, 63);
            this.Btn_RemoveSelect.TabIndex = 12;
            this.Btn_RemoveSelect.Text = "< 移除";
            this.Btn_RemoveSelect.UseVisualStyleBackColor = true;
            // 
            // Btn_RemoveAll
            // 
            this.Btn_RemoveAll.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Btn_RemoveAll.Location = new System.Drawing.Point(16, 407);
            this.Btn_RemoveAll.Name = "Btn_RemoveAll";
            this.Btn_RemoveAll.Size = new System.Drawing.Size(166, 63);
            this.Btn_RemoveAll.TabIndex = 12;
            this.Btn_RemoveAll.Text = "< 清空";
            this.Btn_RemoveAll.UseVisualStyleBackColor = true;
            // 
            // CmbLimitCustomer
            // 
            this.CmbLimitCustomer.FormattingEnabled = true;
            this.CmbLimitCustomer.Location = new System.Drawing.Point(12, 24);
            this.CmbLimitCustomer.Name = "CmbLimitCustomer";
            this.CmbLimitCustomer.Size = new System.Drawing.Size(295, 36);
            this.CmbLimitCustomer.TabIndex = 13;
            // 
            // TxtSearch
            // 
            this.TxtSearch.Location = new System.Drawing.Point(12, 82);
            this.TxtSearch.Name = "TxtSearch";
            this.TxtSearch.Size = new System.Drawing.Size(462, 35);
            this.TxtSearch.TabIndex = 14;
            // 
            // TableLayoutPanel1
            // 
            this.TableLayoutPanel1.ColumnCount = 3;
            this.TableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.TableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 200F));
            this.TableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.TableLayoutPanel1.Controls.Add(this.Panel5, 2, 0);
            this.TableLayoutPanel1.Controls.Add(this.Panel4, 0, 0);
            this.TableLayoutPanel1.Controls.Add(this.Panel1, 1, 0);
            this.TableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TableLayoutPanel1.Location = new System.Drawing.Point(0, 137);
            this.TableLayoutPanel1.Name = "TableLayoutPanel1";
            this.TableLayoutPanel1.Padding = new System.Windows.Forms.Padding(5, 1, 1, 1);
            this.TableLayoutPanel1.RowCount = 1;
            this.TableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.TableLayoutPanel1.Size = new System.Drawing.Size(2968, 1046);
            this.TableLayoutPanel1.TabIndex = 15;
            // 
            // Panel5
            // 
            this.Panel5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Panel5.Controls.Add(this.SelectedDgv);
            this.Panel5.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Panel5.Location = new System.Drawing.Point(1589, 4);
            this.Panel5.Name = "Panel5";
            this.Panel5.Size = new System.Drawing.Size(1375, 1038);
            this.Panel5.TabIndex = 15;
            // 
            // Panel4
            // 
            this.Panel4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Panel4.Controls.Add(this.Dgv);
            this.Panel4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Panel4.Location = new System.Drawing.Point(8, 4);
            this.Panel4.Name = "Panel4";
            this.Panel4.Size = new System.Drawing.Size(1375, 1038);
            this.Panel4.TabIndex = 15;
            // 
            // Panel1
            // 
            this.Panel1.Controls.Add(this.Btn_ShiftAll);
            this.Panel1.Controls.Add(this.Btn_RemoveAll);
            this.Panel1.Controls.Add(this.Btn_ShiftSelect);
            this.Panel1.Controls.Add(this.Btn_RemoveSelect);
            this.Panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Panel1.Location = new System.Drawing.Point(1389, 4);
            this.Panel1.Name = "Panel1";
            this.Panel1.Size = new System.Drawing.Size(194, 1038);
            this.Panel1.TabIndex = 16;
            // 
            // Panel2
            // 
            this.Panel2.Controls.Add(this.ToolStrip1);
            this.Panel2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.Panel2.Location = new System.Drawing.Point(0, 1183);
            this.Panel2.Name = "Panel2";
            this.Panel2.Size = new System.Drawing.Size(2968, 80);
            this.Panel2.TabIndex = 16;
            // 
            // ToolStrip1
            // 
            this.ToolStrip1.AutoSize = false;
            this.ToolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.ToolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.LblStatue,
            this.BtnCancel,
            this.BtnOK,
            this.toolStripSeparator1});
            this.ToolStrip1.Location = new System.Drawing.Point(0, 0);
            this.ToolStrip1.Name = "ToolStrip1";
            this.ToolStrip1.Size = new System.Drawing.Size(2968, 60);
            this.ToolStrip1.TabIndex = 1;
            this.ToolStrip1.Text = "toolStrip1";
            // 
            // LblStatue
            // 
            this.LblStatue.Name = "LblStatue";
            this.LblStatue.Size = new System.Drawing.Size(66, 54);
            this.LblStatue.Text = "Tips:";
            // 
            // BtnCancel
            // 
            this.BtnCancel.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.BtnCancel.AutoSize = false;
            this.BtnCancel.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.BtnCancel.Name = "BtnCancel";
            this.BtnCancel.Size = new System.Drawing.Size(98, 60);
            this.BtnCancel.Text = "退出";
            // 
            // BtnOK
            // 
            this.BtnOK.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.BtnOK.AutoSize = false;
            this.BtnOK.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.BtnOK.Name = "BtnOK";
            this.BtnOK.Size = new System.Drawing.Size(98, 60);
            this.BtnOK.Text = "确认";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 60);
            // 
            // Panel3
            // 
            this.Panel3.Controls.Add(this.TxtBatchSearch);
            this.Panel3.Controls.Add(this.CmbLimitCustomer);
            this.Panel3.Controls.Add(this.TxtSearch);
            this.Panel3.Dock = System.Windows.Forms.DockStyle.Top;
            this.Panel3.Location = new System.Drawing.Point(0, 0);
            this.Panel3.Name = "Panel3";
            this.Panel3.Size = new System.Drawing.Size(2968, 137);
            this.Panel3.TabIndex = 17;
            // 
            // TxtBatchSearch
            // 
            this.TxtBatchSearch.Location = new System.Drawing.Point(537, 12);
            this.TxtBatchSearch.Multiline = true;
            this.TxtBatchSearch.Name = "TxtBatchSearch";
            this.TxtBatchSearch.Size = new System.Drawing.Size(846, 105);
            this.TxtBatchSearch.TabIndex = 15;
            // 
            // 图片
            // 
            this.图片.Frozen = true;
            this.图片.HeaderText = "图片";
            this.图片.ImageLayout = System.Windows.Forms.DataGridViewImageCellLayout.Zoom;
            this.图片.MinimumWidth = 10;
            this.图片.Name = "图片";
            this.图片.ReadOnly = true;
            this.图片.Width = 120;
            // 
            // 产品型号
            // 
            this.产品型号.DataPropertyName = "SKU";
            this.产品型号.Frozen = true;
            this.产品型号.HeaderText = "产品型号";
            this.产品型号.MinimumWidth = 10;
            this.产品型号.Name = "产品型号";
            this.产品型号.ReadOnly = true;
            this.产品型号.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.产品型号.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.产品型号.Width = 250;
            // 
            // 描述
            // 
            this.描述.DataPropertyName = "描述";
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.描述.DefaultCellStyle = dataGridViewCellStyle1;
            this.描述.HeaderText = "描述";
            this.描述.MinimumWidth = 10;
            this.描述.Name = "描述";
            this.描述.ReadOnly = true;
            this.描述.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.描述.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.描述.Width = 250;
            // 
            // 颜色
            // 
            this.颜色.DataPropertyName = "颜色";
            this.颜色.HeaderText = "颜色";
            this.颜色.MinimumWidth = 10;
            this.颜色.Name = "颜色";
            this.颜色.ReadOnly = true;
            this.颜色.Width = 150;
            // 
            // 客户代码
            // 
            this.客户代码.DataPropertyName = "客户代码";
            this.客户代码.HeaderText = "客户代码";
            this.客户代码.MinimumWidth = 10;
            this.客户代码.Name = "客户代码";
            this.客户代码.ReadOnly = true;
            this.客户代码.Width = 150;
            // 
            // 客户型号
            // 
            this.客户型号.DataPropertyName = "客户SKU";
            this.客户型号.HeaderText = "客户型号";
            this.客户型号.MinimumWidth = 10;
            this.客户型号.Name = "客户型号";
            this.客户型号.ReadOnly = true;
            this.客户型号.Width = 200;
            // 
            // 配置
            // 
            this.配置.DataPropertyName = "配置";
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.配置.DefaultCellStyle = dataGridViewCellStyle2;
            this.配置.HeaderText = "配置";
            this.配置.MinimumWidth = 10;
            this.配置.Name = "配置";
            this.配置.ReadOnly = true;
            this.配置.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.配置.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.配置.Width = 250;
            // 
            // 主图地址
            // 
            this.主图地址.DataPropertyName = "主图";
            this.主图地址.HeaderText = "主图地址";
            this.主图地址.MinimumWidth = 10;
            this.主图地址.Name = "主图地址";
            this.主图地址.ReadOnly = true;
            this.主图地址.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.主图地址.Visible = false;
            this.主图地址.Width = 200;
            // 
            // 已选图片
            // 
            this.已选图片.Frozen = true;
            this.已选图片.HeaderText = "图片";
            this.已选图片.ImageLayout = System.Windows.Forms.DataGridViewImageCellLayout.Zoom;
            this.已选图片.MinimumWidth = 10;
            this.已选图片.Name = "已选图片";
            this.已选图片.ReadOnly = true;
            this.已选图片.Width = 70;
            // 
            // 已选产品型号
            // 
            this.已选产品型号.DataPropertyName = "SKU";
            this.已选产品型号.Frozen = true;
            this.已选产品型号.HeaderText = "产品型号";
            this.已选产品型号.MinimumWidth = 10;
            this.已选产品型号.Name = "已选产品型号";
            this.已选产品型号.ReadOnly = true;
            this.已选产品型号.Width = 250;
            // 
            // 已选描述
            // 
            this.已选描述.DataPropertyName = "描述";
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.已选描述.DefaultCellStyle = dataGridViewCellStyle3;
            this.已选描述.HeaderText = "描述";
            this.已选描述.MinimumWidth = 10;
            this.已选描述.Name = "已选描述";
            this.已选描述.ReadOnly = true;
            this.已选描述.Width = 250;
            // 
            // 已选颜色
            // 
            this.已选颜色.DataPropertyName = "颜色";
            this.已选颜色.HeaderText = "颜色";
            this.已选颜色.MinimumWidth = 10;
            this.已选颜色.Name = "已选颜色";
            this.已选颜色.ReadOnly = true;
            this.已选颜色.Width = 200;
            // 
            // 已选客户代码
            // 
            this.已选客户代码.DataPropertyName = "客户代码";
            this.已选客户代码.HeaderText = "客户代码";
            this.已选客户代码.MinimumWidth = 10;
            this.已选客户代码.Name = "已选客户代码";
            this.已选客户代码.ReadOnly = true;
            this.已选客户代码.Width = 200;
            // 
            // 已选客户型号
            // 
            this.已选客户型号.DataPropertyName = "客户SKU";
            this.已选客户型号.HeaderText = "客户型号";
            this.已选客户型号.MinimumWidth = 10;
            this.已选客户型号.Name = "已选客户型号";
            this.已选客户型号.ReadOnly = true;
            this.已选客户型号.Width = 200;
            // 
            // 已选配置
            // 
            this.已选配置.DataPropertyName = "配置";
            dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.已选配置.DefaultCellStyle = dataGridViewCellStyle4;
            this.已选配置.HeaderText = "配置";
            this.已选配置.MinimumWidth = 10;
            this.已选配置.Name = "已选配置";
            this.已选配置.ReadOnly = true;
            this.已选配置.Width = 200;
            // 
            // 已选主图地址
            // 
            this.已选主图地址.DataPropertyName = "主图";
            this.已选主图地址.HeaderText = "主图地址";
            this.已选主图地址.MinimumWidth = 10;
            this.已选主图地址.Name = "已选主图地址";
            this.已选主图地址.ReadOnly = true;
            this.已选主图地址.Visible = false;
            this.已选主图地址.Width = 200;
            // 
            // FrmProductSelector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(2968, 1263);
            this.Controls.Add(this.TableLayoutPanel1);
            this.Controls.Add(this.Panel3);
            this.Controls.Add(this.Panel2);
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "FrmProductSelector";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "选择产品";
            this.TopMost = true;
            ((System.ComponentModel.ISupportInitialize)(this.Dgv)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SelectedDgv)).EndInit();
            this.TableLayoutPanel1.ResumeLayout(false);
            this.Panel5.ResumeLayout(false);
            this.Panel4.ResumeLayout(false);
            this.Panel1.ResumeLayout(false);
            this.Panel2.ResumeLayout(false);
            this.ToolStrip1.ResumeLayout(false);
            this.ToolStrip1.PerformLayout();
            this.Panel3.ResumeLayout(false);
            this.Panel3.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView Dgv;
        private System.Windows.Forms.DataGridView SelectedDgv;
        private System.Windows.Forms.Button Btn_ShiftAll;
        private System.Windows.Forms.Button Btn_ShiftSelect;
        private System.Windows.Forms.Button Btn_RemoveSelect;
        private System.Windows.Forms.Button Btn_RemoveAll;
        private System.Windows.Forms.ComboBox CmbLimitCustomer;
        private System.Windows.Forms.TextBox TxtSearch;
        private System.Windows.Forms.TableLayoutPanel TableLayoutPanel1;
        private System.Windows.Forms.Panel Panel1;
        private System.Windows.Forms.Panel Panel2;
        private System.Windows.Forms.Panel Panel3;
        private System.Windows.Forms.Panel Panel5;
        private System.Windows.Forms.Panel Panel4;
        private System.Windows.Forms.TextBox TxtBatchSearch;
        private System.Windows.Forms.ToolStrip ToolStrip1;
        private System.Windows.Forms.ToolStripButton BtnOK;
        private System.Windows.Forms.ToolStripButton BtnCancel;
        private System.Windows.Forms.ToolStripLabel LblStatue;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.DataGridViewImageColumn 图片;
        private System.Windows.Forms.DataGridViewTextBoxColumn 产品型号;
        private System.Windows.Forms.DataGridViewTextBoxColumn 描述;
        private System.Windows.Forms.DataGridViewTextBoxColumn 颜色;
        private System.Windows.Forms.DataGridViewTextBoxColumn 客户代码;
        private System.Windows.Forms.DataGridViewTextBoxColumn 客户型号;
        private System.Windows.Forms.DataGridViewTextBoxColumn 配置;
        private System.Windows.Forms.DataGridViewTextBoxColumn 主图地址;
        private System.Windows.Forms.DataGridViewImageColumn 已选图片;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选产品型号;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选描述;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选颜色;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选客户代码;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选客户型号;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选配置;
        private System.Windows.Forms.DataGridViewTextBoxColumn 已选主图地址;
    }
}