namespace SwiftExport.UI.Framework.AppForms
{
    partial class FrmProductClassManager
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
            System.Windows.Forms.TreeNode treeNode1 = new System.Windows.Forms.TreeNode("节点3");
            System.Windows.Forms.TreeNode treeNode2 = new System.Windows.Forms.TreeNode("节点4");
            System.Windows.Forms.TreeNode treeNode3 = new System.Windows.Forms.TreeNode("节点0", new System.Windows.Forms.TreeNode[] {
            treeNode1,
            treeNode2});
            System.Windows.Forms.TreeNode treeNode4 = new System.Windows.Forms.TreeNode("节点5");
            System.Windows.Forms.TreeNode treeNode5 = new System.Windows.Forms.TreeNode("节点6");
            System.Windows.Forms.TreeNode treeNode6 = new System.Windows.Forms.TreeNode("节点1", new System.Windows.Forms.TreeNode[] {
            treeNode4,
            treeNode5});
            System.Windows.Forms.TreeNode treeNode7 = new System.Windows.Forms.TreeNode("节点7");
            System.Windows.Forms.TreeNode treeNode8 = new System.Windows.Forms.TreeNode("节点8");
            System.Windows.Forms.TreeNode treeNode9 = new System.Windows.Forms.TreeNode("节点2", new System.Windows.Forms.TreeNode[] {
            treeNode7,
            treeNode8});
            this.Tsp1 = new System.Windows.Forms.ToolStrip();
            this.TsBtn_Add = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Edit = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Delete = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Save = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.TsLbl_Find = new System.Windows.Forms.ToolStripLabel();
            this.Txt_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.Tsp2 = new System.Windows.Forms.ToolStrip();
            this.LblTips = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.Tsp1.SuspendLayout();
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
            this.Tsp1.Size = new System.Drawing.Size(1286, 30);
            this.Tsp1.TabIndex = 8;
            this.Tsp1.Text = "toolStrip1";
            // 
            // TsBtn_Add
            // 
            this.TsBtn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Add.Name = "TsBtn_Add";
            this.TsBtn_Add.Size = new System.Drawing.Size(46, 25);
            this.TsBtn_Add.Text = "新建";
            this.TsBtn_Add.Click += new System.EventHandler(this.TsBtn_Add_Click);
            // 
            // TsBtn_Edit
            // 
            this.TsBtn_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Edit.Name = "TsBtn_Edit";
            this.TsBtn_Edit.Size = new System.Drawing.Size(46, 25);
            this.TsBtn_Edit.Text = "编辑";
            this.TsBtn_Edit.Visible = false;
            // 
            // TsBtn_Delete
            // 
            this.TsBtn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Delete.Name = "TsBtn_Delete";
            this.TsBtn_Delete.Size = new System.Drawing.Size(46, 25);
            this.TsBtn_Delete.Text = "删除";
            this.TsBtn_Delete.Click += new System.EventHandler(this.TsBtn_Delete_Click);
            // 
            // TsBtn_Save
            // 
            this.TsBtn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Save.Name = "TsBtn_Save";
            this.TsBtn_Save.Size = new System.Drawing.Size(46, 25);
            this.TsBtn_Save.Text = "保存";
            this.TsBtn_Save.Click += new System.EventHandler(this.TsBtn_Save_Click);
            // 
            // TsBtn_Refresh
            // 
            this.TsBtn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Refresh.Name = "TsBtn_Refresh";
            this.TsBtn_Refresh.Size = new System.Drawing.Size(46, 25);
            this.TsBtn_Refresh.Text = "刷新";
            this.TsBtn_Refresh.Click += new System.EventHandler(this.TsBtn_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 30);
            // 
            // TsLbl_Find
            // 
            this.TsLbl_Find.Name = "TsLbl_Find";
            this.TsLbl_Find.Size = new System.Drawing.Size(46, 25);
            this.TsLbl_Find.Text = "筛选:";
            // 
            // Txt_FindResult
            // 
            this.Txt_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Txt_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Txt_FindResult.Name = "Txt_FindResult";
            this.Txt_FindResult.Size = new System.Drawing.Size(300, 30);
            this.Txt_FindResult.TextChanged += new System.EventHandler(this.Txt_FindResult_TextChanged);
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.treeView1);
            this.MainPanel.Controls.Add(this.Tsp1);
            this.MainPanel.Controls.Add(this.Tsp2);
            this.MainPanel.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Margin = new System.Windows.Forms.Padding(4);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Size = new System.Drawing.Size(1286, 872);
            this.MainPanel.TabIndex = 12;
            // 
            // Tsp2
            // 
            this.Tsp2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.Tsp2.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.Tsp2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.LblTips,
            this.toolStripSeparator1});
            this.Tsp2.Location = new System.Drawing.Point(0, 846);
            this.Tsp2.Name = "Tsp2";
            this.Tsp2.Size = new System.Drawing.Size(1286, 26);
            this.Tsp2.TabIndex = 13;
            this.Tsp2.Text = "toolStrip1";
            // 
            // LblTips
            // 
            this.LblTips.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.LblTips.Name = "LblTips";
            this.LblTips.Size = new System.Drawing.Size(58, 21);
            this.LblTips.Text = "状态栏";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 26);
            // 
            // treeView1
            // 
            this.treeView1.Location = new System.Drawing.Point(47, 59);
            this.treeView1.Name = "treeView1";
            treeNode1.Name = "节点3";
            treeNode1.Text = "节点3";
            treeNode2.Name = "节点4";
            treeNode2.Text = "节点4";
            treeNode3.Name = "节点0";
            treeNode3.Text = "节点0";
            treeNode4.Name = "节点5";
            treeNode4.Text = "节点5";
            treeNode5.Name = "节点6";
            treeNode5.Text = "节点6";
            treeNode6.Name = "节点1";
            treeNode6.Text = "节点1";
            treeNode7.Name = "节点7";
            treeNode7.Text = "节点7";
            treeNode8.Name = "节点8";
            treeNode8.Text = "节点8";
            treeNode9.Name = "节点2";
            treeNode9.Text = "节点2";
            this.treeView1.Nodes.AddRange(new System.Windows.Forms.TreeNode[] {
            treeNode3,
            treeNode6,
            treeNode9});
            this.treeView1.Size = new System.Drawing.Size(982, 618);
            this.treeView1.TabIndex = 14;
            // 
            // FrmProductClassManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 21F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1286, 872);
            this.Controls.Add(this.MainPanel);
            this.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(2, 4, 2, 4);
            this.MinimumSize = new System.Drawing.Size(814, 615);
            this.Name = "FrmProductClassManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "白名单管理";
            this.Tsp1.ResumeLayout(false);
            this.Tsp1.PerformLayout();
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
        private System.Windows.Forms.Panel MainPanel;
        private System.Windows.Forms.ToolStripButton TsBtn_Edit;
        private System.Windows.Forms.ToolStrip Tsp2;
        private System.Windows.Forms.ToolStripLabel LblTips;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.TreeView treeView1;
    }
}