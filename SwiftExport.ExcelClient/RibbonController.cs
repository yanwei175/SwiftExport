using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Refit;
using SwiftExport.Contracts.Interfaces;
using SwiftExport.ExcelClient.Managers;
using SwiftExport.UI;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel; // ✅ 解决 Application 类型报错

namespace SwiftExport.ExcelClient;

[ComVisible(true)]
public class RibbonController : ExcelRibbon
{
    public override string GetCustomUI(string RibbonID)
    {
        // ✅ 修复 Path 警告：
        // ExcelDnaUtil.XllPath 在运行时绝不会为空，我们加个 "!" 告诉编译器闭嘴
        // 或者更安全地使用 AppDomain.CurrentDomain.BaseDirectory
        var xllPath = ExcelDnaUtil.XllPath ?? string.Empty;
        var dir = Path.GetDirectoryName(xllPath) ?? string.Empty;
        var xmlPath = Path.Combine(dir, "Ribbon.xml");

        if (!File.Exists(xmlPath))
        {
            return "<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui'><ribbon/></customUI>";
        }

        return File.ReadAllText(xmlPath);
    }
    public void OnShowTaskPanePressed(IRibbonControl control)
    {
        // 仅仅是调用管理器显示侧边栏
        TaskPaneManager.ShowTaskPane();
    }
    public async void OnGetProductPressed(IRibbonControl control)
    {
        try
        {
            // ✅ 修复 Application 类型转换报错
            // (前提是你安装了 ExcelDna.Interop 包)
            var app = (Excel.Application)ExcelDnaUtil.Application;

            // 获取当前活动单元格，如果没选中则不处理
            if (app.ActiveCell == null) return;
            var cell = app.ActiveCell;

            cell.Value2 = "正在请求 API...";

            // 配置 HttpClient (忽略 SSL)
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost:7086") // 确保端口号对
            };

            // 配置 Refit 使用 Newtonsoft.Json
            var refitSettings = new RefitSettings
            {
                ContentSerializer = new NewtonsoftJsonContentSerializer(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                })
            };

            var api = RestService.For<IProductApi>(httpClient, refitSettings);

            // 调用 API
            var result = await api.GetByIdAsync(5);

            if (result != null && result.Success && result.Data != null)
            {
                // ✅ 修复 Data 可能为空的警告
                // 我们在 if 里已经检查了 result.Data != null
                cell.Value2 = $"SKU: {result.Data.Sku} | {result.Data.Description}";

                cell.EntireColumn.AutoFit();
                cell.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightGreen);
            }
            else
            {
                var msg = result?.Message ?? "未知错误";
                cell.Value2 = $"失败: {msg}";
                cell.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.Red);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"报错啦: {ex.Message}");
        }
    }

    public void OnOpenWindow(IRibbonControl control)
    {
        // 获取 Excel 句柄
        var excelHwnd = (IntPtr)ExcelDnaUtil.WindowHandle;

        // 调用共享库，把句柄传进去
        AppStarter.ShowMain(excelHwnd);
    }







}