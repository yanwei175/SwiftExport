using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Files;

/// <summary>列举与下载服务器受控 XLSX 模板；不上传模板、不生成报表，也不访问业务数据库。</summary>
public interface IExportTemplateCatalogService
{
    /// <summary>完整核对当前清单后，按精确单据类型及数据契约筛选可用模板；合法但无匹配时返回空列表。</summary>
    Task<ServiceResult<List<ExportTemplateInfoDto>>> ListAsync(
        string documentType,
        string dataContractKey,
        int dataContractVersion,
        CancellationToken cancellationToken = default);

    /// <summary>核对当前清单与所有启用文件后下载指定版本；返回已验证的独立流，由 HTTP 响应释放。</summary>
    Task<ServiceResult<FileReadHandle>> OpenReadAsync(
        string templateId,
        int templateVersion,
        CancellationToken cancellationToken = default);
}
