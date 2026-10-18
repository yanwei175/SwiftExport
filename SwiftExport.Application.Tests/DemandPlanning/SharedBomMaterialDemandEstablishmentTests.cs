using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>通过真实公共建立入口验证共享编号读取、真实根关系及事务隔离；局部仓储不模拟实库并发。</summary>
[TestClass]
public sealed class SharedBomMaterialDemandEstablishmentTests
{
    /// <summary>根正标记不同仍读取同一编号组成，直接客供数量与来源快照保持正确，只拥有一个 Serializable 事务。</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public async Task Establish_SharedNumberKeepsSourceAndUsesSerializable(int marker)
    {
        var fixture = new Fixture();
        fixture.Root.BomVersion = marker;
        var before = JsonSerializer.Serialize(fixture.Line);
        var result = await fixture.Service.EstablishAsync(fixture.Command());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.CreatedMaterialDemandCount);
        Assert.HasCount(2, fixture.Written);
        Assert.AreEqual(fixture.Root.Uid, fixture.Written[0].ProductId);
        Assert.AreEqual(9m, fixture.Written[0].NetQty);
        Assert.AreEqual(6m, fixture.Written[1].NetQty);
        Assert.AreEqual("ROOT", fixture.Written[1].ParentSkuSnapshot);
        Assert.AreEqual(fixture.Line.Uid, fixture.Written[1].BomLineId);
        Assert.AreEqual("source-basis", fixture.Written[1].SourceDemandBasisFingerprintSnapshot);
        Assert.AreEqual(7, fixture.Written[1].DecompositionRevision);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Line));
        Assert.AreEqual(1, fixture.BomReads);
        Assert.AreEqual(fixture.Child.Uid, fixture.TerminalCheckUid);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>非来源共享父自环或客供子件存在有效共享组成时，在物料写入前回滚公共事务。</summary>
    [TestMethod]
    [DataRow(false, "INVALID_BOM_LINE")]
    [DataRow(true, "INVALID_CUSTOMER_SUPPLIED_PRODUCT")]
    public async Task Establish_InvalidSharedRelationshipWritesNothing(bool childHasBom, string expectedCode)
    {
        var fixture = new Fixture { ChildHasBom = childHasBom };
        if (!childHasBom) fixture.Line.ComponentProductId = fixture.Root.Uid;
        var before = JsonSerializer.Serialize(fixture.Line);
        var result = await fixture.Service.EstablishAsync(fixture.Command());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(expectedCode, result.Error.Code);
        Assert.HasCount(0, fixture.Written);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Line));
        fixture.AssertTransaction(0, 1);
    }

    /// <summary>根与直接客供件采用各自默认供应商，不能从父行继承建议。</summary>
    [TestMethod]
    public async Task Establish_SupplierSuggestionsRemainIndependent()
    {
        var fixture = new Fixture();
        var parentSupplier = new Supplier { Uid = Guid.NewGuid(), SupplierCode = "PARENT", SupplierName = "PARENT", DocRelateNo = "PARENT" };
        var childSupplier = new Supplier { Uid = Guid.NewGuid(), SupplierCode = "CHILD", SupplierName = "CHILD", DocRelateNo = "CHILD" };
        fixture.Suppliers.AddRange([parentSupplier, childSupplier]);
        fixture.Root.DefaultSupplierId = parentSupplier.Uid;
        fixture.Child.DefaultSupplierId = childSupplier.Uid;
        var result = await fixture.Service.EstablishAsync(fixture.Command());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(parentSupplier.Uid, fixture.Written[0].SuggestedPurchaseSupplierId);
        Assert.AreEqual(childSupplier.Uid, fixture.Written[1].SuggestedPurchaseSupplierId);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>无产品的非标行、失效默认供应商或无默认值均保持空建议。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Establish_MissingProductOrSupplierLeavesSuggestionEmpty(bool nonStandard)
    {
        var fixture = new Fixture();
        fixture.Demand.IsNonStandard = nonStandard;
        if (nonStandard) fixture.Demand.ProductId = null;
        fixture.Root.DefaultSupplierId = Guid.NewGuid();
        var result = await fixture.Service.EstablishAsync(fixture.Command());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(nonStandard ? 1 : 2, fixture.Written.Count);
        Assert.IsTrue(fixture.Written.All(row => row.SuggestedPurchaseSupplierId is null));
        if (nonStandard)
        {
            Assert.IsNull(fixture.Written[0].ProductId);
            Assert.AreEqual(0, fixture.BomReads);
        }
    }

    /// <summary>事务前准备排序来源集合，先锁全部来源再建立，审计和回执之后才统一提交。</summary>
    [TestMethod]
    public async Task Establish_PreparesAndLocksAllSourcesBeforeWriting()
    {
        var fixture = new Fixture();
        var command = fixture.Command();
        command.Items.Insert(0, new() { GrossDemandId = fixture.OtherDemand.Uid, SourceDemandVersion = 1 });
        var result = await fixture.Service.EstablishAsync(command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.CreatedSourceCount);
        Assert.AreEqual(4, result.Data.CreatedMaterialDemandCount);
        CollectionAssert.AreEqual(new[] { fixture.Demand.Uid, fixture.OtherDemand.Uid }, fixture.PreparedIds);
        CollectionAssert.AreEqual(new[] { "prepare", "begin", "receipt:LockAsync", "receipt:FindAsync",
            $"source:{fixture.Demand.Uid}", $"source:{fixture.OtherDemand.Uid}" }, fixture.Calls.Take(6).ToArray());
        Assert.IsTrue(fixture.Calls.IndexOf("product:GetByIdAsync") < fixture.Calls.IndexOf("bom:GetBomByBomNoAsync"));
        Assert.IsTrue(fixture.Calls.IndexOf("bom:GetBomByBomNoAsync") < fixture.Calls.IndexOf("product:GetByIdListAsync"));
        Assert.IsTrue(fixture.Calls.IndexOf("product:GetByIdListAsync") < fixture.Calls.IndexOf("supplier"));
        Assert.IsTrue(fixture.Calls.IndexOf("supplier") < fixture.Calls.IndexOf("material:AddDecompositionAsync"));
        CollectionAssert.AreEqual(new[] { "audit", "receipt:InsertAsync", "commit", "release" }, fixture.Calls.TakeLast(4).ToArray());
        Assert.AreEqual(1, fixture.Logs.Count);
        Assert.AreEqual(1, fixture.ReceiptCount);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>已写入第一来源或部分物料后，后项版本、审计、回执失败及取消仍恢复整批持久化状态。</summary>
    [TestMethod]
    [DataRow("material", "WRITE_FAILED")]
    [DataRow("audit", "LOG_WRITE_FAILED")]
    [DataRow("receipt", "UNEXPECTED")]
    [DataRow("later-version", "SOURCE_CHANGED")]
    [DataRow("cancel", "")]
    public async Task Establish_FailureAfterWritesRestoresAllFacts(string phase, string expectedCode)
    {
        var fixture = new Fixture { FailAt = phase };
        var command = fixture.Command();
        command.Items.Add(new() { GrossDemandId = fixture.OtherDemand.Uid, SourceDemandVersion = 1 });
        if (phase == "later-version") fixture.OtherDemand.Version = 2;
        using var cancellation = new CancellationTokenSource();
        if (phase == "cancel") fixture.OnMaterialWrite = cancellation.Cancel;
        if (phase == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.EstablishAsync(command, cancellation.Token));
        else
        {
            var result = await fixture.Service.EstablishAsync(command, cancellation.Token);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedCode, result.Error.Code);
        }
        Assert.IsTrue(fixture.MaterialWriteAttempts > 0, "必须覆盖实际写入之后的失败阶段。");
        Assert.HasCount(0, fixture.Written);
        Assert.HasCount(0, fixture.Logs);
        Assert.AreEqual(0, fixture.ReceiptCount);
        Assert.AreEqual("release", fixture.Calls[^1]);
        fixture.AssertTransaction(0, 1);
    }
    /// <summary>提供建立入口的事实与可恢复写入状态，未声明接口调用立即失败。</summary>
    private sealed class Fixture
    {
        /// <summary>本次成品需求指向的真实共享根，正标记可由场景改变。</summary>
        public readonly Product Root = new() { Uid = Guid.NewGuid(), Sku = "ROOT", BomNo = "SHARED", BomVersion = 3, SupplyType = SupplyTypeEnums.采购 };
        /// <summary>直接客供终端产品，物理来源父不参与它的终端身份查询。</summary>
        public readonly Product Child = new() { Uid = Guid.NewGuid(), Sku = "CUSTOMER", MaterialType = MaterialTypesEnum.客供件, SupplyType = SupplyTypeEnums.采购 };
        /// <summary>建立命令读取的活动来源事实。</summary>
        public readonly GrossDemand Demand;
        /// <summary>第二来源用于核对先锁整批和后项失败回滚。</summary>
        public readonly GrossDemand OtherDemand;
        /// <summary>共享组内唯一物理行，测试比较其完整来源字段。</summary>
        public readonly Bom Line;
        /// <summary>所有局部仓储共享的公共事务会话。</summary>
        public readonly SessionStub Session = new();
        /// <summary>使用真实管道、执行器和规划器的被测服务。</summary>
        public readonly MaterialDemandEstablishmentService Service;
        public IReadOnlyList<NetDemand> Written { get; private set; } = [];
        /// <summary>按编号读取次数，用于捕捉未读取或重复建立。</summary>
        public int BomReads;
        /// <summary>终端校验实际使用的产品身份。</summary>
        public Guid? TerminalCheckUid;
        /// <summary>注入客供终端存在有效组成的数据库事实。</summary>
        public bool ChildHasBom;
        /// <summary>指定在真实写入之后注入失败的阶段。</summary>
        public string? FailAt;
        /// <summary>物料写入时触发取消等测试事件。</summary>
        public Action? OnMaterialWrite;
        /// <summary>记录写入尝试，确保回滚场景确实经过业务写入。</summary>
        public int MaterialWriteAttempts;
        public List<Supplier> Suppliers { get; } = [];
        public List<BusinessOperationLog> Logs { get; } = [];
        public List<string> Calls { get; } = [];
        public Guid[] PreparedIds { get; private set; } = [];
        public int ReceiptCount => _receipts.Count;
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];

        /// <summary>组合真实命令管道、执行器和规划器，仅替换仓储；物理来源父不是实际根。</summary>
        public Fixture()
        {
            Demand = new() { Uid = Guid.Parse("00000000-0000-0000-0000-000000000001"), Version = 1, ProductId = Root.Uid, DemandQty = 9,
                SkuSnapshot = "ROOT", DemandBasisFingerprint = "source-basis", SourceNo = "MD-1" };
            OtherDemand = new() { Uid = Guid.Parse("00000000-0000-0000-0000-000000000002"), Version = 1,
                ProductId = Root.Uid, DemandQty = 9, SkuSnapshot = "ROOT", DemandBasisFingerprint = "source-basis", SourceNo = "MD-2" };
            // 回滚恢复事务开始前的事实，不能只用回滚次数代替原子性验证。
            IReadOnlyList<NetDemand> rowsBefore = [];
            var logCountBefore = 0;
            Dictionary<Guid, CommandReceipt> receiptsBefore = [];
            Session.ConnectionStub.OnBegin = () =>
            {
                rowsBefore = Written;
                logCountBefore = Logs.Count;
                receiptsBefore = new(_receipts);
                Calls.Add("begin");
            };
            Session.ConnectionStub.OnRollback = () =>
            {
                Written = rowsBefore;
                Logs.RemoveRange(logCountBefore, Logs.Count - logCountBefore);
                _receipts.Clear();
                foreach (var pair in receiptsBefore) _receipts.Add(pair.Key, pair.Value);
                Calls.Add("rollback");
            };
            Session.ConnectionStub.OnCommit = () => Calls.Add("commit");
            Line = new() { Uid = Guid.NewGuid(), ParentProductId = Guid.NewGuid(), ParentSkuSnapshot = "历史来源父",
                ComponentProductId = Child.Uid, ComponentSkuSnapshot = "历史子编码", BomNo = "SHARED", BomVersion = 1,
                Numerator = 2, Denominator = 3, LineNumber = 1 };
            var user = Proxy<ICurrentUser>((name, _) => name switch
            {
                "get_IsAuthenticated" => true, "get_UserId" => (Guid?)Guid.Parse("6db48c38-7443-46b1-89bd-f1f21b321874"),
                "get_UserName" => "需求共享测试", _ => throw Unexpected(name)
            });
            var correlation = Proxy<ICorrelationContext>((name, _) => name == "get_CorrelationId" ? "shared-bom-demand" : throw Unexpected(name));
            var validators = new ServiceCollection().AddSingleton<IValidator<EstablishMaterialDemandsCommand>, EstablishMaterialDemandsCommandValidator>().BuildServiceProvider();
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var receipts = Proxy<ICommandReceiptRepository>((name, args) =>
            {
                Record("receipt:" + name);
                if (name == "LockAsync") return Task.FromResult(0);
                if (name == "FindAsync") return Task.FromResult(_receipts.GetValueOrDefault((Guid)args[0]!));
                if (name == "InsertAsync")
                {
                    if (FailAt == "receipt") throw new InvalidOperationException("模拟回执写入失败");
                    var receipt = (CommandReceipt)args[0]!;
                    _receipts.Add(receipt.CommandId, receipt);
                    return Task.CompletedTask;
                }
                throw Unexpected(name);
            });
            Service = new(context,
                Proxy<IPreparedSourceRepository>(SourceCall),
                Proxy<IMaterialDemandDocumentRepository>(MaterialCall), Proxy<IProductRepository>(ProductCall),
                Proxy<ISupplierRepository>((name, args) =>
                {
                    Record("supplier");
                    Assert.AreEqual("GetByIdListAsync", name);
                    Assert.IsTrue((bool)args[1]!);
                    var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Supplier>>(Suppliers.Where(row => ids.Contains(row.Uid)).ToArray());
                }),
                Proxy<IBomRepository>(BomCall),
                Proxy<IBusinessOperationLogRepository>((name, args) =>
                {
                    Record("audit");
                    Assert.AreEqual("InsertAsync", name);
                    Logs.Add((BusinessOperationLog)args[0]!);
                    return Task.FromResult(FailAt == "audit" ? 0 : 1);
                }),
                correlation, user, new GrossDemandDecompositionPlanner(),
                Proxy<IPermissionEvaluationService>((name, _) => name == "HasPermissionAsync" ? Task.FromResult(true) : throw Unexpected(name)),
                new CommandPipeline(new CommandRequestValidator(validators), correlation, user, NullLogger<CommandPipeline>.Instance),
                new BusinessCommandExecutor(context, receipts, user));
        }

        /// <summary>建立只含真实来源身份和 wrapper 版本的命令。</summary>
        public EstablishMaterialDemandsCommand Command() => new() { CommandId = Guid.NewGuid(),
            Items = [new() { GrossDemandId = Demand.Uid, SourceDemandVersion = Demand.Version }] };

        /// <summary>核对公共执行器唯一事务的隔离、提交回滚、释放和会话清空。</summary>
        public void AssertTransaction(int commits, int rollbacks)
        {
            Assert.AreEqual(1, Session.ConnectionStub.Begins);
            Assert.AreEqual(IsolationLevel.Serializable, Session.ConnectionStub.Level);
            Assert.AreEqual(commits, Session.ConnectionStub.Commits);
            Assert.AreEqual(rollbacks, Session.ConnectionStub.Rollbacks);
            Assert.AreEqual(1, Session.ConnectionStub.Disposals);
            Assert.IsNull(Session.Transaction);
        }

        /// <summary>读取真实根和直接子身份时必须请求更新锁，不根据物理来源父查询产品。</summary>
        private object ProductCall(string name, object?[] args)
        {
            Record("product:" + name);
            Assert.IsTrue((bool)args[1]!);
            if (name == "GetByIdAsync") { Assert.AreEqual(Root.Uid, args[0]); return Task.FromResult<Product?>(Root); }
            if (name == "GetByIdListAsync")
            {
                var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                return Task.FromResult<IReadOnlyList<Product>>(new[] { Root, Child }.Where(product => ids.Contains(product.Uid)).ToArray());
            }
            throw Unexpected(name);
        }

        /// <summary>按锁定根的编号返回共享物理行；终端查询记录真实客供身份，不允许旧版本查询。</summary>
        private object BomCall(string name, object?[] args)
        {
            Record("bom:" + name);
            if (name == "GetBomByBomNoAsync") { BomReads++; Assert.AreEqual(Root.BomNo, args[0]); return Task.FromResult<IEnumerable<Bom>>([Line]); }
            if (name == "HasActiveBomForProductAsync")
            {
                TerminalCheckUid = (Guid)args[0]!;
                Assert.AreEqual(Child.Uid, TerminalCheckUid);
                Assert.IsNull(args[1]);
                return Task.FromResult(ChildHasBom);
            }
            throw Unexpected(name);
        }

        /// <summary>保存真实规划器的完整输出，不重新计算数量或替代规划校验。</summary>
        private object MaterialCall(string name, object?[] args)
        {
            Record("material:" + name);
            if (name == "GetActiveByGrossDemandForUpdateAsync") return Task.FromResult<IReadOnlyList<NetDemand>>([]);
            if (name == "GetNextDecompositionRevisionForUpdateAsync") return Task.FromResult(7);
            if (name == "AddDecompositionAsync")
            {
                MaterialWriteAttempts++;
                var created = JsonSerializer.Deserialize<NetDemand[]>(JsonSerializer.Serialize(args[0]))!;
                Written = Written.Concat(created).ToArray();
                OnMaterialWrite?.Invoke();
                if (FailAt == "material")
                    return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>(
                        [RepositoryWriteResult.Fail(created[0].Uid, null, "WRITE_FAILED", "模拟部分物料写入失败")]);
                return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>(created.Select(row => RepositoryWriteResult.Ok(row.Uid, null, 1)).ToArray());
            }
            throw Unexpected(name);
        }

        /// <summary>所有事实查询与物料写入必须在同一公共事务内。</summary>
        private void Record(string step)
        {
            Assert.IsNotNull(Session.Transaction);
            Calls.Add(step);
        }

        /// <summary>准备钩子在事务外返回可观察租约，来源锁定读取必须在唯一父事务内。</summary>
        private object SourceCall(string name, object?[] args)
        {
            if (name == "AcquireBeforeTransactionAsync")
            {
                Assert.IsNull(Session.Transaction);
                Assert.AreEqual(0, ((IReadOnlyCollection<Guid>)args[0]!).Count);
                PreparedIds = ((IReadOnlyCollection<Guid>)args[1]!).ToArray();
                Calls.Add("prepare");
                return Task.FromResult<IAsyncDisposable?>(new PreparationLease(() => Calls.Add("release")));
            }
            if (name == "GetByIdForUpdateAsync")
            {
                var id = (Guid)args[0]!;
                Record("source:" + id);
                return Task.FromResult<GrossDemand?>(id == Demand.Uid ? Demand : id == OtherDemand.Uid ? OtherDemand : null);
            }
            throw Unexpected(name);
        }

        /// <summary>未配置接口是夹具错误，禁止静默返回成功。</summary>
        private static AssertFailedException Unexpected(string name) => new("未配置调用：" + name);
    }

    /// <summary>给测试代理同时提供来源仓储与真实可选准备契约。</summary>
    public interface IPreparedSourceRepository : IGrossDemandDocumentRepository, ISourceDemandTransactionPreparation { }

    /// <summary>记录准备租约在事务结束后释放，不实现数据库资源锁。</summary>
    private sealed class PreparationLease(Action release) : IAsyncDisposable
    {
        /// <summary>追加释放事件，验证租约与业务事务的生命周期顺序。</summary>
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }

    /// <summary>只转发声明的真实接口调用，不内置业务规则。</summary>
    private static T Proxy<T>(Func<string, object?[], object> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>将接口调用及原始参数交给当前局部仓储。</summary>
    public class MethodProxy : DispatchProxy
    {
        public Func<string, object?[], object> Handler { get; set; } = null!;
        /// <summary>保留方法名和原始参数，不捕获或转换失败。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args ?? []);
    }

    /// <summary>只提供局部会话和未知异常分类，不连接数据库或实现 SQL 锁。</summary>
    private sealed class SessionStub : IDbSession, IDatabaseExceptionClassifier
    {
        public ConnectionStub ConnectionStub { get; } = new();
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;
        /// <summary>本组不注入数据库提供者异常，不推定重试或序列化失败。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>记录公共执行器申请的隔离和事务生命周期，禁止执行 SQL。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        /// <summary>记录公共执行器的事务生命周期计数。</summary>
        public int Begins, Commits, Rollbacks, Disposals;
        /// <summary>驱动内存事实快照、回滚恢复和提交顺序记录。</summary>
        public Action? OnBegin, OnRollback, OnCommit;
        /// <summary>连接收到的实际隔离参数。</summary>
        public IsolationLevel Level;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "offline-shared-demand";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>需求建立必须显式指定已批准隔离，不能退回连接默认值。</summary>
        public IDbTransaction BeginTransaction() => throw new AssertFailedException("需求建立必须显式声明 Serializable。");
        /// <summary>记录唯一事务，后续仓储共享同一实例。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel) { Begins++; Level = isolationLevel; OnBegin?.Invoke(); return new TransactionStub(this); }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Close() { }
        public void Dispose() { }
        /// <summary>只记录提交、回滚及释放，不宣称验证真实数据库隔离。</summary>
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => owner.Level;
            /// <summary>记录公共执行器成功提交。</summary>
            public void Commit() { owner.Commits++; owner.OnCommit?.Invoke(); }
            /// <summary>记录失败清理并恢复事务开始前的物料、审计和回执事实。</summary>
            public void Rollback() { owner.Rollbacks++; owner.OnRollback?.Invoke(); }
            /// <summary>记录自有事务释放。</summary>
            public void Dispose() => owner.Disposals++;
        }
    }
}
