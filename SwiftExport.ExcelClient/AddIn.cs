using ExcelDna.Integration;
using SwiftExport.UI.Services;

namespace SwiftExport.ExcelClient;

public class AddIn : IExcelAddIn
{
    // Excel 启动时自动执行
    public void AutoOpen()
    {
        // 1. 初始化我们的全局 API 服务
        GlobalApi.Initialize();

        // 可以在这里做其他初始化，比如日志、配置读取
    }

    // Excel 关闭时自动执行
    public void AutoClose()
    {
        // 清理资源
    }
}