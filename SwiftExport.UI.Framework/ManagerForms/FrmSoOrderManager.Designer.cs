namespace SwiftExport.UI.Framework.ManagerForms
{
    partial class FrmSoOrderManager
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
            this.SplitContainer1 = new System.Windows.Forms.SplitContainer();
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
            this.VisibleScPane1 = new System.Windows.Forms.Label();
            this.TabControl_BOM = new System.Windows.Forms.TabControl();
            this.TabPage_BomList = new System.Windows.Forms.TabPage();
            this.TabPage_BomDetail = new System.Windows.Forms.TabPage();
            this.Tsp2 = new System.Windows.Forms.ToolStrip();
            this.Tsp2btn_Add = new System.Windows.Forms.ToolStripButton();
            this.Tsp2btn_Edit = new System.Windows.Forms.ToolStripButton();
            this.Tsp2btn_Delete = new System.Windows.Forms.ToolStripButton();
            this.Tsp2btn_Save = new System.Windows.Forms.ToolStripButton();
            this.Tsp2btn_Refresh = new System.Windows.Forms.ToolStripButton();
            this.Tsp2Separator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Tsp2Lbl_Filter = new System.Windows.Forms.ToolStripLabel();
            this.Tsp2Txt_FindResult = new System.Windows.Forms.ToolStripTextBox();
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer1)).BeginInit();
            this.SplitContainer1.Panel1.SuspendLayout();
            this.SplitContainer1.Panel2.SuspendLayout();
            this.SplitContainer1.SuspendLayout();
            this.TabControl_Product.SuspendLayout();
            this.TabPage_ProductList.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DgvProduct)).BeginInit();
            this.Tsp1.SuspendLayout();
            this.TabControl_BOM.SuspendLayout();
            this.Tsp2.SuspendLayout();
            this.SuspendLayout();
            // 
            // SplitContainer1
            // 
            this.SplitContainer1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SplitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitContainer1.Location = new System.Drawing.Point(0, 0);
            this.SplitContainer1.Name = "SplitContainer1";
            this.SplitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // SplitContainer1.Panel1
            // 
            this.SplitContainer1.Panel1.Controls.Add(this.TabControl_Product);
            this.SplitContainer1.Panel1.Controls.Add(this.Tsp1);
            this.SplitContainer1.Panel1.Controls.Add(this.dataSplitUserControl1);
            this.SplitContainer1.Panel1.Controls.Add(this.VisibleScPane2);
            this.SplitContainer1.Panel1.Controls.Add(this.VisibleScPane1);
            // 
            // SplitContainer1.Panel2
            // 
            this.SplitContainer1.Panel2.Controls.Add(this.TabControl_BOM);
            this.SplitContainer1.Panel2.Controls.Add(this.Tsp2);
            this.SplitContainer1.Size = new System.Drawing.Size(2422, 1234);
            this.SplitContainer1.SplitterDistance = 699;
            this.SplitContainer1.TabIndex = 0;
            // 
            // TabControl_Product
            // 
            this.TabControl_Product.AccessibleName = "T";
            this.TabControl_Product.Controls.Add(this.TabPage_ProductList);
            this.TabControl_Product.Controls.Add(this.TabPage_ProductDetail);
            this.TabControl_Product.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TabControl_Product.Location = new System.Drawing.Point(0, 72);
            this.TabControl_Product.Name = "TabControl_Product";
            this.TabControl_Product.SelectedIndex = 0;
            this.TabControl_Product.Size = new System.Drawing.Size(2420, 529);
            this.TabControl_Product.TabIndex = 12;
            // 
            // TabPage_ProductList
            // 
            this.TabPage_ProductList.Controls.Add(this.DgvProduct);
            this.TabPage_ProductList.Location = new System.Drawing.Point(8, 42);
            this.TabPage_ProductList.Margin = new System.Windows.Forms.Padding(0);
            this.TabPage_ProductList.Name = "TabPage_ProductList";
            this.TabPage_ProductList.Size = new System.Drawing.Size(2404, 479);
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
            this.DgvProduct.Size = new System.Drawing.Size(2404, 479);
            this.DgvProduct.TabIndex = 10;
            // 
            // TabPage_ProductDetail
            // 
            this.TabPage_ProductDetail.Location = new System.Drawing.Point(8, 39);
            this.TabPage_ProductDetail.Name = "TabPage_ProductDetail";
            this.TabPage_ProductDetail.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_ProductDetail.Size = new System.Drawing.Size(2404, 463);
            this.TabPage_ProductDetail.TabIndex = 1;
            this.TabPage_ProductDetail.Text = "详细";
            this.TabPage_ProductDetail.UseVisualStyleBackColor = true;
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
            this.Tsp1.Location = new System.Drawing.Point(0, 30);
            this.Tsp1.Name = "Tsp1";
            this.Tsp1.Size = new System.Drawing.Size(2420, 42);
            this.Tsp1.TabIndex = 9;
            this.Tsp1.Text = "toolStrip1";
            // 
            // TsBtn_Add
            // 

            this.TsBtn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Add.Name = "TsBtn_Add";
            this.TsBtn_Add.Size = new System.Drawing.Size(93, 36);
            this.TsBtn_Add.Text = "新建";
            // 
            // TsBtn_Edit
            // 

            this.TsBtn_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Edit.Name = "TsBtn_Edit";
            this.TsBtn_Edit.Size = new System.Drawing.Size(93, 36);
            this.TsBtn_Edit.Text = "编辑";
            // 
            // TsBtn_Delete
            // 

            this.TsBtn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Delete.Name = "TsBtn_Delete";
            this.TsBtn_Delete.Size = new System.Drawing.Size(93, 36);
            this.TsBtn_Delete.Text = "删除";
            // 
            // TsBtn_Save
            // 

            this.TsBtn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Save.Name = "TsBtn_Save";
            this.TsBtn_Save.Size = new System.Drawing.Size(93, 36);
            this.TsBtn_Save.Text = "保存";
            // 
            // TsBtn_Refresh
            // 

            this.TsBtn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsBtn_Refresh.Name = "TsBtn_Refresh";
            this.TsBtn_Refresh.Size = new System.Drawing.Size(93, 36);
            this.TsBtn_Refresh.Text = "刷新";
            this.TsBtn_Refresh.Click += new System.EventHandler(this.TsBtn_Refresh_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 42);
            // 
            // TsLbl_Find
            // 

            this.TsLbl_Find.Name = "TsLbl_Find";
            this.TsLbl_Find.Size = new System.Drawing.Size(94, 36);
            this.TsLbl_Find.Text = "搜索:";
            // 
            // Txt_FindResult
            // 
            this.Txt_FindResult.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Txt_FindResult.Name = "Txt_FindResult";
            this.Txt_FindResult.Size = new System.Drawing.Size(300, 42);
            this.Txt_FindResult.TextChanged += new System.EventHandler(this.Txt_FindResult_TextChanged);
            // 
            // dataSplitUserControl1
            // 
            this.dataSplitUserControl1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.dataSplitUserControl1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.dataSplitUserControl1.Location = new System.Drawing.Point(0, 601);
            this.dataSplitUserControl1.Margin = new System.Windows.Forms.Padding(4);
            this.dataSplitUserControl1.Name = "dataSplitUserControl1";
            this.dataSplitUserControl1.Size = new System.Drawing.Size(2420, 66);
            this.dataSplitUserControl1.TabIndex = 14;
            // 
            // VisibleScPane2
            // 
            this.VisibleScPane2.BackColor = System.Drawing.SystemColors.Control;
            this.VisibleScPane2.Cursor = System.Windows.Forms.Cursors.Hand;
            this.VisibleScPane2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.VisibleScPane2.Location = new System.Drawing.Point(0, 667);
            this.VisibleScPane2.Name = "VisibleScPane2";
            this.VisibleScPane2.Size = new System.Drawing.Size(2420, 30);
            this.VisibleScPane2.TabIndex = 13;
            this.VisibleScPane2.Text = "▼ 显示";
            this.VisibleScPane2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.VisibleScPane2.Click += new System.EventHandler(this.VisibleScPane2_Click);
            // 
            // VisibleScPane1
            // 
            this.VisibleScPane1.BackColor = System.Drawing.SystemColors.Control;
            this.VisibleScPane1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.VisibleScPane1.Dock = System.Windows.Forms.DockStyle.Top;
            this.VisibleScPane1.Location = new System.Drawing.Point(0, 0);
            this.VisibleScPane1.Name = "VisibleScPane1";
            this.VisibleScPane1.Size = new System.Drawing.Size(2420, 30);
            this.VisibleScPane1.TabIndex = 15;
            this.VisibleScPane1.Text = "▼ 隐藏";
            this.VisibleScPane1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.VisibleScPane1.Click += new System.EventHandler(this.VisibleScPane1_Click);
            // 
            // TabControl_BOM
            // 
            this.TabControl_BOM.Controls.Add(this.TabPage_BomList);
            this.TabControl_BOM.Controls.Add(this.TabPage_BomDetail);
            this.TabControl_BOM.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TabControl_BOM.Location = new System.Drawing.Point(0, 42);
            this.TabControl_BOM.Name = "TabControl_BOM";
            this.TabControl_BOM.SelectedIndex = 0;
            this.TabControl_BOM.Size = new System.Drawing.Size(2420, 487);
            this.TabControl_BOM.TabIndex = 0;
            // 
            // TabPage_BomList
            // 
            this.TabPage_BomList.Location = new System.Drawing.Point(8, 42);
            this.TabPage_BomList.Name = "TabPage_BomList";
            this.TabPage_BomList.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_BomList.Size = new System.Drawing.Size(2404, 437);
            this.TabPage_BomList.TabIndex = 0;
            this.TabPage_BomList.Text = "列表";
            this.TabPage_BomList.UseVisualStyleBackColor = true;
            // 
            // TabPage_BomDetail
            // 
            this.TabPage_BomDetail.Location = new System.Drawing.Point(8, 39);
            this.TabPage_BomDetail.Name = "TabPage_BomDetail";
            this.TabPage_BomDetail.Padding = new System.Windows.Forms.Padding(3);
            this.TabPage_BomDetail.Size = new System.Drawing.Size(2404, 421);
            this.TabPage_BomDetail.TabIndex = 1;
            this.TabPage_BomDetail.Text = "详细";
            this.TabPage_BomDetail.UseVisualStyleBackColor = true;
            // 
            // Tsp2
            // 
            this.Tsp2.BackColor = System.Drawing.SystemColors.Window;
            this.Tsp2.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Tsp2.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.Tsp2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Tsp2btn_Add,
            this.Tsp2btn_Edit,
            this.Tsp2btn_Delete,
            this.Tsp2btn_Save,
            this.Tsp2btn_Refresh,
            this.Tsp2Separator1,
            this.Tsp2Lbl_Filter,
            this.Tsp2Txt_FindResult});
            this.Tsp2.Location = new System.Drawing.Point(0, 0);
            this.Tsp2.Name = "Tsp2";
            this.Tsp2.Size = new System.Drawing.Size(2420, 42);
            this.Tsp2.TabIndex = 13;
            this.Tsp2.Text = "toolStrip1";
            // 
            // Tsp2btn_Add
            // 

            this.Tsp2btn_Add.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsp2btn_Add.Name = "Tsp2btn_Add";
            this.Tsp2btn_Add.Size = new System.Drawing.Size(93, 44);
            this.Tsp2btn_Add.Text = "新建";
            // 
            // Tsp2btn_Edit
            // 

            this.Tsp2btn_Edit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsp2btn_Edit.Name = "Tsp2btn_Edit";
            this.Tsp2btn_Edit.Size = new System.Drawing.Size(93, 44);
            this.Tsp2btn_Edit.Text = "编辑";
            // 
            // Tsp2btn_Delete
            // 

            this.Tsp2btn_Delete.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsp2btn_Delete.Name = "Tsp2btn_Delete";
            this.Tsp2btn_Delete.Size = new System.Drawing.Size(93, 44);
            this.Tsp2btn_Delete.Text = "删除";
            // 
            // Tsp2btn_Save
            // 

            this.Tsp2btn_Save.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsp2btn_Save.Name = "Tsp2btn_Save";
            this.Tsp2btn_Save.Size = new System.Drawing.Size(93, 44);
            this.Tsp2btn_Save.Text = "保存";
            // 
            // Tsp2btn_Refresh
            // 

            this.Tsp2btn_Refresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.Tsp2btn_Refresh.Name = "Tsp2btn_Refresh";
            this.Tsp2btn_Refresh.Size = new System.Drawing.Size(93, 44);
            this.Tsp2btn_Refresh.Text = "刷新";
            // 
            // Tsp2Separator1
            // 
            this.Tsp2Separator1.Name = "Tsp2Separator1";
            this.Tsp2Separator1.Size = new System.Drawing.Size(6, 50);
            // 
            // Tsp2Lbl_Filter
            // 

            this.Tsp2Lbl_Filter.Name = "Tsp2Lbl_Filter";
            this.Tsp2Lbl_Filter.Size = new System.Drawing.Size(94, 44);
            this.Tsp2Lbl_Filter.Text = "筛选:";
            // 
            // Tsp2Txt_FindResult
            // 
            this.Tsp2Txt_FindResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Tsp2Txt_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Tsp2Txt_FindResult.Name = "Tsp2Txt_FindResult";
            this.Tsp2Txt_FindResult.Size = new System.Drawing.Size(300, 50);
            // 
            // FrmSoOrderManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(2422, 1234);
            this.Controls.Add(this.SplitContainer1);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "FrmSoOrderManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "销售订单";
            this.Shown += new System.EventHandler(this.FrmSoOrderManager_Shown);
            this.SplitContainer1.Panel1.ResumeLayout(false);
            this.SplitContainer1.Panel1.PerformLayout();
            this.SplitContainer1.Panel2.ResumeLayout(false);
            this.SplitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SplitContainer1)).EndInit();
            this.SplitContainer1.ResumeLayout(false);
            this.TabControl_Product.ResumeLayout(false);
            this.TabPage_ProductList.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.DgvProduct)).EndInit();
            this.Tsp1.ResumeLayout(false);
            this.Tsp1.PerformLayout();
            this.TabControl_BOM.ResumeLayout(false);
            this.Tsp2.ResumeLayout(false);
            this.Tsp2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.SplitContainer SplitContainer1;
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
        private System.Windows.Forms.ToolStrip Tsp2;
        private System.Windows.Forms.ToolStripButton Tsp2btn_Add;
        private System.Windows.Forms.ToolStripButton Tsp2btn_Edit;
        private System.Windows.Forms.ToolStripButton Tsp2btn_Delete;
        private System.Windows.Forms.ToolStripButton Tsp2btn_Save;
        private System.Windows.Forms.ToolStripButton Tsp2btn_Refresh;
        private System.Windows.Forms.ToolStripSeparator Tsp2Separator1;
        private System.Windows.Forms.ToolStripLabel Tsp2Lbl_Filter;
        private System.Windows.Forms.ToolStripTextBox Tsp2Txt_FindResult;
        private System.Windows.Forms.Label VisibleScPane2;
        private AppUserControls.DataSplitUserControl dataSplitUserControl1;
        private System.Windows.Forms.ToolStripComboBox Txt_FindResult;
        private System.Windows.Forms.Label VisibleScPane1;
    }
}