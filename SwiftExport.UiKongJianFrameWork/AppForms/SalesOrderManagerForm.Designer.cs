namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    partial class SalesOrderManagerForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SalesOrderManagerForm));
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.ToolStripButton_Save = new System.Windows.Forms.ToolStripButton();
            this.ToolStripButton_Add = new System.Windows.Forms.ToolStripButton();
            this.ToolStripComboBox_QueryField = new System.Windows.Forms.ToolStripComboBox();
            this.ToolStripTextBox_QueryResult = new System.Windows.Forms.ToolStripTextBox();
            this.ToolStripButton_Query = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel_Find = new System.Windows.Forms.ToolStripLabel();
            this.toolStripTextBox_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.dataSplitUserControl1 = new SwiftExport.UiKongJianFrameWork.AppUserControls.DataSplitUserControl();
            this.button1 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // splitContainer1
            // 
            this.splitContainer1.Location = new System.Drawing.Point(253, 71);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.button1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.tabControl1);
            this.splitContainer1.Size = new System.Drawing.Size(1570, 657);
            this.splitContainer1.SplitterDistance = 56;
            this.splitContainer1.SplitterWidth = 5;
            this.splitContainer1.TabIndex = 0;
            // 
            // toolStrip1
            // 
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripButton_Save,
            this.ToolStripButton_Add,
            this.ToolStripComboBox_QueryField,
            this.ToolStripTextBox_QueryResult,
            this.ToolStripButton_Query,
            this.toolStripSeparator1,
            this.toolStripLabel_Find,
            this.toolStripTextBox_FindResult});
            this.toolStrip1.Location = new System.Drawing.Point(3, 3);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(1556, 41);
            this.toolStrip1.TabIndex = 6;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // ToolStripButton_Save
            // 
            this.ToolStripButton_Save.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.ToolStripButton_Save.Image = ((System.Drawing.Image)(resources.GetObject("ToolStripButton_Save.Image")));
            this.ToolStripButton_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Save.Name = "ToolStripButton_Save";
            this.ToolStripButton_Save.Size = new System.Drawing.Size(82, 36);
            this.ToolStripButton_Save.Text = "保存";
            // 
            // ToolStripButton_Add
            // 
            this.ToolStripButton_Add.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.ToolStripButton_Add.Image = ((System.Drawing.Image)(resources.GetObject("ToolStripButton_Add.Image")));
            this.ToolStripButton_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Add.Name = "ToolStripButton_Add";
            this.ToolStripButton_Add.Size = new System.Drawing.Size(82, 36);
            this.ToolStripButton_Add.Text = "新建";
            // 
            // ToolStripComboBox_QueryField
            // 
            this.ToolStripComboBox_QueryField.Name = "ToolStripComboBox_QueryField";
            this.ToolStripComboBox_QueryField.Size = new System.Drawing.Size(233, 41);
            this.ToolStripComboBox_QueryField.Text = "选择查询字段";
            // 
            // ToolStripTextBox_QueryResult
            // 
            this.ToolStripTextBox_QueryResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.ToolStripTextBox_QueryResult.Name = "ToolStripTextBox_QueryResult";
            this.ToolStripTextBox_QueryResult.Size = new System.Drawing.Size(233, 41);
            // 
            // ToolStripButton_Query
            // 
            this.ToolStripButton_Query.Image = ((System.Drawing.Image)(resources.GetObject("ToolStripButton_Query.Image")));
            this.ToolStripButton_Query.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ToolStripButton_Query.Name = "ToolStripButton_Query";
            this.ToolStripButton_Query.Size = new System.Drawing.Size(82, 36);
            this.ToolStripButton_Query.Text = "查询";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 41);
            // 
            // toolStripLabel_Find
            // 
            this.toolStripLabel_Find.Name = "toolStripLabel_Find";
            this.toolStripLabel_Find.Size = new System.Drawing.Size(50, 36);
            this.toolStripLabel_Find.Text = "筛选:";
            // 
            // toolStripTextBox_FindResult
            // 
            this.toolStripTextBox_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.toolStripTextBox_FindResult.Name = "toolStripTextBox_FindResult";
            this.toolStripTextBox_FindResult.Size = new System.Drawing.Size(233, 41);
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1570, 596);
            this.tabControl1.TabIndex = 7;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.dataGridView1);
            this.tabPage1.Controls.Add(this.dataSplitUserControl1);
            this.tabPage1.Controls.Add(this.toolStrip1);
            this.tabPage1.Location = new System.Drawing.Point(4, 40);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(1562, 552);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "列表";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // dataGridView1
            // 
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(3, 44);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 62;
            this.dataGridView1.RowTemplate.Height = 37;
            this.dataGridView1.Size = new System.Drawing.Size(1556, 465);
            this.dataGridView1.TabIndex = 0;
            // 
            // dataSplitUserControl1
            // 
            this.dataSplitUserControl1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.dataSplitUserControl1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.dataSplitUserControl1.Location = new System.Drawing.Point(3, 509);
            this.dataSplitUserControl1.Margin = new System.Windows.Forms.Padding(4);
            this.dataSplitUserControl1.Name = "dataSplitUserControl1";
            this.dataSplitUserControl1.PageCount = 0;
            this.dataSplitUserControl1.PageIndex = 0;
            this.dataSplitUserControl1.PageSize = 20;
            this.dataSplitUserControl1.Size = new System.Drawing.Size(1556, 40);
            this.dataSplitUserControl1.TabIndex = 8;
            // 
            // button1
            // 
            this.button1.BackColor = System.Drawing.Color.Transparent;
            this.button1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button1.Location = new System.Drawing.Point(0, 0);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(1570, 56);
            this.button1.TabIndex = 0;
            this.button1.Text = "button1";
            this.button1.UseVisualStyleBackColor = false;
            // 
            // SalesOrderManagerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1897, 1084);
            this.Controls.Add(this.splitContainer1);
            this.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "SalesOrderManagerForm";
            this.Text = "SalesOrderManagerForm";
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.DataGridView dataGridView1;
        private AppUserControls.DataSplitUserControl dataSplitUserControl1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Save;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Add;
        private System.Windows.Forms.ToolStripComboBox ToolStripComboBox_QueryField;
        private System.Windows.Forms.ToolStripTextBox ToolStripTextBox_QueryResult;
        private System.Windows.Forms.ToolStripButton ToolStripButton_Query;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel toolStripLabel_Find;
        private System.Windows.Forms.ToolStripTextBox toolStripTextBox_FindResult;
        private System.Windows.Forms.Button button1;
    }
}