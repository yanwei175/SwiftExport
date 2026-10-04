using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
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
using SwiftExport.Application.Services.DemandPlanning.Materials.Reversal;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>以内存端口验证物料建立和回退的权限、公共回执和事务编排；不模拟 SQL 锁。</summary>
[TestClass]
public sealed class MaterialDemandPublicCommandTests
{
    /// <summary>建立命令按来源排序重放，首次成功后来源版本变化也不再次读取业务事实。</summary>
    [TestMethod]
    public async Task Establish_ReorderedReplayPrecedesChangedSourceVersion()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var first = f.EstablishCommand(id);
        var result = await f.Establish.EstablishAsync(first);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.AlreadyEstablishedCount);
        f.Demand.Version = 2;
        var replay = await f.Establish.EstablishAsync(f.EstablishCommand(id));
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(1, replay.Data.AlreadyEstablishedCount);
        Assert.AreEqual(1, f.GrossReads);
        Assert.AreEqual(1, f.AuditWrites);
        Assert.AreEqual(1, f.ReceiptWrites);
        Assert.AreEqual(2, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);

        var fresh = await f.Establish.EstablishAsync(f.EstablishCommand(Guid.NewGuid()));
        Assert.AreEqual("SOURCE_CHANGED", fresh.Error.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
    }

    /// <summary>相同来源集合改变选择顺序仍命中首次回执，来源版本作为业务输入参与冲突校验。</summary>
    [TestMethod]
    public async Task Establish_ItemOrderEquivalentButVersionDifferenceConflicts()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var first = f.EstablishCommand(id);
        first.Items.Add(new EstablishMaterialDemandItem
        {
            GrossDemandId = f.OtherDemand.Uid, SourceDemandVersion = 1
        });
        Assert.IsTrue((await f.Establish.EstablishAsync(first)).Success);
        var swapped = f.EstablishCommand(id);
        swapped.Items.Insert(0, new EstablishMaterialDemandItem
        {
            GrossDemandId = f.OtherDemand.Uid, SourceDemandVersion = 1
        });
        Assert.IsTrue((await f.Establish.EstablishAsync(swapped)).Success);
        Assert.AreEqual(2, f.GrossReads);
        Assert.AreEqual(1, f.AuditWrites);
        swapped.Items[0].SourceDemandVersion = 2;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Establish.EstablishAsync(swapped)).Error.Code);
    }

    /// <summary>回退原因 Trim 后等价；更换内容或操作人不能占用原命令身份。</summary>
    [TestMethod]
    public async Task Rollback_ReplayNormalizationAndConflicts()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var first = await f.Reverse.RollbackAsync(f.RollbackCommand(id, "  重做  "));
        Assert.IsTrue(first.Success, first.Message);
        var replay = await f.Reverse.RollbackAsync(f.RollbackCommand(id, "重做"));
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(1, f.RollbackWrites);

        var stale = f.RollbackCommand(Guid.NewGuid(), "重做");
        stale.Items[0].DecompositionRevision = 2;
        Assert.AreEqual("REVISION_CONFLICT", (await f.Reverse.RollbackAsync(stale)).Error.Code);
        Assert.AreEqual(1, f.AuditWrites);
        Assert.AreEqual(1, f.ReceiptWrites);

        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Reverse.RollbackAsync(f.RollbackCommand(id, "另一个原因"))).Error.Code);
        f.User.UserIdValue = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Reverse.RollbackAsync(f.RollbackCommand(id, "重做"))).Error.Code);
        Assert.AreEqual(1, f.RollbackWrites);
    }

    /// <summary>回退集合改变选择顺序仍命中首次回执，不重复修改物料修订或业务审计。</summary>
    [TestMethod]
    public async Task Rollback_ItemOrderEquivalent_ReplaysOriginalResult()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var first = f.RollbackCommand(id, "  重做  ");
        first.Items.Add(new RollbackMaterialDemandItem
        {
            GrossDemandId = f.OtherDemand.Uid, DecompositionRevision = 1
        });
        Assert.IsTrue((await f.Reverse.RollbackAsync(first)).Success);
        var swapped = f.RollbackCommand(id, "重做");
        swapped.Items.Insert(0, new RollbackMaterialDemandItem
        {
            GrossDemandId = f.OtherDemand.Uid, DecompositionRevision = 1
        });
        Assert.IsTrue((await f.Reverse.RollbackAsync(swapped)).Success);
        Assert.AreEqual(2, f.RollbackWrites);
        Assert.AreEqual(2, f.GrossReads);
        Assert.AreEqual(1, f.AuditWrites);
        Assert.AreEqual(1, f.ReceiptWrites);
    }

    /// <summary>未登录、模块单独授权、单项交叉授权及无权重放均在事务外拒绝。</summary>
    [TestMethod]
    public async Task IndependentPermissions_RejectBeforeReceiptAndTransaction()
    {
        var f = new Fixture { AllowEstablish = false, AllowRollback = false };
        var establish = f.EstablishCommand(Guid.NewGuid());
        var rollback = f.RollbackCommand(Guid.NewGuid(), "重做");
        f.User.UserIdValue = null;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await f.Establish.EstablishAsync(establish)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await f.Reverse.RollbackAsync(rollback)).Error.Code);
        f.User.UserIdValue = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Establish.EstablishAsync(establish)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Reverse.RollbackAsync(rollback)).Error.Code);
        f.AllowEstablish = true;
        Assert.IsTrue((await f.Establish.EstablishAsync(establish)).Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Reverse.RollbackAsync(rollback)).Error.Code);
        f.AllowEstablish = false;
        f.AllowRollback = true;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Establish.EstablishAsync(establish)).Error.Code);
        Assert.IsTrue((await f.Reverse.RollbackAsync(rollback)).Success);
        Assert.AreEqual(2, f.Connection.Begins);
        Assert.AreEqual(2, f.ReceiptReads);
    }

    /// <summary>业务异常、审计失败和回执失败都不提交，取消后清空执行器会话事务。</summary>
    [TestMethod]
    public async Task FailureAndCancellation_RollBackAndClearTransaction()
    {
        var failed = new Fixture();
        failed.Demand.Version = 2;
        Assert.AreEqual("SOURCE_CHANGED", (await failed.Establish.EstablishAsync(failed.EstablishCommand(Guid.NewGuid()))).Error.Code);
        Assert.AreEqual(1, failed.Connection.Rollbacks);
        Assert.IsNull(failed.Session.Transaction);

        var audit = new Fixture { FailAudit = true };
        Assert.AreEqual("LOG_WRITE_FAILED", (await audit.Establish.EstablishAsync(audit.EstablishCommand(Guid.NewGuid()))).Error.Code);
        Assert.AreEqual(1, audit.Connection.Rollbacks);
        Assert.AreEqual(0, audit.ReceiptWrites);

        var receipt = new Fixture { FailReceipt = true };
        Assert.AreEqual(ServiceErrorCodes.Unexpected,
            (await receipt.Reverse.RollbackAsync(receipt.RollbackCommand(Guid.NewGuid(), "重做"))).Error.Code);
        Assert.AreEqual(1, receipt.Connection.Rollbacks);
        Assert.AreEqual(0, receipt.Connection.Commits);
        Assert.IsNull(receipt.Session.Transaction);

        var cancelled = new Fixture();
        using var source = new CancellationTokenSource();
        cancelled.OnGrossRead = source.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancelled.Establish.EstablishAsync(cancelled.EstablishCommand(Guid.NewGuid()), source.Token));
        Assert.AreEqual(1, cancelled.Connection.Rollbacks);
        Assert.IsNull(cancelled.Session.Transaction);

        var unknown = new Fixture();
        unknown.Connection.FailCommit = true;
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown,
            (await unknown.Reverse.RollbackAsync(unknown.RollbackCommand(Guid.NewGuid(), "重做"))).Error.Code);
        Assert.IsNull(unknown.Session.Transaction);
    }

    /// <summary>命令格式错误在事务外返回验证错误，回退原因不修改原请求对象。</summary>
    [TestMethod]
    public async Task Validation_RejectsBeforeTransactionAndLeavesRequestUnchanged()
    {
        var f = new Fixture();
        var invalid = f.EstablishCommand(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await f.Establish.EstablishAsync(invalid)).Error.Code);
        var rollback = f.RollbackCommand(Guid.NewGuid(), "  重做  ");
        Assert.IsTrue((await f.Reverse.RollbackAsync(rollback)).Success);
        Assert.AreEqual("  重做  ", rollback.Reason);
        Assert.AreEqual(1, f.Connection.Begins);
    }

    /// <summary>夹具只替代调用到的端口，并让回执、业务审计共享同一可观察事务。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly UserStub User = new();
        public readonly GrossDemand Demand = new() { Uid = Guid.NewGuid(), Version = 1, DemandBasisFingerprint = "basis" };
        public readonly GrossDemand OtherDemand = new() { Uid = Guid.NewGuid(), Version = 1, DemandBasisFingerprint = "basis" };
        public readonly MaterialDemandEstablishmentService Establish;
        public readonly MaterialDemandReversalService Reverse;
        public bool AllowEstablish = true, AllowRollback = true, FailAudit, FailReceipt;
        public int GrossReads, AuditWrites, ReceiptWrites, ReceiptReads, RollbackWrites;
        public Action? OnGrossRead;
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var source = Proxy<IGrossDemandDocumentRepository>((method, args) => method.Name switch
            {
                "GetByIdForUpdateAsync" => ReadGross((Guid)args![0]!),
                _ => throw new AssertFailedException($"意外来源仓储调用 {method.Name}")
            });
            var material = Proxy<IMaterialDemandDocumentRepository>((method, args) => method.Name switch
            {
                "GetActiveByGrossDemandForUpdateAsync" => Task.FromResult<IReadOnlyList<NetDemand>>(
                    [new NetDemand { Uid = Guid.NewGuid(), GrossDemandId = (Guid)args![0]!,
                        SourceDemandBasisFingerprintSnapshot = "basis", DecompositionRevision = 1 }]),
                "RollbackRevisionAsync" => Rollback(),
                _ => throw new AssertFailedException($"意外物料仓储调用 {method.Name}")
            });
            var execution = Proxy<INetDemandExecutionLinkRepository>((method, args) => method.Name switch
            {
                "GetRollbackBlockersAsync" => Task.FromResult<IReadOnlyList<MaterialDemandRollbackBlockerDto>>([]),
                _ => throw new AssertFailedException($"意外执行关系仓储调用 {method.Name}")
            });
            var logs = Proxy<IBusinessOperationLogRepository>((method, args) => method.Name switch
            {
                "InsertAsync" => WriteAudit(),
                _ => throw new AssertFailedException($"审计不能承担回执读取：{method.Name}")
            });
            var permissions = Proxy<IPermissionEvaluationService>((method, args) => method.Name switch
            {
                "HasPermissionAsync" => Task.FromResult((string)args![1]! == PermissionKeys.DemandPlanningMaterialEstablish
                    ? AllowEstablish : AllowRollback),
                _ => throw new AssertFailedException($"意外权限服务调用 {method.Name}")
            });
            var receipts = Proxy<ICommandReceiptRepository>((method, args) => method.Name switch
            {
                "LockAsync" => Task.FromResult(0),
                "FindAsync" => ReadReceipt((Guid)args![0]!),
                "InsertAsync" => WriteReceipt((CommandReceipt)args![0]!),
                _ => throw new AssertFailedException($"意外回执调用 {method.Name}")
            });
            var correlation = Proxy<ICorrelationContext>((method, args) => method.Name == "get_CorrelationId"
                ? "material-command-test" : throw new AssertFailedException(method.Name));
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<EstablishMaterialDemandsCommand>, EstablishMaterialDemandsCommandValidator>()
                .AddSingleton<IValidator<RollbackMaterialDemandsCommand>, RollbackMaterialDemandsCommandValidator>()
                .BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, User,
                NullLogger<CommandPipeline>.Instance);
            var executor = new BusinessCommandExecutor(context, receipts, User);
            Establish = new MaterialDemandEstablishmentService(context, source, material,
                Proxy<IProductRepository>((m, a) => throw new AssertFailedException(m.Name)),
                Proxy<ISupplierRepository>((m, a) => throw new AssertFailedException(m.Name)),
                Proxy<IBomRepository>((m, a) => throw new AssertFailedException(m.Name)),
                logs, correlation, User, new GrossDemandDecompositionPlanner(),
                new MaterialDemandSuggestedSupplierInitializer(), permissions, pipeline, executor);
            Reverse = new MaterialDemandReversalService(context, source, material, execution, logs,
                correlation, User, permissions, pipeline, executor);
        }

        public EstablishMaterialDemandsCommand EstablishCommand(Guid commandId) => new()
        {
            CommandId = commandId,
            Items = [new() { GrossDemandId = Demand.Uid, SourceDemandVersion = 1 }]
        };

        public RollbackMaterialDemandsCommand RollbackCommand(Guid commandId, string reason) => new()
        {
            CommandId = commandId, Reason = reason,
            Items = [new() { GrossDemandId = Demand.Uid, DecompositionRevision = 1 }]
        };

        private Task<GrossDemand?> ReadGross(Guid uid)
        {
            Assert.IsNotNull(Session.Transaction);
            GrossReads++;
            OnGrossRead?.Invoke();
            return Task.FromResult<GrossDemand?>(uid == Demand.Uid ? Demand : uid == OtherDemand.Uid ? OtherDemand : null);
        }

        private Task<int> Rollback() { RollbackWrites++; return Task.FromResult(1); }
        private Task<int> WriteAudit()
        {
            Assert.IsNotNull(Session.Transaction);
            AuditWrites++;
            return Task.FromResult(FailAudit ? 0 : 1);
        }
        private Task<CommandReceipt?> ReadReceipt(Guid id)
        {
            Assert.IsNotNull(Session.Transaction);
            ReceiptReads++;
            return Task.FromResult(_receipts.GetValueOrDefault(id));
        }
        private Task WriteReceipt(CommandReceipt receipt)
        {
            Assert.IsNotNull(Session.Transaction);
            if (FailReceipt) throw new InvalidOperationException("模拟回执写入失败");
            ReceiptWrites++;
            _receipts.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }
    }

    /// <summary>为未触发的方法设置失败哨兵，避免替身默默返回默认值掩盖新增调用。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class MethodProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    /// <summary>允许切换操作人，观察公共回执是否拒绝跨用户重放。</summary>
    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserIdValue = Guid.NewGuid();
        public Guid? UserId => UserIdValue;
        public bool IsAuthenticated => UserIdValue.HasValue;
        public string UserName => "物料命令测试";
    }

    /// <summary>记录开始、提交和回滚次数；不连接实际数据库。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Commits, Rollbacks;
        public bool FailCommit;
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; return new TransactionStub(this); }
        /// <summary>需求建立使用已批准的 Serializable；其他公共命令仍可沿用默认事务。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            Assert.AreEqual(IsolationLevel.Serializable, isolationLevel);
            Begins++;
            return new TransactionStub(this, isolationLevel);
        }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }

        private sealed class TransactionStub(ConnectionStub owner, IsolationLevel level = IsolationLevel.ReadCommitted) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => level;
            public void Commit()
            {
                if (owner.FailCommit) throw new IOException("模拟提交结果不明");
                owner.Commits++;
            }
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
