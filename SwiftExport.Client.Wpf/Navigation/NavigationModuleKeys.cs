namespace SwiftExportClient.Navigation;

/// <summary>集中定义客户端导航路由所属的稳定模块标识；权限键仍由共享契约定义。</summary>
public static class NavigationModuleKeys
{
    /// <summary>所有已登录用户可访问的首页模块。</summary>
    public const string Home = "home";
    /// <summary>产品模块。</summary>
    public const string Products = "products";
    /// <summary>客户模块。</summary>
    public const string Customers = "customers";
    /// <summary>供应商模块。</summary>
    public const string Suppliers = "suppliers";
    /// <summary>销售模块。</summary>
    public const string Sales = "sales";
    /// <summary>需求计划模块。</summary>
    public const string DemandPlanning = "demand-planning";
    /// <summary>采购模块。</summary>
    public const string Procurement = "procurement";
    /// <summary>库存模块。</summary>
    public const string Inventory = "inventory";
    /// <summary>出货模块。</summary>
    public const string Shipping = "shipping";
    /// <summary>受管理能力保护的系统设置模块。</summary>
    public const string SystemSettings = "system-settings";
    /// <summary>所有已登录用户可访问的个人设置中心。</summary>
    public const string SettingsCenter = "settings-center";
}
