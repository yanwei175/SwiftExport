using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Manual;

/// <summary>维护手工来源需求；公共执行器拥有创建、修改和关闭各自的事务与完整回执。</summary>
public sealed class ManualGrossDemandService(
    ServiceBaseContext context,
    IGrossDemandDocumentRepository grossDemandRepository, IGrossDemandQueryRepository grossDemandQueryRepository,
    IProductRepository productRepository,
    IDocumentNoService documentNoService,
    IMaterialDemandSourceExitService materialDemandSourceExit,
    ICurrentUser currentUser, IUserPermissionService permissions,
    CommandPipeline pipeline, BusinessCommandExecutor executor) : IManualGrossDemandService
{
    private readonly ILogger<ManualGrossDemandService> _logger = context.LoggerFactory.CreateLogger<ManualGrossDemandService>();
    private const string CreateType = "DemandPlanning.Source.Manual.Create";
    private const string UpdateType = "DemandPlanning.Source.Manual.Update";
    private const string CloseType = "DemandPlanning.Source.Manual.Close";

    /// <summary>建立一条没有上游业务单的手工来源需求。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request)
        => RunCommandAsync(() => pipeline.ExecuteAsync<CreateManualGrossDemandRequest, GrossDemandDetailDto>(
            request, request?.CommandId, CreateType, AuthorizeAsync,
            token => ExecuteCreateCommandAsync(request!, token), CancellationToken.None));

    /// <summary>创建指纹仅包含实际生效输入；客户端 Sku 不参与事实生成。</summary>
    private async Task<ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>> ExecuteCreateCommandAsync(
        CreateManualGrossDemandRequest request, CancellationToken token)
    {
        var result = await executor.ExecuteAsync(request.CommandId, CreateType, null, ManualInput(request),
            _ => CreateInTransactionAsync(request), token);
        return ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>.Ok(result);
    }

    /// <summary>首次执行时生成来源身份和单号，并在同一事务内写入及读取完整结果。</summary>
    private async Task<GrossDemandDetailDto> CreateInTransactionAsync(CreateManualGrossDemandRequest request)
    {
        var uid = GuidSequentialGenerator.NewGuid();
        var demand = new GrossDemand
        {
            Uid = uid, SourceType = DocumentTypeStaticsEnum.ManualDemand, SourceUid = uid, SourceLineId = uid,
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Active
        };
        await ApplyManualChangesAsync(demand, request.ProductId, request.IsNonStandard, request.LinkSku,
            request.CustomerSku, request.Description, request.Color, request.Specification, request.ImagePath,
            request.DemandQty, request.RequiredDate, request.Remark);
        var number = await documentNoService.GetNextDocumentNoAsync(new DocumentNoRequest
        {
            DocType = DocumentTypeStaticsEnum.ManualDemand, BusinessDate = DateTime.Today
        });
        if (!number.Success || number.Data is null) throw new GrossDemandValidationException(number.Message);
        demand.SourceNo = number.Data.DocumentNo;
        EnsureWriteSucceeded((await grossDemandRepository.AddSourceDemandsAsync([demand])).Single());
        return await grossDemandQueryRepository.GetWorkspaceDetailAsync(uid)
            ?? throw new GrossDemandValidationException("手工来源需求已创建，但重新加载失败。");
    }

    /// <summary>修改尚无活动物料需求的手工来源需求。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request)
        => RunCommandAsync(() => pipeline.ExecuteAsync<UpdateManualGrossDemandRequest, GrossDemandDetailDto>(
            request, request?.CommandId, UpdateType, AuthorizeAsync,
            token => ExecuteUpdateCommandAsync(uid, request!, token), CancellationToken.None));

    /// <summary>路由身份先于回执校验，原版本及生效字段用于稳定指纹。</summary>
    private async Task<ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>> ExecuteUpdateCommandAsync(
        Guid uid, UpdateManualGrossDemandRequest request, CancellationToken token)
    {
        if (uid == Guid.Empty || request.Uid != uid)
            return ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>.Fail(ServiceErrorCodes.Validation, "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(request.CommandId, UpdateType, uid,
            new { request.Uid, request.Version, Input = ManualInput(request) },
            _ => UpdateInTransactionAsync(uid, request), token);
        return ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>.Ok(result);
    }

    /// <summary>锁定手工来源后复核版本及活动物料，再应用原有修改规则。</summary>
    private async Task<GrossDemandDetailDto> UpdateInTransactionAsync(Guid uid, UpdateManualGrossDemandRequest request)
    {
        var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求不存在或已删除。");
        EnsureEditableManual(demand);
        if (demand.Version != request.Version) throw new GrossDemandValidationException("来源需求已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        if (await materialDemandSourceExit.HasActiveAsync(uid)) throw new GrossDemandValidationException("来源需求仍有活动物料需求，请先从物料需求页回退。");
        await ApplyManualChangesAsync(demand, request.ProductId, request.IsNonStandard, request.LinkSku,
            request.CustomerSku, request.Description, request.Color, request.Specification, request.ImagePath,
            request.DemandQty, request.RequiredDate, request.Remark);
        EnsureWriteSucceeded(await grossDemandRepository.UpdateManualAsync(demand, request.Version));
        return await grossDemandQueryRepository.GetWorkspaceDetailAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求已更新，但重新加载失败。");
    }

    /// <summary>关闭尚无活动物料需求的手工来源需求并保留历史。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command)
        => RunCommandAsync(() => pipeline.ExecuteAsync<CloseGrossDemandCommand, GrossDemandDetailDto>(
            command, command?.CommandId, CloseType, AuthorizeAsync,
            token => ExecuteCloseCommandAsync(uid, command!, token), CancellationToken.None));

    /// <summary>关闭原因按现有 Trim 语义参与指纹，回执读取早于活动状态检查。</summary>
    private async Task<ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>> ExecuteCloseCommandAsync(
        Guid uid, CloseGrossDemandCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty || command.Uid != uid)
            return ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>.Fail(ServiceErrorCodes.Validation, "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(command.CommandId, CloseType, uid,
            new { command.Uid, command.Version, Reason = command.Reason?.Trim() },
            _ => CloseInTransactionAsync(uid, command), token);
        return ServiceResult<CommandExecutionResult<GrossDemandDetailDto>>.Ok(result);
    }

    /// <summary>锁定活动手工来源、复核版本和物料占用后关闭，保留原来源事实。</summary>
    private async Task<GrossDemandDetailDto> CloseInTransactionAsync(Guid uid, CloseGrossDemandCommand command)
    {
        var reason = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new GrossDemandValidationException("关闭原因不能为空且不能超过 500 个字符。");
        var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求不存在或已删除。");
        EnsureEditableManual(demand);
        if (demand.Version != command.Version) throw new GrossDemandValidationException("来源需求已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        if (await materialDemandSourceExit.HasActiveAsync(uid)) throw new GrossDemandValidationException("来源需求已有活动物料需求，请先从物料需求页回退。");
        EnsureWriteSucceeded(await grossDemandRepository.CloseManualAsync(uid, command.Version, reason));
        return await grossDemandQueryRepository.GetWorkspaceDetailAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求已关闭，但重新加载失败。");
    }

    /// <summary>仅取原业务实际生效的字段，并复用原 Trim/null 处理，不改变传入请求。</summary>
    private static object ManualInput(CreateManualGrossDemandRequest request) => new
    {
        request.ProductId, request.IsNonStandard,
        LinkSku = request.IsNonStandard ? request.LinkSku?.Trim() : null,
        CustomerSku = request.CustomerSku?.Trim(),
        Description = request.IsNonStandard ? request.Description?.Trim() : null,
        Color = request.Color?.Trim(),
        Specification = request.IsNonStandard ? request.Specification?.Trim() : null,
        ImagePath = request.IsNonStandard ? request.ImagePath?.Trim() : null,
        request.DemandQty, request.RequiredDate, Remark = request.Remark?.Trim()
    };

    /// <summary>修改与创建使用相同的生效字段集合，另由调用方加入身份和版本。</summary>
    private static object ManualInput(UpdateManualGrossDemandRequest request) => new
    {
        request.ProductId, request.IsNonStandard,
        LinkSku = request.IsNonStandard ? request.LinkSku?.Trim() : null,
        CustomerSku = request.CustomerSku?.Trim(),
        Description = request.IsNonStandard ? request.Description?.Trim() : null,
        Color = request.Color?.Trim(),
        Specification = request.IsNonStandard ? request.Specification?.Trim() : null,
        ImagePath = request.IsNonStandard ? request.ImagePath?.Trim() : null,
        request.DemandQty, request.RequiredDate, Remark = request.Remark?.Trim()
    };

    private async Task ApplyManualChangesAsync(GrossDemand demand, Guid? productId, bool isNonStandard,
        string? linkSku, string? customerSku, string? description, string? color, string? specification,
        string? imagePath, decimal quantity, DateTime? requiredDate, string? remark)
    {
        if (quantity <= 0) throw new GrossDemandValidationException("需求数量必须大于零。");
        demand.IsNonStandard = isNonStandard;
        demand.DemandQty = quantity;
        demand.RequiredDate = requiredDate;
        demand.Remark = remark?.Trim();
        demand.CustomerSkuSnapshot = customerSku?.Trim();
        if (isNonStandard)
        {
            if (productId.HasValue) throw new GrossDemandValidationException("非标件不能关联产品主数据。");
            if (string.IsNullOrWhiteSpace(description)) throw new GrossDemandValidationException("非标件描述不能为空。");
            demand.ProductId = null;
            demand.SkuSnapshot = NonStandardSkuGenerator.Create(demand.Uid);
            demand.LinkSkuSnapshot = linkSku?.Trim();
            demand.DescriptionSnapshot = description.Trim();
            demand.ColorSnapshot = color?.Trim();
            demand.SpecificationSnapshot = specification?.Trim();
            demand.ImagePathSnapshot = imagePath?.Trim();
            demand.DemandBasisFingerprint = SourceDemandBasisFingerprintFactory.Create(demand);
            return;
        }
        if (!productId.HasValue || productId.Value == Guid.Empty) throw new GrossDemandValidationException("标准件必须选择产品。");
        var product = await productRepository.GetByIdAsync(productId.Value) ?? throw new GrossDemandValidationException("所选产品不存在或已删除。");
        if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(product)) throw new GrossDemandValidationException("客供件不能作为手工来源需求根产品。");
        demand.ProductId = product.Uid;
        demand.SkuSnapshot = product.Sku;
        demand.LinkSkuSnapshot = product.LinkSku;
        demand.DescriptionSnapshot = product.DescriptionEn;
        demand.ColorSnapshot = color?.Trim();
        demand.SpecificationSnapshot = product.FeaturesEn;
        demand.ImagePathSnapshot = product.MainImage;
        demand.DemandBasisFingerprint = SourceDemandBasisFingerprintFactory.Create(demand);
    }

    /// <summary>独立维护权限在请求验证、回执查找和事务前检查。</summary>
    private async Task<ServiceResult<GrossDemandDetailDto>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.DemandPlanningSourceManualManage, token)
            ? null : ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有手工来源维护权限。");
    }

    /// <summary>保留原业务失败文案；公共命令错误保持统一代码供 HTTP 和客户端识别。</summary>
    private async Task<ServiceResult<GrossDemandDetailDto>> RunCommandAsync(Func<Task<ServiceResult<GrossDemandDetailDto>>> action)
    {
        try
        {
            return await action();
        }
        catch (CommandValidationException ex) { return ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<GrossDemandDetailDto>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (GrossDemandValidationException ex)
        {
            return ServiceResult<GrossDemandDetailDto>.Fail(ex.Code, ex.Message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "维护手工来源需求失败。");
            return ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.Unexpected, "维护手工来源需求失败。");
        }
    }

    private static void EnsureEditableManual(GrossDemand demand)
    {
        if (!string.Equals(demand.SourceType, DocumentTypeStaticsEnum.ManualDemand, StringComparison.OrdinalIgnoreCase)) throw new GrossDemandValidationException("只有手工来源需求允许直接维护。");
        if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active) throw new GrossDemandValidationException("只有活动手工来源需求允许维护。");
    }

    private static void EnsureWriteSucceeded(RepositoryWriteResult result)
    {
        if (!result.Success) throw new GrossDemandValidationException(result.Message ?? "来源需求写入失败。");
    }

    /// <summary>保留原业务提示，并让版本冲突与其他资格阻断分别映射 HTTP 状态。</summary>
    private sealed class GrossDemandValidationException(string? message, string code = ServiceErrorCodes.BusinessBlocked)
        : Exception(message ?? "来源需求校验失败。")
    {
        public string Code { get; } = code;
    }
}
