using System.Drawing;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork
{
    partial class SalesOrderUserControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SalesOrderUserControl));
            this.Product_tabCtrl_Product = new System.Windows.Forms.TabControl();
            this.tbP_Product = new System.Windows.Forms.TabPage();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.toolStrip2 = new System.Windows.Forms.ToolStrip();
            this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton4 = new System.Windows.Forms.ToolStripButton();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.ToolStripButton_Save = new System.Windows.Forms.ToolStripButton();
            this.ToolStripButton_Add = new System.Windows.Forms.ToolStripButton();
            this.ToolStripComboBox_QueryField = new System.Windows.Forms.ToolStripComboBox();
            this.ToolStripTextBox_QueryResult = new System.Windows.Forms.ToolStripTextBox();
            this.ToolStripButton_Query = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel_Find = new System.Windows.Forms.ToolStripLabel();
            this.toolStripTextBox_FindResult = new System.Windows.Forms.ToolStripTextBox();
            this.TSDataStatus = new System.Windows.Forms.ToolStrip();
            this.TSLblDataCount = new System.Windows.Forms.ToolStripLabel();
            this.TSLblPageInfo = new System.Windows.Forms.ToolStripLabel();
            this.TSlblPgUnit = new System.Windows.Forms.ToolStripLabel();
            this.TscmbPgIndex = new System.Windows.Forms.ToolStripComboBox();
            this.TslblGotoPg = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.TsbtnLstPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnNextPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnPrePg = new System.Windows.Forms.ToolStripButton();
            this.TSbtnFstPg = new System.Windows.Forms.ToolStripButton();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
            this.dataGridView2 = new System.Windows.Forms.DataGridView();
            this.Product_Panel_Top = new System.Windows.Forms.Panel();
            this.Product_Panel_Product = new System.Windows.Forms.Panel();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.Product_Panel_Menu = new System.Windows.Forms.Panel();
            this.Cmb_CustomerCode = new System.Windows.Forms.ComboBox();
            this.sptctneDetales = new System.Windows.Forms.SplitContainer();
            this.tabPage1 = new System.Windows.Forms.TabPage();
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
            this.tableLayoutPanel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.toolStrip2.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.TSDataStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.tableLayoutPanel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.Product_Panel_Menu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.sptctneDetales)).BeginInit();
            this.sptctneDetales.SuspendLayout();
            this.Product_Panel_Status.SuspendLayout();
            this.SuspendLayout();
            // 
            // Product_tabCtrl_Product
            // 
            this.Product_tabCtrl_Product.Controls.Add(this.tbP_Product);
            this.Product_tabCtrl_Product.Controls.Add(this.tabPage1);
            this.Product_tabCtrl_Product.Font = new System.Drawing.Font("Microsoft YaHei UI", 10.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Product_tabCtrl_Product.Location = new System.Drawing.Point(46, 26);
            this.Product_tabCtrl_Product.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Product_tabCtrl_Product.Name = "Product_tabCtrl_Product";
            this.Product_tabCtrl_Product.SelectedIndex = 0;
            this.Product_tabCtrl_Product.Size = new System.Drawing.Size(2443, 1152);
            this.Product_tabCtrl_Product.TabIndex = 0;
            // 
            // tbP_Product
            // 
            this.tbP_Product.Controls.Add(this.tableLayoutPanel2);
            this.tbP_Product.Controls.Add(this.toolStrip2);
            this.tbP_Product.Controls.Add(this.toolStrip1);
            this.tbP_Product.Controls.Add(this.TSDataStatus);
            this.tbP_Product.Controls.Add(this.splitContainer1);
            this.tbP_Product.Controls.Add(this.Product_Panel_Top);
            this.tbP_Product.Controls.Add(this.Product_Panel_Product);
            this.tbP_Product.Controls.Add(this.tableLayoutPanel1);
            this.tbP_Product.Controls.Add(this.sptctneDetales);
            this.tbP_Product.Location = new System.Drawing.Point(4, 39);
            this.tbP_Product.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.tbP_Product.Name = "tbP_Product";
            this.tbP_Product.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.tbP_Product.Size = new System.Drawing.Size(2435, 1109);
            this.tbP_Product.TabIndex = 0;
            this.tbP_Product.Text = "订单";
            this.tbP_Product.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel2
            // 
            this.tableLayoutPanel2.ColumnCount = 1;
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel2.Controls.Add(this.dataGridView1, 0, 1);
            this.tableLayoutPanel2.Location = new System.Drawing.Point(89, 423);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 3;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.93182F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 54.73485F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(926, 268);
            this.tableLayoutPanel2.TabIndex = 8;
            // 
            // dataGridView1
            // 
            this.dataGridView1.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(4, 34);
            this.dataGridView1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 82;
            this.dataGridView1.RowTemplate.Height = 120;
            this.dataGridView1.Size = new System.Drawing.Size(865, 119);
            this.dataGridView1.TabIndex = 0;
            // 
            // toolStrip2
            // 
            this.toolStrip2.Dock = System.Windows.Forms.DockStyle.None;
            this.toolStrip2.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton2,
            this.toolStripButton3,
            this.toolStripButton4});
            this.toolStrip2.Location = new System.Drawing.Point(1448, 358);
            this.toolStrip2.Name = "toolStrip2";
            this.toolStrip2.Size = new System.Drawing.Size(264, 41);
            this.toolStrip2.TabIndex = 1;
            this.toolStrip2.Text = "toolStrip2";
            // 
            // toolStripButton2
            // 
            this.toolStripButton2.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton2.Image")));
            this.toolStripButton2.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton2.Name = "toolStripButton2";
            this.toolStripButton2.Size = new System.Drawing.Size(82, 36);
            this.toolStripButton2.Text = "新增";
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton3.Image")));
            this.toolStripButton3.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Size = new System.Drawing.Size(82, 36);
            this.toolStripButton3.Text = "删除";
            // 
            // toolStripButton4
            // 
            this.toolStripButton4.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton4.Image")));
            this.toolStripButton4.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton4.Name = "toolStripButton4";
            this.toolStripButton4.Size = new System.Drawing.Size(82, 36);
            this.toolStripButton4.Text = "保存";
            // 
            // toolStrip1
            // 
            this.toolStrip1.Dock = System.Windows.Forms.DockStyle.None;
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
            this.toolStrip1.Location = new System.Drawing.Point(158, 997);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(1103, 41);
            this.toolStrip1.TabIndex = 5;
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
            this.toolStripLabel_Find.Size = new System.Drawing.Size(122, 36);
            this.toolStripLabel_Find.Text = "搜索当前结果:";
            // 
            // toolStripTextBox_FindResult
            // 
            this.toolStripTextBox_FindResult.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.toolStripTextBox_FindResult.Name = "toolStripTextBox_FindResult";
            this.toolStripTextBox_FindResult.Size = new System.Drawing.Size(233, 41);
            // 
            // TSDataStatus
            // 
            this.TSDataStatus.Dock = System.Windows.Forms.DockStyle.None;
            this.TSDataStatus.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.TSDataStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TSLblDataCount,
            this.TSLblPageInfo,
            this.TSlblPgUnit,
            this.TscmbPgIndex,
            this.TslblGotoPg,
            this.toolStripSeparator2,
            this.TsbtnLstPg,
            this.TsbtnNextPg,
            this.TsbtnPrePg,
            this.TSbtnFstPg});
            this.TSDataStatus.Location = new System.Drawing.Point(158, 928);
            this.TSDataStatus.Name = "TSDataStatus";
            this.TSDataStatus.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.TSDataStatus.Size = new System.Drawing.Size(756, 41);
            this.TSDataStatus.TabIndex = 7;
            this.TSDataStatus.Text = "toolStrip2";
            // 
            // TSLblDataCount
            // 
            this.TSLblDataCount.Name = "TSLblDataCount";
            this.TSLblDataCount.Size = new System.Drawing.Size(96, 36);
            this.TSLblDataCount.Text = "总记录  条.";
            // 
            // TSLblPageInfo
            // 
            this.TSLblPageInfo.Name = "TSLblPageInfo";
            this.TSLblPageInfo.Size = new System.Drawing.Size(109, 36);
            this.TSLblPageInfo.Text = "当前第  / 页.";
            // 
            // TSlblPgUnit
            // 
            this.TSlblPgUnit.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSlblPgUnit.Name = "TSlblPgUnit";
            this.TSlblPgUnit.Size = new System.Drawing.Size(28, 36);
            this.TSlblPgUnit.Text = "页";
            // 
            // TscmbPgIndex
            // 
            this.TscmbPgIndex.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TscmbPgIndex.Name = "TscmbPgIndex";
            this.TscmbPgIndex.Size = new System.Drawing.Size(121, 41);
            // 
            // TslblGotoPg
            // 
            this.TslblGotoPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TslblGotoPg.Name = "TslblGotoPg";
            this.TslblGotoPg.Size = new System.Drawing.Size(46, 36);
            this.TslblGotoPg.Text = "前往";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 41);
            // 
            // TsbtnLstPg
            // 
            this.TsbtnLstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnLstPg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnLstPg.Image")));
            this.TsbtnLstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnLstPg.Name = "TsbtnLstPg";
            this.TsbtnLstPg.Size = new System.Drawing.Size(82, 36);
            this.TsbtnLstPg.Text = "尾页";
            // 
            // TsbtnNextPg
            // 
            this.TsbtnNextPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnNextPg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnNextPg.Image")));
            this.TsbtnNextPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnNextPg.Name = "TsbtnNextPg";
            this.TsbtnNextPg.Size = new System.Drawing.Size(82, 36);
            this.TsbtnNextPg.Text = "下页";
            // 
            // TsbtnPrePg
            // 
            this.TsbtnPrePg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnPrePg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnPrePg.Image")));
            this.TsbtnPrePg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnPrePg.Name = "TsbtnPrePg";
            this.TsbtnPrePg.Size = new System.Drawing.Size(82, 36);
            this.TsbtnPrePg.Text = "上页";
            // 
            // TSbtnFstPg
            // 
            this.TSbtnFstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSbtnFstPg.Image = ((System.Drawing.Image)(resources.GetObject("TSbtnFstPg.Image")));
            this.TSbtnFstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TSbtnFstPg.Name = "TSbtnFstPg";
            this.TSbtnFstPg.Size = new System.Drawing.Size(82, 36);
            this.TSbtnFstPg.Text = "首页";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Location = new System.Drawing.Point(1041, 453);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.tableLayoutPanel3);
            this.splitContainer1.Size = new System.Drawing.Size(1293, 481);
            this.splitContainer1.SplitterDistance = 77;
            this.splitContainer1.TabIndex = 12;
            // 
            // tableLayoutPanel3
            // 
            this.tableLayoutPanel3.ColumnCount = 1;
            this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel3.Controls.Add(this.dataGridView2, 0, 1);
            this.tableLayoutPanel3.Location = new System.Drawing.Point(82, 38);
            this.tableLayoutPanel3.Name = "tableLayoutPanel3";
            this.tableLayoutPanel3.RowCount = 3;
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel3.Size = new System.Drawing.Size(1117, 277);
            this.tableLayoutPanel3.TabIndex = 11;
            // 
            // dataGridView2
            // 
            this.dataGridView2.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView2.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView2.Location = new System.Drawing.Point(3, 43);
            this.dataGridView2.Name = "dataGridView2";
            this.dataGridView2.RowHeadersWidth = 82;
            this.dataGridView2.RowTemplate.Height = 37;
            this.dataGridView2.Size = new System.Drawing.Size(1079, 191);
            this.dataGridView2.TabIndex = 10;
            // 
            // Product_Panel_Top
            // 
            this.Product_Panel_Top.Location = new System.Drawing.Point(1433, 52);
            this.Product_Panel_Top.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Product_Panel_Top.Name = "Product_Panel_Top";
            this.Product_Panel_Top.Size = new System.Drawing.Size(265, 57);
            this.Product_Panel_Top.TabIndex = 3;
            // 
            // Product_Panel_Product
            // 
            this.Product_Panel_Product.Location = new System.Drawing.Point(1796, 52);
            this.Product_Panel_Product.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Product_Panel_Product.Name = "Product_Panel_Product";
            this.Product_Panel_Product.Size = new System.Drawing.Size(282, 208);
            this.Product_Panel_Product.TabIndex = 6;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 140F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.Product_Panel_Menu, 0, 1);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(35, 32);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 3;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 69F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 57F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(1178, 344);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // Product_Panel_Menu
            // 
            this.Product_Panel_Menu.Controls.Add(this.Cmb_CustomerCode);
            this.Product_Panel_Menu.Location = new System.Drawing.Point(4, 72);
            this.Product_Panel_Menu.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Product_Panel_Menu.Name = "Product_Panel_Menu";
            this.tableLayoutPanel1.SetRowSpan(this.Product_Panel_Menu, 2);
            this.Product_Panel_Menu.Size = new System.Drawing.Size(118, 219);
            this.Product_Panel_Menu.TabIndex = 5;
            // 
            // Cmb_CustomerCode
            // 
            this.Cmb_CustomerCode.Dock = System.Windows.Forms.DockStyle.Top;
            this.Cmb_CustomerCode.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Cmb_CustomerCode.FormattingEnabled = true;
            this.Cmb_CustomerCode.Location = new System.Drawing.Point(0, 0);
            this.Cmb_CustomerCode.Margin = new System.Windows.Forms.Padding(4);
            this.Cmb_CustomerCode.Name = "Cmb_CustomerCode";
            this.Cmb_CustomerCode.Size = new System.Drawing.Size(118, 39);
            this.Cmb_CustomerCode.TabIndex = 5;
            // 
            // sptctneDetales
            // 
            this.sptctneDetales.Location = new System.Drawing.Point(107, 738);
            this.sptctneDetales.Name = "sptctneDetales";
            this.sptctneDetales.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.sptctneDetales.Size = new System.Drawing.Size(278, 138);
            this.sptctneDetales.SplitterDistance = 25;
            this.sptctneDetales.TabIndex = 9;
            // 
            // tabPage1
            // 
            this.tabPage1.Location = new System.Drawing.Point(4, 39);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Size = new System.Drawing.Size(2435, 1109);
            this.tabPage1.TabIndex = 1;
            this.tabPage1.Text = "订单进度";
            this.tabPage1.UseVisualStyleBackColor = true;
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
            this.Product_Panel_Status.Location = new System.Drawing.Point(456, 1554);
            this.Product_Panel_Status.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Product_Panel_Status.Name = "Product_Panel_Status";
            this.Product_Panel_Status.Size = new System.Drawing.Size(1911, 94);
            this.Product_Panel_Status.TabIndex = 7;
            // 
            // cmbPage
            // 
            this.cmbPage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPage.FormattingEnabled = true;
            this.cmbPage.Location = new System.Drawing.Point(1049, 4);
            this.cmbPage.Margin = new System.Windows.Forms.Padding(4);
            this.cmbPage.Name = "cmbPage";
            this.cmbPage.Size = new System.Drawing.Size(204, 32);
            this.cmbPage.TabIndex = 7;
            // 
            // LblDatCount
            // 
            this.LblDatCount.AutoSize = true;
            this.LblDatCount.Dock = System.Windows.Forms.DockStyle.Left;
            this.LblDatCount.Location = new System.Drawing.Point(0, 0);
            this.LblDatCount.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.LblDatCount.Name = "LblDatCount";
            this.LblDatCount.Size = new System.Drawing.Size(56, 24);
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
            this.CbmEachItemPage.Location = new System.Drawing.Point(887, 0);
            this.CbmEachItemPage.Margin = new System.Windows.Forms.Padding(4);
            this.CbmEachItemPage.Name = "CbmEachItemPage";
            this.CbmEachItemPage.Size = new System.Drawing.Size(204, 39);
            this.CbmEachItemPage.TabIndex = 4;
            this.CbmEachItemPage.SelectedIndexChanged += new System.EventHandler(this.CbmEachItemPage_SelectedIndexChanged);
            // 
            // LblEachItemPage
            // 
            this.LblEachItemPage.AutoSize = true;
            this.LblEachItemPage.Dock = System.Windows.Forms.DockStyle.Right;
            this.LblEachItemPage.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.LblEachItemPage.Location = new System.Drawing.Point(1091, 0);
            this.LblEachItemPage.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.LblEachItemPage.Name = "LblEachItemPage";
            this.LblEachItemPage.Size = new System.Drawing.Size(72, 31);
            this.LblEachItemPage.TabIndex = 5;
            this.LblEachItemPage.Text = "条/页";
            // 
            // BtnFirst
            // 
            this.BtnFirst.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnFirst.Location = new System.Drawing.Point(1163, 0);
            this.BtnFirst.Margin = new System.Windows.Forms.Padding(4);
            this.BtnFirst.Name = "BtnFirst";
            this.BtnFirst.Size = new System.Drawing.Size(187, 94);
            this.BtnFirst.TabIndex = 0;
            this.BtnFirst.Text = "第一页";
            this.BtnFirst.UseVisualStyleBackColor = true;
            this.BtnFirst.Click += new System.EventHandler(this.BtnFirst_Click);
            // 
            // BtnPrev
            // 
            this.BtnPrev.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnPrev.Location = new System.Drawing.Point(1350, 0);
            this.BtnPrev.Margin = new System.Windows.Forms.Padding(4);
            this.BtnPrev.Name = "BtnPrev";
            this.BtnPrev.Size = new System.Drawing.Size(187, 94);
            this.BtnPrev.TabIndex = 1;
            this.BtnPrev.Text = "上一页";
            this.BtnPrev.UseVisualStyleBackColor = true;
            this.BtnPrev.Click += new System.EventHandler(this.BtnPrev_Click);
            // 
            // BtnNext
            // 
            this.BtnNext.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnNext.Location = new System.Drawing.Point(1537, 0);
            this.BtnNext.Margin = new System.Windows.Forms.Padding(4);
            this.BtnNext.Name = "BtnNext";
            this.BtnNext.Size = new System.Drawing.Size(187, 94);
            this.BtnNext.TabIndex = 2;
            this.BtnNext.Text = "下一页";
            this.BtnNext.UseVisualStyleBackColor = true;
            this.BtnNext.Click += new System.EventHandler(this.BtnNext_Click);
            // 
            // BtnLast
            // 
            this.BtnLast.Dock = System.Windows.Forms.DockStyle.Right;
            this.BtnLast.Location = new System.Drawing.Point(1724, 0);
            this.BtnLast.Margin = new System.Windows.Forms.Padding(4);
            this.BtnLast.Name = "BtnLast";
            this.BtnLast.Size = new System.Drawing.Size(187, 94);
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
            // SalesOrderUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Product_tabCtrl_Product);
            this.Controls.Add(this.Product_Panel_Status);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Name = "SalesOrderUserControl";
            this.Size = new System.Drawing.Size(2578, 1236);
            this.Product_tabCtrl_Product.ResumeLayout(false);
            this.tbP_Product.ResumeLayout(false);
            this.tbP_Product.PerformLayout();
            this.tableLayoutPanel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.toolStrip2.ResumeLayout(false);
            this.toolStrip2.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.TSDataStatus.ResumeLayout(false);
            this.TSDataStatus.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.tableLayoutPanel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.Product_Panel_Menu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.sptctneDetales)).EndInit();
            this.sptctneDetales.ResumeLayout(false);
            this.Product_Panel_Status.ResumeLayout(false);
            this.Product_Panel_Status.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private TabControl Product_tabCtrl_Product;
        private TabPage tbP_Product;
        private TableLayoutPanel tableLayoutPanel1;
        private DataGridView dataGridView1;
        private Panel Product_Panel_Top;
        private Panel Product_Panel_Menu;
        private Panel Product_Panel_Product;
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
        private TabPage tabPage1;
        private ToolStrip toolStrip1;
        private ToolStripButton ToolStripButton_Add;
        private ToolStripButton ToolStripButton_Save;
        private ToolStripButton ToolStripButton_Query;
        private ToolStripPanel BottomToolStripPanel;
        private ToolStripPanel TopToolStripPanel;
        private ToolStripPanel RightToolStripPanel;
        private ToolStripPanel LeftToolStripPanel;
        private ToolStripContentPanel ContentPanel;
        private ToolStripComboBox ToolStripComboBox_QueryField;
        private ToolStripTextBox ToolStripTextBox_QueryResult;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripLabel toolStripLabel_Find;
        private ToolStripTextBox toolStripTextBox_FindResult;
        private ToolStrip TSDataStatus;
        private ToolStripLabel TSLblDataCount;
        private ToolStripLabel TSLblPageInfo;
        private ToolStripButton TSbtnFstPg;
        private ToolStripButton TsbtnPrePg;
        private ToolStripButton TsbtnNextPg;
        private ToolStripButton TsbtnLstPg;
        private ToolStripLabel TSlblPgUnit;
        private ToolStripComboBox TscmbPgIndex;
        private ToolStripLabel TslblGotoPg;
        private ToolStripSeparator toolStripSeparator2;
        private TableLayoutPanel tableLayoutPanel2;
        private SplitContainer sptctneDetales;
        private ToolStrip toolStrip2;
        private ToolStripButton toolStripButton2;
        private ToolStripButton toolStripButton3;
        private ToolStripButton toolStripButton4;
        private DataGridView dataGridView2;
        private SplitContainer splitContainer1;
        private TableLayoutPanel tableLayoutPanel3;
    }
}
