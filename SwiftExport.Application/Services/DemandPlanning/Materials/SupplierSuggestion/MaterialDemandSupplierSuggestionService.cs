using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>按物料版本局部维护推荐采购供应商，不修改采购和交付事实。</summary>
public sealed class MaterialDemandSupplierSuggestionService(
    ServiceBaseContext context,
    ISupplierRepository supplierRepository,
    IMaterialDemandDocumentRepository netDemandRepository,
    ICurrentUser currentUser, IUserPermissionService permissions,
    CommandPipeline pipeline, BusinessCommandExecutor executor) : IMaterialDemandSupplierSuggestionService
{
    private const string CommandType = "DemandPlanning.Material.SupplierSuggestion.Update";
    private readonly ILogger<MaterialDemandSupplierSuggestionService> _logger =
        context.LoggerFactory.CreateLogger<MaterialDemandSupplierSuggestionService>();

    /// <summary>
    /// 以物料需求版本为并发边界局部保存推荐采购供应商；该建议不修改任何既有采购单或送达事实。
    /// </summary>
    public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken = default)
        => RunCommandAsync(() => pipeline.ExecuteAsync<UpdateSuggestedPurchaseSupplierCommand, UpdateSuggestedPurchaseSupplierResult>(
            command, command?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteUpdateCommandAsync(command!, token), cancellationToken));

    /// <summary>单行独立指纹和回执在活动物料版本检查之前处理。</summary>
    private async Task<ServiceResult<CommandExecutionResult<UpdateSuggestedPurchaseSupplierResult>>> ExecuteUpdateCommandAsync(
        UpdateSuggestedPurchaseSupplierCommand command, CancellationToken token)
    {
        if (command is null || command.NetDemandId == Guid.Empty || command.Version <= 0)
            return ServiceResult<CommandExecutionResult<UpdateSuggestedPurchaseSupplierResult>>.Fail("INVALID_COMMAND", "物料需求和版本不能为空。");
        if (command.SuggestedPurchaseSupplierId == Guid.Empty)
            return ServiceResult<CommandExecutionResult<UpdateSuggestedPurchaseSupplierResult>>.Fail("INVALID_SUPPLIER", "供应商 Uid 无效；清空建议请提交 null。");
        var result = await executor.ExecuteAsync(command.CommandId, CommandType, command.NetDemandId,
            new { command.NetDemandId, command.Version, command.SuggestedPurchaseSupplierId },
            ct => UpdateInTransactionAsync(command, ct), token);
        return ServiceResult<CommandExecutionResult<UpdateSuggestedPurchaseSupplierResult>>.Ok(result);
    }

    /// <summary>沿用活动物料族、供应商的原锁定顺序；相同建议保持原版本。</summary>
    private async Task<UpdateSuggestedPurchaseSupplierResult> UpdateInTransactionAsync(
        UpdateSuggestedPurchaseSupplierCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var family = await netDemandRepository.GetActiveFamilyForUpdateAsync(command.NetDemandId);
        var demand = family.SingleOrDefault(item => item.Uid == command.NetDemandId)
            ?? throw new SuggestedSupplierException("MATERIAL_DEMAND_NOT_ACTIVE", "物料需求不存在、已回退或已作废。");
        if (demand.Version != command.Version)
            throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");

        Supplier? supplier = null;
        if (command.SuggestedPurchaseSupplierId.HasValue)
        {
            supplier = await supplierRepository.GetByIdAsync(command.SuggestedPurchaseSupplierId.Value, withUpdLock: true)
                ?? throw new SuggestedSupplierException("INVALID_SUPPLIER", "推荐采购供应商不存在或已停用。");
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (demand.SuggestedPurchaseSupplierId == command.SuggestedPurchaseSupplierId)
        {
            return new UpdateSuggestedPurchaseSupplierResult
            {
                NetDemandId = demand.Uid,
                Version = demand.Version,
                SuggestedPurchaseSupplierId = supplier?.Uid,
                SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
                SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
            };
        }

        var affected = await netDemandRepository.UpdateSuggestedPurchaseSupplierAsync(
            demand.Uid, demand.Version, command.SuggestedPurchaseSupplierId);
        if (affected != 1)
            throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");
        cancellationToken.ThrowIfCancellationRequested();

        return new UpdateSuggestedPurchaseSupplierResult
        {
            NetDemandId = demand.Uid,
            Version = demand.Version + 1,
            SuggestedPurchaseSupplierId = supplier?.Uid,
            SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
            SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
        };
    }

    /// <summary>独立建议维护权限先于验证、回执及事务。</summary>
    private async Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage, token)
            ? null : ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有推荐供应商维护权限。");
    }

    /// <summary>业务错误沿用原代码；公共回执错误保持可识别，取消由调用方处理。</summary>
    private async Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> RunCommandAsync(
        Func<Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex) { return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SuggestedSupplierException ex) { return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新推荐采购供应商失败。");
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ServiceErrorCodes.Unexpected, "更新推荐采购供应商失败。");
        }
    }

    /// <summary>表示推荐供应商局部更新可预期的业务失败。</summary>
    private sealed class SuggestedSupplierException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

}
