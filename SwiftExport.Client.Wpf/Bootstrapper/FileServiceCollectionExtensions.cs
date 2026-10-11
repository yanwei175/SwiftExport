using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwiftExportClient.Services.Files.Csv;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExportClient.Bootstrapper;

/// <summary>注册客户端独立文件服务；每次操作内部创建解析器和工作簿，不持有 UI 或业务会话状态。</summary>
public static class FileServiceCollectionExtensions
{
    /// <summary>建立 Excel/CSV 读取和受控 Excel 模板导出能力；重复调用不重复注册默认实现。</summary>
    public static IServiceCollection AddClientFileServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IExcelReadService, ClosedXmlExcelReadService>();
        services.TryAddSingleton<ICsvReadService, CsvFileReadService>();
        services.TryAddSingleton<IExcelTemplateExportService, ClosedXmlReportTemplateExportService>();
        return services;
    }
}
