using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductNeutrals.Import;

/// <summary>活动管理员提交中性资料的窄入口；每行独立事务，不承担工作簿解析或批量归属调整。</summary>
public interface IProductNeutralImportService
{
    /// <summary>逐行保存一至一百行原始输入，返回与原工作表行号对应的明确业务结果。</summary>
    Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductNeutralImportRowDto> rows, CancellationToken cancellationToken = default);
}
