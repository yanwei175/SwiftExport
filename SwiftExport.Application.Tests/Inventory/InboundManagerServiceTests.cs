using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using System.Data;
using System.Text.Json;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Entities.Systems;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Refit;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExport.WebApi.Controllers.Inventory.Inbounds;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Application.Commands;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>不连接数据库，验证入库草稿服务的事务、身份、状态和幂等编排。</summary>
[TestClass]
public sealed partial class InboundManagerServiceTests
{
    /// <summary>验证五个窄用例的依赖注入，以及十个 HTTP 端点与 Refit 契约的路由、授权、绑定和返回类型。</summary>
    [TestMethod]
    public void SplitServicesKeepDiAndHttpRoutes()
    {
        var services = new ServiceCollection().AddApplicationServices();
        Assert.AreEqual(typeof(InboundQueryService), services.Single(x => x.ServiceType == typeof(IInboundQueryService)).ImplementationType);
        Assert.AreEqual(typeof(InboundSourceQueryService), services.Single(x => x.ServiceType == typeof(IInboundSourceQueryService)).ImplementationType);
        Assert.AreEqual(typeof(InboundDraftService), services.Single(x => x.ServiceType == typeof(IInboundDraftService)).ImplementationType);
        Assert.AreEqual(typeof(InboundPostingService), services.Single(x => x.ServiceType == typeof(IInboundPostingService)).ImplementationType);
        Assert.AreEqual(typeof(InboundReversalService), services.Single(x => x.ServiceType == typeof(IInboundReversalService)).ImplementationType);
        Assert.AreEqual(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(InboundServiceSupport)).Lifetime);

        var routes = new (Type Controller, Type Service, Type Api, string Method, string Verb, string Prefix, string Path, Type? Body, Type Result)[]
        {
            (typeof(InboundQueryController), typeof(IInboundQueryService), typeof(IInboundManagerApi), "SearchAsync", "POST", "api/inventory/inbounds", "search", typeof(InboundManagerSearchRequest), typeof(Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>)),
            (typeof(InboundQueryController), typeof(IInboundQueryService), typeof(IInboundManagerApi), "GetAsync", "GET", "api/inventory/inbounds", "{uid:guid}", null, typeof(Task<ServiceResult<InboundManagerDetailDto>>)),
            (typeof(InboundSourcesController), typeof(IInboundSourceQueryService), typeof(IInboundSourceApi), "SearchAsync", "POST", "api/inventory/inbound-sources", "search", typeof(InboundSourceSearchRequest), typeof(Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>>)),
            (typeof(InboundDraftController), typeof(IInboundDraftService), typeof(IInboundManagerApi), "CreateAsync", "POST", "api/inventory/inbounds", "drafts", typeof(CreateInboundDraftCommand), typeof(Task<ServiceResult<InboundDraftCommandResult>>)),
            (typeof(InboundDraftController), typeof(IInboundDraftService), typeof(IInboundManagerApi), "UpdateAsync", "PUT", "api/inventory/inbounds", "drafts/{uid:guid}", typeof(UpdateInboundDraftCommand), typeof(Task<ServiceResult<InboundDraftCommandResult>>)),
            (typeof(InboundDraftController), typeof(IInboundDraftService), typeof(IInboundManagerApi), "DeleteAsync", "POST", "api/inventory/inbounds", "drafts/{uid:guid}/delete", typeof(DeleteInboundDraftCommand), typeof(Task<ServiceResult<InboundDraftCommandResult>>)),
            (typeof(InboundPostingController), typeof(IInboundPostingService), typeof(IInboundManagerApi), "PreviewPostAsync", "POST", "api/inventory/inbounds", "{uid:guid}/post-preview", typeof(InboundPostPreviewRequest), typeof(Task<ServiceResult<InboundPostPreviewDto>>)),
            (typeof(InboundPostingController), typeof(IInboundPostingService), typeof(IInboundManagerApi), "PostAsync", "POST", "api/inventory/inbounds", "{uid:guid}/post", typeof(PostInboundCommand), typeof(Task<ServiceResult<InboundPostResult>>)),
            (typeof(InboundReversalController), typeof(IInboundReversalService), typeof(IInboundManagerApi), "PreviewReverseAsync", "POST", "api/inventory/inbounds", "{uid:guid}/reverse-preview", typeof(InboundReversePreviewRequest), typeof(Task<ServiceResult<InboundReversePreviewDto>>)),
            (typeof(InboundReversalController), typeof(IInboundReversalService), typeof(IInboundManagerApi), "ReverseAsync", "POST", "api/inventory/inbounds", "{uid:guid}/reverse", typeof(ReverseInboundCommand), typeof(Task<ServiceResult<InboundReverseResult>>)),
        };
        Assert.AreEqual(10, routes.Length);
        Assert.AreEqual(10, routes.Select(x => (x.Verb, x.Prefix, x.Path)).Distinct().Count(), "入库端点不能重叠。");

        foreach (var route in routes)
        {
            Assert.IsNotNull(route.Controller.GetCustomAttribute<ApiControllerAttribute>());
            Assert.IsNotNull(route.Controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.AreEqual(route.Prefix, route.Controller.GetCustomAttribute<RouteAttribute>()!.Template);
            var constructor = route.Controller.GetConstructors().Single();
            Assert.AreEqual(1, constructor.GetParameters().Length);
            Assert.AreEqual(route.Service, constructor.GetParameters()[0].ParameterType);

            var controllerMethod = route.Controller.GetMethod(route.Method)!;
            var http = controllerMethod.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            Assert.AreEqual(route.Verb, http.HttpMethods.Single());
            Assert.AreEqual(route.Path, http.Template);
            Assert.AreEqual(route.Result, controllerMethod.ReturnType);
            Assert.AreEqual(typeof(CancellationToken), controllerMethod.GetParameters().Last().ParameterType);

            var apiMethod = route.Api.GetMethod(route.Method)!;
            Assert.AreEqual(route.Result, apiMethod.ReturnType);
            var refit = apiMethod.GetCustomAttributes().OfType<Refit.HttpMethodAttribute>().Single();
            Assert.AreEqual("/" + route.Path.Replace(":guid", ""), refit.Path);
            Assert.AreEqual(route.Verb, refit.Method.Method);
            if (route.Body is not null)
            {
                var body = controllerMethod.GetParameters().Single(x => x.ParameterType == route.Body);
                Assert.IsNotNull(body.GetCustomAttribute<FromBodyAttribute>());
                Assert.IsNotNull(apiMethod.GetParameters().Single(x => x.ParameterType == route.Body).GetCustomAttribute<BodyAttribute>());
            }
        }
    }

    /// <summary>代表性命令序列化字段与既有 JSON 契约一致，命名空间移动不改变传输名称。</summary>
    [TestMethod]
    public void MovedInboundCommandsKeepJsonFieldNames()
    {
        var commandId = Guid.NewGuid();
        using var posting = JsonDocument.Parse(JsonSerializer.Serialize(new PostInboundCommand { CommandId = commandId, ExpectedVersion = 3 }));
        Assert.AreEqual(commandId.ToString(), posting.RootElement.GetProperty("CommandId").GetString());
        Assert.AreEqual(3, posting.RootElement.GetProperty("ExpectedVersion").GetInt32());
        using var reversal = JsonDocument.Parse(JsonSerializer.Serialize(new ReverseInboundCommand { CommandId = commandId, ExpectedVersion = 4 }));
        Assert.AreEqual(commandId.ToString(), reversal.RootElement.GetProperty("CommandId").GetString());
        Assert.AreEqual(4, reversal.RootElement.GetProperty("ExpectedVersion").GetInt32());
    }

    /// <summary>入库单号按收货月份生成，而不是按保存当天；取号失败不得写入草稿。</summary>
    [TestMethod]
    public async Task Create_NumberUsesReceiptMonth_AndFailureRollsBack()
    {
        var f = new Fixture();
        var command = f.Create();
        command.Draft.ReceiptDate = new DateTime(2025, 12, 20);
        var result = await f.DraftService.CreateAsync(command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("IR2025120001", result.Data.DocumentNo);
        var failed = new Fixture { FailNumber = true };
        Assert.IsFalse((await failed.DraftService.CreateAsync(failed.Create())).Success);
        Assert.AreEqual(0, failed.Saves);
        Assert.AreEqual(1, failed.Connection.Rollbacks);
        Assert.AreEqual(0, failed.Connection.Commits);
    }

    /// <summary>状态筛选原样交给仓储；无效枚举在查询前返回结构化校验错误。</summary>
    [TestMethod]
    public async Task Search_ValidatesAndForwardsPostingStatus()
    {
        var f = new Fixture();
        var request = new InboundManagerSearchRequest { PostingStatus = InboundPostingStatusEnum.Reversed, PageIndex = 1, PageSize = 20 };
        Assert.IsTrue((await f.QueryService.SearchAsync(request)).Success);
        Assert.AreSame(request, f.LastSearch);
        request.PostingStatus = (InboundPostingStatusEnum)999;
        var invalid = await f.QueryService.SearchAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Validation, invalid.Error.Code);
        Assert.AreEqual(nameof(request.PostingStatus), invalid.Error.Field);
        Assert.AreEqual(1, f.Searches);
        request.PostingStatus = null;
        Assert.IsTrue((await f.QueryService.SearchAsync(request)).Success);
        Assert.AreEqual(0, f.Connection.Commits, "查询不得提交写事务。");
        Assert.AreEqual(0, f.Saves, "查询不得保存草稿。");
    }

    /// <summary>允许草稿数量超过当前剩余量；仓库独立，保存从真实来源取得快照。</summary>
    [TestMethod]
    public async Task Create_UsesSourceSnapshot_DoesNotReserveRemaining()
    {
        var f = new Fixture();
        var result = await f.DraftService.CreateAsync(f.Create());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, f.Saves);
        Assert.AreEqual("PCS", f.SavedLines.Single().UnitSnapshot);
        Assert.AreEqual($"IR{DateTime.Today:yyyyMM}0001", result.Data.DocumentNo);
        Assert.AreEqual(1, f.NumberCalls);
        Assert.AreEqual(10m, f.SavedLines.Single().Quantity);
        Assert.AreEqual(f.WarehouseId, f.Header!.WarehouseSiteId);
        Assert.AreEqual(1, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>同一命令同一输入只保存一次，不同输入返回明确幂等冲突。</summary>
    [TestMethod]
    public async Task Create_Replays_AndRejectsChangedPayload()
    {
        var f = new Fixture();
        var command = f.Create();
        var first = await f.DraftService.CreateAsync(command);
        var second = await f.DraftService.CreateAsync(command);
        Assert.IsTrue(second.Success, second.Message);
        Assert.AreEqual(first.Data.Uid, second.Data.Uid);
        Assert.AreEqual(first.Data.DocumentNo, second.Data.DocumentNo);
        Assert.AreEqual(1, f.NumberCalls);
        Assert.AreEqual(1, f.Saves);
        command.Draft.Lines[0].Quantity++;
        var conflict = await f.DraftService.CreateAsync(command);
        Assert.IsFalse(conflict.Success);
        StringAssert.Contains(conflict.Message, "COMMAND_CONFLICT");
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error.Code);
        Assert.AreEqual(1, f.Saves);
    }

    /// <summary>已提交的更新先重放原结果；权限撤销后不能读取历史回执。</summary>
    [TestMethod]
    public async Task Update_ReplaysWithOldVersion_ButRequiresCurrentPermission()
    {
        var f = new Fixture();
        await f.DraftService.CreateAsync(f.Create());
        var command = new UpdateInboundDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = f.Header!.Version, Draft = f.Create().Draft
        };
        var first = await f.DraftService.UpdateAsync(f.Header.Uid, command);
        Assert.IsTrue(first.Success, first.Message);
        var replay = await f.DraftService.UpdateAsync(f.Header.Uid, command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(first.Data.Version, replay.Data.Version);
        Assert.AreEqual(2, f.Saves);
        f.Allowed = false;
        var denied = await f.DraftService.UpdateAsync(f.Header.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.Error.Code);
        Assert.AreEqual(2, f.Saves);
    }

    /// <summary>日志失败必须回滚草稿事务，不能报告成功。</summary>
    [TestMethod]
    public async Task Create_LogFailure_RollsBack()
    {
        var f = new Fixture { FailLog = true };
        var result = await f.DraftService.CreateAsync(f.Create());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>整单版本冲突阻断修改；已过账和自动单据也不能删除。</summary>
    [TestMethod]
    public async Task UpdateAndDelete_EnforceVersionAndState()
    {
        var f = new Fixture();
        await f.DraftService.CreateAsync(f.Create());
        var update = await f.DraftService.UpdateAsync(f.Header!.Uid, new UpdateInboundDraftCommand
        { CommandId = Guid.NewGuid(), ExpectedVersion = 99, Draft = f.Create().Draft });
        Assert.IsFalse(update.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, update.Error.Code);
        Assert.AreEqual(1, f.Saves);
        foreach (var state in new[] { InboundPostingStatusEnum.Posted, InboundPostingStatusEnum.Reversed })
        {
            f.Header.PostingStatus = state;
            Assert.IsFalse((await f.DraftService.DeleteAsync(f.Header.Uid, f.Delete())).Success);
        }
        f.Header.PostingStatus = InboundPostingStatusEnum.Unposted;
        f.Header.CreationMode = InboundCreationModeEnum.Automatic;
        Assert.IsFalse((await f.DraftService.DeleteAsync(f.Header.Uid, f.Delete())).Success);
        Assert.AreEqual(0, f.Deletes);
    }

    /// <summary>已有行身份不能跨单提交；正常更新保留来源快照。</summary>
    [TestMethod]
    public async Task Update_RejectsForeignLine_AndPreservesSnapshot()
    {
        var f = new Fixture();
        await f.DraftService.CreateAsync(f.Create());
        var command = new UpdateInboundDraftCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Draft = f.Create().Draft };
        command.Draft.Lines[0].Uid = Guid.NewGuid();
        Assert.IsFalse((await f.DraftService.UpdateAsync(f.Header!.Uid, command)).Success);
        command.CommandId = Guid.NewGuid();
        command.Draft.Lines[0].Uid = f.SavedLines[0].Uid;
        command.Draft.Lines[0].Quantity = 5;
        var result = await f.DraftService.UpdateAsync(f.Header.Uid, command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.Version);
        Assert.AreEqual("PCS", f.SavedLines[0].UnitSnapshot);
        Assert.AreEqual(1, f.SourceReads);
    }

    /// <summary>软删支持响应丢失后重试；异常正式流水阻断删除。</summary>
    [TestMethod]
    public async Task Delete_ChecksLedger_AndReplays()
    {
        var f = new Fixture();
        await f.DraftService.CreateAsync(f.Create());
        f.HasLedger = true;
        Assert.IsFalse((await f.DraftService.DeleteAsync(f.Header!.Uid, f.Delete())).Success);
        f.HasLedger = false;
        var command = f.Delete();
        var first = await f.DraftService.DeleteAsync(f.Header.Uid, command);
        var second = await f.DraftService.DeleteAsync(f.Header.Uid, command);
        Assert.IsTrue(first.Success, first.Message);
        Assert.IsTrue(second.Success, second.Message);
        Assert.IsTrue(second.Data.IsDeleted);
        Assert.AreEqual(1, f.Deletes);
    }

    /// <summary>生产类型及无权限请求不访问来源仓储，采购查询不需要仓库参数。</summary>
    [TestMethod]
    public async Task Picker_ValidatesTypeAndPermission()
    {
        var f = new Fixture();
        var request = new InboundSourceSearchRequest { DocumentType = InboundDocumentTypeEnum.ProductionReceipt, PurchaseCategory = InboundPurchaseCategoryEnum.CustomerSupplied };
        Assert.IsFalse((await f.SourceQueryService.SearchSourcesAsync(request)).Success);
        request.DocumentType = InboundDocumentTypeEnum.PurchaseReceipt;
        Assert.IsTrue((await f.SourceQueryService.SearchSourcesAsync(request)).Success);
        Assert.AreEqual(1, f.Searches);
        f.Allowed = false;
        Assert.IsFalse((await f.SourceQueryService.SearchSourcesAsync(request)).Success);
        Assert.AreEqual(1, f.Searches);
    }

    /// <summary>命令锁繁忙、格式错误及权限失败提供机器可识别代码，不依赖消息文本。</summary>
    [TestMethod]
    public async Task WriteFailures_ExposeStructuredCodes()
    {
        var f = new Fixture { CommandLockResult = -1 };
        var busy = await f.DraftService.CreateAsync(f.Create());
        Assert.AreEqual(ServiceErrorCodes.CommandBusy, busy.Error.Code);
        Assert.AreEqual(0, f.Saves);
        var invalid = f.Create();
        invalid.Draft.Lines[0].Quantity = 1.00001m;
        var validation = await Assert.ThrowsAsync<SwiftExport.Application.Commands.Validation.CommandValidationException>(() => f.DraftService.CreateAsync(invalid));
        Assert.AreEqual("Draft.Lines[0].Quantity", validation.Field);
        f.Allowed = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.DraftService.CreateAsync(f.Create())).Error.Code);
    }

    /// <summary>仓储并发失败映射为版本冲突并回滚，不错误提示为可能已经保存。</summary>
    [TestMethod]
    public async Task RepositoryConcurrency_RollsBackAndReportsConflict()
    {
        var f = new Fixture { SaveFailure = new DBConcurrencyException("测试并发") };
        var result = await f.DraftService.CreateAsync(f.Create());
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>不能确认的提交异常使用专属代码；后续前端必须保留原命令。</summary>
    [TestMethod]
    public async Task UnknownRepositoryFailure_PreservesRetrySemantics()
    {
        var f = new Fixture { SaveFailure = new IOException("模拟连接中断") };
        var command = f.Create();
        var result = await f.DraftService.CreateAsync(command);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        f.SaveFailure = null;
        Assert.IsTrue((await f.DraftService.CreateAsync(command)).Success);
        Assert.AreEqual(1, f.Connection.Commits);
    }

    /// <summary>构造仅包含本用例调用面的替身，意外调用立即使测试失败。</summary>
    private sealed partial class Fixture
    {
        public readonly Guid WarehouseId = Guid.NewGuid();
        public Guid SourceId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Dictionary<Guid, BusinessOperationLog> _logs = [];
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly InboundReversalService ReversalService;
        public readonly InboundPostingService PostingService;
        public readonly InboundDraftService DraftService;
        public readonly InboundQueryService QueryService;
        public readonly InboundSourceQueryService SourceQueryService;
        public InboundDocument? Header;
        public List<InboundDocumentLine> SavedLines = [];
        public int Saves, Deletes, Searches, SourceReads;
        public bool FailLog, FailReceipt, HasLedger;
        public bool Allowed = true;
        public bool AllowReverse = true;
        public int CommandLockResult;
        public Exception? SaveFailure;
        public InboundManagerSearchRequest? LastSearch;
        public int NumberCalls;
        public int ReceiptReads, ReceiptWrites, LogReads;
        public bool FailNumber;

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            var user = Proxy<ICurrentUser>((method, _) => method switch { "get_IsAuthenticated" => true, "get_UserId" => _userId, "get_UserName" => "测试", _ => throw new NotSupportedException(method) });
            var execution = new BusinessCommandExecutor(new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                Proxy<ICommandReceiptRepository>((method, args) => method switch
                {
                    "LockAsync" => Task.FromResult(CommandLockResult),
                    "FindAsync" => FindReceipt((Guid)args[0]!),
                    "InsertAsync" => SaveReceipt((CommandReceipt)args[0]!),
                    _ => throw new NotSupportedException(method)
                }), user);
            var correlation = Proxy<ICorrelationContext>((method, _) => method == "get_CorrelationId" ? "inbound-test-call" : throw new NotSupportedException(method));
            var pipeline = new CommandPipeline(new SwiftExport.Application.Commands.Validation.CommandRequestValidator(
                Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
                    SwiftExport.Application.DependencyInjection.AddApplicationServices(new Microsoft.Extensions.DependencyInjection.ServiceCollection()))),
                correlation, user, Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandPipeline>.Instance);
            var repository = Proxy<IInboundManagerRepository>((method, args) => method switch
                {
                    "LockCommandAsync" => Task.FromResult(CommandLockResult),
                    "LockHeaderAsync" => Task.FromResult(Header),
                    "LockLinesAsync" => Task.FromResult<IReadOnlyList<InboundDocumentLine>>(SavedLines),
                    "HasLedgerAsync" => Task.FromResult(HasLedger),
                    "GetSourcesAsync" => Sources(),
                    "SaveAsync" => Save((InboundDocument)args[0]!, (IReadOnlyList<InboundDocumentLine>)args[1]!, (int)args[2]!),
                    "DeleteAsync" => DeleteHeader(),
                    "SearchSourcesAsync" => Search(),
                    "SearchAsync" => SearchManager((InboundManagerSearchRequest)args[0]!),
                    _ => PostingRepository(method, args)
                });
            var warehouses = Proxy<IWarehouseSiteRepository>((method, _) => method == "GetForUpdateAsync"
                ? Task.FromResult<WarehouseSite?>(new WarehouseSite { Uid = WarehouseId, Code = "W", Name = "参考仓", IsActive = true }) : throw new NotSupportedException(method));
            var logs = Proxy<IBusinessOperationLogRepository>((method, args) => method switch
                {
                    "GetByModuleCommandAsync" => FindLog((Guid)args[1]!),
                    "InsertAsync" => Log((BusinessOperationLog)args[0]!),
                    _ => throw new NotSupportedException(method)
                });
            var permissions = Proxy<IUserPermissionService>((method, args) => method == "HasPermissionAsync" ? Task.FromResult(Allowed &&
                ((string)args[1]! != SwiftExport.Contracts.DTOs.Auth.PermissionKeys.InventoryInboundPost || AllowPost) &&
                ((string)args[1]! != SwiftExport.Contracts.DTOs.Auth.PermissionKeys.InventoryInboundReverse || AllowReverse)) : throw new NotSupportedException(method));
            var numbers = new DocumentNoService(Proxy<IDocumentSequenceRepository>((method, args) =>
                {
                    if (method != "GetNextAsync") throw new NotSupportedException(method);
                    Assert.IsNotNull(Session.Transaction, "取号必须处于入库保存事务内。");
                    if (FailNumber) throw new InvalidOperationException("模拟取号失败");
                    NumberCalls++;
                    return Task.FromResult(new DocumentSequence { DocType = (string)args[0]!, YearMonth = (string)args[1]!, CurrentNo = NumberCalls });
                }));
            var inventoryPosting = Proxy<SwiftExport.Application.Interfaces.Inventory.IInventoryPostingService>((method, args) =>
                method == "PostAsync" ? PostInventory((IReadOnlyList<SwiftExport.Application.Interfaces.Inventory.InventoryPostingItem>)args[1]!) : ReverseInventory((IReadOnlyList<SwiftExport.Application.Interfaces.Inventory.InventoryReverseItem>)args[1]!));
            var support = new InboundServiceSupport(user, permissions, NullLogger<InboundServiceSupport>.Instance);
            ReversalService = new(repository, logs, user, permissions, inventoryPosting, execution, correlation, pipeline, support);
            PostingService = new(repository, logs, user, permissions, inventoryPosting, execution, correlation, pipeline, support);
            DraftService = new(repository, warehouses, logs, user, numbers, execution, correlation, pipeline, support);
            QueryService = new(repository, support);
            SourceQueryService = new(repository, support);
        }

        public CreateInboundDraftCommand Create() => new() { CommandId = Guid.NewGuid(), Draft = new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = WarehouseId, ReceiptDate = DateTime.Today, Lines = [new() { SourceLineId = SourceId, Quantity = 10 }] } };
        private Task<PagedResponseBase<InboundManagerListItemDto>> SearchManager(InboundManagerSearchRequest request)
        {
            Searches++; LastSearch = request;
            return Task.FromResult(new PagedResponseBase<InboundManagerListItemDto> { Items = [] });
        }
        public DeleteInboundDraftCommand Delete() => new() { CommandId = Guid.NewGuid(), ExpectedVersion = Header!.Version, Reason = "录入错误" };
        private Task<IReadOnlyList<InboundSourceCandidateDto>> Sources() { SourceReads++; return Task.FromResult<IReadOnlyList<InboundSourceCandidateDto>>([new() { SourceLineId = SourceId, ProductId = Guid.NewGuid(), Unit = "PCS", Sku = "A", RemainingReceivableQty = 1 }]); }
        private Task Save(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, int version)
        {
            if (SaveFailure is not null) throw SaveFailure;
            Saves++; Header = header; Header.Version = version + 1; SavedLines = lines.ToList();
            foreach (var line in SavedLines) line.Version++;
            return Task.CompletedTask;
        }
        private Task DeleteHeader() { Deletes++; Header!.Version++; return Task.CompletedTask; }
        /// <summary>保存测试回执；生产环境的持久性与唯一性由 SQL 专项验证。</summary>
        private Task<CommandReceipt?> FindReceipt(Guid commandId) { ReceiptReads++; return Task.FromResult(_receipts.GetValueOrDefault(commandId)); }
        private Task<BusinessOperationLog?> FindLog(Guid commandId) { LogReads++; return Task.FromResult(_logs.GetValueOrDefault(commandId)); }
        private Task SaveReceipt(CommandReceipt receipt)
        {
            if (FailReceipt) throw new IOException("模拟回执写入失败");
            ReceiptWrites++; _receipts.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }
        /// <summary>模拟日志 JSON 约束；草稿、过账和反向审计均不承担机器重放。</summary>
        private Task<int> Log(BusinessOperationLog log)
        {
            if (FailLog) return Task.FromResult(0);
            using var changed = JsonDocument.Parse(log.ChangedFieldsJson!);
            Assert.AreEqual(JsonValueKind.Object, changed.RootElement.ValueKind);
            if (log.OperationType is "Create" or "Update" or "Delete")
            {
                Assert.AreEqual("inbound-test-call", log.CorrelationId);
                Assert.IsFalse(changed.RootElement.TryGetProperty("Fingerprint", out _));
            }
            else if (log.OperationType == "Post")
            {
                Assert.AreEqual("inbound-test-call", log.CorrelationId);
                Assert.IsFalse(changed.RootElement.TryGetProperty("Fingerprint", out _));
                Assert.IsTrue(changed.RootElement.TryGetProperty("PostingStatus", out _));
                using var related = JsonDocument.Parse(log.RelatedObjectsJson!);
                Assert.AreEqual(log.CommandId, related.RootElement.GetProperty("InventoryCommandId").GetGuid());
                Assert.IsTrue(related.RootElement.GetProperty("LineUids").GetArrayLength() > 0);
            }
            else if (log.OperationType == "Reverse")
            {
                Assert.AreEqual("inbound-test-call", log.CorrelationId);
                Assert.IsFalse(changed.RootElement.TryGetProperty("Fingerprint", out _));
                Assert.IsTrue(changed.RootElement.TryGetProperty("PostingStatus", out _));
                using var related = JsonDocument.Parse(log.RelatedObjectsJson!);
                Assert.IsTrue(related.RootElement.GetProperty("OriginalLedgerIds").GetArrayLength() > 0);
                Assert.IsTrue(related.RootElement.GetProperty("ReverseCommandItemIds").GetArrayLength() > 0);
            }
            _logs.Add(log.CommandId, log);
            return Task.FromResult(1);
        }
        private Task<PagedResponseBase<InboundSourceCandidateDto>> Search() { Searches++; return Task.FromResult(new PagedResponseBase<InboundSourceCandidateDto> { PageIndex = 1, PageSize = 20 }); }
    }

    private static T Proxy<T>(Func<string, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>测试接口转发器，只模拟明确配置的方法。</summary>
    public class MethodProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Handler { get; set; } = null!;
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!.Name, args ?? []);
    }

    /// <summary>跟踪应用层提交和回滚，无真实数据库连接。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Commits, Rollbacks;
        public Action<bool>? Finished;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new TransactionStub(this);
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub connection) : IDbTransaction
        {
            public IDbConnection Connection => connection;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() { connection.Commits++; Finish(true); }
            public void Rollback() { connection.Rollbacks++; Finish(false); }
            /// <summary>每笔事务只触发自己的完成回调，避免后续冲突回滚误改已提交事实。</summary>
            private void Finish(bool committed)
            {
                var finished = connection.Finished;
                connection.Finished = null;
                finished?.Invoke(committed);
            }
            public void Dispose() { }
        }
    }
}
