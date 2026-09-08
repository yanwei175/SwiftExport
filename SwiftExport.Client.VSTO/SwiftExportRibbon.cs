using SwiftExport.Client.VSTO.Bootstrapper;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Office = Microsoft.Office.Core;
namespace SwiftExport.Client.VSTO
{
    /// <summary>Office Ribbon XML 回调，只读内存状态，不在 getter 请求服务器。</summary>
    [ComVisible(true)]
    public sealed class SwiftExportRibbon : Office.IRibbonExtensibility, IDisposable
    {
        private Office.IRibbonUI ribbon;
        private AddInBootstrapper host;
        /// <summary>允许 Ribbon 先于 Startup 创建，随后绑定宿主。</summary>
        public void Attach(AddInBootstrapper value)
        {
            if (host != null) host.Changed -= Host_Changed;
            host = value; host.Changed += Host_Changed; Refresh();
        }
        /// <summary>仅返回嵌入 XML。</summary>
        public string GetCustomUI(string ribbonID)
        {
            using (var stream = typeof(SwiftExportRibbon).Assembly.GetManifestResourceStream("SwiftExport.Client.VSTO.SwiftExportRibbon.xml"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Ribbon XML 资源缺失。"))) return reader.ReadToEnd();
        }
        /// <summary>保存 Office 的刷新入口。</summary>
        public void Ribbon_Load(Office.IRibbonUI value) { ribbon = value; Refresh(); }
        /// <summary>返回当前身份文本。</summary>
        public string GetStatus(Office.IRibbonControl control) { return host?.Status ?? "正在初始化"; }
        /// <summary>未登录时显示登录按钮。</summary>
        public bool GetLoginVisible(Office.IRibbonControl control) { return host?.IsAuthenticated != true; }
        /// <summary>已登录时显示账号操作。</summary>
        public bool GetAccountVisible(Office.IRibbonControl control) { return host?.IsAuthenticated == true; }
        /// <summary>恢复期间禁止账号操作。</summary>
        public bool GetAccountEnabled(Office.IRibbonControl control) { return host?.IsReady == true && !host.IsRestoring && !host.IsLoginOpen; }
        /// <summary>打开或激活登录窗口。</summary>
        public void OnLogin(Office.IRibbonControl control) { host?.Login(control); }
        /// <summary>退出插件登录。</summary>
        public void OnLogout(Office.IRibbonControl control) { host?.Logout(); }
        private void Host_Changed(object sender, EventArgs args) { Refresh(); }
        // Office 缓存动态属性，状态变化后必须主动失效。
        private void Refresh()
        {
            if (ribbon == null) return;
            try { foreach (var id in new[] { "accountStatus", "login", "switchAccount", "logout" }) ribbon.InvalidateControl(id); }
            catch (COMException) { ribbon = null; }
        }
        /// <summary>断开宿主事件与 Ribbon 引用。</summary>
        public void Dispose() { if (host != null) host.Changed -= Host_Changed; host = null; ribbon = null; }
    }
}
