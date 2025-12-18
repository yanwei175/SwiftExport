namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    partial class DataTopUserControl
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
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
            this.toolStripTextBox_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStrip1
            // 
            this.toolStrip1.BackColor = System.Drawing.SystemColors.Window;
            this.toolStrip1.Dock = System.Windows.Forms.DockStyle.Fill;
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
            this.toolStripTextBox_FindResult});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(1742, 66);
            this.toolStrip1.TabIndex = 6;
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
            // 
            // ToolStripButton_Save
            // 
            this.ToolStripButton_Save.AutoSize = false;
            this.ToolStripButton_Save.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Save.Name = "ToolStripButton_Save";
            this.ToolStripButton_Save.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Save.Text = "保存";
            // 
            // ToolStripButton_Delete
            // 
            this.ToolStripButton_Delete.AutoSize = false;
            this.ToolStripButton_Delete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Delete.Name = "ToolStripButton_Delete";
            this.ToolStripButton_Delete.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Delete.Text = "删除";
            // 
            // ToolStripButton_Refresh
            // 
            this.ToolStripButton_Refresh.AutoSize = false;
            this.ToolStripButton_Refresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.ToolStripButton_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Refresh.Name = "ToolStripButton_Refresh";
            this.ToolStripButton_Refresh.Size = new System.Drawing.Size(100, 60);
            this.ToolStripButton_Refresh.Text = "刷新";
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
            this.ToolStripComboBox_QueryField.Text = "选择查询字段";
            // 
            // ToolStripTextBox_QueryResult
            // 
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
            // toolStripTextBox_FindResult
            // 
            this.toolStripTextBox_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.toolStripTextBox_FindResult.Name = "toolStripTextBox_FindResult";
            this.toolStripTextBox_FindResult.Size = new System.Drawing.Size(233, 66);
            this.toolStripTextBox_FindResult.TextChanged += new System.EventHandler(this.ToolStripTextBox_FindResult_TextChanged);
            // 
            // DataTopUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(19F, 41F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.toolStrip1);
            this.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "DataTopUserControl";
            this.Size = new System.Drawing.Size(1742, 66);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Save;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Add;
        private System.Windows.Forms.ToolStripComboBox ToolStripComboBox_QueryField;
        private System.Windows.Forms.ToolStripTextBox ToolStripTextBox_QueryResult;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Query;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel toolStripLabel_Find;
        private System.Windows.Forms.ToolStripTextBox toolStripTextBox_FindResult;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Delete;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    }
}
