using System.Drawing;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork
{
    partial class ProductUserControl
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
            this.Product_tabCtrl_Product = new System.Windows.Forms.TabControl();
            this.tbP_Product = new System.Windows.Forms.TabPage();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.Product_Panel_Top = new System.Windows.Forms.Panel();
            this.Txtbox_搜索结果 = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.Product_Panel_TopLeft = new System.Windows.Forms.Panel();
            this.Product_Panel_Menu = new System.Windows.Forms.Panel();
            this.Cmb_CustomerCode = new System.Windows.Forms.ComboBox();
            this.Product_Panel_Product = new System.Windows.Forms.Panel();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.Product_Panel_Status = new System.Windows.Forms.Panel();
            this.cmbPage = new System.Windows.Forms.ComboBox();
            this.LblDatCount = new System.Windows.Forms.Label();
            this.CbmEachItemPage = new System.Windows.Forms.ComboBox();
            this.LblEachItemPage = new System.Windows.Forms.Label();
            this.BtnFirst = new System.Windows.Forms.Button();
            this.BtnPrev = new System.Windows.Forms.Button();
            this.BtnNext = new System.Windows.Forms.Button();
            this.BtnLast = new System.Windows.Forms.Button();
            this.BottomToolStripPanel = new System.Windows.Forms.ToolStripPanel();
            this.TopToolStripPanel = new System.Windows.Forms.ToolStripPanel();
            this.RightToolStripPanel = new System.Windows.Forms.ToolStripPanel();
            this.LeftToolStripPanel = new System.Windows.Forms.ToolStripPanel();
            this.ContentPanel = new System.Windows.Forms.ToolStripContentPanel();
            this.Product_tabCtrl_Product.SuspendLayout();
            this.tbP_Product.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.Product_Panel_Top.SuspendLayout();
            this.Product_Panel_Menu.SuspendLayout();
            this.Product_Panel_Product.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.Product_Panel_Status.SuspendLayout();
            this.SuspendLayout();
            // 
            // Product_tabCtrl_Product
            // 
            this.Product_tabCtrl_Product.Controls.Add(this.tbP_Product);
            this.Product_tabCtrl_Product.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Product_tabCtrl_Product.Font = new System.Drawing.Font("Microsoft YaHei UI", 10.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Product_tabCtrl_Product.Location = new System.Drawing.Point(0, 0);
            this.Product_tabCtrl_Product.Name = "Product_tabCtrl_Product";
            this.Product_tabCtrl_Product.SelectedIndex = 0;
            this.Product_tabCtrl_Product.Size = new System.Drawing.Size(2477, 1315);
            this.Product_tabCtrl_Product.TabIndex = 0;
            // 
            // tbP_Product
            // 
            this.tbP_Product.Controls.Add(this.tableLayoutPanel1);
            this.tbP_Product.Location = new System.Drawing.Point(4, 39);
            this.tbP_Product.Name = "tbP_Product";
            this.tbP_Product.Padding = new System.Windows.Forms.Padding(3, 3, 3, 3);
            this.tbP_Product.Size = new System.Drawing.Size(2469, 1272);
            this.tbP_Product.TabIndex = 0;
            this.tbP_Product.Text = "产品";
            this.tbP_Product.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 220F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_Top, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_TopLeft, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_Menu, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_Product, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_Status, 1, 2);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(3, 3);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 3;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 77F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 77F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(2463, 1266);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // Product_Panel_Top
            // 
            this.Product_Panel_Top.Controls.Add(this.Txtbox_搜索结果);
            this.Product_Panel_Top.Controls.Add(this.button1);
            this.Product_Panel_Top.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Product_Panel_Top.Location = new System.Drawing.Point(223, 3);
            this.Product_Panel_Top.Name = "Product_Panel_Top";
            this.Product_Panel_Top.Size = new System.Drawing.Size(2237, 71);
            this.Product_Panel_Top.TabIndex = 3;
            // 
            // Txtbox_搜索结果
            // 
            this.Txtbox_搜索结果.Dock = System.Windows.Forms.DockStyle.Left;
            this.Txtbox_搜索结果.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Txtbox_搜索结果.Location = new System.Drawing.Point(0, 0);
            this.Txtbox_搜索结果.Name = "Txtbox_搜索结果";
            this.Txtbox_搜索结果.Size = new System.Drawing.Size(545, 38);
            this.Txtbox_搜索结果.TabIndex = 4;
            this.Txtbox_搜索结果.TextChanged += new System.EventHandler(this.Txtbox_搜索结果_TextChanged);
            // 
            // button1
            // 
            this.button1.Dock = System.Windows.Forms.DockStyle.Right;
            this.button1.Location = new System.Drawing.Point(2082, 0);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(155, 71);
            this.button1.TabIndex = 3;
            this.button1.Text = "新增";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // Product_Panel_TopLeft
            // 
            this.Product_Panel_TopLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Product_Panel_TopLeft.Location = new System.Drawing.Point(3, 3);
            this.Product_Panel_TopLeft.Name = "Product_Panel_TopLeft";
            this.Product_Panel_TopLeft.Size = new System.Drawing.Size(214, 71);
            this.Product_Panel_TopLeft.TabIndex = 4;
            // 
            // Product_Panel_Menu
            // 
            this.Product_Panel_Menu.Controls.Add(this.Cmb_CustomerCode);
            this.Product_Panel_Menu.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Product_Panel_Menu.Location = new System.Drawing.Point(3, 80);
            this.Product_Panel_Menu.Name = "Product_Panel_Menu";
            this.tableLayoutPanel1.SetRowSpan(this.Product_Panel_Menu, 2);
            this.Product_Panel_Menu.Size = new System.Drawing.Size(214, 1183);
            this.Product_Panel_Menu.TabIndex = 5;
            // 
            // Cmb_CustomerCode
            // 
            this.Cmb_CustomerCode.Dock = System.Windows.Forms.DockStyle.Top;
            this.Cmb_CustomerCode.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Cmb_CustomerCode.FormattingEnabled = true;
            this.Cmb_CustomerCode.Location = new System.Drawing.Point(0, 0);
            this.Cmb_CustomerCode.Name = "Cmb_CustomerCode";
            this.Cmb_CustomerCode.Size = new System.Drawing.Size(214, 39);
            this.Cmb_CustomerCode.TabIndex = 5;
            // 
            // Product_Panel_Product
            // 
            this.Product_Panel_Product.Controls.Add(this.dataGridView1);
            this.Product_Panel_Product.Location = new System.Drawing.Point(223, 80);
            this.Product_Panel_Product.Name = "Product_Panel_Product";
            this.Product_Panel_Product.Size = new System.Drawing.Size(1785, 787);
            this.Product_Panel_Product.TabIndex = 6;
            // 
            // dataGridView1
            // 
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 82;
            this.dataGridView1.RowTemplate.Height = 120;
            this.dataGridView1.Size = new System.Drawing.Size(1240, 547);
            this.dataGridView1.TabIndex = 0;
            // 
            // Product_Panel_Status
            // 
            this.Product_Panel_Status.Controls.Add(this.cmbPage);
            this.Product_Panel_Status.Controls.Add(this.LblDatCount);
            this.Product_Panel_Status.Controls.Add(this.CbmEachItemPage);
            this.Product_Panel_Status.Controls.Add(this.LblEachItemPage);
            this.Product_Panel_Status.Controls.Add(this.BtnFirst);
            this.Product_Panel_Status.Controls.Add(this.BtnPrev);
            this.Product_Panel_Status.Controls.Add(this.BtnNext);
            this.Product_Panel_Status.Controls.Add(this.BtnLast);
            this.Product_Panel_Status.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Product_Panel_Status.Location = new System.Drawing.Point(223, 1192);
            this.Product_Panel_Status.Name = "Product_Panel_Status";
            this.Product_Panel_Status.Size = new System.Drawing.Size(2237, 71);
            this.Product_Panel_Status.TabIndex = 7;
            // 
            // cmbPage
            // 
            this.cmbPage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPage.FormattingEnabled = true;
            this.cmbPage.Location = new System.Drawing.Point(899, 3);
            this.cmbPage.Name = "cmbPage";
            this.cmbPage.Size = new System.Drawing.Size(175, 38);
            this.cmbPage.TabIndex = 7;
            // 
            // LblDatCount
            // 
            this.LblDatCount.AutoSize = true;
            this.LblDatCount.Dock = System.Windows.Forms.DockStyle.Left;
            this.LblDatCount.Location = new System.Drawing.Point(0, 0);
            this.LblDatCount.Name = "LblDatCount";
            this.LblDatCount.Size = new System.Drawing.Size(71, 30);
            this.LblDatCount.TabIndex = 6;
            this.LblDatCount.Text = "共  条";
            // 
            // CbmEachItemPage
            // 
            this.CbmEachItemPage.Dock = System.Windows.Forms.DockStyle.Right;
            this.CbmEachItemPage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CbmEachItemPage.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.CbmEachItemPage.FormattingEnabled = true;
            this.CbmEachItemPage.Items.AddRange(new object[] {
            "20",
            "50",
            "100"});
            this.CbmEachItemPage.Location = new System.Drawing.Point(1350, 0);
            this.CbmEachItemPage.Name = "CbmEachItemPage";
            this.CbmEachItemPage.Size = new System.Drawing.Size(175, 39);
            this.CbmEachItemPage.TabIndex = 4;
            this.CbmEachItemPage.SelectedIndexChanged += new System.EventHandler(this.CbmEachItemPage_SelectedIndexChanged);
            // 
            // LblEachItemPage
            // 
            this.LblEachItemPage.AutoSize = true;
            this.LblEachItemPage.Dock = System.Windows.Forms.DockStyle.Right;
            this.LblEachItemPage.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.LblEachItemPage.Location = new System.Drawing.Point(1525, 0);
            this.LblEachItemPage.Name = "LblEachItemPage";
            this.LblEachItemPage.Size = new System.Drawing.Size(72, 31);
            this.LblEachItemPage.TabIndex = 5;
            this.LblEachItemPage.Text = "条/页";
            // 
            // BtnFirst
            // 
            this.BtnFirst.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnFirst.Location = new System.Drawing.Point(1597, 0);
            this.BtnFirst.Name = "BtnFirst";
            this.BtnFirst.Size = new System.Drawing.Size(160, 71);
            this.BtnFirst.TabIndex = 0;
            this.BtnFirst.Text = "第一页";
            this.BtnFirst.UseVisualStyleBackColor = true;
            this.BtnFirst.Click += new System.EventHandler(this.BtnFirst_Click);
            // 
            // BtnPrev
            // 
            this.BtnPrev.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnPrev.Location = new System.Drawing.Point(1757, 0);
            this.BtnPrev.Name = "BtnPrev";
            this.BtnPrev.Size = new System.Drawing.Size(160, 71);
            this.BtnPrev.TabIndex = 1;
            this.BtnPrev.Text = "上一页";
            this.BtnPrev.UseVisualStyleBackColor = true;
            this.BtnPrev.Click += new System.EventHandler(this.BtnPrev_Click);
            // 
            // BtnNext
            // 
            this.BtnNext.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnNext.Location = new System.Drawing.Point(1917, 0);
            this.BtnNext.Name = "BtnNext";
            this.BtnNext.Size = new System.Drawing.Size(160, 71);
            this.BtnNext.TabIndex = 2;
            this.BtnNext.Text = "下一页";
            this.BtnNext.UseVisualStyleBackColor = true;
            this.BtnNext.Click += new System.EventHandler(this.BtnNext_Click);
            // 
            // BtnLast
            // 
            this.BtnLast.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnLast.Location = new System.Drawing.Point(2077, 0);
            this.BtnLast.Name = "BtnLast";
            this.BtnLast.Size = new System.Drawing.Size(160, 71);
            this.BtnLast.TabIndex = 3;
            this.BtnLast.Text = "最后页";
            this.BtnLast.UseVisualStyleBackColor = true;
            this.BtnLast.Click += new System.EventHandler(this.BtnLast_Click);
            // 
            // BottomToolStripPanel
            // 
            this.BottomToolStripPanel.Location = new System.Drawing.Point(0, 0);
            this.BottomToolStripPanel.Name = "BottomToolStripPanel";
            this.BottomToolStripPanel.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.BottomToolStripPanel.RowMargin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.BottomToolStripPanel.Size = new System.Drawing.Size(0, 0);
            // 
            // TopToolStripPanel
            // 
            this.TopToolStripPanel.Location = new System.Drawing.Point(0, 0);
            this.TopToolStripPanel.Name = "TopToolStripPanel";
            this.TopToolStripPanel.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.TopToolStripPanel.RowMargin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.TopToolStripPanel.Size = new System.Drawing.Size(0, 0);
            // 
            // RightToolStripPanel
            // 
            this.RightToolStripPanel.Location = new System.Drawing.Point(0, 0);
            this.RightToolStripPanel.Name = "RightToolStripPanel";
            this.RightToolStripPanel.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.RightToolStripPanel.RowMargin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.RightToolStripPanel.Size = new System.Drawing.Size(0, 0);
            // 
            // LeftToolStripPanel
            // 
            this.LeftToolStripPanel.Location = new System.Drawing.Point(0, 0);
            this.LeftToolStripPanel.Name = "LeftToolStripPanel";
            this.LeftToolStripPanel.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.LeftToolStripPanel.RowMargin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.LeftToolStripPanel.Size = new System.Drawing.Size(0, 0);
            // 
            // ContentPanel
            // 
            this.ContentPanel.Size = new System.Drawing.Size(1920, 1101);
            // 
            // ProductUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Product_tabCtrl_Product);
            this.Name = "ProductUserControl";
            this.Size = new System.Drawing.Size(2477, 1315);
            this.Product_tabCtrl_Product.ResumeLayout(false);
            this.tbP_Product.ResumeLayout(false);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.Product_Panel_Top.ResumeLayout(false);
            this.Product_Panel_Top.PerformLayout();
            this.Product_Panel_Menu.ResumeLayout(false);
            this.Product_Panel_Product.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.Product_Panel_Status.ResumeLayout(false);
            this.Product_Panel_Status.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private TabControl Product_tabCtrl_Product;
        private TabPage tbP_Product;
        private ToolStripPanel BottomToolStripPanel;
        private ToolStripPanel TopToolStripPanel;
        private ToolStripPanel RightToolStripPanel;
        private ToolStripPanel LeftToolStripPanel;
        private ToolStripContentPanel ContentPanel;
        private TableLayoutPanel tableLayoutPanel1;
        private DataGridView dataGridView1;
        private Panel Product_Panel_Top;
        private Button button1;
        private Panel Product_Panel_TopLeft;
        private Panel Product_Panel_Menu;
        private Panel Product_Panel_Product;
        private TextBox Txtbox_搜索结果;
        private Panel Product_Panel_Status;
        private ComboBox Cmb_CustomerCode;
        private Button BtnFirst;
        private Button BtnPrev;
        private Button BtnLast;
        private Button BtnNext;
        private Label LblEachItemPage;
        private ComboBox CbmEachItemPage;
        private Label LblDatCount;
        private ComboBox cmbPage;
    }
}
