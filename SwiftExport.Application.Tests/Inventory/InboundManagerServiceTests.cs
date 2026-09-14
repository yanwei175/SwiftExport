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
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>不连接数据库，验证入库草稿服务的事务、身份、状态和幂等编排。</summary>
[TestClass]
public sealed class InboundManagerServiceTests
{
    /// <summary>入库单号按收货月份生成，而不是按保存当天；取号失败不得写入草稿。</summary>
    [TestMethod]
    public async Task Create_NumberUsesReceiptMonth_AndFailureRollsBack()
    {
        var f = new Fixture();
        var command = f.Create();
        command.Draft.ReceiptDate = new DateTime(2025, 12, 20);
        var result = await f.Service.CreateAsync(command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("IR2025120001", result.Data.DocumentNo);
        var failed = new Fixture { FailNumber = true };
        Assert.IsFalse((await failed.Service.CreateAsync(failed.Create())).Success);
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
        Assert.IsTrue((await f.Service.SearchAsync(request)).Success);
        Assert.AreSame(request, f.LastSearch);
        request.PostingStatus = (InboundPostingStatusEnum)999;
        var invalid = await f.Service.SearchAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Validation, invalid.Error.Code);
        Assert.AreEqual(nameof(request.PostingStatus), invalid.Error.Field);
        Assert.AreEqual(1, f.Searches);
        request.PostingStatus = null;
        Assert.IsTrue((await f.Service.SearchAsync(request)).Success);
    }

    /// <summary>允许草稿数量超过当前剩余量；仓库独立，保存从真实来源取得快照。</summary>
    [TestMethod]
    public async Task Create_UsesSourceSnapshot_DoesNotReserveRemaining()
    {
        var f = new Fixture();
        var result = await f.Service.CreateAsync(f.Create());
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
        var first = await f.Service.CreateAsync(command);
        var second = await f.Service.CreateAsync(command);
        Assert.IsTrue(second.Success, second.Message);
        Assert.AreEqual(first.Data.Uid, second.Data.Uid);
        Assert.AreEqual(first.Data.DocumentNo, second.Data.DocumentNo);
        Assert.AreEqual(1, f.NumberCalls);
        Assert.AreEqual(1, f.Saves);
        command.Draft.Lines[0].Quantity++;
        var conflict = await f.Service.CreateAsync(command);
        Assert.IsFalse(conflict.Success);
        StringAssert.Contains(conflict.Message, "IDEMPOTENCY_CONFLICT");
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error.Code);
        Assert.AreEqual(1, f.Saves);
    }

    /// <summary>日志失败必须回滚草稿事务，不能报告成功。</summary>
    [TestMethod]
    public async Task Create_LogFailure_RollsBack()
    {
        var f = new Fixture { FailLog = true };
        var result = await f.Service.CreateAsync(f.Create());
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
        await f.Service.CreateAsync(f.Create());
        var update = await f.Service.UpdateAsync(f.Header!.Uid, new UpdateInboundDraftCommand
        { CommandId = Guid.NewGuid(), ExpectedVersion = 99, Draft = f.Create().Draft });
        Assert.IsFalse(update.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, update.Error.Code);
        Assert.AreEqual(1, f.Saves);
        foreach (var state in new[] { InboundPostingStatusEnum.Posted, InboundPostingStatusEnum.Reversed })
        {
            f.Header.PostingStatus = state;
            Assert.IsFalse((await f.Service.DeleteAsync(f.Header.Uid, f.Delete())).Success);
        }
        f.Header.PostingStatus = InboundPostingStatusEnum.Unposted;
        f.Header.CreationMode = InboundCreationModeEnum.Automatic;
        Assert.IsFalse((await f.Service.DeleteAsync(f.Header.Uid, f.Delete())).Success);
        Assert.AreEqual(0, f.Deletes);
    }

    /// <summary>已有行身份不能跨单提交；正常更新保留来源快照。</summary>
    [TestMethod]
    public async Task Update_RejectsForeignLine_AndPreservesSnapshot()
    {
        var f = new Fixture();
        await f.Service.CreateAsync(f.Create());
        var command = new UpdateInboundDraftCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Draft = f.Create().Draft };
        command.Draft.Lines[0].Uid = Guid.NewGuid();
        Assert.IsFalse((await f.Service.UpdateAsync(f.Header!.Uid, command)).Success);
        command.CommandId = Guid.NewGuid();
        command.Draft.Lines[0].Uid = f.SavedLines[0].Uid;
        command.Draft.Lines[0].Quantity = 5;
        var result = await f.Service.UpdateAsync(f.Header.Uid, command);
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
        await f.Service.CreateAsync(f.Create());
        f.HasLedger = true;
        Assert.IsFalse((await f.Service.DeleteAsync(f.Header!.Uid, f.Delete())).Success);
        f.HasLedger = false;
        var command = f.Delete();
        var first = await f.Service.DeleteAsync(f.Header.Uid, command);
        var second = await f.Service.DeleteAsync(f.Header.Uid, command);
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
        Assert.IsFalse((await f.Service.SearchSourcesAsync(request)).Success);
        request.DocumentType = InboundDocumentTypeEnum.PurchaseReceipt;
        Assert.IsTrue((await f.Service.SearchSourcesAsync(request)).Success);
        Assert.AreEqual(1, f.Searches);
        f.Allowed = false;
        Assert.IsFalse((await f.Service.SearchSourcesAsync(request)).Success);
        Assert.AreEqual(1, f.Searches);
    }

    /// <summary>命令锁繁忙、格式错误及权限失败提供机器可识别代码，不依赖消息文本。</summary>
    [TestMethod]
    public async Task WriteFailures_ExposeStructuredCodes()
    {
        var f = new Fixture { CommandLockResult = -1 };
        var busy = await f.Service.CreateAsync(f.Create());
        Assert.AreEqual(InboundErrorCodes.CommandBusy, busy.Error.Code);
        Assert.AreEqual(0, f.Saves);
        var invalid = f.Create();
        invalid.Draft.Lines[0].Quantity = 1.00001m;
        Assert.AreEqual(ServiceErrorCodes.Validation, (await f.Service.CreateAsync(invalid)).Error.Code);
        f.Allowed = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.CreateAsync(f.Create())).Error.Code);
    }

    /// <summary>仓储并发失败映射为版本冲突并回滚，不错误提示为可能已经保存。</summary>
    [TestMethod]
    public async Task RepositoryConcurrency_RollsBackAndReportsConflict()
    {
        var f = new Fixture { SaveFailure = new DBConcurrencyException("测试并发") };
        var result = await f.Service.CreateAsync(f.Create());
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
        var result = await f.Service.CreateAsync(command);
        Assert.AreEqual(InboundErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        f.SaveFailure = null;
        Assert.IsTrue((await f.Service.CreateAsync(command)).Success);
        Assert.AreEqual(1, f.Connection.Commits);
    }

    /// <summary>构造仅包含本用例调用面的替身，意外调用立即使测试失败。</summary>
    private sealed class Fixture
    {
        public readonly Guid WarehouseId = Guid.NewGuid();
        public readonly Guid SourceId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Dictionary<Guid, BusinessOperationLog> _logs = [];
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly InboundManagerService Service;
        public InboundDocument? Header;
        public List<InboundDocumentLine> SavedLines = [];
        public int Saves, Deletes, Searches, SourceReads;
        public bool FailLog, HasLedger;
        public bool Allowed = true;
        public int CommandLockResult;
        public Exception? SaveFailure;
        public InboundManagerSearchRequest? LastSearch;
        public int NumberCalls;
        public bool FailNumber;

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            Service = new(new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                Proxy<IInboundManagerRepository>((method, args) => method switch
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
                    _ => throw new NotSupportedException(method)
                }),
                Proxy<IWarehouseSiteRepository>((method, _) => method == "GetForUpdateAsync"
                    ? Task.FromResult<WarehouseSite?>(new WarehouseSite { Uid = WarehouseId, Code = "W", Name = "参考仓", IsActive = true }) : throw new NotSupportedException(method)),
                Proxy<IBusinessOperationLogRepository>((method, args) => method switch
                {
                    "GetByModuleCommandAsync" => Task.FromResult(_logs.GetValueOrDefault((Guid)args[1]!)),
                    "InsertAsync" => Log((BusinessOperationLog)args[0]!),
                    _ => throw new NotSupportedException(method)
                }),
                Proxy<ICurrentUser>((method, _) => method switch { "get_IsAuthenticated" => true, "get_UserId" => _userId, "get_UserName" => "测试", _ => throw new NotSupportedException(method) }),
                Proxy<IUserPermissionService>((method, _) => method == "HasPermissionAsync" ? Task.FromResult(Allowed) : throw new NotSupportedException(method)),
                new DocumentNoService(Proxy<IDocumentSequenceRepository>((method, args) =>
                {
                    if (method != "GetNextAsync") throw new NotSupportedException(method);
                    Assert.IsNotNull(Session.Transaction, "取号必须处于入库保存事务内。");
                    if (FailNumber) throw new InvalidOperationException("模拟取号失败");
                    NumberCalls++;
                    return Task.FromResult(new DocumentSequence { DocType = (string)args[0]!, YearMonth = (string)args[1]!, CurrentNo = NumberCalls });
                })));
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
        /// <summary>模拟日志 JSON 对象约束，防止字符串标量在替身中通过、真实保存却失败。</summary>
        private Task<int> Log(BusinessOperationLog log)
        {
            if (FailLog) return Task.FromResult(0);
            using var changed = JsonDocument.Parse(log.ChangedFieldsJson!);
            Assert.AreEqual(JsonValueKind.Object, changed.RootElement.ValueKind);
            Assert.AreEqual(64, changed.RootElement.GetProperty("Fingerprint").GetString()!.Length);
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
            public void Commit() => connection.Commits++;
            public void Rollback() => connection.Rollbacks++;
            public void Dispose() { }
        }
    }
}
