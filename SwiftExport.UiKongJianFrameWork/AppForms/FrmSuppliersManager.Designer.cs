namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    partial class FrmSuppliersManager
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
            this.Tsp1 = new System.Windows.Forms.ToolStrip();
            this.Btn_Add = new System.Windows.Forms.ToolStripButton();
            this.Btn_Save = new System.Windows.Forms.ToolStripButton();
            this.Btn_Delete = new System.Windows.Forms.ToolStripButton();
            this.Btn_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel_Find = new System.Windows.Forms.ToolStripLabel();
            this.Txt_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.操作提示 = new System.Windows.Forms.ToolStripLabel();
            this.BdSourceSuppliers = new System.Windows.Forms.BindingSource(this.components);
            this.Dgv1 = new System.Windows.Forms.DataGridView();
            this.iDDataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.evenDataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.状态DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.视图状态DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.供应商代码DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.供应商名称DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.折扣DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.折扣备注DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.是否删除DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.创建时间DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.修改时间DataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.Tsp1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.BdSourceSuppliers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Dgv1)).BeginInit();
            this.MainPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // Tsp1
            // 
            this.Tsp1.BackColor = System.Drawing.SystemColors.Window;
            this.Tsp1.Font = new System.Drawing.Font("Segoe UI", 7.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Tsp1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.Tsp1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Btn_Add,
            this.Btn_Save,
            this.Btn_Delete,
            this.Btn_Refresh,
            this.toolStripSeparator2,
            this.toolStripLabel_Find,
            this.Txt_FindResult,
            this.操作提示});
            this.Tsp1.Location = new System.Drawing.Point(0, 0);
            this.Tsp1.Name = "Tsp1";
            this.Tsp1.Size = new System.Drawing.Size(1608, 66);
            this.Tsp1.TabIndex = 8;
            this.Tsp1.Text = "toolStrip1";
            // 
            // Btn_Add
            // 
            this.Btn_Add.AutoSize = false;
            this.Btn_Add.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Btn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Btn_Add.Name = "Btn_Add";
            this.Btn_Add.Size = new System.Drawing.Size(100, 60);
            this.Btn_Add.Text = "新建";
            this.Btn_Add.Click += new System.EventHandler(this.Btn_Add_Click);
            // 
            // Btn_Save
            // 
            this.Btn_Save.AutoSize = false;
            this.Btn_Save.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Btn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Btn_Save.Name = "Btn_Save";
            this.Btn_Save.Size = new System.Drawing.Size(100, 60);
            this.Btn_Save.Text = "保存";
            // 
            // Btn_Delete
            // 
            this.Btn_Delete.AutoSize = false;
            this.Btn_Delete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Btn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Btn_Delete.Name = "Btn_Delete";
            this.Btn_Delete.Size = new System.Drawing.Size(100, 60);
            this.Btn_Delete.Text = "删除";
            // 
            // Btn_Refresh
            // 
            this.Btn_Refresh.AutoSize = false;
            this.Btn_Refresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Btn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Btn_Refresh.Name = "Btn_Refresh";
            this.Btn_Refresh.Size = new System.Drawing.Size(100, 60);
            this.Btn_Refresh.Text = "刷新";
            this.Btn_Refresh.Click += new System.EventHandler(this.Btn_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 66);
            // 
            // toolStripLabel_Find
            // 
            this.toolStripLabel_Find.Name = "toolStripLabel_Find";
            this.toolStripLabel_Find.Size = new System.Drawing.Size(106, 60);
            this.toolStripLabel_Find.Text = "结果筛选:";
            // 
            // Txt_FindResult
            // 
            this.Txt_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Txt_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Txt_FindResult.Name = "Txt_FindResult";
            this.Txt_FindResult.Size = new System.Drawing.Size(179, 66);
            // 
            // 操作提示
            // 
            this.操作提示.ForeColor = System.Drawing.Color.Red;
            this.操作提示.Name = "操作提示";
            this.操作提示.Size = new System.Drawing.Size(0, 60);
            // 
            // BdSourceSuppliers
            // 
            this.BdSourceSuppliers.DataSource = typeof(SwiftExport.AppLayer.DTOs.SuppliersDto);
            this.BdSourceSuppliers.CurrentChanged += new System.EventHandler(this.BdSourceSuppliers_CurrentChanged);
            // 
            // Dgv1
            // 
            this.Dgv1.AllowUserToAddRows = false;
            this.Dgv1.AllowUserToDeleteRows = false;
            this.Dgv1.AutoGenerateColumns = false;
            this.Dgv1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.Dgv1.ColumnHeadersHeight = 55;
            this.Dgv1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.iDDataGridViewTextBoxColumn1,
            this.evenDataGridViewTextBoxColumn1,
            this.状态DataGridViewTextBoxColumn1,
            this.视图状态DataGridViewTextBoxColumn1,
            this.供应商代码DataGridViewTextBoxColumn1,
            this.供应商名称DataGridViewTextBoxColumn1,
            this.折扣DataGridViewTextBoxColumn1,
            this.折扣备注DataGridViewTextBoxColumn1,
            this.是否删除DataGridViewTextBoxColumn1,
            this.创建时间DataGridViewTextBoxColumn1,
            this.修改时间DataGridViewTextBoxColumn1});
            this.Dgv1.DataSource = this.BdSourceSuppliers;
            this.Dgv1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Dgv1.Location = new System.Drawing.Point(0, 66);
            this.Dgv1.Name = "Dgv1";
            this.Dgv1.RowHeadersWidth = 82;
            this.Dgv1.RowTemplate.Height = 50;
            this.Dgv1.Size = new System.Drawing.Size(1608, 843);
            this.Dgv1.TabIndex = 11;
            // 
            // iDDataGridViewTextBoxColumn1
            // 
            this.iDDataGridViewTextBoxColumn1.DataPropertyName = "ID";
            this.iDDataGridViewTextBoxColumn1.HeaderText = "ID";
            this.iDDataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.iDDataGridViewTextBoxColumn1.Name = "iDDataGridViewTextBoxColumn1";
            this.iDDataGridViewTextBoxColumn1.Visible = false;
            // 
            // evenDataGridViewTextBoxColumn1
            // 
            this.evenDataGridViewTextBoxColumn1.DataPropertyName = "Even";
            this.evenDataGridViewTextBoxColumn1.HeaderText = "Even";
            this.evenDataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.evenDataGridViewTextBoxColumn1.Name = "evenDataGridViewTextBoxColumn1";
            this.evenDataGridViewTextBoxColumn1.Visible = false;
            // 
            // 状态DataGridViewTextBoxColumn1
            // 
            this.状态DataGridViewTextBoxColumn1.DataPropertyName = "状态";
            this.状态DataGridViewTextBoxColumn1.HeaderText = "状态";
            this.状态DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.状态DataGridViewTextBoxColumn1.Name = "状态DataGridViewTextBoxColumn1";
            this.状态DataGridViewTextBoxColumn1.Visible = false;
            // 
            // 视图状态DataGridViewTextBoxColumn1
            // 
            this.视图状态DataGridViewTextBoxColumn1.DataPropertyName = "视图状态";
            this.视图状态DataGridViewTextBoxColumn1.FillWeight = 50F;
            this.视图状态DataGridViewTextBoxColumn1.HeaderText = "编辑";
            this.视图状态DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.视图状态DataGridViewTextBoxColumn1.Name = "视图状态DataGridViewTextBoxColumn1";
            this.视图状态DataGridViewTextBoxColumn1.ReadOnly = true;
            // 
            // 供应商代码DataGridViewTextBoxColumn1
            // 
            this.供应商代码DataGridViewTextBoxColumn1.DataPropertyName = "供应商代码";
            this.供应商代码DataGridViewTextBoxColumn1.HeaderText = "供应商代码";
            this.供应商代码DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.供应商代码DataGridViewTextBoxColumn1.Name = "供应商代码DataGridViewTextBoxColumn1";
            // 
            // 供应商名称DataGridViewTextBoxColumn1
            // 
            this.供应商名称DataGridViewTextBoxColumn1.DataPropertyName = "供应商名称";
            this.供应商名称DataGridViewTextBoxColumn1.HeaderText = "供应商名称";
            this.供应商名称DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.供应商名称DataGridViewTextBoxColumn1.Name = "供应商名称DataGridViewTextBoxColumn1";
            // 
            // 折扣DataGridViewTextBoxColumn1
            // 
            this.折扣DataGridViewTextBoxColumn1.DataPropertyName = "折扣";
            this.折扣DataGridViewTextBoxColumn1.HeaderText = "折扣";
            this.折扣DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.折扣DataGridViewTextBoxColumn1.Name = "折扣DataGridViewTextBoxColumn1";
            // 
            // 折扣备注DataGridViewTextBoxColumn1
            // 
            this.折扣备注DataGridViewTextBoxColumn1.DataPropertyName = "折扣备注";
            this.折扣备注DataGridViewTextBoxColumn1.HeaderText = "折扣备注";
            this.折扣备注DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.折扣备注DataGridViewTextBoxColumn1.Name = "折扣备注DataGridViewTextBoxColumn1";
            // 
            // 是否删除DataGridViewTextBoxColumn1
            // 
            this.是否删除DataGridViewTextBoxColumn1.DataPropertyName = "是否删除";
            this.是否删除DataGridViewTextBoxColumn1.HeaderText = "是否删除";
            this.是否删除DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.是否删除DataGridViewTextBoxColumn1.Name = "是否删除DataGridViewTextBoxColumn1";
            this.是否删除DataGridViewTextBoxColumn1.Visible = false;
            // 
            // 创建时间DataGridViewTextBoxColumn1
            // 
            this.创建时间DataGridViewTextBoxColumn1.DataPropertyName = "创建时间";
            this.创建时间DataGridViewTextBoxColumn1.HeaderText = "创建时间";
            this.创建时间DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.创建时间DataGridViewTextBoxColumn1.Name = "创建时间DataGridViewTextBoxColumn1";
            this.创建时间DataGridViewTextBoxColumn1.Visible = false;
            // 
            // 修改时间DataGridViewTextBoxColumn1
            // 
            this.修改时间DataGridViewTextBoxColumn1.DataPropertyName = "修改时间";
            this.修改时间DataGridViewTextBoxColumn1.HeaderText = "修改时间";
            this.修改时间DataGridViewTextBoxColumn1.MinimumWidth = 10;
            this.修改时间DataGridViewTextBoxColumn1.Name = "修改时间DataGridViewTextBoxColumn1";
            this.修改时间DataGridViewTextBoxColumn1.Visible = false;
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.Dgv1);
            this.MainPanel.Controls.Add(this.Tsp1);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Size = new System.Drawing.Size(1608, 909);
            this.MainPanel.TabIndex = 12;
            // 
            // FrmSuppliersManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 30F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1608, 909);
            this.Controls.Add(this.MainPanel);
            this.Font = new System.Drawing.Font("Segoe UI", 7.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(2, 3, 2, 3);
            this.Name = "FrmSuppliersManager";
            this.Text = "FrmSuppliersManager";
            this.Tsp1.ResumeLayout(false);
            this.Tsp1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.BdSourceSuppliers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.Dgv1)).EndInit();
            this.MainPanel.ResumeLayout(false);
            this.MainPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.ToolStrip Tsp1;
        private System.Windows.Forms.ToolStripButton Btn_Add;
        private System.Windows.Forms.ToolStripButton Btn_Save;
        private System.Windows.Forms.ToolStripButton Btn_Delete;
        private System.Windows.Forms.ToolStripButton Btn_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripLabel toolStripLabel_Find;
        private System.Windows.Forms.ToolStripTextBox Txt_FindResult;
        private System.Windows.Forms.ToolStripLabel 操作提示;
        private System.Windows.Forms.BindingSource BdSourceSuppliers;
        private System.Windows.Forms.DataGridView Dgv1;
        private System.Windows.Forms.DataGridViewTextBoxColumn iDDataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn evenDataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 状态DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 视图状态DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 供应商代码DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 供应商名称DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 折扣DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 折扣备注DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 是否删除DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 创建时间DataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn 修改时间DataGridViewTextBoxColumn1;
        private System.Windows.Forms.Panel MainPanel;
    }
}