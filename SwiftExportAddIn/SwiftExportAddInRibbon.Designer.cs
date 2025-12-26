namespace SwiftExportAddIn
{
    partial class SwiftExportAddInRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public SwiftExportAddInRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

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
            this.tab1 = this.Factory.CreateRibbonTab();
            this.tab2 = this.Factory.CreateRibbonTab();
            this.Rbon_Login_Group = this.Factory.CreateRibbonGroup();
            this.Banna_Login = this.Factory.CreateRibbonButton();
            this.Rbon_Product_Group = this.Factory.CreateRibbonGroup();
            this.Rbn_btn_Product = this.Factory.CreateRibbonButton();
            this.Rbn_btn_BOM = this.Factory.CreateRibbonButton();
            this.Rbon_Sales_Group = this.Factory.CreateRibbonGroup();
            this.Rbn_btn_SaleProduct = this.Factory.CreateRibbonButton();
            this.Rbn_btn_SaleOrder = this.Factory.CreateRibbonButton();
            this.Rbon_Purcase_Group = this.Factory.CreateRibbonGroup();
            this.Rbn_btn_PurchaseProduct = this.Factory.CreateRibbonButton();
            this.Rbn_btn_Supplier = this.Factory.CreateRibbonButton();
            this.Rbon_Shipped_Group = this.Factory.CreateRibbonGroup();
            this.Rbn_btn_GenJinOrder = this.Factory.CreateRibbonButton();
            this.Rbn_btn_Shipped = this.Factory.CreateRibbonButton();
            this.Rbn_btn_PurchaseShouHuo = this.Factory.CreateRibbonButton();
            this.Rbon_BaseSetting_Group = this.Factory.CreateRibbonGroup();
            this.SptBtn_BaseSetting = this.Factory.CreateRibbonSplitButton();
            this.Btn_BaseSetting_FrmCustomersManager = this.Factory.CreateRibbonButton();
            this.Btn_BaseSetting_FrmExcelFieldMappManager = this.Factory.CreateRibbonButton();
            this.Btn_BaseSetting_FrmProductColorManager = this.Factory.CreateRibbonButton();
            this.Btn_BaseSetting_FrmSupplierManager = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.tab2.SuspendLayout();
            this.Rbon_Login_Group.SuspendLayout();
            this.Rbon_Product_Group.SuspendLayout();
            this.Rbon_Sales_Group.SuspendLayout();
            this.Rbon_Purcase_Group.SuspendLayout();
            this.Rbon_Shipped_Group.SuspendLayout();
            this.Rbon_BaseSetting_Group.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab1.Label = "TabAddIns";
            this.tab1.Name = "tab1";
            // 
            // tab2
            // 
            this.tab2.Groups.Add(this.Rbon_Login_Group);
            this.tab2.Groups.Add(this.Rbon_BaseSetting_Group);
            this.tab2.Groups.Add(this.Rbon_Product_Group);
            this.tab2.Groups.Add(this.Rbon_Sales_Group);
            this.tab2.Groups.Add(this.Rbon_Purcase_Group);
            this.tab2.Groups.Add(this.Rbon_Shipped_Group);
            this.tab2.Label = "SwiftExport";
            this.tab2.Name = "tab2";
            // 
            // Rbon_Login_Group
            // 
            this.Rbon_Login_Group.Items.Add(this.Banna_Login);
            this.Rbon_Login_Group.Label = "用户";
            this.Rbon_Login_Group.Name = "Rbon_Login_Group";
            this.Rbon_Login_Group.Tag = "LoginSheet";
            // 
            // Banna_Login
            // 
            this.Banna_Login.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Banna_Login.Label = "登录";
            this.Banna_Login.Name = "Banna_Login";
            this.Banna_Login.ShowImage = true;
            this.Banna_Login.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Banna_Login_Click);
            // 
            // Rbon_Product_Group
            // 
            this.Rbon_Product_Group.Items.Add(this.Rbn_btn_Product);
            this.Rbon_Product_Group.Items.Add(this.Rbn_btn_BOM);
            this.Rbon_Product_Group.Label = "产品管理";
            this.Rbon_Product_Group.Name = "Rbon_Product_Group";
            this.Rbon_Product_Group.Tag = "ProductSheet";
            this.Rbon_Product_Group.Visible = false;
            // 
            // Rbn_btn_Product
            // 
            this.Rbn_btn_Product.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_Product.Label = "基础产品";
            this.Rbn_btn_Product.Name = "Rbn_btn_Product";
            this.Rbn_btn_Product.ShowImage = true;
            this.Rbn_btn_Product.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_Product_Click);
            // 
            // Rbn_btn_BOM
            // 
            this.Rbn_btn_BOM.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_BOM.Label = "BOM管理";
            this.Rbn_btn_BOM.Name = "Rbn_btn_BOM";
            this.Rbn_btn_BOM.ShowImage = true;
            this.Rbn_btn_BOM.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_BOM_Click);
            // 
            // Rbon_Sales_Group
            // 
            this.Rbon_Sales_Group.Items.Add(this.Rbn_btn_SaleProduct);
            this.Rbon_Sales_Group.Items.Add(this.Rbn_btn_SaleOrder);
            this.Rbon_Sales_Group.Label = "销售管理";
            this.Rbon_Sales_Group.Name = "Rbon_Sales_Group";
            this.Rbon_Sales_Group.Tag = "SalesOrderSheet";
            // 
            // Rbn_btn_SaleProduct
            // 
            this.Rbn_btn_SaleProduct.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_SaleProduct.Label = "客户产品";
            this.Rbn_btn_SaleProduct.Name = "Rbn_btn_SaleProduct";
            this.Rbn_btn_SaleProduct.ShowImage = true;
            this.Rbn_btn_SaleProduct.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_SaleProduct_Click);
            // 
            // Rbn_btn_SaleOrder
            // 
            this.Rbn_btn_SaleOrder.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_SaleOrder.Label = "销售订单";
            this.Rbn_btn_SaleOrder.Name = "Rbn_btn_SaleOrder";
            this.Rbn_btn_SaleOrder.ShowImage = true;
            this.Rbn_btn_SaleOrder.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_SaleOrder_Click);
            // 
            // Rbon_Purcase_Group
            // 
            this.Rbon_Purcase_Group.Items.Add(this.Rbn_btn_PurchaseProduct);
            this.Rbon_Purcase_Group.Items.Add(this.Rbn_btn_Supplier);
            this.Rbon_Purcase_Group.Label = "采购管理";
            this.Rbon_Purcase_Group.Name = "Rbon_Purcase_Group";
            this.Rbon_Purcase_Group.Tag = "PurchaseOrderSheet";
            // 
            // Rbn_btn_PurchaseProduct
            // 
            this.Rbn_btn_PurchaseProduct.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_PurchaseProduct.Label = "采购产品";
            this.Rbn_btn_PurchaseProduct.Name = "Rbn_btn_PurchaseProduct";
            this.Rbn_btn_PurchaseProduct.ShowImage = true;
            this.Rbn_btn_PurchaseProduct.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_PurchaseProduct_Click);
            // 
            // Rbn_btn_Supplier
            // 
            this.Rbn_btn_Supplier.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_Supplier.Label = "供应商管理";
            this.Rbn_btn_Supplier.Name = "Rbn_btn_Supplier";
            this.Rbn_btn_Supplier.ShowImage = true;
            // 
            // Rbon_Shipped_Group
            // 
            this.Rbon_Shipped_Group.Items.Add(this.Rbn_btn_GenJinOrder);
            this.Rbon_Shipped_Group.Items.Add(this.Rbn_btn_Shipped);
            this.Rbon_Shipped_Group.Items.Add(this.Rbn_btn_PurchaseShouHuo);
            this.Rbon_Shipped_Group.Label = "出货管理";
            this.Rbon_Shipped_Group.Name = "Rbon_Shipped_Group";
            // 
            // Rbn_btn_GenJinOrder
            // 
            this.Rbn_btn_GenJinOrder.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_GenJinOrder.Label = "订单跟进";
            this.Rbn_btn_GenJinOrder.Name = "Rbn_btn_GenJinOrder";
            this.Rbn_btn_GenJinOrder.ShowImage = true;
            this.Rbn_btn_GenJinOrder.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Rbn_btn_GenJinOrder_Click);
            // 
            // Rbn_btn_Shipped
            // 
            this.Rbn_btn_Shipped.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_Shipped.Label = "订单出货";
            this.Rbn_btn_Shipped.Name = "Rbn_btn_Shipped";
            this.Rbn_btn_Shipped.ShowImage = true;
            // 
            // Rbn_btn_PurchaseShouHuo
            // 
            this.Rbn_btn_PurchaseShouHuo.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.Rbn_btn_PurchaseShouHuo.Label = "客供件收货";
            this.Rbn_btn_PurchaseShouHuo.Name = "Rbn_btn_PurchaseShouHuo";
            this.Rbn_btn_PurchaseShouHuo.ShowImage = true;
            // 
            // Rbon_BaseSetting_Group
            // 
            this.Rbon_BaseSetting_Group.Items.Add(this.SptBtn_BaseSetting);
            this.Rbon_BaseSetting_Group.Label = "基础设置";
            this.Rbon_BaseSetting_Group.Name = "Rbon_BaseSetting_Group";
            // 
            // SptBtn_BaseSetting
            // 
            this.SptBtn_BaseSetting.ButtonType = Microsoft.Office.Tools.Ribbon.RibbonButtonType.ToggleButton;
            this.SptBtn_BaseSetting.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.SptBtn_BaseSetting.Items.Add(this.Btn_BaseSetting_FrmCustomersManager);
            this.SptBtn_BaseSetting.Items.Add(this.Btn_BaseSetting_FrmSupplierManager);
            this.SptBtn_BaseSetting.Items.Add(this.Btn_BaseSetting_FrmProductColorManager);
            this.SptBtn_BaseSetting.Items.Add(this.Btn_BaseSetting_FrmExcelFieldMappManager);
            this.SptBtn_BaseSetting.Label = "基础设置";
            this.SptBtn_BaseSetting.Name = "SptBtn_BaseSetting";
            // 
            // Btn_BaseSetting_FrmCustomersManager
            // 
            this.Btn_BaseSetting_FrmCustomersManager.Label = "客户设置";
            this.Btn_BaseSetting_FrmCustomersManager.Name = "Btn_BaseSetting_FrmCustomersManager";
            this.Btn_BaseSetting_FrmCustomersManager.ShowImage = true;
            this.Btn_BaseSetting_FrmCustomersManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Btn_BaseSetting_FrmCustomersManager_Click);
            // 
            // Btn_BaseSetting_FrmExcelFieldMappManager
            // 
            this.Btn_BaseSetting_FrmExcelFieldMappManager.Label = "Excel字段设置";
            this.Btn_BaseSetting_FrmExcelFieldMappManager.Name = "Btn_BaseSetting_FrmExcelFieldMappManager";
            this.Btn_BaseSetting_FrmExcelFieldMappManager.ShowImage = true;
            this.Btn_BaseSetting_FrmExcelFieldMappManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Btn_BaseSetting_FrmExcelFieldMappManager_Click);
            // 
            // Btn_BaseSetting_FrmProductColorManager
            // 
            this.Btn_BaseSetting_FrmProductColorManager.Label = "产品颜色设置";
            this.Btn_BaseSetting_FrmProductColorManager.Name = "Btn_BaseSetting_FrmProductColorManager";
            this.Btn_BaseSetting_FrmProductColorManager.ShowImage = true;
            this.Btn_BaseSetting_FrmProductColorManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Btn_BaseSetting_FrmProductColorManager_Click);
            // 
            // Btn_BaseSetting_FrmSupplierManager
            // 
            this.Btn_BaseSetting_FrmSupplierManager.Label = "供应商设置";
            this.Btn_BaseSetting_FrmSupplierManager.Name = "Btn_BaseSetting_FrmSupplierManager";
            this.Btn_BaseSetting_FrmSupplierManager.ShowImage = true;
            this.Btn_BaseSetting_FrmSupplierManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Btn_BaseSetting_FrmSupplierManager_Click);
            // 
            // SwiftExportAddInRibbon
            // 
            this.Name = "SwiftExportAddInRibbon";
            this.RibbonType = "Microsoft.Excel.Workbook";
            this.Tabs.Add(this.tab1);
            this.Tabs.Add(this.tab2);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.SwiftExportAddInRibbon_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.tab2.ResumeLayout(false);
            this.tab2.PerformLayout();
            this.Rbon_Login_Group.ResumeLayout(false);
            this.Rbon_Login_Group.PerformLayout();
            this.Rbon_Product_Group.ResumeLayout(false);
            this.Rbon_Product_Group.PerformLayout();
            this.Rbon_Sales_Group.ResumeLayout(false);
            this.Rbon_Sales_Group.PerformLayout();
            this.Rbon_Purcase_Group.ResumeLayout(false);
            this.Rbon_Purcase_Group.PerformLayout();
            this.Rbon_Shipped_Group.ResumeLayout(false);
            this.Rbon_Shipped_Group.PerformLayout();
            this.Rbon_BaseSetting_Group.ResumeLayout(false);
            this.Rbon_BaseSetting_Group.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab2;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_Login_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Banna_Login;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_Product_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_Product;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_BOM;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_Sales_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_Purcase_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_Shipped_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_SaleProduct;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_SaleOrder;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_PurchaseProduct;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_Supplier;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_GenJinOrder;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_Shipped;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Rbn_btn_PurchaseShouHuo;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup Rbon_BaseSetting_Group;
        internal Microsoft.Office.Tools.Ribbon.RibbonSplitButton SptBtn_BaseSetting;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Btn_BaseSetting_FrmCustomersManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Btn_BaseSetting_FrmExcelFieldMappManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Btn_BaseSetting_FrmProductColorManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton Btn_BaseSetting_FrmSupplierManager;
    }

    partial class ThisRibbonCollection
    {
        internal SwiftExportAddInRibbon SwiftExportAddInRibbon
        {
            get { return this.GetRibbon<SwiftExportAddInRibbon>(); }
        }
    }
}
