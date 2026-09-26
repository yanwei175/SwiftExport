using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>定义物料需求推荐采购供应商的局部维护用例。</summary>
public interface IMaterialDemandSupplierSuggestionService
{
    /// <summary>按物料版本更新或清空建议，不改已有采购事实。</summary>
    Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(UpdateSuggestedPurchaseSupplierCommand command, CancellationToken cancellationToken = default);
}
