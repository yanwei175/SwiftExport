using Microsoft.Extensions.DependencyInjection;
using SwiftExport.VSTO.UserWindow;
using SwiftExport.VSTO.UserWindow.ProductViews.Views;
using System;

namespace SwiftExport.VSTO
{
    public partial class ThisAddIn
    {
        //产品选择器窗口实例（如果需要在多个地方访问，可以放在这里）
        public ProductSelectorWindow _productSelectorWindow;
        private async void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            //初始化WPF环境
            await App.EnsureApplication();

            //注册依赖
            var services = new ServiceCollection();

            services.AddVstoServices();

            var provider = services.BuildServiceProvider();
            // 注册到全局
            DiServices.Init(provider);

            WarmUpWPF();
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
        }
        private void WarmUpWPF()
        {
            try
            {
                // 1. 创建一个“牺牲品”窗口
                // 这个窗口必须引用了 样式（最好就是你要用的那个 TestWindow）
                var dummy = new TestWindow
                {
                    // 2. 把在这个窗口彻底“隐形”
                    Width = 0,
                    Height = 0,
                    WindowStyle = System.Windows.WindowStyle.None,
                    ShowInTaskbar = false,
                    Opacity = 0, // 透明度设为 0，双重保险
                    AllowsTransparency = true // 有些环境需要这个才能彻底透明
                };

                // 3. 强行显示它
                // 这一步会触发 WPF 解析所有 MD 的资源字典、加载字体、JIT 编译
                dummy.Show();

                // 4. 立即关闭并销毁
                dummy.Close();
                dummy = null;
                System.Diagnostics.Debug.WriteLine("资源预热成功: ");
                // 强制 GC 清理一下（可选）
                GC.Collect();
            }
            catch (Exception ex)
            {
                // 预热失败不要崩，静默处理
                System.Diagnostics.Debug.WriteLine("WPF 预热失败: " + ex.Message);
            }
        }


        #region VSTO 生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
