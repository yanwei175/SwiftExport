using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using System.Text.Json;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单工作区应用服务。
/// </summary>
/// <remarks>
/// 负责销售订单管理页查询、详情编辑、确认、取消确认、关闭和草稿删除等业务编排。
/// 所有写操作均通过 <see cref="ExecuteTransactionAsync{T}"/> 在同一数据库事务中完成；
/// Repository 负责数据访问，本服务负责校验状态、控制调用顺序以及维护销售订单与毛需求之间的一致性。
/// </remarks>
public sealed class SalesOrderWorkspaceService(
    ServiceBaseContext context,
    ISalesOrderHeadRepository headRepository,
    ISalesOrderLineRepository lineRepository,
    IProductRepository productRepository,
    ICustomerRepository customerRepository,
    ICustomerProductRepository customerProductRepository,
    ISourceDemandReceptionService sourceDemandReceptionService,
    IDocumentNoService documentNoService,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser) : ISalesOrderWorkspaceService
{
    private const string Module = "SalesOrder";
    private const string CancelConfirmationOperation = "CancelConfirmation";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SalesOrderWorkspaceService> _logger = context.LoggerFactory.CreateLogger<SalesOrderWorkspaceService>();

    /// <summary>
    /// 按管理页筛选条件分页查询销售订单，不加载完整订单明细。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderListItemDto>>> SearchAsync(
        SalesOrderSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => headRepository.SearchWorkspaceAsync(request, cancellationToken),
            "查询销售订单失败",
            cancellationToken);

    /// <summary>
    /// 加载一个销售订单的完整编辑文档，供详情 Tab 初始化或保存后刷新。
    /// </summary>
    public async Task<ServiceResult<SalesOrderDocumentDto>> GetDocumentAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<SalesOrderDocumentDto>.Fail("销售订单 Uid 不能为空。");
        return await ExecuteReadAsync(async () =>
        {
            var document = await headRepository.GetWorkspaceDocumentAsync(uid, cancellationToken);
            return document ?? throw new WorkspaceValidationException("未找到销售订单。");
        }, "加载销售订单失败", cancellationToken);
    }

    /// <summary>
    /// 查询销售订单行可选择的产品，同时返回客户产品关系和价格快照所需信息。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => productRepository.SearchSalesOrderProductsAsync(request, cancellationToken),
            "查询销售产品失败",
            cancellationToken);

    /// <summary>
    /// 查询销售订单头可选择的客户，用于客户 Lookup。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(
        SalesOrderCustomerSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => customerRepository.SearchSalesOrderCustomersAsync(request, cancellationToken),
            "查询销售客户失败",
            cancellationToken);

    /// <summary>
    /// 获取客户代码建议列表，仅用于管理页 AutoSuggestBox 的输入提示。
    /// </summary>
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => customerRepository.GetSalesOrderCustomerCodesAsync(cancellationToken),
            "加载客户代码失败",
            cancellationToken);

    /// <summary>
    /// 新建或更新未确认、未关闭的销售订单，并一次性保存订单头和全部行变更。
    /// </summary>
    /// <remarks>
    /// 新建时申请正式订单号；更新时校验头、行 Version，并把旧行号暂存到安全区，
    /// 以支持明细上下移动后重新编号。请求必须显式提交全部活动行以及待删除行。
    /// </remarks>
    public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            // 第一阶段：识别新建/更新，并锁定更新订单以校验可编辑状态和乐观并发版本。
            ValidateSaveShape(request);
            var headerRequest = request.Header;
            var isCreate = headerRequest.Uid == Guid.Empty;
            var customer = await customerRepository.GetByIdAsync(headerRequest.CustomerId)
                ?? throw new WorkspaceValidationException("所选客户不存在或已删除。");

            SalesOrderHeader header;
            IReadOnlyList<SalesOrderLine> existingLines;
            if (isCreate)
            {
                var documentNoResult = await documentNoService.GetNextDocumentNoAsync(new DocumentNoRequest
                {
                    DocType = DocumentTypeStaticsEnum.SalesOrder,
                    BusinessDate = headerRequest.OrderDate
                });
                if (!documentNoResult.Success || documentNoResult.Data is null)
                    throw new WorkspaceValidationException(documentNoResult.Message);

                header = new SalesOrderHeader { OrderNo = documentNoResult.Data.DocumentNo };
                existingLines = [];
            }
            else
            {
                header = await headRepository.GetByIdAsync(headerRequest.Uid, true)
                    ?? throw new WorkspaceValidationException("销售订单不存在或已删除。");
                EnsureEditable(header);
                if (header.Version != headerRequest.Version)
                    throw new WorkspaceValidationException("销售订单已被其他用户修改，请刷新后重试。");
                existingLines = await lineRepository.GetLinesByOrderIdAsync(header.Uid, true);
            }

            var lineBuild = await BuildLinesAsync(request, header, existingLines, customer);
            ApplyHeaderChanges(header, headerRequest, customer, lineBuild.ActiveLines);

            // 第二阶段：先写订单头。更新时将所有既有行号移至正数安全区，释放最终行号的唯一约束。
            if (isCreate)
            {
                header.DocumentStatus = SalesOrderDocumentStatusEnum.Open;
                header.ConfirmationStatus = ConfirmationStatusEnum.待确认;
                var addHeaderResult = await headRepository.AddAsync(header);
                EnsureWriteSucceeded(addHeaderResult);
                foreach (var line in lineBuild.ActiveLines)
                {
                    line.SalesOrderId = header.Uid;
                    line.OrderNo = header.OrderNo;
                }
            }
            else
            {
                var updateHeaderResult = await headRepository.UpdateAsync(header);
                EnsureWriteSucceeded(updateHeaderResult);
                var stagedLineCount = await lineRepository.StageLineNumbersAsync(
                    header.Uid,
                    lineBuild.ActiveLines.Count);
                if (stagedLineCount != existingLines.Count)
                    throw new WorkspaceValidationException("销售订单明细已发生变化，请刷新后重试。");
            }

            // 第三阶段：按删除、更新、新增顺序落库，最终恢复请求中的连续行号。
            EnsureAllWritesSucceeded(await lineRepository.DeleteWorkspaceLinesAsync(lineBuild.DeletedLines));
            EnsureAllWritesSucceeded(await lineRepository.UpdateWorkspaceLinesAsync(lineBuild.UpdatedLines));
            EnsureAllWritesSucceeded(await lineRepository.AddWorkspaceLinesAsync(lineBuild.AddedLines));

            var document = await headRepository.GetWorkspaceDocumentAsync(header.Uid)
                ?? throw new WorkspaceValidationException("销售订单已保存，但重新加载失败。");
            return new SaveSalesOrderResponse { Document = document };
        }, "保存销售订单失败", request?.Header?.Uid);

    /// <summary>
    /// 确认销售订单，并为每条有效订单行生成一条毛需求。
    /// </summary>
    /// <remarks>
    /// 确认是销售订单进入需求链路的唯一放行动作，不执行 BOM 拆解。
    /// 方法允许对已经确认的订单进行幂等重试，但会拒绝重复、孤立或状态异常的毛需求数据。
    /// 标准件和非标件都会生成毛需求；非标件不要求关联产品主数据。
    /// </remarks>
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, ConfirmSalesOrderCommand request)
        => ExecuteTransactionAsync(async () =>
        {
            // 锁定订单头并检查单据生命周期；首次确认还必须通过 Version 乐观并发校验。
            ValidateCommandIdentity(uid, request.Uid);
            if (request.CommandId == Guid.Empty)
                throw new WorkspaceValidationException("确认命令标识不能为空。");
            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new WorkspaceValidationException("销售订单不存在或已删除。");
            if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
                throw new WorkspaceValidationException("已关闭的销售订单不能确认。");

            var alreadyConfirmed = header.ConfirmationStatus == ConfirmationStatusEnum.已确认;
            if (!alreadyConfirmed && header.Version != request.Version)
                throw new WorkspaceValidationException("销售订单已被其他用户修改，请刷新后重试。");

            var lines = await lineRepository.GetLinesByOrderIdAsync(uid, true);
            if (lines.Count == 0) throw new WorkspaceValidationException("销售订单没有有效明细，不能确认。");

            // 先取得确认后的销售事实版本，再由需求领域按该版本幂等接收全部销售行。
            var version = header.Version;
            if (!alreadyConfirmed)
            {
                var statusResult = await headRepository.SetConfirmationStatusAsync(uid, request.Version, ConfirmationStatusEnum.已确认);
                EnsureWriteSucceeded(statusResult);
                version = statusResult.NewVersion ?? request.Version + 1;
            }

            var productIds = lines.Where(line => line.ProductId.HasValue).Select(line => line.ProductId!.Value).Distinct().ToArray();
            var products = (await productRepository.GetByIdListAsync(productIds, true)).ToDictionary(product => product.Uid);
            SourceDemandReceptionResult reception;
            try
            {
                reception = await sourceDemandReceptionService.ReceiveAsync(
                    CreateSourceDemandCommand(header, lines, products, version, request.CommandId));
            }
            catch (SourceDemandReceptionException ex)
            {
                throw new WorkspaceValidationException($"{ex.Code}：{ex.Message}");
            }

            return new SalesOrderConfirmationResultDto
            {
                Uid = uid,
                Version = version,
                ConfirmationStatus = ConfirmationStatusEnum.已确认,
                GrossDemandCount = reception.TotalCount
            };
        }, "确认销售订单失败", uid);

    /// <summary>
    /// 取消销售订单确认，并按毛需求历史决定物理删除或作废保留。
    /// </summary>
    /// <remarks>
    /// 从未拆解的毛需求可以物理删除；曾拆解但已安全回退的毛需求必须作废保留历史。
    /// 一旦存在活动拆解、净需求/分配历史缺口或正式下游执行事实，取消确认会被拒绝。
    /// <see cref="CancelSalesOrderConfirmationCommand.CommandId"/> 用作幂等键，同一命令重试返回首次执行结果。
    /// </remarks>
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
        Guid uid,
        CancelSalesOrderConfirmationCommand request)
        => ExecuteTransactionAsync(async () =>
        {
            ValidateCommandIdentity(uid, request.Uid);
            var reason = request.Reason?.Trim();
            if (request.CommandId == Guid.Empty)
                throw new WorkspaceValidationException("取消确认命令标识不能为空。");
            if (string.IsNullOrWhiteSpace(reason))
                throw new WorkspaceValidationException("取消确认原因不能为空。");
            if (reason.Length > 1000)
                throw new WorkspaceValidationException("取消确认原因不能超过 1000 个字符。");

            // 第一阶段：固定锁序为订单头 -> 幂等日志 -> 订单行 -> 毛需求，降低并发死锁风险。
            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new WorkspaceValidationException("销售订单不存在或已删除。");
            // 已成功执行过的 CommandId 直接重放日志结果，不再次修改订单或毛需求。
            var prior = await operationLogs.GetByCommandAsync(Module, CancelConfirmationOperation, request.CommandId);
            if (prior is not null)
            {
                if (prior.AggregateUid != uid || !string.Equals(prior.Reason, reason, StringComparison.Ordinal))
                    throw new WorkspaceValidationException("COMMAND_REUSED：同一 CommandId 已用于其他销售订单或不同取消原因。");
                if (header.ConfirmationStatus != ConfirmationStatusEnum.待确认)
                    throw new WorkspaceValidationException("取消确认日志与销售订单当前状态不一致，请先修复数据。");
                var replay = DeserializeCancellationResult(prior.ChangedFieldsJson);
                replay.Uid = uid;
                replay.ConfirmationStatus = ConfirmationStatusEnum.待确认;
                return replay;
            }

            if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
                throw new WorkspaceValidationException("已关闭的销售订单不能取消确认。");
            if (header.ConfirmationStatus == ConfirmationStatusEnum.待确认)
                throw new WorkspaceValidationException("销售订单已经是待确认状态。");
            if (header.Version != request.Version)
                throw new WorkspaceValidationException("销售订单已被其他用户修改，请刷新后重试。");

            // 第二阶段：锁定订单行和来源毛需求，验证一行一需求以及所有下游取消安全条件。
            var lines = await lineRepository.GetLinesByOrderIdAsync(uid, true);
            SourceDemandExitResult exitResult;
            try
            {
                exitResult = await sourceDemandReceptionService.CancelConfirmationAsync(
                    DocumentTypeStaticsEnum.SalesOrder, uid, lines.Select(line => line.Uid).ToArray(), reason);
            }
            catch (SourceDemandReceptionException ex)
            {
                throw new WorkspaceValidationException($"{ex.Code}：{ex.Message}");
            }

            // 最后恢复订单为待确认并写入幂等业务日志；日志与状态变更随同一事务提交。
            var statusResult = await headRepository.SetConfirmationStatusAsync(uid, request.Version, ConfirmationStatusEnum.待确认);
            EnsureWriteSucceeded(statusResult);
            var result = new SalesOrderConfirmationResultDto
            {
                Uid = uid,
                Version = statusResult.NewVersion ?? request.Version + 1,
                ConfirmationStatus = ConfirmationStatusEnum.待确认,
                GrossDemandCount = 0,
                HardDeletedGrossDemandCount = exitResult.HardDeletedCount,
                VoidedGrossDemandCount = exitResult.VoidedCount
            };
            await WriteCancellationLogAsync(header, request.CommandId, reason, result);
            return result;
        }, "取消销售订单确认失败", uid);

    /// <summary>
    /// 关闭销售订单，使其退出后续编辑和确认流程。
    /// </summary>
    /// <remarks>
    /// 若订单仍有未进入下游执行的活动毛需求，会先将其作废；存在拆解或下游执行时禁止直接关闭。
    /// 关闭保留订单及明细历史，不等同于删除草稿。
    /// </remarks>
    public Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            ValidateCommandIdentity(uid, request.Uid);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason)) throw new WorkspaceValidationException("关闭原因不能为空。");
            if (reason.Length > 1000) throw new WorkspaceValidationException("关闭原因不能超过 1000 个字符。");

            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new WorkspaceValidationException("销售订单不存在或已删除。");
            if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
                throw new WorkspaceValidationException("销售订单已经关闭。");
            if (header.Version != request.Version)
                throw new WorkspaceValidationException("销售订单已被其他用户修改，请刷新后重试。");

            var lines = await lineRepository.GetLinesByOrderIdAsync(uid, true);
            try
            {
                await sourceDemandReceptionService.ClosePendingAsync(
                    DocumentTypeStaticsEnum.SalesOrder, uid, lines.Select(line => line.Uid).ToArray(), reason);
            }
            catch (SourceDemandReceptionException ex)
            {
                throw new WorkspaceValidationException($"{ex.Code}：{ex.Message}");
            }

            EnsureWriteSucceeded(await headRepository.CloseWorkspaceAsync(uid, request.Version, reason));
            return await headRepository.GetWorkspaceDocumentAsync(uid)
                ?? throw new WorkspaceValidationException("销售订单已关闭，但重新加载失败。");
        }, "关闭销售订单失败", uid);

    /// <summary>
    /// 删除尚未确认且从未生成毛需求的销售订单草稿。
    /// </summary>
    /// <remarks>
    /// 这是物理业务删除入口，仅适用于 Open + 待确认且没有来源毛需求的订单；
    /// 已确认或需要保留历史的订单应使用取消确认、关闭等明确业务命令。
    /// </remarks>
    public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            ValidateCommandIdentity(uid, request.Uid);
            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new WorkspaceValidationException("销售订单不存在或已删除。");
            EnsureEditable(header);
            if (header.Version != request.Version)
                throw new WorkspaceValidationException("销售订单已被其他用户修改，请刷新后重试。");
            if (await sourceDemandReceptionService.HasActiveAsync(DocumentTypeStaticsEnum.SalesOrder, uid))
                throw new WorkspaceValidationException("销售订单已经生成毛需求，不能作为草稿删除。");

            EnsureWriteSucceeded(await headRepository.DeleteDraftAsync(uid, request.Version));
            await lineRepository.DeleteByOrderIdAsync(uid);
            return true;
        }, "删除销售订单草稿失败", uid);

    /// <summary>
    /// 校验保存请求中的完整行集合，并拆分为新增、更新、删除三组实体。
    /// </summary>
    /// <remarks>
    /// 同时校验行归属、行 Version、标准件/非标件不可原地切换，以及产品和客户产品引用的有效性。
    /// </remarks>
    private async Task<LineBuildResult> BuildLinesAsync(
        SaveSalesOrderRequest request,
        SalesOrderHeader header,
        IReadOnlyList<SalesOrderLine> existingLines,
        Customer customer)
    {
        var requestedLines = request.Lines ?? [];
        if (requestedLines.Count == 0) throw new WorkspaceValidationException("销售订单至少需要一条明细。");
        if (requestedLines.Any(line => line.Uid == Guid.Empty))
            throw new WorkspaceValidationException("销售订单行 Uid 不能为空。");
        if (requestedLines.GroupBy(line => line.Uid).Any(group => group.Count() > 1))
            throw new WorkspaceValidationException("销售订单行 Uid 不能重复。");

        var deletedLines = request.DeletedLines ?? [];
        if (deletedLines.Any(line => line.Uid == Guid.Empty || line.Version <= 0))
            throw new WorkspaceValidationException("删除行必须包含有效的 Uid 和 Version。");
        if (deletedLines.GroupBy(line => line.Uid).Any(group => group.Count() > 1))
            throw new WorkspaceValidationException("删除行 Uid 不能重复。");
        if (deletedLines.Any(deleted => requestedLines.Any(line => line.Uid == deleted.Uid)))
            throw new WorkspaceValidationException("同一订单行不能同时更新和删除。");

        var existingById = existingLines.ToDictionary(line => line.Uid);
        foreach (var deleted in deletedLines)
        {
            if (!existingById.TryGetValue(deleted.Uid, out var existing))
                throw new WorkspaceValidationException($"待删除订单行 {deleted.Uid} 不属于当前订单。");
            if (existing.Version != deleted.Version)
                throw new WorkspaceValidationException($"订单行 {existing.LineNumber} 已被其他用户修改，请刷新后重试。");
        }

        var existingIdSet = (await lineRepository.GetExistingIdListAsync(requestedLines.Select(line => line.Uid))).ToHashSet();
        if (existingIdSet.Any(uid => !existingById.ContainsKey(uid)))
            throw new WorkspaceValidationException("请求中包含属于其他订单的行 Uid。");
        var handledExistingIds = requestedLines.Where(line => existingById.ContainsKey(line.Uid)).Select(line => line.Uid)
            .Concat(deletedLines.Select(line => line.Uid)).ToHashSet();
        if (existingLines.Any(line => !handledExistingIds.Contains(line.Uid)))
            throw new WorkspaceValidationException("保存请求必须明确包含每条现有订单行，或在 DeletedLines 中提交删除版本。");

        var productIds = requestedLines.Where(line => !line.IsNonStandard && line.ProductId.HasValue)
            .Select(line => line.ProductId!.Value).Distinct().ToArray();
        var products = (await productRepository.GetByIdListAsync(productIds)).ToDictionary(product => product.Uid);
        var customerProductIds = requestedLines.Where(line => !line.IsNonStandard && line.CustomerProductId.HasValue)
            .Select(line => line.CustomerProductId!.Value).Distinct().ToArray();
        var customerProducts = (await customerProductRepository.GetByIdListAsync(customerProductIds)).ToDictionary(item => item.Uid);

        var activeLines = new List<SalesOrderLine>(requestedLines.Count);
        var addedLines = new List<SalesOrderLine>();
        var updatedLines = new List<SalesOrderLine>();
        for (var index = 0; index < requestedLines.Count; index++)
        {
            var dto = requestedLines[index];
            existingById.TryGetValue(dto.Uid, out var existing);
            if (existing is not null)
            {
                if (existing.Version != dto.Version)
                    throw new WorkspaceValidationException($"订单行 {existing.LineNumber} 已被其他用户修改，请刷新后重试。");
                if (existing.IsNonStandard != dto.IsNonStandard)
                    throw new WorkspaceValidationException($"订单行 {existing.LineNumber} 不能在标准件与非标件之间原地切换。");
            }

            var entity = existing ?? new SalesOrderLine
            {
                Uid = dto.Uid,
                SalesOrderId = header.Uid,
                Sku = string.Empty
            };
            ApplyLineChanges(entity, dto, index + 1, header, customer, products, customerProducts);
            activeLines.Add(entity);
            if (existing is null) addedLines.Add(entity); else updatedLines.Add(entity);
        }

        return new LineBuildResult(activeLines, addedLines, updatedLines, deletedLines);
    }

    /// <summary>
    /// 把单行编辑 DTO 应用到订单行实体，并计算金额、箱数、重量和体积小计。
    /// </summary>
    /// <remarks>
    /// 标准件必须关联有效产品；非标件禁止关联产品，并按行 Uid 生成稳定的临时 SKU。
    /// 描述、价格等字段在此保存为订单历史快照。
    /// </remarks>
    private static void ApplyLineChanges(
        SalesOrderLine entity,
        SalesOrderEditLineDto dto,
        int lineNumber,
        SalesOrderHeader header,
        Customer customer,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, CustomerProduct> customerProducts)
    {
        if (dto.Qty <= 0) throw new WorkspaceValidationException($"第 {lineNumber} 行数量必须大于零。");
        entity.LineNumber = lineNumber;
        entity.SalesOrderId = header.Uid;
        entity.OrderNo = header.OrderNo;
        entity.IsNonStandard = dto.IsNonStandard;

        Product? product = null;
        if (dto.IsNonStandard)
        {
            if (dto.ProductId.HasValue || dto.CustomerProductId.HasValue)
                throw new WorkspaceValidationException($"第 {lineNumber} 行是非标件，不能关联产品主数据。");
            if (string.IsNullOrWhiteSpace(dto.DescriptionEn))
                throw new WorkspaceValidationException($"第 {lineNumber} 行非标件必须填写产品描述。");
            entity.ProductId = null;
            entity.CustomerProductId = null;
            entity.Sku = NonStandardSkuGenerator.Create(dto.Uid);
            entity.MainImage = null;
        }
        else
        {
            if (!dto.ProductId.HasValue || dto.ProductId.Value == Guid.Empty || !products.TryGetValue(dto.ProductId.Value, out product))
                throw new WorkspaceValidationException($"第 {lineNumber} 行必须选择有效产品。");
            if (!string.Equals(dto.Sku?.Trim(), product.Sku, StringComparison.OrdinalIgnoreCase))
                throw new WorkspaceValidationException($"第 {lineNumber} 行的 ProductId 与 SKU 不一致，请重新选择产品。");
            entity.ProductId = product.Uid;
            entity.Sku = product.Sku;
            entity.MainImage = product.MainImage;
            if (dto.CustomerProductId.HasValue)
            {
                if (!customerProducts.TryGetValue(dto.CustomerProductId.Value, out var customerProduct)
                    || customerProduct.CustomerId != customer.Uid
                    || customerProduct.SkuId != product.Uid)
                    throw new WorkspaceValidationException($"第 {lineNumber} 行客户产品不属于当前客户或产品。");
                entity.CustomerProductId = customerProduct.Uid;
            }
            else
            {
                entity.CustomerProductId = null;
            }
        }

        entity.LinkSku = dto.LinkSku?.Trim();
        entity.CustomerSku = dto.CustomerSku?.Trim();
        entity.DescriptionEn = dto.DescriptionEn?.Trim();
        entity.ColorNameEn = dto.ColorNameEn?.Trim();
        entity.FeaturesEn = dto.FeaturesEn?.Trim();
        entity.Qty = dto.Qty;
        entity.CnyPrice = dto.CnyPrice;
        entity.UsdPrice = dto.UsdPrice;
        entity.CnyAmount = dto.CnyPrice * dto.Qty;
        entity.UsdAmount = dto.UsdPrice * dto.Qty;
        entity.PcsOfCarton = dto.PcsOfCarton;
        entity.NwOfCarton = dto.NwOfCarton;
        entity.GwOfCarton = dto.GwOfCarton;
        entity.CbmOfCarton = dto.CbmOfCarton;
        entity.Ctns = dto.PcsOfCarton > 0 ? dto.Qty / dto.PcsOfCarton : null;
        entity.NwSubTotal = entity.Ctns * dto.NwOfCarton;
        entity.GwSubTotal = entity.Ctns * dto.GwOfCarton;
        entity.CbmSubTotal = entity.Ctns * dto.CbmOfCarton;
        entity.ExpectedDeliveryDate = dto.ExpectedDeliveryDate;
        entity.SubNote = dto.SubNote?.Trim();
    }

    /// <summary>
    /// 把订单头编辑 DTO 应用到实体，并根据当前活动行重新计算订单汇总金额。
    /// </summary>
    private static void ApplyHeaderChanges(
        SalesOrderHeader header,
        SalesOrderEditHeaderDto dto,
        Customer customer,
        IReadOnlyList<SalesOrderLine> lines)
    {
        if (!dto.OrderDate.HasValue) throw new WorkspaceValidationException("订单日期不能为空。");
        if (dto.ExchangeRate <= 0) throw new WorkspaceValidationException("汇率必须大于零。");
        if (dto.DepositRate is < 0 or > 1) throw new WorkspaceValidationException("定金比例必须在 0 到 1 之间。");

        header.CustomerId = customer.Uid;
        header.CustomerCode = customer.CustomerCode;
        header.InvoiceNo = dto.InvoiceNo?.Trim();
        header.CustomerPoNo = dto.CustomerPoNo?.Trim();
        header.ShippedBy = dto.ShippedBy?.Trim();
        header.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
        header.ExchangeRate = dto.ExchangeRate;
        header.TotalAmountLocalCurrency = lines.Sum(line => line.CnyAmount ?? 0);
        header.TotalAmountOriginalCurrency = string.Equals(header.Currency, "CNY", StringComparison.OrdinalIgnoreCase)
            ? header.TotalAmountLocalCurrency
            : lines.Sum(line => line.UsdAmount ?? 0);
        header.ShipToName = dto.ShipToName?.Trim();
        header.ShipToAddress = dto.ShipToAddress?.Trim();
        header.ShipToPhone = dto.ShipToPhone?.Trim();
        header.Incoterm = dto.Incoterm?.Trim();
        header.IncotermSortRemark = dto.IncotermSortRemark?.Trim();
        header.IncotermLocation = dto.IncotermLocation?.Trim();
        header.DepositRate = dto.DepositRate;
        header.LeadTimeDays = dto.LeadTimeDays;
        header.PaymentMethod = dto.PaymentMethod?.Trim();
        header.SoFolder = dto.SoFolder?.Trim();
        header.Remark = dto.Remark?.Trim();
        header.OrderDate = dto.OrderDate;
        header.DeliveryDate = dto.DeliveryDate;
        header.ReplyDate = dto.ReplyDate;
        header.ETA = dto.Eta;
        header.ETD = dto.Etd;
    }

    /// <summary>将确认后的销售头行事实转换为需求领域接收命令，不在销售域构造毛需求实体。</summary>
    private static SourceDemandReceptionCommand CreateSourceDemandCommand(
        SalesOrderHeader header,
        IReadOnlyList<SalesOrderLine> lines,
        IReadOnlyDictionary<Guid, Product> products,
        int sourceVersion,
        Guid commandId)
    {
        return new SourceDemandReceptionCommand
        {
            CommandId = commandId,
            SourceType = DocumentTypeStaticsEnum.SalesOrder,
            SourceUid = header.Uid,
            SourceNo = header.OrderNo,
            SourceVersion = sourceVersion,
            CustomerId = header.CustomerId ?? Guid.Empty,
            RejectUnsubmittedActiveLines = true,
            Items = lines.Select(line =>
            {
                products.TryGetValue(line.ProductId ?? Guid.Empty, out var product);
                return new SourceDemandReceptionItem
                {
                    SourceLineId = line.Uid,
                    SourceLineNumber = line.LineNumber,
                    ProductId = line.ProductId,
                    IsNonStandard = line.IsNonStandard,
                    SkuSnapshot = line.Sku,
                    LinkSkuSnapshot = line.LinkSku,
                    CustomerSkuSnapshot = line.CustomerSku,
                    DescriptionSnapshot = line.DescriptionEn,
                    ColorSnapshot = line.ColorNameEn,
                    SpecificationSnapshot = line.FeaturesEn,
                    ImagePathSnapshot = line.MainImage,
                    UnitSnapshot = product?.Unit,
                    DemandQty = line.Qty,
                    RequiredDate = line.ExpectedDeliveryDate ?? header.DeliveryDate,
                    Remark = line.SubNote
                };
            }).ToArray()
        };
    }

    /// <summary>
    /// 校验保存请求最外层结构和必填主数据引用。
    /// </summary>
    private static void ValidateSaveShape(SaveSalesOrderRequest request)
    {
        if (request?.Header is null) throw new WorkspaceValidationException("销售订单头不能为空。");
        if (request.Header.CustomerId == Guid.Empty) throw new WorkspaceValidationException("请选择客户。");
    }

    /// <summary>
    /// 确保订单仍处于 Open + 待确认，因而允许编辑或作为草稿删除。
    /// </summary>
    private static void EnsureEditable(SalesOrderHeader header)
    {
        if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
            throw new WorkspaceValidationException("已关闭的销售订单不能编辑或删除。");
        if (header.ConfirmationStatus != ConfirmationStatusEnum.待确认)
            throw new WorkspaceValidationException("已确认的销售订单不能编辑或删除。");
    }

    /// <summary>
    /// 防止路由订单 Uid 与请求体 Uid 不一致而误操作其他单据。
    /// </summary>
    private static void ValidateCommandIdentity(Guid routeUid, Guid bodyUid)
    {
        if (routeUid == Guid.Empty || bodyUid == Guid.Empty || routeUid != bodyUid)
            throw new WorkspaceValidationException("路由 Uid 与请求体 Uid 不一致。");
    }

    /// <summary>
    /// 写入取消确认审计及幂等日志，保存本次回退数量和返回版本。
    /// </summary>
    private async Task WriteCancellationLogAsync(
        SalesOrderHeader header,
        Guid commandId,
        string reason,
        SalesOrderConfirmationResultDto result)
    {
        var payload = JsonSerializer.Serialize(new CancellationLogPayload
        {
            Version = result.Version,
            HardDeletedGrossDemandCount = result.HardDeletedGrossDemandCount,
            VoidedGrossDemandCount = result.VoidedGrossDemandCount
        });
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlationContext.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId,
            Module = Module,
            AggregateType = "SalesOrder",
            AggregateUid = header.Uid,
            AggregateNoSnapshot = header.OrderNo,
            OperationType = CancelConfirmationOperation,
            OperationName = "销售订单取消确认",
            Reason = reason,
            Summary = $"取消确认完成：硬删除 {result.HardDeletedGrossDemandCount} 条毛需求，作废保留 {result.VoidedGrossDemandCount} 条毛需求。",
            ChangedFieldsJson = payload,
            UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new WorkspaceValidationException("销售订单取消确认日志写入失败。");
    }

    /// <summary>
    /// 从既有取消确认日志恢复首次执行结果，用于安全响应同一 CommandId 的重试。
    /// </summary>
    private static SalesOrderConfirmationResultDto DeserializeCancellationResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new WorkspaceValidationException("取消确认幂等日志缺少结果数据。");
        CancellationLogPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CancellationLogPayload>(json);
        }
        catch (JsonException)
        {
            throw new WorkspaceValidationException("取消确认幂等日志结果数据损坏。");
        }
        if (payload is null || payload.Version <= 0)
            throw new WorkspaceValidationException("取消确认幂等日志结果数据无效。");
        return new SalesOrderConfirmationResultDto
        {
            Version = payload.Version,
            GrossDemandCount = 0,
            HardDeletedGrossDemandCount = payload.HardDeletedGrossDemandCount,
            VoidedGrossDemandCount = payload.VoidedGrossDemandCount
        };
    }

    /// <summary>
    /// 清理日志文本并限制长度，避免超出审计字段容量。
    /// </summary>
    private static string? Normalize(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>
    /// 将 Repository 写入失败统一转换为可返回给调用端的业务校验异常。
    /// </summary>
    private static void EnsureWriteSucceeded(RepositoryWriteResult result)
    {
        if (!result.Success) throw new WorkspaceValidationException(result.Message ?? "数据库写入失败。");
    }

    /// <summary>
    /// 校验批量 Repository 写入结果，发现第一条失败后终止当前事务。
    /// </summary>
    private static void EnsureAllWritesSucceeded(IEnumerable<RepositoryWriteResult> results)
    {
        var failure = results.FirstOrDefault(result => !result.Success);
        if (failure is not null) EnsureWriteSucceeded(failure);
    }

    /// <summary>
    /// 执行只读查询并统一包装业务错误；调用方主动取消时继续向上传播取消异常。
    /// </summary>
    private async Task<ServiceResult<T>> ExecuteReadAsync<T>(Func<Task<T>> action, string failureMessage, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ServiceResult<T>.Ok(await action());
        }
        catch (WorkspaceValidationException ex)
        {
            return ServiceResult<T>.Fail(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
    }

    /// <summary>
    /// 为一个完整业务命令创建事务，并统一处理提交、回滚、错误编号和资源释放。
    /// </summary>
    /// <remarks>
    /// 传入的 action 及其调用的 Repository 共享当前 scoped <see cref="DbSession"/>，
    /// 因此禁止在 Repository 内另开业务事务。业务校验返回具体消息，未知技术异常只暴露可追踪错误编号。
    /// </remarks>
    private async Task<ServiceResult<T>> ExecuteTransactionAsync<T>(
        Func<Task<T>> action,
        string failureMessage,
        Guid? documentUid)
    {
        if (_session.Transaction is not null)
            return ServiceResult<T>.Fail("当前请求已存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var data = await action();
            _session.Transaction.Commit();
            return ServiceResult<T>.Ok(data);
        }
        catch (WorkspaceValidationException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<T>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            var errorReference = CreateErrorReference();
            _logger.LogError(
                ex,
                "{FailureMessage}，事务已回滚。ErrorReference={ErrorReference}, DocumentUid={DocumentUid}",
                failureMessage,
                errorReference,
                documentUid);
            return ServiceResult<T>.Fail($"{failureMessage}（错误编号：{errorReference}）");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 优先从当前 TraceId 生成日志错误编号；无活动链路时使用随机 Guid。
    /// </summary>
    private static string CreateErrorReference()
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString();
        return !string.IsNullOrWhiteSpace(traceId)
            ? traceId[..Math.Min(12, traceId.Length)].ToUpperInvariant()
            : Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    /// <summary>
    /// 表示可安全返回给客户端的工作区业务校验错误。
    /// </summary>
    private sealed class WorkspaceValidationException(string message) : Exception(message);

    /// <summary>
    /// 取消确认幂等日志中保存的最小结果载荷。
    /// </summary>
    private sealed class CancellationLogPayload
    {
        public int Version { get; set; }
        public int HardDeletedGrossDemandCount { get; set; }
        public int VoidedGrossDemandCount { get; set; }
    }

    /// <summary>
    /// 保存流程完成校验后得到的订单行写入分组。
    /// </summary>
    private sealed record LineBuildResult(
        IReadOnlyList<SalesOrderLine> ActiveLines,
        IReadOnlyList<SalesOrderLine> AddedLines,
        IReadOnlyList<SalesOrderLine> UpdatedLines,
        IReadOnlyList<SalesOrderDeletedLineDto> DeletedLines);
}
