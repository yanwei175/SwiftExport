using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using System.Text.Json;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>
/// 编排人工入库的过账预览与正式过账，当前仅支持采购入库。
/// 预览只读；正式过账通过公共命令管道完成授权、参数验证和耗时日志，
/// 再由公共执行器统一管理事务与回执，本类负责来源、单据、库存和业务审计的协作。
/// </summary>
/// <param name="repository">锁定入库头行并更新过账状态，与公共执行器共用数据库事务。</param>
/// <param name="executionReads">读取库存流水、余额和仓库状态，供过账业务校验使用。</param>
/// <param name="queries">读取入库完整详情，供来源发现和预览计算使用。</param>
/// <param name="sourceRepository">锁定采购来源并读取采购收货依据。</param>
/// <param name="logs">写业务操作审计，记录谁对哪张单据做了什么，不承担命令重放。</param>
/// <param name="user">当前操作人，用于授权、过账人及审计快照。</param>
/// <param name="permissions">核对入库过账的细粒度权限。</param>
/// <param name="inventoryPosting">在同一事务中写入库存流水并更新余额的库存内核。</param>
/// <param name="executor">统一处理命令锁、回执重放、事务提交和失败回滚。</param>
/// <param name="correlation">本次调用的链路标识，用于关联技术日志与业务审计。</param>
/// <param name="pipeline">统一执行命令授权、FluentValidation 验证及完成耗时日志。</param>
/// <param name="support">提供入库模块访问检查，并将模块异常转换为统一服务结果。</param>
public sealed class InboundPostingService(
    IInboundDocumentRepository repository, IInboundExecutionReadRepository executionReads, IInboundQueryRepository queries, IInboundSourceRepository sourceRepository, IBusinessOperationLogRepository logs, ICurrentUser user,
    IPermissionEvaluationService permissions, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    InboundServiceSupport support) : IInboundPostingService
{
    // 业务审计的模块分类值，写入 BusinessOperationLog.Module。
    // InboundManager 是既有分类名称，不代表仍依赖同名 Manager 服务；不参与权限或命令幂等判断。
    private const string Module = "InboundManager";

    /// <summary>
    /// 根据单据身份和期望版本生成过账预览，返回阻断原因及预计库存变化。
    /// 仅检查权限和读取当前事实，不预留库存、不保存回执，也不保证稍后的正式提交一定成功。
    /// </summary>
    public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            // RunAsync 默认先检查登录与库存模块访问权限，此处再检查过账细权限。
            await RequirePostingPermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new DraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
            return await BuildPostingPreviewAsync(detail, request.ExpectedVersion, token);
        }, token);

    /// <summary>
    /// 按 CommandId 执行入库过账；首次成功时原子写入库存、单据状态、业务审计和命令回执。
    /// 相同命令、相同输入重试时返回首次结果，不重复执行下面的业务委托。
    /// ExpectedVersion 防止旧版本操作新事实；取消令牌传递到验证、锁定和数据库操作。
    /// </summary>
    public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token = default) =>
        // 管道先授权、再验证请求，最后记录结果和耗时；通过验证后才进入命令执行方法。
        pipeline.ExecuteAsync<PostInboundCommand, InboundPostResult>(command, command?.CommandId,
            "Inventory.Inbound.Post", AuthorizePostingAsync<InboundPostResult>,
            ct => ExecutePostCommandAsync(uid, command!, ct), token);

    /// <summary>校验路由身份并交给公共执行器管理事务和回执；已有成功回执时跳过业务方法。</summary>
    private Task<ServiceResult<CommandExecutionResult<InboundPostResult>>> ExecutePostCommandAsync(
        Guid uid, PostInboundCommand command, CancellationToken token) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("单据标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            // 目标单据和业务输入参与指纹校验，防止同一 CommandId 被用于不同请求。
            return await executor.ExecuteAsync(command.CommandId, "Inventory.Inbound.Post", uid,
                new { command.ExpectedVersion }, ct => PostInTransactionAsync(uid, command, ct), token);
        }, token, checkPermission: false); // 管道已检查权限，此处保留模块错误包装。

    /// <summary>
    /// 在公共执行器拥有的事务内完成首次业务执行：锁定、校验、库存写入、单据更新和业务审计。
    /// 本方法不自行提交事务；异常交给执行器回滚，成功结果由执行器保存回执后提交。
    /// </summary>
    private async Task<InboundPostResult> PostInTransactionAsync(Guid uid, PostInboundCommand command, CancellationToken token)
    {
        // 1. 进入此方法时，公共执行器已开启事务并取得命令锁。
        // 先读取详情以发现采购来源，再按采购来源、入库头、入库行的顺序取得业务锁。
        // 首次详情读取不是最终业务依据，锁定后仍要重新读取并校验。
        var initial = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");

        if (initial.Header.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
            throw new DraftException("当前仅支持采购入库过账，其他来源类型尚未实现。");

        var ids = initial.Lines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).ToArray();

        await sourceRepository.LockPurchaseSourcesAsync(ids, token);

        var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");

        var rows = await repository.LockLinesAsync(uid, token);

        // 防止发现来源到取得锁期间，单据被修改或来源集合被替换。
        if (header.Version != command.ExpectedVersion || !rows.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).SequenceEqual(ids))
            throw new DraftException("入库版本或来源已变化，请重新加载后预览。", ServiceErrorCodes.VersionConflict);

        // 2. 持有业务锁后重算预览，不接受前端此前预览的 CanPost 作为提交依据。
        var detail = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");

        var preview = await BuildPostingPreviewAsync(detail, command.ExpectedVersion, token);

        if (!preview.CanPost)
        {
            // 预览返回全部阻断项；正式提交遇到阻断时返回第一项并交由执行器回滚。
            var first = preview.Blockers[0];
            throw new DraftException(first.Message, first.Code, first.LineId.HasValue ? $"Lines[{first.LineId}].Quantity" : null);
        }

        // 3. 将入库明细转换成库存内核的输入；采购来源决定普通采购或客供入库类型。
        // CommandItemId 使用稳定的入库行 Uid；库存流水关联入库凭证，而非直接关联采购行。
        var sources = (await sourceRepository.ReadPostingSourcesAsync(ids, token)).ToDictionary(x => x.SourceLineId);

        var items = rows.OrderBy(x => x.Uid).Select(x => new InventoryPostingItem
        {
            CommandItemId = x.Uid,
            WarehouseSiteId = header.WarehouseSiteId,
            ProductId = x.ProductId,
            SkuSnapshot = x.SkuSnapshot,
            ProductNameSnapshot = x.DescriptionSnapshot,
            UnitSnapshot = x.UnitSnapshot,
            QuantityDelta = x.Quantity,
            TransactionType = sources[x.SourceLineId].IsCustomerSupplied
                ? InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound
                : InventoryTransactionTypeEnum.PurchaseReceiptInbound,
            SourceDocumentType = "InboundDocument",
            SourceDocumentId = uid,
            SourceLineId = x.Uid,
            SourceDocumentNoSnapshot = header.DocumentNo,
            OccurredDate = header.ReceiptDate
        }).ToArray();

        // 库存内核执行最终库存约束并写流水、余额；异常转换后继续向外传播以回滚整个命令。
        try
        {
            await inventoryPosting.PostAsync(command.CommandId, items, token);
        }
        catch (InventoryPostingException ex)
        {
            throw new DraftException(ex.Message, ex.Code);
        }

        // 4. 更新过账状态和操作人。仓储带版本条件更新数据库，并递增 header.Version。
        header.PostedDate = DateTime.Now;
        header.PostedBy = user.UserName;
        await repository.MarkPostedAsync(header, token);
        var result = new InboundPostResult
        {
            Uid = uid,
            Version = header.Version,
            DocumentNo = header.DocumentNo,
            PostingStatus = InboundPostingStatusEnum.Posted
        };

        // 5. 业务审计与库存、单据共用事务，写入失败则不允许过账成功。
        // CommandId 关联业务意图；CorrelationId 关联本次调用；Module 仅用于审计分类。
        // ChangedFieldsJson 记录状态变化，RelatedObjectsJson 记录相关单据、行及库存命令。
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = command.CommandId,
            CorrelationId = correlation.CorrelationId,
            Module = Module,
            AggregateType = "InboundDocument",
            AggregateUid = uid,
            AggregateNoSnapshot = header.DocumentNo,
            OperationType = "Post",
            OperationName = "入库过账",
            Summary = "入库状态、库存事实及余额同事务提交。",
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                PostingStatus = new { Before = InboundPostingStatusEnum.Unposted, After = InboundPostingStatusEnum.Posted },
                Version = new { Before = command.ExpectedVersion, After = header.Version },
                header.PostedDate,
                header.PostedBy
            }),
            RelatedObjectsJson = JsonSerializer.Serialize(new
            {
                InboundUid = uid,
                LineUids = rows.Select(x => x.Uid).ToArray(),
                SourceLineIds = ids,
                InventoryCommandId = command.CommandId
            }),
            UserUid = user.UserId,
            UserNameSnapshot = user.UserName,
            OccurredDate = header.PostedDate.Value
        }) != 1) throw new DraftException("过账日志写入失败，过账已回滚。");

        // 6. 返回只表示业务委托完成；随后执行器保存成功回执并提交事务，才返回给外层管道。
        return result;
    }

    /// <summary>
    /// 正式命令在参数验证和回执读取前核对登录、库存模块访问与入库过账细权限。
    /// 返回 null 表示允许继续，返回失败结果则停止管道；重放也不能绕过当前权限。
    /// </summary>
    private async Task<ServiceResult<T>?> AuthorizePostingAsync<T>(CancellationToken token)
    {
        var refusal = await support.AuthorizeAsync<T>(token);
        if (refusal is not null) return refusal;
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundPost, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有入库过账权限。");
        return null;
    }

    /// <summary>预览专用的过账权限检查；调用前由 support 完成登录和模块权限检查，无权限时抛出业务异常。</summary>
    private async Task RequirePostingPermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundPost, token))
            throw new DraftException("当前账号没有入库过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>
    /// 根据当前详情和期望版本汇总过账阻断项、仓库信息及预计库存变化。
    /// 预览和正式提交共用此只读计算；本方法自身不取得业务写锁，提交时由调用方先锁定。
    /// 预计余额用于解释影响，最终库存正确性仍由库存写入内核保障。
    /// </summary>
    private async Task<InboundPostPreviewDto> BuildPostingPreviewAsync(InboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new InboundPostPreviewDto
        {
            Uid = h.Uid,
            Version = h.Version,
            DocumentNo = h.DocumentNo,
            WarehouseSiteId = h.WarehouseSiteId,
            WarehouseName = h.WarehouseNameSnapshot
        };
        // 汇总阻断原因，便于预览一次展示多项问题，而非发现第一项即抛出异常。
        void Block(string message, string code = ServiceErrorCodes.BusinessBlocked) => preview.Blockers.Add(new() { Message = message, Code = code });
        // A. 核对版本及适用范围：仅活动、人工、未过账且非 PackingList 自动触发的采购入库。
        if (h.Version != version) Block("入库版本已变化，请重新加载后预览。", ServiceErrorCodes.VersionConflict);
        if (h.PostingStatus != InboundPostingStatusEnum.Unposted || h.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active
            || h.CreationMode != InboundCreationModeEnum.Manual || h.TriggerPackingListId.HasValue || h.IsDeleted)
            Block("只有活动、人工、未过账且非 PL 触发的入库单可以过账。");
        if (h.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
        { Block("当前仅支持采购入库过账，其他来源类型尚未实现。"); return preview; }
        // B. 核对已保存明细的身份、数量精度、单位和来源唯一性，防止无效草稿进入库存内核。
        if (detail.Lines.Count is < 1 or > 500 || detail.Lines.Any(x => x.Uid == Guid.Empty || x.SourceLineId == Guid.Empty
            || x.ProductId == Guid.Empty || x.OriginalLineId.HasValue || x.Quantity <= 0 || x.Quantity > 99999999999999.9999m
            || decimal.Round(x.Quantity, 4) != x.Quantity || string.IsNullOrWhiteSpace(x.UnitSnapshot))
            || detail.Lines.Select(x => x.SourceLineId).Distinct().Count() != detail.Lines.Count)
            Block("入库明细身份、数量、单位或来源重复校验失败，请修改草稿。", ServiceErrorCodes.Validation);
        if (h.ReceiptDate == default || string.IsNullOrWhiteSpace(h.DocumentNo)) Block("入库日期或单号无效。", ServiceErrorCodes.Validation);
        // C. 已有库存流水时阻止新命令再次过账；原 CommandId 的合法重试已在执行器处重放。
        // 来源无法证明时不能可靠计算采购收货量；目标仓库还必须存在且可用。
        if (await executionReads.HasLedgerAsync(h.Uid, token)) Block("该入库单已有正式库存流水，不能再次过账。");
        if (await sourceRepository.HasUnprovenReceiptAsync(token)) Block("存在无法证明采购来源的正式入库历史，请先核实。");
        var name = await executionReads.ReadActiveWarehouseNameAsync(h.WarehouseSiteId, token);
        if (name is null) Block("目标仓库不存在、已删除或已停用。");
        else preview.WarehouseName = name;
        // D. 将采购来源匹配、可收货量等采购专属规则交给来源验证器。
        var ids = detail.Lines.Select(x => x.SourceLineId).Distinct().ToArray();
        InboundPurchasePostingValidator.Validate(detail, await sourceRepository.ReadPostingSourcesAsync(ids, token), preview);
        // E. 同一产品可能来自多条采购行，按产品合并数量后计算目标仓库的预计余额。
        foreach (var group in detail.Lines.GroupBy(x => x.ProductId))
        {
            var current = await executionReads.ReadBalanceAsync(h.WarehouseSiteId, group.Key, token);
            var delta = group.Sum(x => x.Quantity);
            preview.StockImpacts.Add(new()
            {
                ProductId = group.Key,
                Sku = group.First().SkuSnapshot,
                CurrentQuantity = current,
                QuantityDelta = delta,
                ExpectedQuantity = current + delta
            });
            if (current + delta > 99999999999999.9999m || current + delta < 0) Block("预计库存余额超出允许范围。");
        }
        // 所有检查均无阻断才允许提交；独立预览的结论只代表本次读取时的事实。
        preview.CanPost = preview.Blockers.Count == 0;
        return preview;
    }
}
