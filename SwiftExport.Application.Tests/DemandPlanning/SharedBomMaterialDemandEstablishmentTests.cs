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

    /// <summary>提供保存入口实际需要的局部事实；未声明接口调用立即失败，不能借假成功隐藏旧版本查询。</summary>
    private sealed class Fixture
    {
        /// <summary>本次来源需求指向的真实共享根，正标记可由场景改变。</summary>
        public readonly Product Root = new() { Uid = Guid.NewGuid(), Sku = "ROOT", BomNo = "SHARED", BomVersion = 3, SupplyType = SupplyTypeEnums.采购 };
        /// <summary>直接客供终端产品，物理来源父不参与它的终端身份查询。</summary>
        public readonly Product Child = new() { Uid = Guid.NewGuid(), Sku = "CUSTOMER", MaterialType = MaterialTypesEnum.客供件, SupplyType = SupplyTypeEnums.采购 };
        /// <summary>建立命令读取的活动来源事实。</summary>
        public readonly GrossDemand Demand;
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

        /// <summary>组合真实命令管道、执行器和规划器，仅替换仓储；物理来源父不是实际根。</summary>
        public Fixture()
        {
            Demand = new() { Uid = Guid.NewGuid(), Version = 1, ProductId = Root.Uid, DemandQty = 9,
                SkuSnapshot = "ROOT", DemandBasisFingerprint = "source-basis", SourceNo = "MD-1" };
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
            var receipts = Proxy<ICommandReceiptRepository>((name, _) =>
            {
                Record();
                return name switch
                {
                    "LockAsync" => Task.FromResult(0), "FindAsync" => Task.FromResult<CommandReceipt?>(null),
                    "InsertAsync" => Task.CompletedTask, _ => throw Unexpected(name)
                };
            });
            Service = new(context,
                Proxy<IGrossDemandDocumentRepository>((name, _) => { Record(); return name == "GetByIdForUpdateAsync" ? Task.FromResult<GrossDemand?>(Demand) : throw Unexpected(name); }),
                Proxy<IMaterialDemandDocumentRepository>(MaterialCall), Proxy<IProductRepository>(ProductCall),
                Proxy<ISupplierRepository>((name, _) => { Record(); return name == "GetByIdListAsync" ? Task.FromResult<IReadOnlyList<Supplier>>([]) : throw Unexpected(name); }),
                Proxy<IBomRepository>(BomCall),
                Proxy<IBusinessOperationLogRepository>((name, _) => { Record(); return name == "InsertAsync" ? Task.FromResult(1) : throw Unexpected(name); }),
                correlation, user, new GrossDemandDecompositionPlanner(), new MaterialDemandSuggestedSupplierInitializer(),
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
            Record();
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
            Record();
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
            Record();
            if (name == "GetActiveByGrossDemandForUpdateAsync") return Task.FromResult<IReadOnlyList<NetDemand>>([]);
            if (name == "GetNextDecompositionRevisionForUpdateAsync") return Task.FromResult(7);
            if (name == "AddDecompositionAsync")
            {
                Written = JsonSerializer.Deserialize<NetDemand[]>(JsonSerializer.Serialize(args[0]))!;
                return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>(Written.Select(row => RepositoryWriteResult.Ok(row.Uid, null, 1)).ToArray());
            }
            throw Unexpected(name);
        }

        /// <summary>所有事实查询与物料写入必须在同一公共事务内。</summary>
        private void Record() => Assert.IsNotNull(Session.Transaction);
        /// <summary>未配置接口是夹具错误，禁止静默返回成功。</summary>
        private static AssertFailedException Unexpected(string name) => new("未配置调用：" + name);
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
        /// <summary>连接收到的实际隔离参数。</summary>
        public IsolationLevel Level;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "offline-shared-demand";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>需求建立必须显式指定已批准隔离，不能退回连接默认值。</summary>
        public IDbTransaction BeginTransaction() => throw new AssertFailedException("需求建立必须显式声明 Serializable。");
        /// <summary>记录唯一事务，后续仓储共享同一实例。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel) { Begins++; Level = isolationLevel; return new TransactionStub(this); }
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
            public void Commit() => owner.Commits++;
            /// <summary>记录失败清理；本组阻断场景均发生在物料写入之前。</summary>
            public void Rollback() => owner.Rollbacks++;
            /// <summary>记录自有事务释放。</summary>
            public void Dispose() => owner.Disposals++;
        }
    }
}
