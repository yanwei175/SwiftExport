using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Manual;

/// <summary>维护手工来源需求，拥有现有创建、修改和关闭事务。</summary>
public sealed class ManualGrossDemandService(
    ServiceBaseContext context,
    IGrossDemandDocumentRepository grossDemandRepository, IGrossDemandQueryRepository grossDemandQueryRepository,
    IProductRepository productRepository,
    IDocumentNoService documentNoService,
    IMaterialDemandSourceExitService materialDemandSourceExit) : IManualGrossDemandService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ManualGrossDemandService> _logger = context.LoggerFactory.CreateLogger<ManualGrossDemandService>();

    /// <summary>建立一条没有上游业务单的手工来源需求。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
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
        }, "创建手工来源需求失败");

    /// <summary>修改尚无活动物料需求的手工来源需求。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            if (request is null || uid == Guid.Empty || request.Uid != uid) throw new GrossDemandValidationException("路由 Uid 与请求体 Uid 不一致。");
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求不存在或已删除。");
            EnsureEditableManual(demand);
            if (demand.Version != request.Version) throw new GrossDemandValidationException("来源需求已被其他用户修改，请刷新后重试。");
            if (await materialDemandSourceExit.HasActiveAsync(uid)) throw new GrossDemandValidationException("来源需求仍有活动物料需求，请先从物料需求页回退。");
            await ApplyManualChangesAsync(demand, request.ProductId, request.IsNonStandard, request.LinkSku,
                request.CustomerSku, request.Description, request.Color, request.Specification, request.ImagePath,
                request.DemandQty, request.RequiredDate, request.Remark);
            EnsureWriteSucceeded(await grossDemandRepository.UpdateManualAsync(demand, request.Version));
            return await grossDemandQueryRepository.GetWorkspaceDetailAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求已更新，但重新加载失败。");
        }, "更新手工来源需求失败");

    /// <summary>关闭尚无活动物料需求的手工来源需求并保留历史。</summary>
    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command)
        => ExecuteTransactionAsync(async () =>
        {
            if (command is null || uid == Guid.Empty || command.Uid != uid) throw new GrossDemandValidationException("路由 Uid 与请求体 Uid 不一致。");
            var reason = command.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new GrossDemandValidationException("关闭原因不能为空且不能超过 500 个字符。");
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求不存在或已删除。");
            EnsureEditableManual(demand);
            if (demand.Version != command.Version) throw new GrossDemandValidationException("来源需求已被其他用户修改，请刷新后重试。");
            if (await materialDemandSourceExit.HasActiveAsync(uid)) throw new GrossDemandValidationException("来源需求已有活动物料需求，请先从物料需求页回退。");
            EnsureWriteSucceeded(await grossDemandRepository.CloseManualAsync(uid, command.Version, reason));
            return await grossDemandQueryRepository.GetWorkspaceDetailAsync(uid) ?? throw new GrossDemandValidationException("手工来源需求已关闭，但重新加载失败。");
        }, "关闭手工来源需求失败");

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

    private async Task<ServiceResult<T>> ExecuteTransactionAsync<T>(Func<Task<T>> action, string failureMessage)
    {
        if (_session.Transaction is not null) return ServiceResult<T>.Fail("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var result = await action();
            _session.Transaction.Commit();
            return ServiceResult<T>.Ok(result);
        }
        catch (GrossDemandValidationException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<T>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
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

    private sealed class GrossDemandValidationException(string? message) : Exception(message ?? "来源需求校验失败。") { }
}
