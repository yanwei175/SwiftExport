namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    partial class FrmExcelSheetFieldsMappingManager
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
            this.Tsp1 = new System.Windows.Forms.ToolStrip();
            this.TsBtn_Add = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Edit = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Delete = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Save = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.TsLbl_Find = new System.Windows.Forms.ToolStripLabel();
            this.Txt_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.Dgv1 = new System.Windows.Forms.DataGridView();
            this.ID = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.生效表 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.源字段 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.目标字段 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Save = new System.Windows.Forms.DataGridViewButtonColumn();
            this.Delete = new System.Windows.Forms.DataGridViewButtonColumn();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.Tsp2 = new System.Windows.Forms.ToolStrip();
            this.LblTips = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Tsp1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Dgv1)).BeginInit();
            this.MainPanel.SuspendLayout();
            this.Tsp2.SuspendLayout();
            this.SuspendLayout();
            // 
            // Tsp1
            // 
            this.Tsp1.BackColor = System.Drawing.SystemColors.Window;
            this.Tsp1.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Tsp1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.Tsp1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TsBtn_Add,
            this.TsBtn_Edit,
            this.TsBtn_Delete,
            this.TsBtn_Save,
            this.TsBtn_Refresh,
            this.toolStripSeparator2,
            this.TsLbl_Find,
            this.Txt_FindResult});
            this.Tsp1.Location = new System.Drawing.Point(0, 0);
            this.Tsp1.Name = "Tsp1";
            this.Tsp1.Size = new System.Drawing.Size(923, 61);
            this.Tsp1.TabIndex = 8;
            this.Tsp1.Text = "toolStrip1";
            // 
            // TsBtn_Add
            // 
            this.TsBtn_Add.AutoSize = false;
            this.TsBtn_Add.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsBtn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Add.Name = "TsBtn_Add";
            this.TsBtn_Add.Size = new System.Drawing.Size(80, 55);
            this.TsBtn_Add.Text = "新建";
            this.TsBtn_Add.Click += new System.EventHandler(this.TsBtn_Add_Click);
            // 
            // TsBtn_Edit
            // 
            this.TsBtn_Edit.AutoSize = false;
            this.TsBtn_Edit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsBtn_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Edit.Name = "TsBtn_Edit";
            this.TsBtn_Edit.Size = new System.Drawing.Size(80, 55);
            this.TsBtn_Edit.Text = "编辑";
            // 
            // TsBtn_Delete
            // 
            this.TsBtn_Delete.AutoSize = false;
            this.TsBtn_Delete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsBtn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Delete.Name = "TsBtn_Delete";
            this.TsBtn_Delete.Size = new System.Drawing.Size(80, 55);
            this.TsBtn_Delete.Text = "删除";
            this.TsBtn_Delete.Click += new System.EventHandler(this.TsBtn_Delete_Click);
            // 
            // TsBtn_Save
            // 
            this.TsBtn_Save.AutoSize = false;
            this.TsBtn_Save.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsBtn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Save.Name = "TsBtn_Save";
            this.TsBtn_Save.Size = new System.Drawing.Size(80, 55);
            this.TsBtn_Save.Text = "保存";
            this.TsBtn_Save.Click += new System.EventHandler(this.TsBtn_Save_Click);
            // 
            // TsBtn_Refresh
            // 
            this.TsBtn_Refresh.AutoSize = false;
            this.TsBtn_Refresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsBtn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Refresh.Name = "TsBtn_Refresh";
            this.TsBtn_Refresh.Size = new System.Drawing.Size(80, 55);
            this.TsBtn_Refresh.Text = "刷新";
            this.TsBtn_Refresh.Click += new System.EventHandler(this.TsBtn_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 61);
            // 
            // TsLbl_Find
            // 
            this.TsLbl_Find.Name = "TsLbl_Find";
            this.TsLbl_Find.Size = new System.Drawing.Size(62, 55);
            this.TsLbl_Find.Text = "筛选:";
            // 
            // Txt_FindResult
            // 
            this.Txt_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Txt_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Txt_FindResult.Name = "Txt_FindResult";
            this.Txt_FindResult.Size = new System.Drawing.Size(300, 61);
            this.Txt_FindResult.TextChanged += new System.EventHandler(this.Txt_FindResult_TextChanged);
            // 
            // Dgv1
            // 
            this.Dgv1.AllowUserToAddRows = false;
            this.Dgv1.AllowUserToDeleteRows = false;
            this.Dgv1.AllowUserToOrderColumns = true;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.LightGray;
            this.Dgv1.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.Dgv1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.Dgv1.BackgroundColor = System.Drawing.SystemColors.Window;
            this.Dgv1.ClipboardCopyMode = System.Windows.Forms.DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            this.Dgv1.ColumnHeadersHeight = 50;
            this.Dgv1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ID,
            this.生效表,
            this.源字段,
            this.目标字段,
            this.Save,
            this.Delete});
            this.Dgv1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Dgv1.Location = new System.Drawing.Point(0, 61);
            this.Dgv1.Margin = new System.Windows.Forms.Padding(4);
            this.Dgv1.Name = "Dgv1";
            this.Dgv1.RowHeadersVisible = false;
            this.Dgv1.RowHeadersWidth = 40;
            this.Dgv1.RowTemplate.Height = 50;
            this.Dgv1.RowTemplate.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.Dgv1.Size = new System.Drawing.Size(923, 821);
            this.Dgv1.TabIndex = 11;
            this.Dgv1.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.Dgv1_CellContentClick);
            // 
            // ID
            // 
            this.ID.DataPropertyName = "ID";
            this.ID.HeaderText = "ID";
            this.ID.MinimumWidth = 10;
            this.ID.Name = "ID";
            this.ID.Visible = false;
            // 
            // 生效表
            // 
            this.生效表.DataPropertyName = "生效表";
            this.生效表.HeaderText = "生效表";
            this.生效表.MinimumWidth = 10;
            this.生效表.Name = "生效表";
            // 
            // 源字段
            // 
            this.源字段.DataPropertyName = "源字段";
            this.源字段.HeaderText = "源字段";
            this.源字段.MinimumWidth = 10;
            this.源字段.Name = "源字段";
            // 
            // 目标字段
            // 
            this.目标字段.DataPropertyName = "目标字段";
            this.目标字段.HeaderText = "目标字段";
            this.目标字段.MinimumWidth = 10;
            this.目标字段.Name = "目标字段";
            // 
            // Save
            // 
            this.Save.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Save.FillWeight = 40F;
            this.Save.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Save.HeaderText = "保存";
            this.Save.MinimumWidth = 8;
            this.Save.Name = "Save";
            this.Save.ReadOnly = true;
            this.Save.Text = "保存";
            this.Save.UseColumnTextForButtonValue = true;
            this.Save.Width = 67;
            // 
            // Delete
            // 
            this.Delete.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Delete.FillWeight = 40F;
            this.Delete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Delete.HeaderText = "删除";
            this.Delete.MinimumWidth = 8;
            this.Delete.Name = "Delete";
            this.Delete.ReadOnly = true;
            this.Delete.Text = "删除";
            this.Delete.UseColumnTextForButtonValue = true;
            this.Delete.Width = 68;
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.Dgv1);
            this.MainPanel.Controls.Add(this.Tsp1);
            this.MainPanel.Controls.Add(this.Tsp2);
            this.MainPanel.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Margin = new System.Windows.Forms.Padding(4);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Size = new System.Drawing.Size(923, 918);
            this.MainPanel.TabIndex = 12;
            // 
            // Tsp2
            // 
            this.Tsp2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.Tsp2.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.Tsp2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.LblTips,
            this.toolStripSeparator1});
            this.Tsp2.Location = new System.Drawing.Point(0, 882);
            this.Tsp2.Name = "Tsp2";
            this.Tsp2.Size = new System.Drawing.Size(923, 36);
            this.Tsp2.TabIndex = 13;
            this.Tsp2.Text = "toolStrip1";
            // 
            // LblTips
            // 
            this.LblTips.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.LblTips.Name = "LblTips";
            this.LblTips.Size = new System.Drawing.Size(79, 30);
            this.LblTips.Text = "状态栏";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 36);
            // 
            // FrmExcelSheetFieldsMappingManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(923, 918);
            this.Controls.Add(this.MainPanel);
            this.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(2, 4, 2, 4);
            this.MinimumSize = new System.Drawing.Size(814, 615);
            this.Name = "FrmExcelSheetFieldsMappingManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Excel字段映射";
            this.Tsp1.ResumeLayout(false);
            this.Tsp1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Dgv1)).EndInit();
            this.MainPanel.ResumeLayout(false);
            this.MainPanel.PerformLayout();
            this.Tsp2.ResumeLayout(false);
            this.Tsp2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.ToolStrip Tsp1;
        private System.Windows.Forms.ToolStripButton TsBtn_Add;
        private System.Windows.Forms.ToolStripButton TsBtn_Save;
        private System.Windows.Forms.ToolStripButton TsBtn_Delete;
        private System.Windows.Forms.ToolStripButton TsBtn_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripLabel TsLbl_Find;
        private System.Windows.Forms.ToolStripTextBox Txt_FindResult;
        private System.Windows.Forms.DataGridView Dgv1;
        private System.Windows.Forms.Panel MainPanel;
        private System.Windows.Forms.ToolStripButton TsBtn_Edit;
        private System.Windows.Forms.ToolStrip Tsp2;
        private System.Windows.Forms.ToolStripLabel LblTips;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.DataGridViewTextBoxColumn ID;
        private System.Windows.Forms.DataGridViewTextBoxColumn 生效表;
        private System.Windows.Forms.DataGridViewTextBoxColumn 源字段;
        private System.Windows.Forms.DataGridViewTextBoxColumn 目标字段;
        private System.Windows.Forms.DataGridViewButtonColumn Save;
        private System.Windows.Forms.DataGridViewButtonColumn Delete;
    }
}