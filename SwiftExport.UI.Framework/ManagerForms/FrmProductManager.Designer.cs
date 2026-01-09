namespace SwiftExport.UI.Framework.ManagerForms
{
    partial class FrmProductManager
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
            System.Windows.Forms.TreeNode treeNode5 = new System.Windows.Forms.TreeNode("节点2");
            System.Windows.Forms.TreeNode treeNode6 = new System.Windows.Forms.TreeNode("节点0", new System.Windows.Forms.TreeNode[] {
            treeNode5});
            System.Windows.Forms.TreeNode treeNode7 = new System.Windows.Forms.TreeNode("节点3");
            System.Windows.Forms.TreeNode treeNode8 = new System.Windows.Forms.TreeNode("节点1", new System.Windows.Forms.TreeNode[] {
            treeNode7});
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmProductManager));
            this.SplitContainer_Main = new System.Windows.Forms.SplitContainer();
            this.CmbLimitCustomer = new System.Windows.Forms.ComboBox();
            this.Pane_FilterCriteria = new System.Windows.Forms.Panel();
            this.TreeView1 = new System.Windows.Forms.TreeView();
            this.SearchTextBox = new System.Windows.Forms.TextBox();
            this.BtnApplyFilter = new System.Windows.Forms.Button();
            this.BtnResetFilter = new System.Windows.Forms.Button();
            this.SplitContainer_Product = new System.Windows.Forms.SplitContainer();
            this.TabControl_Product = new System.Windows.Forms.TabControl();
            this.TabPage_ProductList = new System.Windows.Forms.TabPage();
            this.DgvProduct = new System.Windows.Forms.DataGridView();
            this.TabPage_ProductDetail = new System.Windows.Forms.TabPage();
            this.Tsp1 = new System.Windows.Forms.ToolStrip();
            this.TsBtn_Add = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Edit = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Delete = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Save = new System.Windows.Forms.ToolStripButton();
            this.TsBtn_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.TsLbl_Find = new System.Windows.Forms.ToolStripLabel();
            this.Txt_FindResult = new System.Windows.Forms.ToolStripComboBox();
            this.dataSplitUserControl1 = new SwiftExport.UI.Framework.AppUserControls.DataSplitUserControl();
            this.VisibleScPane2 = new System.Windows.Forms.Label();
            this.TabControl_BOM = new System.Windows.Forms.TabControl();
            this.TabPage_BomList = new System.Windows.Forms.TabPage();
            this.TabPage_BomDetail = new System.Windows.Forms.TabPage();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.Tsbtn_Bom_Add = new System.Windows.Forms.ToolStripButton();
            this.Tsbtn_Bom_Edit = new System.Windows.Forms.ToolStripButton();
            this.Tsbtn_Bom_Delete = new System.Windows.Forms.ToolStripButton();
            this.Tsbtn_Bom_Save = new System.Windows.Forms.ToolStripButton();
            this.Tsbtn_Bom_Refresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.TsLbl_BomFilter = new System.Windows.Forms.ToolStripLabel();
            this.TsTxt_Bom_FindResult = new System.Windows.Forms.ToolStripTextBox();
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer_Main)).BeginInit();
            this.SplitContainer_Main.Panel1.SuspendLayout();
            this.SplitContainer_Main.Panel2.SuspendLayout();
            this.SplitContainer_Main.SuspendLayout();
            this.Pane_FilterCriteria.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer_Product)).BeginInit();
            this.SplitContainer_Product.Panel1.SuspendLayout();
            this.SplitContainer_Product.Panel2.SuspendLayout();
            this.SplitContainer_Product.SuspendLayout();
            this.TabControl_Product.SuspendLayout();
            this.TabPage_ProductList.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DgvProduct)).BeginInit();
            this.Tsp1.SuspendLayout();
            this.TabControl_BOM.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // SplitContainer_Main
            // 
            this.SplitContainer_Main.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SplitContainer_Main.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitContainer_Main.Location = new System.Drawing.Point(0, 0);
            this.SplitContainer_Main.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.SplitContainer_Main.Name = "SplitContainer_Main";
            // 
            // SplitContainer_Main.Panel1
            // 
            this.SplitContainer_Main.Panel1.BackColor = System.Drawing.SystemColors.Window;
            this.SplitContainer_Main.Panel1.Controls.Add(this.CmbLimitCustomer);
            this.SplitContainer_Main.Panel1.Controls.Add(this.Pane_FilterCriteria);
            this.SplitContainer_Main.Panel1.Controls.Add(this.BtnApplyFilter);
            this.SplitContainer_Main.Panel1.Controls.Add(this.BtnResetFilter);
            // 
            // SplitContainer_Main.Panel2
            // 
            this.SplitContainer_Main.Panel2.BackColor = System.Drawing.SystemColors.Window;
            this.SplitContainer_Main.Panel2.Controls.Add(this.SplitContainer_Product);
            this.SplitContainer_Main.Size = new System.Drawing.Size(2422, 1234);
            this.SplitContainer_Main.SplitterDistance = 292;
            this.SplitContainer_Main.TabIndex = 0;
            // 
            // CmbLimitCustomer
            // 
            this.CmbLimitCustomer.Dock = System.Windows.Forms.DockStyle.Top;
            this.CmbLimitCustomer.FormattingEnabled = true;
            this.CmbLimitCustomer.Location = new System.Drawing.Point(0, 0);
            this.CmbLimitCustomer.Name = "CmbLimitCustomer";
            this.CmbLimitCustomer.Size = new System.Drawing.Size(290, 36);
            this.CmbLimitCustomer.TabIndex = 14;
            // 
            // Pane_FilterCriteria
            // 
            this.Pane_FilterCriteria.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Pane_FilterCriteria.Controls.Add(this.TreeView1);
            this.Pane_FilterCriteria.Controls.Add(this.SearchTextBox);
            this.Pane_FilterCriteria.Location = new System.Drawing.Point(0, 60);
            this.Pane_FilterCriteria.Name = "Pane_FilterCriteria";
            this.Pane_FilterCriteria.Size = new System.Drawing.Size(290, 1020);
            this.Pane_FilterCriteria.TabIndex = 2;
            // 
            // TreeView1
            // 
            this.TreeView1.CheckBoxes = true;
            this.TreeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TreeView1.Location = new System.Drawing.Point(0, 35);
            this.TreeView1.Name = "TreeView1";
            treeNode5.Name = "节点2";
            treeNode5.Text = "节点2";
            treeNode6.Name = "节点0";
            treeNode6.Text = "节点0";
            treeNode7.Name = "节点3";
            treeNode7.Text = "节点3";
            treeNode8.Name = "节点1";
            treeNode8.Text = "节点1";
            this.TreeView1.Nodes.AddRange(new System.Windows.Forms.TreeNode[] {
            treeNode6,
            treeNode8});
            this.TreeView1.Size = new System.Drawing.Size(290, 985);
            this.TreeView1.TabIndex = 2;
            // 
            // SearchTextBox
            // 
            this.SearchTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.SearchTextBox.Location = new System.Drawing.Point(0, 0);
            this.SearchTextBox.Name = "SearchTextBox";
            this.SearchTextBox.Size = new System.Drawing.Size(290, 35);
            this.SearchTextBox.TabIndex = 1;
            // 
            // BtnApplyFilter
            // 
            this.BtnApplyFilter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BtnApplyFilter.Location = new System.Drawing.Point(0, 1118);
            this.BtnApplyFilter.Name = "BtnApplyFilter";
            this.BtnApplyFilter.Size = new System.Drawing.Size(290, 57);
            this.BtnApplyFilter.TabIndex = 2;
            this.BtnApplyFilter.Text = "应用";
            this.BtnApplyFilter.UseVisualStyleBackColor = true;
            // 
            // BtnResetFilter
            // 
            this.BtnResetFilter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BtnResetFilter.Location = new System.Drawing.Point(0, 1175);
            this.BtnResetFilter.Name = "BtnResetFilter";
            this.BtnResetFilter.Size = new System.Drawing.Size(290, 57);
            this.BtnResetFilter.TabIndex = 2;
            this.BtnResetFilter.Text = "重置";
            this.BtnResetFilter.UseVisualStyleBackColor = true;
            // 
            // SplitContainer_Product
            // 
            this.SplitContainer_Product.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SplitContainer_Product.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitContainer_Product.Location = new System.Drawing.Point(0, 0);
            this.SplitContainer_Product.Name = "SplitContainer_Product";
            this.SplitContainer_Product.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // SplitContainer_Product.Panel1
            // 
            this.SplitContainer_Product.Panel1.Controls.Add(this.TabControl_Product);
            this.SplitContainer_Product.Panel1.Controls.Add(this.Tsp1);
            this.SplitContainer_Product.Panel1.Controls.Add(this.dataSplitUserControl1);
            this.SplitContainer_Product.Panel1.Controls.Add(this.VisibleScPane2);
            // 
            // SplitContainer_Product.Panel2
            // 
            this.SplitContainer_Product.Panel2.Controls.Add(this.TabControl_BOM);
            this.SplitContainer_Product.Panel2.Controls.Add(this.toolStrip1);
            this.SplitContainer_Product.Panel2Collapsed = true;
            this.SplitContainer_Product.Size = new System.Drawing.Size(2126, 1234);
            this.SplitContainer_Product.SplitterDistance = 699;
            this.SplitContainer_Product.TabIndex = 0;
            // 
            // TabControl_Product
            // 
            this.TabControl_Product.AccessibleName = "T";
            this.TabControl_Product.Controls.Add(this.TabPage_ProductList);
            this.TabControl_Product.Controls.Add(this.TabPage_ProductDetail);
            this.TabControl_Product.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TabControl_Product.Location = new System.Drawing.Point(0, 60);
            this.TabControl_Product.Name = "TabControl_Product";
            this.TabControl_Product.SelectedIndex = 0;
            this.TabControl_Product.Size = new System.Drawing.Size(2124, 1076);
            this.TabControl_Product.TabIndex = 12;
            // 
            // TabPage_ProductList
            // 
            this.TabPage_ProductList.Controls.Add(this.DgvProduct);
            this.TabPage_ProductList.Location = new System.Drawing.Point(8, 42);
            this.TabPage_ProductList.Margin = new System.Windows.Forms.Padding(0);
            this.TabPage_ProductList.Name = "TabPage_ProductList";
            this.TabPage_ProductList.Size = new System.Drawing.Size(2108, 1026);
            this.TabPage_ProductList.TabIndex = 0;
            this.TabPage_ProductList.Text = "列表";
            this.TabPage_ProductList.UseVisualStyleBackColor = true;
            // 
            // DgvProduct
            // 
            this.DgvProduct.BackgroundColor = System.Drawing.SystemColors.Window;
            this.DgvProduct.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DgvProduct.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DgvProduct.Location = new System.Drawing.Point(0, 0);
            this.DgvProduct.Margin = new System.Windows.Forms.Padding(0);
            this.DgvProduct.Name = "DgvProduct";
            this.DgvProduct.RowHeadersWidth = 82;
            this.DgvProduct.RowTemplate.Height = 37;
            this.DgvProduct.Size = new System.Drawing.Size(2108, 1026);
            this.DgvProduct.TabIndex = 10;
            // 
            // TabPage_ProductDetail
            // 
            this.TabPage_ProductDetail.Location = new System.Drawing.Point(8, 39);
            this.TabPage_ProductDetail.Name = "TabPage_ProductDetail";
            this.TabPage_ProductDetail.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_ProductDetail.Size = new System.Drawing.Size(2108, 1029);
            this.TabPage_ProductDetail.TabIndex = 1;
            this.TabPage_ProductDetail.Text = "详细";
            this.TabPage_ProductDetail.UseVisualStyleBackColor = true;
            // 
            // Tsp1
            // 
            this.Tsp1.AutoSize = false;
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
            this.Tsp1.Size = new System.Drawing.Size(2124, 60);
            this.Tsp1.TabIndex = 9;
            this.Tsp1.Text = "toolStrip1";
            // 
            // TsBtn_Add
            // 
            this.TsBtn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Add.Name = "TsBtn_Add";
            this.TsBtn_Add.Size = new System.Drawing.Size(93, 54);
            this.TsBtn_Add.Text = "新建";
            // 
            // TsBtn_Edit
            // 
            this.TsBtn_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Edit.Name = "TsBtn_Edit";
            this.TsBtn_Edit.Size = new System.Drawing.Size(93, 54);
            this.TsBtn_Edit.Text = "编辑";
            // 
            // TsBtn_Delete
            // 
            this.TsBtn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Delete.Name = "TsBtn_Delete";
            this.TsBtn_Delete.Size = new System.Drawing.Size(61, 54);
            this.TsBtn_Delete.Text = "删除";
            // 
            // TsBtn_Save
            // 
            this.TsBtn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Save.Name = "TsBtn_Save";
            this.TsBtn_Save.Size = new System.Drawing.Size(61, 54);
            this.TsBtn_Save.Text = "保存";
            // 
            // TsBtn_Refresh
            // 
            this.TsBtn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Refresh.Name = "TsBtn_Refresh";
            this.TsBtn_Refresh.Size = new System.Drawing.Size(61, 54);
            this.TsBtn_Refresh.Text = "刷新";
            this.TsBtn_Refresh.Click += new System.EventHandler(this.TsBtn_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 60);
            // 
            // TsLbl_Find
            // 
            this.TsLbl_Find.Name = "TsLbl_Find";
            this.TsLbl_Find.Size = new System.Drawing.Size(62, 54);
            this.TsLbl_Find.Text = "搜索:";
            // 
            // Txt_FindResult
            // 
            this.Txt_FindResult.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Txt_FindResult.Name = "Txt_FindResult";
            this.Txt_FindResult.Size = new System.Drawing.Size(300, 60);
            this.Txt_FindResult.TextChanged += new System.EventHandler(this.Txt_FindResult_TextChanged);
            // 
            // dataSplitUserControl1
            // 
            this.dataSplitUserControl1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.dataSplitUserControl1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.dataSplitUserControl1.Location = new System.Drawing.Point(0, 1136);
            this.dataSplitUserControl1.Margin = new System.Windows.Forms.Padding(4);
            this.dataSplitUserControl1.Name = "dataSplitUserControl1";
            this.dataSplitUserControl1.Size = new System.Drawing.Size(2124, 66);
            this.dataSplitUserControl1.TabIndex = 14;
            // 
            // VisibleScPane2
            // 
            this.VisibleScPane2.BackColor = System.Drawing.SystemColors.Control;
            this.VisibleScPane2.Cursor = System.Windows.Forms.Cursors.Hand;
            this.VisibleScPane2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.VisibleScPane2.Location = new System.Drawing.Point(0, 1202);
            this.VisibleScPane2.Name = "VisibleScPane2";
            this.VisibleScPane2.Size = new System.Drawing.Size(2124, 30);
            this.VisibleScPane2.TabIndex = 13;
            this.VisibleScPane2.Text = "▼ 隐藏BOM区";
            this.VisibleScPane2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.VisibleScPane2.Click += new System.EventHandler(this.VisibleScPane2_Click);
            // 
            // TabControl_BOM
            // 
            this.TabControl_BOM.Controls.Add(this.TabPage_BomList);
            this.TabControl_BOM.Controls.Add(this.TabPage_BomDetail);
            this.TabControl_BOM.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TabControl_BOM.Location = new System.Drawing.Point(0, 60);
            this.TabControl_BOM.Name = "TabControl_BOM";
            this.TabControl_BOM.SelectedIndex = 0;
            this.TabControl_BOM.Size = new System.Drawing.Size(148, 0);
            this.TabControl_BOM.TabIndex = 0;
            // 
            // TabPage_BomList
            // 
            this.TabPage_BomList.Location = new System.Drawing.Point(8, 42);
            this.TabPage_BomList.Name = "TabPage_BomList";
            this.TabPage_BomList.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_BomList.Size = new System.Drawing.Size(132, 0);
            this.TabPage_BomList.TabIndex = 0;
            this.TabPage_BomList.Text = "列表";
            this.TabPage_BomList.UseVisualStyleBackColor = true;
            // 
            // TabPage_BomDetail
            // 
            this.TabPage_BomDetail.Location = new System.Drawing.Point(8, 42);
            this.TabPage_BomDetail.Name = "TabPage_BomDetail";
            this.TabPage_BomDetail.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_BomDetail.Size = new System.Drawing.Size(132, 0);
            this.TabPage_BomDetail.TabIndex = 1;
            this.TabPage_BomDetail.Text = "详细";
            this.TabPage_BomDetail.UseVisualStyleBackColor = true;
            // 
            // toolStrip1
            // 
            this.toolStrip1.AutoSize = false;
            this.toolStrip1.BackColor = System.Drawing.SystemColors.Window;
            this.toolStrip1.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Tsbtn_Bom_Add,
            this.Tsbtn_Bom_Edit,
            this.Tsbtn_Bom_Delete,
            this.Tsbtn_Bom_Save,
            this.Tsbtn_Bom_Refresh,
            this.toolStripSeparator1,
            this.TsLbl_BomFilter,
            this.TsTxt_Bom_FindResult});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(148, 60);
            this.toolStrip1.TabIndex = 13;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // Tsbtn_Bom_Add
            // 
            this.Tsbtn_Bom_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsbtn_Bom_Add.Name = "Tsbtn_Bom_Add";
            this.Tsbtn_Bom_Add.Size = new System.Drawing.Size(61, 54);
            this.Tsbtn_Bom_Add.Text = "新建";
            // 
            // Tsbtn_Bom_Edit
            // 
            this.Tsbtn_Bom_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsbtn_Bom_Edit.Name = "Tsbtn_Bom_Edit";
            this.Tsbtn_Bom_Edit.Size = new System.Drawing.Size(61, 34);
            this.Tsbtn_Bom_Edit.Text = "编辑";
            // 
            // Tsbtn_Bom_Delete
            // 
            this.Tsbtn_Bom_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsbtn_Bom_Delete.Name = "Tsbtn_Bom_Delete";
            this.Tsbtn_Bom_Delete.Size = new System.Drawing.Size(61, 34);
            this.Tsbtn_Bom_Delete.Text = "删除";
            // 
            // Tsbtn_Bom_Save
            // 
            this.Tsbtn_Bom_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsbtn_Bom_Save.Name = "Tsbtn_Bom_Save";
            this.Tsbtn_Bom_Save.Size = new System.Drawing.Size(61, 34);
            this.Tsbtn_Bom_Save.Text = "保存";
            // 
            // Tsbtn_Bom_Refresh
            // 
            this.Tsbtn_Bom_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsbtn_Bom_Refresh.Name = "Tsbtn_Bom_Refresh";
            this.Tsbtn_Bom_Refresh.Size = new System.Drawing.Size(61, 34);
            this.Tsbtn_Bom_Refresh.Text = "刷新";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 60);
            // 
            // TsLbl_BomFilter
            // 
            this.TsLbl_BomFilter.Name = "TsLbl_BomFilter";
            this.TsLbl_BomFilter.Size = new System.Drawing.Size(62, 30);
            this.TsLbl_BomFilter.Text = "筛选:";
            // 
            // TsTxt_Bom_FindResult
            // 
            this.TsTxt_Bom_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.TsTxt_Bom_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.TsTxt_Bom_FindResult.Name = "TsTxt_Bom_FindResult";
            this.TsTxt_Bom_FindResult.Size = new System.Drawing.Size(300, 38);
            // 
            // FrmProductManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(2422, 1234);
            this.Controls.Add(this.SplitContainer_Main);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "FrmProductManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "FrmProductManager";
            this.SplitContainer_Main.Panel1.ResumeLayout(false);
            this.SplitContainer_Main.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer_Main)).EndInit();
            this.SplitContainer_Main.ResumeLayout(false);
            this.Pane_FilterCriteria.ResumeLayout(false);
            this.Pane_FilterCriteria.PerformLayout();
            this.SplitContainer_Product.Panel1.ResumeLayout(false);
            this.SplitContainer_Product.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer_Product)).EndInit();
            this.SplitContainer_Product.ResumeLayout(false);
            this.TabControl_Product.ResumeLayout(false);
            this.TabPage_ProductList.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.DgvProduct)).EndInit();
            this.Tsp1.ResumeLayout(false);
            this.Tsp1.PerformLayout();
            this.TabControl_BOM.ResumeLayout(false);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer SplitContainer_Main;
        private System.Windows.Forms.SplitContainer SplitContainer_Product;
        private System.Windows.Forms.TabControl TabControl_Product;
        private System.Windows.Forms.TabPage TabPage_ProductList;
        private System.Windows.Forms.DataGridView DgvProduct;
        private System.Windows.Forms.TabPage TabPage_ProductDetail;
        private System.Windows.Forms.ToolStrip Tsp1;
        private System.Windows.Forms.ToolStripButton TsBtn_Add;
        private System.Windows.Forms.ToolStripButton TsBtn_Edit;
        private System.Windows.Forms.ToolStripButton TsBtn_Delete;
        private System.Windows.Forms.ToolStripButton TsBtn_Save;
        private System.Windows.Forms.ToolStripButton TsBtn_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripLabel TsLbl_Find;
        private System.Windows.Forms.TabControl TabControl_BOM;
        private System.Windows.Forms.TabPage TabPage_BomList;
        private System.Windows.Forms.TabPage TabPage_BomDetail;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton Tsbtn_Bom_Add;
        private System.Windows.Forms.ToolStripButton Tsbtn_Bom_Edit;
        private System.Windows.Forms.ToolStripButton Tsbtn_Bom_Delete;
        private System.Windows.Forms.ToolStripButton Tsbtn_Bom_Save;
        private System.Windows.Forms.ToolStripButton Tsbtn_Bom_Refresh;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel TsLbl_BomFilter;
        private System.Windows.Forms.ToolStripTextBox TsTxt_Bom_FindResult;
        private System.Windows.Forms.Label VisibleScPane2;
        private System.Windows.Forms.ToolStripComboBox Txt_FindResult;
        private System.Windows.Forms.Panel Pane_FilterCriteria;
        private System.Windows.Forms.TextBox SearchTextBox;
        private System.Windows.Forms.Button BtnApplyFilter;
        private System.Windows.Forms.Button BtnResetFilter;
        private AppUserControls.DataSplitUserControl dataSplitUserControl1;
        private System.Windows.Forms.ComboBox CmbLimitCustomer;
        private System.Windows.Forms.TreeView TreeView1;
    }
}