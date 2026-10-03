using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Core.Interfaces.Customers.CustomerProducts;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using System.Text.Json;

using SwiftExport.Application.Services.SalesOrders.Internal;
using static SwiftExport.Application.Services.SalesOrders.Internal.SalesOrderRules;
using SwiftExport.Application.Interfaces.SalesOrders.Draft;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
namespace SwiftExport.Application.Services.SalesOrders.Draft;

/// <summary>保存销售草稿头行及删除未确认草稿，保留行身份、版本和事务顺序。</summary>
public sealed class SalesOrderDraftService(ServiceBaseContext context, ISalesOrderHeadRepository headRepository, ISalesOrderLineRepository lineRepository, ISalesOrderQueryRepository queryRepository, IProductRepository productRepository, ICustomerRepository customerRepository, ICustomerProductRepository customerProductRepository, ISourceDemandReceptionService sourceDemandReceptionService, IDocumentNoService documentNoService, ICurrentUser currentUser, IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : ISalesOrderDraftService
{
    private const string CreateType = "Orders.SalesOrder.CreateDraft";
    private const string UpdateType = "Orders.SalesOrder.UpdateDraft";
    private const string DeleteType = "Orders.SalesOrder.DeleteDraft";
    /// <summary>
    /// 新建或更新未确认、未关闭的销售订单，并一次性保存订单头和全部行变更。
    /// </summary>
    /// <remarks>
    /// 新建时申请正式订单号；更新时校验头、行 Version，并把旧行号暂存到安全区，
    /// 以支持明细上下移动后重新编号。请求必须显式提交全部活动行以及待删除行。
    /// </remarks>
    public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request)
        => RunAsync(() => pipeline.ExecuteAsync<SaveSalesOrderRequest, SaveSalesOrderResponse>(
            request, request?.CommandId,
            request?.Header?.Uid == Guid.Empty ? CreateType : UpdateType,
            AuthorizeSaveAsync,
            token => ExecuteSaveCommandAsync(request!, token), CancellationToken.None));

    /// <summary>创建仍以空头身份为输入目标；实际身份和单号只在首次事务内生成。</summary>
    private async Task<ServiceResult<CommandExecutionResult<SaveSalesOrderResponse>>> ExecuteSaveCommandAsync(
        SaveSalesOrderRequest request, CancellationToken token)
    {
        var creating = request.Header.Uid == Guid.Empty;
        var commandType = creating ? CreateType : UpdateType;
        var target = creating ? (Guid?)null : request.Header.Uid;
        var result = await executor.ExecuteAsync(request.CommandId, commandType, target,
            new { request.Header, request.Lines, request.DeletedLines },
            ct => SaveInTransactionAsync(request, ct), token);
        return ServiceResult<CommandExecutionResult<SaveSalesOrderResponse>>.Ok(result);
    }

    /// <summary>首次执行按原顺序校验、锁定、写入头行并读取完整保存结果。</summary>
    private async Task<SaveSalesOrderResponse> SaveInTransactionAsync(SaveSalesOrderRequest request, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            // 第一阶段：识别新建/更新，并锁定更新订单以校验可编辑状态和乐观并发版本。
            ValidateSaveShape(request);
            var headerRequest = request.Header;
            var isCreate = headerRequest.Uid == Guid.Empty;
            var customer = await customerRepository.GetByIdAsync(headerRequest.CustomerId)
                ?? throw new SalesOrderValidationException("所选客户不存在或已删除。");

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
                    throw new SalesOrderValidationException(documentNoResult.Message);

                header = new SalesOrderHeader { OrderNo = documentNoResult.Data.DocumentNo };
                existingLines = [];
            }
            else
            {
                header = await headRepository.GetByIdAsync(headerRequest.Uid, true)
                    ?? throw new SalesOrderValidationException("销售订单不存在或已删除。", ServiceErrorCodes.NotFound);
                EnsureEditable(header);
                if (header.Version != headerRequest.Version)
                    throw new SalesOrderValidationException("销售订单已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
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
                    throw new SalesOrderValidationException("销售订单明细已发生变化，请刷新后重试。", ServiceErrorCodes.VersionConflict);
            }

            // 第三阶段：按删除、更新、新增顺序落库，最终恢复请求中的连续行号。
            EnsureAllWritesSucceeded(await lineRepository.DeleteLinesAsync(lineBuild.DeletedLines));
            EnsureAllWritesSucceeded(await lineRepository.UpdateLinesAsync(lineBuild.UpdatedLines));
            EnsureAllWritesSucceeded(await lineRepository.AddLinesAsync(lineBuild.AddedLines));

            var document = await queryRepository.GetDetailAsync(header.Uid)
                ?? throw new SalesOrderValidationException("销售订单已保存，但重新加载失败。");
            return new SaveSalesOrderResponse { Document = document };
    }

    /// <summary>
    /// 删除尚未确认且从未生成毛需求的销售订单草稿。
    /// </summary>
    /// <remarks>
    /// 这是物理业务删除入口，仅适用于 Open + 待确认且没有来源毛需求的订单；
    /// 已确认或需要保留历史的订单应使用取消确认、关闭等明确业务命令。
    /// </remarks>
    public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request)
        => RunAsync(() => pipeline.ExecuteAsync<SalesOrderCommandRequest, bool>(
            request, request?.CommandId, DeleteType, AuthorizeDeleteAsync,
            token => ExecuteDeleteCommandAsync(uid, request!, token), CancellationToken.None));

    /// <summary>路由身份在回执前校验；删除后的重试可直接读到持久化 true。</summary>
    private async Task<ServiceResult<CommandExecutionResult<bool>>> ExecuteDeleteCommandAsync(
        Guid uid, SalesOrderCommandRequest request, CancellationToken token)
    {
        if (uid == Guid.Empty || uid != request.Uid)
            return ServiceResult<CommandExecutionResult<bool>>.Fail(ServiceErrorCodes.Validation,
                "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(request.CommandId, DeleteType, uid,
            new { request.Uid, request.Version },
            ct => DeleteInTransactionAsync(uid, request, ct), token);
        return ServiceResult<CommandExecutionResult<bool>>.Ok(result);
    }

    /// <summary>只在首次执行时锁头、验证来源和版本，再删除草稿及明细。</summary>
    private async Task<bool> DeleteInTransactionAsync(Guid uid, SalesOrderCommandRequest request, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            ValidateCommandIdentity(uid, request.Uid);
            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new SalesOrderValidationException("销售订单不存在或已删除。", ServiceErrorCodes.NotFound);
            EnsureEditable(header);
            if (header.Version != request.Version)
                throw new SalesOrderValidationException("销售订单已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
            if (await sourceDemandReceptionService.HasActiveAsync(DocumentTypeStaticsEnum.SalesOrder, uid))
                throw new SalesOrderValidationException("销售订单已经生成毛需求，不能作为草稿删除。", ServiceErrorCodes.BusinessBlocked);

            EnsureWriteSucceeded(await headRepository.DeleteDraftAsync(uid, request.Version));
            await lineRepository.DeleteByOrderIdAsync(uid);
            return true;
    }

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
        if (requestedLines.Count == 0) throw new SalesOrderValidationException("销售订单至少需要一条明细。");
        if (requestedLines.Any(line => line.Uid == Guid.Empty))
            throw new SalesOrderValidationException("销售订单行 Uid 不能为空。");
        if (requestedLines.GroupBy(line => line.Uid).Any(group => group.Count() > 1))
            throw new SalesOrderValidationException("销售订单行 Uid 不能重复。");

        var deletedLines = request.DeletedLines ?? [];
        if (deletedLines.Any(line => line.Uid == Guid.Empty || line.Version <= 0))
            throw new SalesOrderValidationException("删除行必须包含有效的 Uid 和 Version。");
        if (deletedLines.GroupBy(line => line.Uid).Any(group => group.Count() > 1))
            throw new SalesOrderValidationException("删除行 Uid 不能重复。");
        if (deletedLines.Any(deleted => requestedLines.Any(line => line.Uid == deleted.Uid)))
            throw new SalesOrderValidationException("同一订单行不能同时更新和删除。");

        var existingById = existingLines.ToDictionary(line => line.Uid);
        foreach (var deleted in deletedLines)
        {
            if (!existingById.TryGetValue(deleted.Uid, out var existing))
                throw new SalesOrderValidationException($"待删除订单行 {deleted.Uid} 不属于当前订单。");
            if (existing.Version != deleted.Version)
                throw new SalesOrderValidationException($"订单行 {existing.LineNumber} 已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        }

        var existingIdSet = (await lineRepository.GetExistingIdListAsync(requestedLines.Select(line => line.Uid))).ToHashSet();
        if (existingIdSet.Any(uid => !existingById.ContainsKey(uid)))
            throw new SalesOrderValidationException("请求中包含属于其他订单的行 Uid。");
        var handledExistingIds = requestedLines.Where(line => existingById.ContainsKey(line.Uid)).Select(line => line.Uid)
            .Concat(deletedLines.Select(line => line.Uid)).ToHashSet();
        if (existingLines.Any(line => !handledExistingIds.Contains(line.Uid)))
            throw new SalesOrderValidationException("保存请求必须明确包含每条现有订单行，或在 DeletedLines 中提交删除版本。");

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
                    throw new SalesOrderValidationException($"订单行 {existing.LineNumber} 已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
                if (existing.IsNonStandard != dto.IsNonStandard)
                    throw new SalesOrderValidationException($"订单行 {existing.LineNumber} 不能在标准件与非标件之间原地切换。");
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
    /// 描述、价格等字段在此保存为订单历史快照；标准件单位只在新建、换产品或旧行缺失时取当前产品单位。
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
        if (dto.Qty <= 0) throw new SalesOrderValidationException($"第 {lineNumber} 行数量必须大于零。");
        entity.LineNumber = lineNumber;
        entity.SalesOrderId = header.Uid;
        entity.OrderNo = header.OrderNo;
        entity.IsNonStandard = dto.IsNonStandard;

        Product? product = null;
        var originalProductId = entity.ProductId;
        if (dto.IsNonStandard)
        {
            if (dto.ProductId.HasValue || dto.CustomerProductId.HasValue)
                throw new SalesOrderValidationException($"第 {lineNumber} 行是非标件，不能关联产品主数据。");
            if (string.IsNullOrWhiteSpace(dto.DescriptionEn))
                throw new SalesOrderValidationException($"第 {lineNumber} 行非标件必须填写产品描述。");
            entity.ProductId = null;
            entity.CustomerProductId = null;
            entity.Sku = NonStandardSkuGenerator.Create(dto.Uid);
            entity.MainImage = null;
            if (string.IsNullOrWhiteSpace(dto.UnitSnapshot))
                throw new SalesOrderValidationException($"第 {lineNumber} 行非标件必须填写单位。");
            entity.UnitSnapshot = dto.UnitSnapshot.Trim();
        }
        else
        {
            if (!dto.ProductId.HasValue || dto.ProductId.Value == Guid.Empty || !products.TryGetValue(dto.ProductId.Value, out product))
                throw new SalesOrderValidationException($"第 {lineNumber} 行必须选择有效产品。");
            if (!string.Equals(dto.Sku?.Trim(), product.Sku, StringComparison.OrdinalIgnoreCase))
                throw new SalesOrderValidationException($"第 {lineNumber} 行的 ProductId 与 SKU 不一致，请重新选择产品。");
            entity.ProductId = product.Uid;
            entity.Sku = product.Sku;
            entity.MainImage = product.MainImage;
            if (string.IsNullOrWhiteSpace(product.Unit))
                throw new SalesOrderValidationException($"第 {lineNumber} 行产品未设置单位，不能保存销售订单。");
            // 同一产品的历史单位无法从当前主数据证明；仅新选择产品时建立单位快照。
            if (originalProductId != product.Uid)
                entity.UnitSnapshot = product.Unit.Trim();
            else if (string.IsNullOrWhiteSpace(entity.UnitSnapshot))
                throw new SalesOrderValidationException("历史销售行单位未知，请核实来源单位后再保存。");
            if (dto.CustomerProductId.HasValue)
            {
                if (!customerProducts.TryGetValue(dto.CustomerProductId.Value, out var customerProduct)
                    || customerProduct.CustomerId != customer.Uid
                    || customerProduct.SkuId != product.Uid)
                    throw new SalesOrderValidationException($"第 {lineNumber} 行客户产品不属于当前客户或产品。");
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
        if (!dto.OrderDate.HasValue) throw new SalesOrderValidationException("订单日期不能为空。");
        if (dto.ExchangeRate <= 0) throw new SalesOrderValidationException("汇率必须大于零。");
        if (dto.DepositRate is < 0 or > 1) throw new SalesOrderValidationException("定金比例必须在 0 到 1 之间。");

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

    /// <summary>
    /// 校验保存请求最外层结构和必填主数据引用。
    /// </summary>
    private static void ValidateSaveShape(SaveSalesOrderRequest request)
    {
        if (request?.Header is null) throw new SalesOrderValidationException("销售订单头不能为空。");
        if (request.Header.CustomerId == Guid.Empty) throw new SalesOrderValidationException("请选择客户。");
    }

    /// <summary>在验证及回执读取前分别检查登录与销售维护权限。</summary>
    private Task<ServiceResult<SaveSalesOrderResponse>?> AuthorizeSaveAsync(CancellationToken token)
        => AuthorizeAsync<SaveSalesOrderResponse>(PermissionKeys.SalesOrderMaintain, token);

    /// <summary>删除草稿使用独立删除权限，不由保存或销售导航自动授权。</summary>
    private Task<ServiceResult<bool>?> AuthorizeDeleteAsync(CancellationToken token)
        => AuthorizeAsync<bool>(PermissionKeys.SalesOrderDelete, token);

    /// <summary>用当前权限服务检查本次销售操作的具体稳定键。</summary>
    private async Task<ServiceResult<T>?> AuthorizeAsync<T>(string permissionKey, CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, permissionKey, token)
            ? null : ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有此销售订单操作权限。");
    }

    /// <summary>仅转换已知请求和业务错误；公共执行器的结果不明码原样返回。</summary>
    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<T>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SalesOrderValidationException ex)
        { return ServiceResult<T>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesOrderDraftService>()
                .LogError(ex, "销售草稿命令结果需按原 CommandId 核实。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, "销售草稿命令失败，请核实命令结果。");
        }
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
