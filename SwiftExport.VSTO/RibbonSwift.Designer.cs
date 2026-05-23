namespace SwiftExport.VSTO
{
    partial class RibbonSwift : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public RibbonSwift()
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
            this.tabSwiftExport = this.Factory.CreateRibbonTab();
            this.MainMenuGroup = this.Factory.CreateRibbonGroup();
            this.BaseSettingMenuGroup = this.Factory.CreateRibbonGroup();
            this.ProductMenuGroup = this.Factory.CreateRibbonGroup();
            this.SalesMenuGroup = this.Factory.CreateRibbonGroup();
            this.PurchaseMenuGroup = this.Factory.CreateRibbonGroup();
            this.BtnLogin = this.Factory.CreateRibbonButton();
            this.button1 = this.Factory.CreateRibbonButton();
            this.ProductSettingMenu = this.Factory.CreateRibbonMenu();
            this.ProductColorManager = this.Factory.CreateRibbonButton();
            this.SalesSettingMenu = this.Factory.CreateRibbonMenu();
            this.CustomerManager = this.Factory.CreateRibbonButton();
            this.PurchaseSettingMenu = this.Factory.CreateRibbonMenu();
            this.SupplierManager = this.Factory.CreateRibbonButton();
            this.PoConfigManager = this.Factory.CreateRibbonButton();
            this.button2 = this.Factory.CreateRibbonButton();
            this.tabSwiftExport.SuspendLayout();
            this.MainMenuGroup.SuspendLayout();
            this.ProductMenuGroup.SuspendLayout();
            this.SalesMenuGroup.SuspendLayout();
            this.PurchaseMenuGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabSwiftExport
            // 
            this.tabSwiftExport.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tabSwiftExport.Groups.Add(this.MainMenuGroup);
            this.tabSwiftExport.Groups.Add(this.BaseSettingMenuGroup);
            this.tabSwiftExport.Groups.Add(this.ProductMenuGroup);
            this.tabSwiftExport.Groups.Add(this.SalesMenuGroup);
            this.tabSwiftExport.Groups.Add(this.PurchaseMenuGroup);
            this.tabSwiftExport.Label = "SwiftExport";
            this.tabSwiftExport.Name = "tabSwiftExport";
            // 
            // MainMenuGroup
            // 
            this.MainMenuGroup.Items.Add(this.BtnLogin);
            this.MainMenuGroup.Items.Add(this.button1);
            this.MainMenuGroup.Label = "首页";
            this.MainMenuGroup.Name = "MainMenuGroup";
            // 
            // BaseSettingMenuGroup
            // 
            this.BaseSettingMenuGroup.Label = "基础设置";
            this.BaseSettingMenuGroup.Name = "BaseSettingMenuGroup";
            // 
            // ProductMenuGroup
            // 
            this.ProductMenuGroup.Items.Add(this.ProductSettingMenu);
            this.ProductMenuGroup.Label = "产品";
            this.ProductMenuGroup.Name = "ProductMenuGroup";
            // 
            // SalesMenuGroup
            // 
            this.SalesMenuGroup.Items.Add(this.SalesSettingMenu);
            this.SalesMenuGroup.Label = "销售";
            this.SalesMenuGroup.Name = "SalesMenuGroup";
            // 
            // PurchaseMenuGroup
            // 
            this.PurchaseMenuGroup.Items.Add(this.PurchaseSettingMenu);
            this.PurchaseMenuGroup.Items.Add(this.button2);
            this.PurchaseMenuGroup.Label = "采购";
            this.PurchaseMenuGroup.Name = "PurchaseMenuGroup";
            // 
            // BtnLogin
            // 
            this.BtnLogin.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.BtnLogin.Label = "登录";
            this.BtnLogin.Name = "BtnLogin";
            this.BtnLogin.ShowImage = true;
            this.BtnLogin.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.BtnLogin_Click);
            // 
            // button1
            // 
            this.button1.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.button1.Label = "Test";
            this.button1.Name = "button1";
            this.button1.ShowImage = true;
            this.button1.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Button1_Click);
            // 
            // ProductSettingMenu
            // 
            this.ProductSettingMenu.Items.Add(this.ProductColorManager);
            this.ProductSettingMenu.Label = "产品设置";
            this.ProductSettingMenu.Name = "ProductSettingMenu";
            // 
            // ProductColorManager
            // 
            this.ProductColorManager.Label = "产品颜色管理";
            this.ProductColorManager.Name = "ProductColorManager";
            this.ProductColorManager.ShowImage = true;
            this.ProductColorManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.ProductColorManager_Click);
            // 
            // SalesSettingMenu
            // 
            this.SalesSettingMenu.Items.Add(this.CustomerManager);
            this.SalesSettingMenu.Label = "销售设置";
            this.SalesSettingMenu.Name = "SalesSettingMenu";
            // 
            // CustomerManager
            // 
            this.CustomerManager.Label = "客户管理";
            this.CustomerManager.Name = "CustomerManager";
            this.CustomerManager.ShowImage = true;
            this.CustomerManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.CustomerManager_Click);
            // 
            // PurchaseSettingMenu
            // 
            this.PurchaseSettingMenu.Items.Add(this.SupplierManager);
            this.PurchaseSettingMenu.Items.Add(this.PoConfigManager);
            this.PurchaseSettingMenu.Label = "采购设置";
            this.PurchaseSettingMenu.Name = "PurchaseSettingMenu";
            // 
            // SupplierManager
            // 
            this.SupplierManager.Label = "供应商管理";
            this.SupplierManager.Name = "SupplierManager";
            this.SupplierManager.ShowImage = true;
            this.SupplierManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.SupplierManager_Click);
            // 
            // PoConfigManager
            // 
            this.PoConfigManager.Label = "采购单表头管理";
            this.PoConfigManager.Name = "PoConfigManager";
            this.PoConfigManager.ShowImage = true;
            this.PoConfigManager.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.PoConfigManager_Click);
            // 
            // button2
            // 
            this.button2.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.button2.Label = "Test";
            this.button2.Name = "button2";
            this.button2.ShowImage = true;
            this.button2.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.Buttontes_Click);
            // 
            // RibbonSwift
            // 
            this.Name = "RibbonSwift";
            this.RibbonType = "Microsoft.Excel.Workbook";
            this.Tabs.Add(this.tabSwiftExport);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.RibbonSwift_Load);
            this.tabSwiftExport.ResumeLayout(false);
            this.tabSwiftExport.PerformLayout();
            this.MainMenuGroup.ResumeLayout(false);
            this.MainMenuGroup.PerformLayout();
            this.ProductMenuGroup.ResumeLayout(false);
            this.ProductMenuGroup.PerformLayout();
            this.SalesMenuGroup.ResumeLayout(false);
            this.SalesMenuGroup.PerformLayout();
            this.PurchaseMenuGroup.ResumeLayout(false);
            this.PurchaseMenuGroup.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tabSwiftExport;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup MainMenuGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton BtnLogin;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup ProductMenuGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup SalesMenuGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup PurchaseMenuGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup BaseSettingMenuGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu SalesSettingMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton CustomerManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu PurchaseSettingMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton SupplierManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu ProductSettingMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton ProductColorManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton PoConfigManager;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button2;
    }

    partial class ThisRibbonCollection
    {
        internal RibbonSwift RibbonSwift
        {
            get { return this.GetRibbon<RibbonSwift>(); }
        }
    }
}
