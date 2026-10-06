using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Services.DemandPlanning.Sources.Manual;
using SwiftExport.Application.Services.Systems.Numbering;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证真实手工来源服务、父执行器及PG仓储；成功事实保留，失败父事务回滚，不部署或清库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlManualGrossDemandServiceIntegration")]
public sealed class PostgreSqlManualGrossDemandServiceIntegrationTests
{
    /// <summary>输出随机身份及持久影响的JSON清单，供主控串行验收；不包含连接凭据。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>完整非标实体和DTO一致；另一物理连接Trim/忽略Sku等价重放不再取号、写入或查询本体。</summary>
    [TestMethod]
    public async Task NonStandardCreate_CompleteFactsAndIndependentReplay()
    {
        using var first = FixtureFor();
        var request = Request();
        var dto = Success(await first.Service.CreateManualAsync(request));
        var entity = await first.Gross.Inner.GetByIdForUpdateAsync(dto.Uid);
        Assert.IsNotNull(entity);
        Assert.AreEqual(dto.Uid, entity.Uid); Assert.AreEqual(dto.Uid, entity.SourceUid);
        Assert.AreEqual(dto.Uid, entity.SourceLineId); Assert.AreEqual("MD", entity.SourceType);
        Assert.AreEqual(1, entity.Version); Assert.AreEqual(GrossDemandLifecycleStatusEnum.Active, entity.LifecycleStatus);
        Assert.IsFalse(entity.IsDeleted); Assert.IsNull(entity.ProductId); Assert.IsNull(entity.CustomerId);
        Assert.IsNull(entity.SourceVersion); Assert.IsNull(entity.SourceLineNumber); Assert.IsNull(entity.UnitSnapshot);
        Assert.AreEqual(NonStandardSkuGenerator.Create(dto.Uid), dto.Sku);
        Assert.AreEqual("关联", dto.LinkSku); Assert.AreEqual("客户型号", dto.CustomerSku);
        Assert.AreEqual("描述", dto.Description); Assert.AreEqual("颜色", dto.Color);
        Assert.AreEqual("规格", dto.Specification); Assert.AreEqual("图片", dto.ImagePath);
        Assert.AreEqual("备注", dto.Remark); Assert.AreEqual(2.5m, dto.DemandQty);
        Assert.AreEqual(request.RequiredDate, dto.RequiredDate);
        Assert.AreEqual(entity.Version, dto.Version); Assert.AreEqual(entity.SourceType, dto.SourceType);
        Assert.AreEqual(entity.SourceUid, dto.SourceUid); Assert.AreEqual(entity.SourceLineId, dto.SourceLineId);
        Assert.AreEqual(entity.SourceNo, dto.SourceNo); Assert.AreEqual(entity.SourceVersion, dto.SourceVersion);
        Assert.AreEqual(entity.SourceLineNumber, dto.SourceLineNumber); Assert.AreEqual(entity.CustomerId, dto.CustomerId);
        Assert.AreEqual(entity.IsNonStandard, dto.IsNonStandard); Assert.AreEqual(entity.SkuSnapshot, dto.Sku);
        Assert.AreEqual(entity.LinkSkuSnapshot, dto.LinkSku); Assert.AreEqual(entity.CustomerSkuSnapshot, dto.CustomerSku);
        Assert.AreEqual(entity.DescriptionSnapshot, dto.Description); Assert.AreEqual(entity.ColorSnapshot, dto.Color);
        Assert.AreEqual(entity.SpecificationSnapshot, dto.Specification); Assert.AreEqual(entity.ImagePathSnapshot, dto.ImagePath);
        Assert.AreEqual(entity.DemandQty, dto.DemandQty); Assert.AreEqual(entity.RequiredDate, dto.RequiredDate);
        Assert.AreEqual(entity.Remark, dto.Remark); Assert.AreEqual(entity.CreatedDate, dto.CreatedDate);
        Assert.AreEqual(entity.CreatedBy, dto.CreatedBy); Assert.AreEqual(entity.UpdatedDate, dto.UpdatedDate);
        Assert.AreEqual(entity.UpdatedBy, dto.UpdatedBy); Assert.AreEqual(first.User.UserName, dto.CreatedBy);
        Assert.IsNotNull(dto.CreatedDate); Assert.IsNull(dto.LifecycleChangedAt);
        Assert.IsNull(dto.LifecycleChangedBy); Assert.IsNull(dto.LifecycleChangedReason);
        Assert.AreEqual(64, entity.DemandBasisFingerprint!.Length);
        // 原实物指纹序列化保留输入decimal尺度；数据库numeric读回尺度不能替代首次请求尺度。
        entity.DemandQty = request.DemandQty;
        var factory = typeof(ManualGrossDemandService).Assembly.GetType(
            "SwiftExport.Application.Services.DemandPlanning.Sources.Policies.SourceDemandBasisFingerprintFactory", true)!;
        Assert.AreEqual(entity.DemandBasisFingerprint,
            factory.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [entity]));
        Assert.AreEqual(first.Gross.Candidates.Single(), dto.Uid);
        Assert.AreEqual(1, first.Numbers.Calls); Assert.AreEqual(1, first.Gross.AddCalls);
        Assert.AreEqual(1, first.Query.Reads); Assert.IsNull(first.Session.Transaction);
        var original = JsonSerializer.Serialize(dto);
        await first.AssertPersisted(request.CommandId, dto, 1);
        using var replay = FixtureFor(actor: first.User.UserId);
        Assert.AreNotEqual(Pid(first.Session), Pid(replay.Session));
        request.Sku = "客户端不同SKU"; request.Description = "描述"; request.LinkSku = "关联";
        request.CustomerSku = "客户型号"; request.Color = "颜色"; request.Specification = "规格";
        request.ImagePath = "图片"; request.Remark = "备注";
        var result = Success(await replay.Service.CreateManualAsync(request));
        Assert.AreEqual(original, JsonSerializer.Serialize(result));
        Assert.AreNotEqual(dto.Uid, replay.Gross.Candidates.Single());
        Assert.AreEqual(0, replay.Numbers.Calls); Assert.AreEqual(0, replay.Gross.AddCalls);
        Assert.AreEqual(0, replay.Query.Reads); Assert.AreEqual(1, replay.Receipts.FindCalls);
        await replay.AssertPersisted(request.CommandId, result, 1);
    }

    /// <summary>相同生效内容的不同命令分别创建，真实MD键合法递增，不按内容去重。</summary>
    [TestMethod]
    public async Task SameContentDifferentCommand_CreatesTwoSources()
    {
        using var f = FixtureFor(); var request = Request();
        var first = Success(await f.Service.CreateManualAsync(request));
        await f.AssertPersisted(request.CommandId, first, 1);
        request.CommandId = Guid.NewGuid();
        var second = Success(await f.Service.CreateManualAsync(request));
        Assert.AreNotEqual(first.Uid, second.Uid); Assert.AreNotEqual(first.SourceNo, second.SourceNo);
        Assert.AreEqual(2, f.Numbers.Calls); Assert.AreEqual(2, f.Gross.AddCalls);
        await f.AssertPersisted(request.CommandId, second, 1);
    }

    /// <summary>修改与关闭真实推进版本和原因；旧Create/Update/Close回执先于当前状态版本重验。</summary>
    [TestMethod]
    public async Task UpdateClose_RealVersionsAndOldCreateReplay()
    {
        using var f = FixtureFor(); var create = Request();
        var original = Success(await f.Service.CreateManualAsync(create));
        await f.AssertPersisted(create.CommandId, original, 1);
        var update = new UpdateManualGrossDemandRequest
        { CommandId = Guid.NewGuid(), Uid = original.Uid, Version = 1,
            IsNonStandard = true, Description = "更新", DemandQty = 3, Remark = "修改备注" };
        var changed = Success(await f.Service.UpdateManualAsync(original.Uid, update));
        Assert.AreEqual(2, changed.Version); Assert.AreEqual("更新", changed.Description);
        Assert.AreEqual(3m, changed.DemandQty);
        await f.AssertPersisted(update.CommandId, changed, 1);
        var updateAgain = Success(await f.Service.UpdateManualAsync(original.Uid, update));
        Assert.AreEqual(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(updateAgain));
        update.CommandId = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.VersionConflict,
            (await f.Service.UpdateManualAsync(original.Uid, update)).Error.Code);
        await f.AssertNoReceipt(update.CommandId);
        var staleClose = new CloseGrossDemandCommand
        { CommandId = Guid.NewGuid(), Uid = original.Uid, Version = 1, Reason = "结束" };
        Assert.AreEqual(ServiceErrorCodes.VersionConflict,
            (await f.Service.CloseManualAsync(original.Uid, staleClose)).Error.Code);
        await f.AssertNoReceipt(staleClose.CommandId);
        var close = new CloseGrossDemandCommand
        { CommandId = Guid.NewGuid(), Uid = original.Uid, Version = 2, Reason = "  结束  " };
        var closed = Success(await f.Service.CloseManualAsync(original.Uid, close));
        Assert.AreEqual(3, closed.Version); Assert.AreEqual(GrossDemandLifecycleStatusEnum.Closed, closed.LifecycleStatus);
        Assert.AreEqual("结束", closed.LifecycleChangedReason); Assert.AreEqual(f.User.UserName, closed.LifecycleChangedBy);
        Assert.IsNotNull(closed.LifecycleChangedAt);
        await f.AssertPersisted(close.CommandId, closed, 1);
        close.Reason = "结束";
        Assert.AreEqual(JsonSerializer.Serialize(closed),
            JsonSerializer.Serialize(Success(await f.Service.CloseManualAsync(original.Uid, close))));
        using var replay = FixtureFor(actor: f.User.UserId);
        Assert.AreEqual(JsonSerializer.Serialize(original),
            JsonSerializer.Serialize(Success(await replay.Service.CreateManualAsync(create))));
        Assert.AreEqual(0, replay.Numbers.Calls); Assert.AreEqual(0, replay.Gross.AddCalls);
        Assert.AreEqual(0, replay.Query.Reads); Assert.AreEqual(1, f.Gross.UpdateCalls);
        Assert.AreEqual(1, f.Gross.CloseCalls); Assert.AreEqual(1, f.Numbers.Calls);
        await replay.AssertPersisted(create.CommandId, original, 1);
    }

    /// <summary>改变实际输入或操作者仍原CommandConflict，不重复业务；成功基线随机事实保留。</summary>
    [TestMethod]
    [DataRow("input")]
    [DataRow("actor")]
    public async Task CreateConflict_DoesNotWriteAgain(string difference)
    {
        using var f = FixtureFor(); var request = Request();
        var first = Success(await f.Service.CreateManualAsync(request));
        await f.AssertPersisted(request.CommandId, first, 1);
        using var challenger = FixtureFor(actor: difference == "actor" ? Guid.NewGuid() : f.User.UserId);
        if (difference == "input") request.DemandQty++;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await challenger.Service.CreateManualAsync(request)).Error.Code);
        Assert.AreEqual(0, challenger.Numbers.Calls); Assert.AreEqual(0, challenger.Gross.AddCalls);
        Assert.AreEqual(0, challenger.Query.Reads);
        await challenger.AssertPersisted(request.CommandId, first, 1);
    }

    /// <summary>认证/权限拒绝及路由不一致在准备和首次回执之前，未形成业务或号码事实。</summary>
    [TestMethod]
    [DataRow("unauthenticated")]
    [DataRow("forbidden")]
    [DataRow("route")]
    public async Task PermissionAndRoute_PrecedePreparation(string mode)
    {
        using var f = FixtureFor(failure: true);
        var before = await f.SequenceSnapshot();
        if (mode == "unauthenticated") f.User.Actor = null;
        if (mode == "forbidden") f.Allow = false;
        var request = Request();
        ServiceResult<GrossDemandDetailDto> result;
        if (mode == "route")
            result = await f.Service.UpdateManualAsync(Guid.NewGuid(), new UpdateManualGrossDemandRequest
            { CommandId = request.CommandId, Uid = Guid.NewGuid(), Version = 1 });
        else result = await f.Service.CreateManualAsync(request);
        Assert.AreEqual(mode == "unauthenticated" ? ServiceErrorCodes.Unauthenticated
            : mode == "forbidden" ? ServiceErrorCodes.Forbidden : ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Gross.Preparations); Assert.AreEqual(0, f.Receipts.FindCalls);
        Assert.AreEqual(0, f.Numbers.Calls); await f.AssertRolledBack(request.CommandId, before);
    }

    /// <summary>业务输入失败在原父事务内拒绝且取号零次，保持原中文提示，不用非法数据库夹具绕约束。</summary>
    [TestMethod]
    [DataRow("quantity", "需求数量必须大于零。")]
    [DataRow("description", "非标件描述不能为空。")]
    [DataRow("nonstandard-product", "非标件不能关联产品主数据。")]
    [DataRow("missing-product", "所选产品不存在或已删除。")]
    [DataRow("empty-product", "标准件必须选择产品。")]
    public async Task InvalidBusinessInput_DoesNotAllocateNumber(string mode, string message)
    {
        using var f = FixtureFor(failure: true); var before = await f.SequenceSnapshot(); var request = Request();
        if (mode == "quantity") request.DemandQty = 0;
        if (mode == "description") request.Description = " ";
        if (mode == "nonstandard-product") request.ProductId = Guid.NewGuid();
        if (mode == "missing-product") { request.IsNonStandard = false; request.ProductId = Guid.NewGuid(); }
        if (mode == "empty-product") request.IsNonStandard = false;
        var result = await f.Service.CreateManualAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreEqual(message, result.Message); Assert.AreEqual(0, f.Numbers.Calls);
        Assert.AreEqual(0, f.Gross.AddCalls); Assert.AreEqual(0, f.Query.Reads);
        Assert.AreEqual(1, f.Receipts.FindCalls); await f.AssertRolledBack(request.CommandId, before);
    }

    /// <summary>真实产品和标准快照保留；单位不被顺手补齐，客户端非生效字段变化仍重放。随机产品明确保留。</summary>
    [TestMethod]
    public async Task StandardProduct_RealReadAndOriginalSnapshots()
    {
        using var f = FixtureFor();
        var product = new Product { Sku = "PGMDP" + Guid.NewGuid().ToString("N"), SupplyType = "采购",
            DescriptionEn = "产品描述", LinkSku = "产品关联", FeaturesEn = "产品规格", MainImage = "产品图片",
            Unit = "PCS", IsActive = true };
        Assert.IsTrue((await f.Products.AddAsync(product)).Success);
        f.Emit("retained-product", new { product.Uid, product.Sku, ExpectedProductCount = 1 });
        var request = Request(); request.IsNonStandard = false; request.ProductId = product.Uid;
        var dto = Success(await f.Service.CreateManualAsync(request));
        Assert.AreEqual(product.Uid, dto.ProductId); Assert.AreEqual(product.Sku, dto.Sku);
        Assert.AreEqual(product.DescriptionEn, dto.Description); Assert.AreEqual(product.LinkSku, dto.LinkSku);
        Assert.AreEqual(product.FeaturesEn, dto.Specification); Assert.AreEqual(product.MainImage, dto.ImagePath);
        Assert.IsNull(dto.Unit); Assert.AreEqual("颜色", dto.Color); Assert.AreEqual("客户型号", dto.CustomerSku);
        await f.AssertPersisted(request.CommandId, dto, 1);
        using var replay = FixtureFor(actor: f.User.UserId);
        request.Description = "忽略"; request.LinkSku = "忽略"; request.Specification = "忽略";
        request.ImagePath = "忽略"; request.Sku = "忽略";
        Assert.AreEqual(JsonSerializer.Serialize(dto),
            JsonSerializer.Serialize(Success(await replay.Service.CreateManualAsync(request))));
        Assert.AreEqual(0, replay.Numbers.Calls); Assert.AreEqual(0, replay.Query.Reads);
        await replay.AssertPersisted(request.CommandId, dto, 1);
    }

    /// <summary>真实合法客供产品在获批提交库保留并记录；原政策先于号码，失败业务不残留。</summary>
    [TestMethod]
    public async Task CustomerSuppliedProduct_IsRejectedBeforeNumber()
    {
        var product = new Product { Sku = "PGMDP" + Guid.NewGuid().ToString("N"),
            SupplyType = "采购", MaterialType = "客供件" };
        using var retained = FixtureFor();
        Assert.IsTrue((await retained.Products.AddAsync(product)).Success);
        retained.Emit("retained-product", new { product.Uid, product.Sku, ExpectedProductCount = 1 });
        var request = Request(); request.IsNonStandard = false; request.ProductId = product.Uid;
        var snapshot = await retained.SequenceSnapshot();
        var result = await retained.Service.CreateManualAsync(request);
        Assert.AreEqual("客供件不能作为手工来源需求根产品。", result.Message);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code); Assert.AreEqual(0, retained.Numbers.Calls);
        await retained.AssertRolledBack(request.CommandId, snapshot);
    }

    /// <summary>真实Add、Workspace重读或回执追加后注入错误，先观测事务内事实再证明号码/来源/回执全部回滚。</summary>
    [TestMethod]
    [DataRow("add")]
    [DataRow("query")]
    [DataRow("receipt")]
    public async Task FailureAfterRealSql_RollsBackWholeCommand(string stage)
    {
        using var f = FixtureFor(failure: true); var before = await f.SequenceSnapshot(); var request = Request();
        f.Gross.FailAfterAdd = stage == "add"; f.Query.FailAfterRead = stage == "query";
        f.Receipts.FailAfterInsert = stage == "receipt";
        var result = await f.Service.CreateManualAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.AreEqual(1, f.Numbers.Calls); Assert.AreEqual(1, f.Gross.AddCalls);
        Assert.AreEqual(stage == "add" ? 0 : 1, f.Query.Reads);
        Assert.AreEqual(stage == "receipt" ? 1 : 0, f.Receipts.InsertCalls);
        Assert.IsNotNull(f.Numbers.Last); await f.AssertRolledBack(request.CommandId, before);
    }

    /// <summary>同Command真实等待机器租约时尚无来源准备/事务/回执；首次提交后另一连接权威重放。</summary>
    [TestMethod, Timeout(60000)]
    public async Task SameCommandConcurrency_WaitsBeforeSnapshotAndReplays()
    {
        using var first = FixtureFor(); using var second = FixtureFor(actor: first.User.UserId);
        using var observer = Open(CommittedConnection());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Query.BeforeRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(timeout.Token); };
        var request = Request(); var one = first.Service.CreateManualAsync(request);
        Task<ServiceResult<GrossDemandDetailDto>>? two = null;
        try
        {
            await reached.Task.WaitAsync(timeout.Token);
            two = second.Service.CreateManualAsync(request);
            await ObserveAttempt(observer, second.Session, timeout.Token);
            Assert.IsFalse(two.IsCompleted); Assert.IsNull(second.Session.Transaction);
            Assert.AreEqual(0, second.Gross.Preparations); Assert.AreEqual(0, second.Receipts.FindCalls);
            Assert.AreEqual(0, second.Numbers.Calls); Assert.AreEqual(0, second.Gross.AddCalls);
            release.TrySetResult();
            var a = Success(await one); var b = Success(await two);
            Assert.AreEqual(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
            Assert.AreEqual(1, first.Numbers.Calls); Assert.AreEqual(0, second.Numbers.Calls);
            Assert.AreEqual(0, second.Gross.AddCalls); Assert.AreEqual(0, second.Query.Reads);
            await first.AssertPersisted(request.CommandId, a, 1);
            await second.AssertPersisted(request.CommandId, b, 1);
        }
        finally
        {
            release.TrySetResult();
            await one;
            if (two is not null) await two;
        }
    }

    /// <summary>取得候选S后真实等U时没有父事务/Find/取号；释放才创建，取消层仅在测试准备转发器暴露。</summary>
    [TestMethod, Timeout(60000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CandidateUidContention_PrecedesReceiptAndBusiness(bool cancel)
    {
        using var f = FixtureFor(failure: cancel);
        var connection = cancel ? PostgreSqlIntegrationEnvironment.GetFailureConnectionString() : CommittedConnection();
        using var holder = Open(connection); using var observer = Open(connection);
        var holderRepository = new PostgreSqlGrossDemandDocumentRepository(Context(holder, f.User));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable? lease = null;
        f.Gross.PreparationCancellation = cancellation.Token;
        f.Gross.BeforePrepare = async (_, uids) =>
        {
            lease = await holderRepository.AcquireBeforeTransactionAsync([], uids, timeout.Token);
            Assert.IsNotNull(lease); entered.TrySetResult();
        };
        var request = Request(); var before = await f.SequenceSnapshot();
        var operation = f.Service.CreateManualAsync(request);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await ObserveAttempt(observer, f.Session, timeout.Token);
            var uid = f.Gross.Candidates.Single();
            Assert.AreEqual(1L, await SourceLockCount(observer, f.Session, uid, sourceOnly: true));
            Assert.IsFalse(operation.IsCompleted); Assert.IsNull(f.Session.Transaction);
            Assert.AreEqual(0, f.Receipts.FindCalls); Assert.AreEqual(0, f.Numbers.Calls);
            Assert.AreEqual(0, f.Gross.AddCalls); Assert.AreEqual(0, f.Query.Reads);
            if (cancel)
            {
                cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
                await f.AssertRolledBack(request.CommandId, before);
            }
            else
            {
                await lease!.DisposeAsync();
                var dto = Success(await operation);
                Assert.AreEqual(uid, dto.Uid); await f.AssertPersisted(request.CommandId, dto, 1);
            }
        }
        finally
        {
            cancellation.Cancel();
            if (lease is not null) await lease.DisposeAsync();
            try { await operation; } catch (OperationCanceledException) { }
        }
    }

    /// <summary>创建失败的准备发生在Find之前，即使已有成功回执也保持原结果并不冒充无额外技术阻断。</summary>
    [TestMethod]
    public async Task ReplayPreparationFailure_PrecedesAuthoritativeReceipt()
    {
        using var first = FixtureFor(); var request = Request();
        var dto = Success(await first.Service.CreateManualAsync(request));
        await first.AssertPersisted(request.CommandId, dto, 1);
        using var replay = FixtureFor(actor: first.User.UserId);
        replay.Gross.BeforePrepare = (_, _) => throw new InjectedFailure();
        Assert.AreEqual(ServiceErrorCodes.Unexpected,
            (await replay.Service.CreateManualAsync(request)).Error.Code);
        Assert.AreEqual(0, replay.Receipts.FindCalls); Assert.AreEqual(0, replay.Numbers.Calls);
        Assert.AreEqual(0, replay.Gross.AddCalls); Assert.IsNull(replay.Session.Transaction);
        await replay.AssertPersisted(request.CommandId, dto, 1);
    }

    /// <summary>仅建立本类随机请求，不提前冻结业务取号日期或使用测试DocType。</summary>
    private static CreateManualGrossDemandRequest Request() => new()
    {
        CommandId = Guid.NewGuid(), IsNonStandard = true, Sku = "客户端SKU", Description = "  描述  ",
        LinkSku = "  关联  ", CustomerSku = "  客户型号  ", Color = "  颜色  ",
        Specification = "  规格  ", ImagePath = "  图片  ", Remark = "  备注  ",
        DemandQty = 2.5m, RequiredDate = new DateTime(2026, 11, 6)
    };

    /// <summary>本批提交仅使用分派明确授权的既存RBAC专用库；缺开关不运行，不输出解析错误输入。</summary>
    private static string CommittedConnection()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_RBAC_TESTS") != "1"
            || string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("未显式启用本批手工来源提交测试；未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { Assert.Fail("本批专用连接格式无效。"); throw; }
        Assert.AreEqual("192.168.3.12", settings.Host); Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_pg_rbac_tests_20261005", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose);
        return connection!;
    }

    /// <summary>本类所有连接均独立原生Npgsql会话，不建立隐含父事务。</summary>
    private static DbSession Open(string connection) => new(new PostgreSqlConnectionFactory(connection));
    /// <summary>真实仓储共用父服务的会话、用户与审计端口。</summary>
    private static RepositoryBaseContext Context(DbSession session, User user)
        => new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    /// <summary>将机器清单输出给测试平台，运行时创建夹具，编译不会打开连接。</summary>
    private Fixture FixtureFor(bool failure = false, Guid? actor = null)
        => new(failure ? PostgreSqlIntegrationEnvironment.GetFailureConnectionString() : CommittedConnection(),
            actor ?? Guid.NewGuid(), item => TestContext.WriteLine(JsonSerializer.Serialize(item)));
    /// <summary>成功必须有实际DTO，不用空结果掩盖服务异常。</summary>
    private static GrossDemandDetailDto Success(ServiceResult<GrossDemandDetailDto> result)
    { Assert.IsTrue(result.Success, result.Message); Assert.IsNotNull(result.Data); return result.Data; }
    /// <summary>物理PID用于目录取证，不能只检查Task启动。</summary>
    private static int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
    /// <summary>等待真实生产try-lock查询且仍无服务器业务事务，轮询由显式令牌限定。</summary>
    private static async Task ObserveAttempt(DbSession observer, DbSession target, CancellationToken token)
    {
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND backend_xid IS NULL AND query LIKE '%pg_try_advisory_lock%')",
            new { Pid = Pid(target) }, cancellationToken: token))) await Task.Delay(25, token);
    }
    /// <summary>只计算生产冻结锁键作目录断言，不反射修改生产租约或重新实现获取。</summary>
    private static long SourceKey(string family, Guid uid) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:{family}:{uid:N}")));
    /// <summary>目录精确匹配已获取的来源/身份两个bigint会话锁，机器Command锁不计入。</summary>
    private static Task<long> SourceLockCount(DbSession observer, DbSession owner, Guid uid, bool sourceOnly = false)
        => observer.Connection.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory' AND granted AND objsubid=1
              AND ((classid::bigint << 32) | objid::bigint) IN (@Source,@Gross)
            """, new { Pid = Pid(owner), Source = SourceKey("Source", uid),
                Gross = sourceOnly ? SourceKey("Source", uid) : SourceKey("Gross", uid) });

    /// <summary>真实业务依赖及观察转发器；保留已提交来源、不可变回执、MD序号与明确记录的产品。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly Action<object> sink;
        private readonly Guid run = Guid.NewGuid();
        private readonly string connectionString;
        private readonly ServiceProvider validators;
        internal DbSession Session { get; }
        internal User User { get; }
        internal GrossProbe Gross { get; }
        internal QueryAdapter Query { get; }
        internal ReceiptProbe Receipts { get; }
        internal NumberProbe Numbers { get; }
        internal PostgreSqlProductRepository Products { get; }
        internal ManualGrossDemandService Service { get; }
        internal bool Allow { get; set; } = true;
        internal int PhysicalPid { get; }

        /// <summary>服务采用实际PG实现，只有权限及未迁物料退出是明确测试端口。</summary>
        internal Fixture(string connection, Guid actor, Action<object> output)
        {
            sink = output; connectionString = connection; User = new User { Actor = actor }; Session = Open(connection);
            PhysicalPid = PostgreSqlManualGrossDemandServiceIntegrationTests.Pid(Session);
            var context = PostgreSqlManualGrossDemandServiceIntegrationTests.Context(Session, User);
            Gross = new(new PostgreSqlGrossDemandDocumentRepository(context), this);
            Query = new(new PostgreSqlGrossDemandQueryRepository(context), this);
            Receipts = new(new PostgreSqlCommandReceiptRepository(context), this);
            Numbers = new(new DocumentNoService(new PostgreSqlDocumentSequenceRepository(context)), this);
            Products = new(context);
            var permissions = DispatchProxy.Create<IPermissionEvaluationService, PermissionProxy>();
            ((PermissionProxy)(object)permissions).Allowed = () => Allow;
            validators = new ServiceCollection()
                .AddSingleton<IValidator<CreateManualGrossDemandRequest>, CreateManualGrossDemandRequestValidator>()
                .AddSingleton<IValidator<UpdateManualGrossDemandRequest>, UpdateManualGrossDemandRequestValidator>()
                .AddSingleton<IValidator<CloseGrossDemandCommand>, CloseGrossDemandCommandValidator>().BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), new Correlation(),
                User, NullLogger<CommandPipeline>.Instance);
            Service = new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Gross, Query, Products,
                Numbers, new NoMaterialExit(Session), User, permissions, pipeline,
                new BusinessCommandExecutor(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Receipts, User));
            Emit("fixture", new { ActorUid = User.UserId, Database = Session.Connection.Database,
                Pid = PostgreSqlManualGrossDemandServiceIntegrationTests.Pid(Session), Retention = "成功来源/回执/MD取号/产品保留，失败父事务回滚" });
        }

        /// <summary>每个实际SQL阶段立即记录，断言中途失败仍可找回随机提交/待回滚身份。</summary>
        internal void Emit(string phase, object data) => sink(new { ManifestVersion = 1, RunUid = run, Phase = phase, Data = data });
        /// <summary>本类只观察当前MD键全部真实状态，失败后包括UID/审计的JSON必须完全相同。</summary>
        internal async Task<string> SequenceSnapshot() => await Session.Connection.ExecuteScalarAsync<string>(
            "SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.uid),'[]'::jsonb)::text FROM public.document_sequence s WHERE doc_type='MD'",
            transaction: Session.Transaction) ?? throw new AssertFailedException("序号快照不能为空。");
        /// <summary>成功回执存在且目标不被候选UID替代，来源只一行；旧回执与当前本体版本可不同。</summary>
        internal async Task AssertPersisted(Guid command, GrossDemandDetailDto dto, int expectedReceipt)
        {
            Assert.IsNull(Session.Transaction);
            var receipts = await Session.Connection.QuerySingleAsync<CommandReceipt>(
                "SELECT command_id AS \"CommandId\",command_type AS \"CommandType\",request_target_uid AS \"RequestTargetUid\",request_fingerprint AS \"RequestFingerprint\",result_json AS \"ResultJson\" FROM public.command_receipt WHERE command_id=@Command",
                new { Command = command });
            Assert.AreEqual(dto.Uid, JsonSerializer.Deserialize<GrossDemandDetailDto>(receipts.ResultJson)!.Uid);
            // 创建目标为空，修改/关闭目标为来源UID，不因准备生成另一个聚合身份。
            if (receipts.CommandType == "DemandPlanning.Source.Manual.Create") Assert.IsNull(receipts.RequestTargetUid);
            else Assert.AreEqual(dto.Uid, receipts.RequestTargetUid);
            Assert.AreEqual(64, receipts.RequestFingerprint.Length);
            var gross = await Session.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.gross_demand WHERE uid=@Uid", new { dto.Uid });
            var receiptCount = await Session.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.command_receipt WHERE command_id=@Command", new { Command = command });
            Assert.AreEqual(1, gross); Assert.AreEqual(expectedReceipt, receiptCount);
            var discarded = Gross.Candidates.Except(Gross.Added).ToArray();
            foreach (var candidate in discarded)
                Assert.AreEqual(0, await Session.Connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*)::integer FROM public.gross_demand WHERE uid=@Uid", new { Uid = candidate }));
            Assert.AreEqual(0L, await LockCount());
            Emit("persisted", new { CommandId = command, SourceUid = dto.Uid, dto.SourceNo,
                DiscardedCandidates = discarded,
                ExpectedGrossCount = 1, ExpectedReceiptCount = expectedReceipt, ActualGrossCount = gross,
                ActualReceiptCount = receiptCount, Sequence = await SequenceSnapshot() });
        }
        /// <summary>失败命令不能追加回执，不删除已有不可变事实。</summary>
        internal async Task AssertNoReceipt(Guid command)
        {
            Assert.AreEqual(0, await Session.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.command_receipt WHERE command_id=@Command", new { Command = command }));
            Emit("no-receipt", new { CommandId = command, ExpectedReceiptCount = 0 });
        }
        /// <summary>失败后同连接事务清空，所有候选来源为零，序号完整回滚，双租约锁零。</summary>
        internal async Task AssertRolledBack(Guid command, string sequence)
        {
            Assert.IsNull(Session.Transaction);
            // 取消可合法隔离原物理连接；独立观察失败事实，不要求已隔离连接还能SELECT。
            using var observer = Open(connectionString);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.command_receipt WHERE command_id=@Command", new { Command = command }));
            foreach (var uid in Gross.Candidates)
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*)::integer FROM public.gross_demand WHERE uid=@Uid", new { Uid = uid }));
            Assert.AreEqual(sequence, await observer.Connection.ExecuteScalarAsync<string>(
                "SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.uid),'[]'::jsonb)::text FROM public.document_sequence s WHERE doc_type='MD'"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'",
                new { Pid = PhysicalPid }, cancellationToken: deadline.Token)) != 0) await Task.Delay(25, deadline.Token);
            Emit("rolled-back", new { CommandId = command, SourceUids = Gross.Candidates, Numbers.Last,
                ExpectedGrossCount = 0, ExpectedReceiptCount = 0, Sequence = sequence });
        }
        /// <summary>检查当前物理会话全部advisory锁，不把关闭连接解除锁冒充正常释放。</summary>
        private Task<long> LockCount() => Session.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM pg_locks WHERE pid=pg_backend_pid() AND locktype='advisory'");
        /// <summary>仅关闭测试自己的会话和验证器，不清理持久事实或重置号码。</summary>
        public void Dispose() { Session.Dispose(); validators.Dispose(); }
    }

    /// <summary>转发真实Gross全部九方法及事务前准备，仅指定SQL后的观察/故障，不伪造返回。</summary>
    private sealed class GrossProbe(PostgreSqlGrossDemandDocumentRepository inner, Fixture f)
        : IGrossDemandDocumentRepository, ISourceDemandTransactionPreparation
    {
        internal PostgreSqlGrossDemandDocumentRepository Inner => inner;
        internal List<Guid> Candidates { get; } = [];
        internal List<Guid> Added { get; } = [];
        internal int Preparations, AddCalls, UpdateCalls, CloseCalls;
        internal bool FailAfterAdd;
        internal Func<IReadOnlyCollection<Guid>, IReadOnlyCollection<Guid>, Task>? BeforePrepare;
        internal CancellationToken PreparationCancellation;
        /// <summary>候选及既有身份都实际转发获取；只有Create有非空S，目录确认同UID的两个锁。</summary>
        public async Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(IReadOnlyCollection<Guid> sources,
            IReadOnlyCollection<Guid> uids, CancellationToken cancellationToken = default)
        {
            Preparations++; Assert.IsNull(f.Session.Transaction);
            if (sources.Count != 0)
            {
                CollectionAssert.AreEqual(sources.ToArray(), uids.ToArray()); Assert.AreEqual(1, sources.Count);
                Candidates.Add(sources.Single());
            }
            f.Emit("preparation", new { SourceUids = sources, GrossUids = uids });
            if (BeforePrepare is not null) await BeforePrepare(sources, uids);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, PreparationCancellation);
            var lease = await inner.AcquireBeforeTransactionAsync(sources, uids, linked.Token);
            try
            {
                if (sources.Count != 0 && lease is not null)
                {
                    // 获取成功后但仍在事务前；后续本体写入需复用这份覆盖。
                    Assert.IsNull(f.Session.Transaction);
                    Assert.AreEqual(2L, await SourceLockCount(f.Session, f.Session, sources.Single()));
                }
                return lease;
            }
            catch { if (lease is not null) await lease.DisposeAsync(); throw; }
        }
        /// <summary>真实INSERT和号码已在父事务内可观测，随后指定故障用于原子回滚验收。</summary>
        public async Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands)
        {
            AddCalls++; Assert.IsNotNull(f.Session.Transaction); var rows = demands.ToArray();
            var result = await inner.AddSourceDemandsAsync(rows);
            Assert.AreEqual(Candidates.Last(), rows.Single().Uid);
            Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.gross_demand WHERE uid=@Uid",
                new { rows.Single().Uid }, f.Session.Transaction));
            Added.Add(rows.Single().Uid);
            f.Emit("pending-gross", new { rows.Single().Uid, rows.Single().SourceNo, Transaction = "未提交" });
            if (FailAfterAdd) throw new InjectedFailure(); return result;
        }
        /// <summary>原来源锁定真实执行，既有身份无提前业务资格判断。</summary>
        public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid) => inner.GetByIdForUpdateAsync(uid);
        /// <summary>原活动范围真实转发。</summary>
        public Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string type, Guid uid) => inner.GetActiveBySourceForUpdateAsync(type, uid);
        /// <summary>原来源更新真实转发。</summary>
        public Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int version) => inner.UpdateSourceDemandAsync(demand, version);
        /// <summary>原Manual更新真实转发并计数首次业务写入。</summary>
        public Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int version)
        { UpdateCalls++; return inner.UpdateManualAsync(demand, version); }
        /// <summary>原关闭真实转发并计数，重放不得重复调用。</summary>
        public Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int version, string reason)
        { CloseCalls++; return inner.CloseManualAsync(uid, version, reason); }
        /// <summary>其他接口完整转发，测试不主动删除来源。</summary>
        public Task<int> HardDeleteForSourceCancellationAsync(IEnumerable<Guid> ids) => inner.HardDeleteForSourceCancellationAsync(ids);
        /// <summary>原物料退出作废端口真实转发。</summary>
        public Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> ids, string reason) => inner.VoidRolledBackForSourceCancellationAsync(ids, reason);
        /// <summary>原范围作废端口真实转发。</summary>
        public Task<int> VoidPendingBySourceAsync(string type, Guid uid, string reason) => inner.VoidPendingBySourceAsync(type, uid, reason);
    }

    /// <summary>局部接口适配仅转发实际PGWorkspace本体查询，其他尚未迁查询明确拒绝，不造生产占位。</summary>
    private sealed class QueryAdapter(PostgreSqlGrossDemandQueryRepository inner, Fixture f) : IGrossDemandQueryRepository
    {
        internal int Reads;
        internal bool FailAfterRead;
        internal Func<Task>? BeforeRead;
        /// <summary>真实重读后才可注入失败，屏障只控制测试观察，不伪造DTO。</summary>
        public async Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        {
            Reads++; Assert.IsNotNull(f.Session.Transaction);
            if (BeforeRead is not null) await BeforeRead();
            var result = await inner.GetWorkspaceDetailAsync(uid, cancellationToken);
            Assert.IsNotNull(result); f.Emit("pending-workspace", new { result.Uid, result.SourceNo });
            if (FailAfterRead) throw new InjectedFailure(); return result;
        }
        /// <summary>Manual不应访问工作台列表，避免缺迁移被默认数据掩盖。</summary>
        public Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("不应调用列表。");
        /// <summary>Manual不应访问来源分组。</summary>
        public Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("不应调用分组。");
        /// <summary>Manual不应访问追溯。</summary>
        public Task<GrossDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default) => throw new AssertFailedException("不应调用Trace。");
    }

    /// <summary>取号完整转发DocumentNoService/PGSequence，仍在原父事务，以真实行观测原子分配。</summary>
    private sealed class NumberProbe(DocumentNoService inner, Fixture f) : IDocumentNoService
    {
        internal int Calls;
        internal DocumentNoResponse? Last;
        /// <summary>只接受真实MD/墙上日期，不提前取号或换测试业务键。</summary>
        public async Task<ServiceResult<DocumentNoResponse>> GetNextDocumentNoAsync(DocumentNoRequest request)
        {
            Calls++; Assert.IsNotNull(f.Session.Transaction); Assert.AreEqual("MD", request.DocType);
            var result = await inner.GetNextDocumentNoAsync(request); Assert.IsTrue(result.Success, result.Message);
            Last = result.Data;
            Assert.AreEqual(Last.CurrentNo, await f.Session.Connection.ExecuteScalarAsync<int>(
                "SELECT current_no FROM public.document_sequence WHERE doc_type='MD' AND year_month=@Month AND is_deleted=FALSE",
                new { Month = Last.YearMonth }, f.Session.Transaction));
            f.Emit("pending-number", new { Last.DocumentNo, Last.CurrentNo, Last.YearMonth, Transaction = "未提交" });
            return result;
        }
    }

    /// <summary>机器租约/Lock/Find/Insert均真实转发，回执SQL执行之后才注入故障。</summary>
    private sealed class ReceiptProbe(PostgreSqlCommandReceiptRepository inner, Fixture f)
        : ICommandReceiptRepository, ICommandTransactionPreparation
    {
        internal int FindCalls, InsertCalls;
        internal bool FailAfterInsert;
        /// <summary>同Command竞争由真实生产租约阻塞，不返回模拟成功。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token)
        { f.Emit("command-intent", new { CommandId = commandId }); return inner.AcquireBeforeTransactionAsync(commandId, token); }
        /// <summary>原事务内Lock真实转发。</summary>
        public Task<int> LockAsync(Guid commandId, CancellationToken token) => inner.LockAsync(commandId, token);
        /// <summary>仅计数实际回执读取，不提前Find抢业务优先级。</summary>
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token)
        { FindCalls++; return inner.FindAsync(commandId, token); }
        /// <summary>追加不可变回执后父事务仍未提交，失败须整体回滚，不尝试删除补偿。</summary>
        public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            InsertCalls++; await inner.InsertAsync(receipt, token);
            Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::integer FROM public.command_receipt WHERE command_id=@Command",
                new { Command = receipt.CommandId }, f.Session.Transaction));
            f.Emit("pending-receipt", new { receipt.CommandId, receipt.CommandType, receipt.RequestTargetUid,
                Result = JsonSerializer.Deserialize<GrossDemandDetailDto>(receipt.ResultJson), Transaction = "未提交" });
            if (FailAfterInsert) throw new InjectedFailure();
        }
    }

    /// <summary>未迁物料退出仅允许原Manual的HasActive=false；不是完整物料建立/回退证据。</summary>
    private sealed class NoMaterialExit(DbSession session) : IMaterialDemandSourceExitService
    {
        /// <summary>资格仍在父事务内检查，本切片明确没有活动物料。</summary>
        public Task<bool> HasActiveAsync(Guid grossDemandId)
        { Assert.IsNotNull(session.Transaction); return Task.FromResult(false); }
        /// <summary>Manual不应评估其他来源退出历史。</summary>
        public Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> AssessSourceExitAsync(IEnumerable<Guid> grossDemandIds)
            => throw new AssertFailedException("Manual不应调用来源历史评估。");
    }
    /// <summary>仅允许真实服务所需独立权限键，其他新依赖立即失败。</summary>
    public class PermissionProxy : DispatchProxy
    {
        internal Func<bool> Allowed = null!;
        /// <summary>认证由真实服务先查测试用户，权限端口只响应指定ManualManage。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "HasPermissionAsync"
                ? Task.FromResult(Allowed() && (string)args![1]! == PermissionKeys.DemandPlanningSourceManualManage)
                : throw new AssertFailedException("未声明的权限调用。");
    }
    /// <summary>随机测试用户不依赖真实账户表，父审计使用固定可追踪名字。</summary>
    private sealed class User : ICurrentUser
    {
        internal Guid? Actor;
        /// <summary>本批审计标记，不含凭据。</summary>
        public string UserName => "__pgtest_manual_service__";
        /// <summary>测试显式认证身份。</summary>
        public Guid? UserId => Actor;
        /// <summary>空身份按真实未认证路径处理。</summary>
        public bool IsAuthenticated => Actor.HasValue;
    }
    /// <summary>固定关联标识只服务测试日志，不进入内容指纹。</summary>
    private sealed class Correlation : ICorrelationContext
    {
        /// <summary>本批关联标记。</summary>
        public string CorrelationId => "PG-manual-service";
    }
    /// <summary>SQL执行之后的可控测试故障，不冒充数据库异常。</summary>
    private sealed class InjectedFailure : Exception;
}

