using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Office.Interop.Excel;
using SwiftExport.AppLayer.DI;
using SwiftExport.UiKongJianFrameWork;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExportAddIn.TaskPanel;
using SwiftExportAddIn.UserFactory;
using SwiftExportVSTO;
using SwiftExportVSTO.静态类;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;


namespace SwiftExportAddIn
{
    public partial class ThisAddIn
    {
        private IServiceProvider _provider;
        public IServerFactory ServerFactory { get; private set; }
        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            Application.WorkbookBeforeClose += DeleteTaskPenByWindowsClose;

            DI_Initialize();

        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {

        }


        private void DeleteTaskPenByWindowsClose(Excel.Workbook Wb, ref bool Cancel)
        {
            foreach (Excel.Window awn in Wb.Windows)
            {
                int hwnd = awn.Hwnd;
                TaskPanelManager.RemoveTaskPane(hwnd);
            }
        }

        private void DI_Initialize()
        {
            string databasePath = Properties.Settings.Default.DataBase.ToString();
            // 1. 加载配置
            var config = new ConfigurationBuilder()
                                .AddInMemoryCollection(new Dictionary<string, string>
                                {
                                    ["ConnectionStrings:DataBase"] = databasePath,
                                })
                                .Build();


            var services = new ServiceCollection();
            services.AddApplicationServers(config);
            services.AddSingleton<IServerFactory, ServiceFactory>();
            services.AddTransient<LoginFrm>();
            services.AddTransient<FrmCustomersManager>();
            services.AddTransient<FrmSuppliersManager>();
            services.AddTransient<FrmExcelSheetFieldsMappingManager>();
            services.AddTransient<FrmProductColorManager>();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            // 构建 ServiceProvider
            _provider = services.BuildServiceProvider();

            // 工厂服务存到全局变量
            Globals.ThisAddIn.ServerFactory = _provider.GetRequiredService<IServerFactory>();

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
