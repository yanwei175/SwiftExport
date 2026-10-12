using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Files
{
    /// <summary>按单据数据契约查询服务器模板目录；基地址须指向 api/export-templates/。</summary>
    public interface IExportTemplateApi
    {
        /// <summary>在线取得精确兼容且当前启用的模板；正文下载由客户端受保护的流式 HTTP 管线负责。</summary>
        [Get("")]
        Task<ServiceResult<List<ExportTemplateInfoDto>>> ListAsync(
            [AliasAs("documentType")] string documentType,
            [AliasAs("dataContractKey")] string dataContractKey,
            [AliasAs("dataContractVersion")] int dataContractVersion,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
