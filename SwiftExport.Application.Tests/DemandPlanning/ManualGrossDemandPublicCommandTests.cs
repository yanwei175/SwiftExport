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
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.DemandPlanning.Sources.Manual;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>用内存端口验证手工来源三个维护命令的公共回执与授权顺序，不连接数据库。</summary>
[TestClass]
public sealed class ManualGrossDemandPublicCommandTests
{
    /// <summary>创建首次分配身份和单号；同 ID 重放不再调用取号或写入。</summary>
    [TestMethod]
    public async Task Create_ReplayKeepsUidAndNumber_AndRejectsChangedInput()
    {
        using var f = new Fixture();
        var command = f.Create(Guid.NewGuid());
        var first = await f.Service.CreateManualAsync(command);
        Assert.IsTrue(first.Success, first.Message);
        var replay = await f.Service.CreateManualAsync(command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(first.Data.Uid, replay.Data.Uid);
        Assert.AreEqual(first.Data.SourceNo, replay.Data.SourceNo);
        Assert.AreEqual(1, f.NumberCalls);
        Assert.AreEqual(1, f.AddCalls);
        Assert.AreEqual(1, f.ReceiptWrites);
        var readsBeforeDenial = f.ReceiptReads;
        var beginsBeforeDenial = f.Connection.Begins;
        f.Allow = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await f.Service.CreateManualAsync(command)).Error.Code);
        Assert.AreEqual(readsBeforeDenial, f.ReceiptReads);
        Assert.AreEqual(beginsBeforeDenial, f.Connection.Begins);
        f.Allow = true;
        command.DemandQty++;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.CreateManualAsync(command)).Error.Code);
        f.UserId = Guid.NewGuid();
        command.DemandQty--;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.CreateManualAsync(command)).Error.Code);
    }

    /// <summary>修改和关闭均先命中原回执，新 ID 才按当前版本和生命周期重验。</summary>
    [TestMethod]
    public async Task UpdateAndClose_ReplayPrecedesVersionAndStateCheck()
    {
        using var f = new Fixture();
        var created = await f.Service.CreateManualAsync(f.Create(Guid.NewGuid()));
        var update = new UpdateManualGrossDemandRequest
        {
            CommandId = Guid.NewGuid(), Uid = created.Data.Uid, Version = 1,
            IsNonStandard = true, Description = "新内容", DemandQty = 2m
        };
        var changed = await f.Service.UpdateManualAsync(update.Uid, update);
        Assert.IsTrue(changed.Success, changed.Message);
        Assert.IsTrue((await f.Service.UpdateManualAsync(update.Uid, update)).Success);
        Assert.AreEqual(1, f.UpdateCalls);
        var stale = new UpdateManualGrossDemandRequest
        {
            CommandId = Guid.NewGuid(), Uid = update.Uid, Version = 1,
            IsNonStandard = true, Description = "新内容", DemandQty = 2m
        };
        Assert.AreEqual(ServiceErrorCodes.VersionConflict,
            (await f.Service.UpdateManualAsync(stale.Uid, stale)).Error.Code);

        var close = new CloseGrossDemandCommand
        {
            CommandId = Guid.NewGuid(), Uid = update.Uid, Version = 2, Reason = "  结束  "
        };
        Assert.IsTrue((await f.Service.CloseManualAsync(close.Uid, close)).Success);
        close.Reason = "结束";
        Assert.IsTrue((await f.Service.CloseManualAsync(close.Uid, close)).Success);
        Assert.AreEqual(1, f.CloseCalls);
        close.Reason = "另一个原因";
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.CloseManualAsync(close.Uid, close)).Error.Code);
    }

    /// <summary>无权请求不得进入回执或事务；回执写入失败使首次创建回滚。</summary>
    [TestMethod]
    public async Task PermissionAndReceiptFailure_KeepTransactionBoundary()
    {
        using var f = new Fixture { Allow = false };
        var request = f.Create(Guid.NewGuid());
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await f.Service.CreateManualAsync(request)).Error.Code);
        Assert.AreEqual(0, f.Connection.Begins);
        Assert.AreEqual(0, f.ReceiptReads);
        f.UserId = null;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated,
            (await f.Service.CreateManualAsync(request)).Error.Code);
        Assert.AreEqual(0, f.Connection.Begins);
        f.UserId = Guid.NewGuid();
        f.Allow = true;
        f.FailReceipt = true;
        Assert.IsFalse((await f.Service.CreateManualAsync(request)).Success);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>只模拟命令直接端口和可观察事务，未声明的调用立即失败。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];
        private GrossDemand? _demand;
        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var user = Proxy<ICurrentUser>((m, a) => m.Name switch
            {
                "get_IsAuthenticated" => UserId.HasValue,
                "get_UserId" => UserId,
                _ => throw new AssertFailedException(m.Name)
            });
            var permissions = Proxy<IUserPermissionService>((m, a) => m.Name == "HasPermissionAsync"
                ? Task.FromResult(Allow && (string)a![1]! == PermissionKeys.DemandPlanningSourceManualManage)
                : throw new AssertFailedException(m.Name));
            var receipts = Proxy<ICommandReceiptRepository>((m, a) => m.Name switch
            {
                "LockAsync" => Task.FromResult(0),
                "FindAsync" => ReadReceipt((Guid)a![0]!),
                "InsertAsync" => WriteReceipt((CommandReceipt)a![0]!),
                _ => throw new AssertFailedException(m.Name)
            });
            var document = Proxy<IGrossDemandDocumentRepository>((m, a) => m.Name switch
            {
                "GetByIdForUpdateAsync" => Task.FromResult(_demand?.Uid == (Guid)a![0]! ? _demand : null),
                "AddSourceDemandsAsync" => Add((IEnumerable<GrossDemand>)a![0]!),
                "UpdateManualAsync" => Update(),
                "CloseManualAsync" => Close(),
                _ => throw new AssertFailedException(m.Name)
            });
            var query = Proxy<IGrossDemandQueryRepository>((m, a) => m.Name == "GetWorkspaceDetailAsync"
                ? Task.FromResult(_demand?.Uid == (Guid)a![0]! ? Detail() : null)
                : throw new AssertFailedException(m.Name));
            var exit = Proxy<IMaterialDemandSourceExitService>((m, a) => m.Name == "HasActiveAsync"
                ? Task.FromResult(false) : throw new AssertFailedException(m.Name));
            var numbers = Proxy<IDocumentNoService>((m, a) => m.Name == "GetNextDocumentNoAsync"
                ? NextNumber() : throw new AssertFailedException(m.Name));
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<CreateManualGrossDemandRequest>, CreateManualGrossDemandRequestValidator>()
                .AddSingleton<IValidator<UpdateManualGrossDemandRequest>, UpdateManualGrossDemandRequestValidator>()
                .AddSingleton<IValidator<CloseGrossDemandCommand>, CloseGrossDemandCommandValidator>()
                .BuildServiceProvider();
            var correlation = Proxy<SwiftExport.Core.Interfaces.Auditing.ICorrelationContext>((m, a) => m.Name == "get_CorrelationId"
                ? "manual-test" : throw new AssertFailedException(m.Name));
            Service = new ManualGrossDemandService(context, document, query,
                Proxy<IProductRepository>((m, a) => throw new AssertFailedException(m.Name)),
                numbers, exit, user, permissions,
                new CommandPipeline(new CommandRequestValidator(validators), correlation, user,
                    NullLogger<CommandPipeline>.Instance),
                new BusinessCommandExecutor(context, receipts, user));
        }

        public Guid? UserId { get; set; } = Guid.NewGuid();
        public bool Allow { get; set; } = true;
        public bool FailReceipt { get; set; }
        public DbSession Session { get; }
        public ConnectionStub Connection { get; } = new();
        public ManualGrossDemandService Service { get; }
        public int NumberCalls { get; private set; }
        public int AddCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int ReceiptReads { get; private set; }
        public int ReceiptWrites { get; private set; }

        public CreateManualGrossDemandRequest Create(Guid commandId) => new()
        {
            CommandId = commandId, IsNonStandard = true, Description = "非标件", DemandQty = 1m
        };

        private Task<ServiceResult<DocumentNoResponse>> NextNumber()
        {
            NumberCalls++;
            Assert.IsNotNull(Session.Transaction);
            return Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new DocumentNoResponse { DocumentNo = $"MD-{NumberCalls}" }));
        }

        private Task<IReadOnlyList<RepositoryWriteResult>> Add(IEnumerable<GrossDemand> demands)
        {
            AddCalls++;
            _demand = demands.Single();
            _demand.Version = 1;
            return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>([RepositoryWriteResult.Ok(_demand.Uid, null, 1)]);
        }

        private Task<RepositoryWriteResult> Update()
        {
            UpdateCalls++;
            _demand!.Version++;
            return Task.FromResult(RepositoryWriteResult.Ok(_demand.Uid, _demand.Version - 1, _demand.Version));
        }

        private Task<RepositoryWriteResult> Close()
        {
            CloseCalls++;
            _demand!.LifecycleStatus = SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Closed;
            _demand.Version++;
            return Task.FromResult(RepositoryWriteResult.Ok(_demand.Uid, _demand.Version - 1, _demand.Version));
        }

        private GrossDemandDetailDto Detail() => new()
        {
            Uid = _demand!.Uid, Version = _demand.Version, SourceNo = _demand.SourceNo,
            LifecycleStatus = _demand.LifecycleStatus, IsNonStandard = _demand.IsNonStandard,
            Description = _demand.DescriptionSnapshot ?? string.Empty, DemandQty = _demand.DemandQty
        };

        private Task<CommandReceipt?> ReadReceipt(Guid id)
        { ReceiptReads++; return Task.FromResult(_receipts.GetValueOrDefault(id)); }

        private Task WriteReceipt(CommandReceipt receipt)
        {
            if (FailReceipt) throw new InvalidOperationException("模拟回执写入失败");
            ReceiptWrites++;
            _receipts.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }

        public void Dispose() => Session.Dispose();
    }

    /// <summary>只在内存中计数事务入口和清理，不承担 SQL 原子性模拟。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Rollbacks;
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; return new TransactionStub(this); }
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel) => throw new AssertFailedException("不得改变默认隔离入口");
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() { }
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }

    /// <summary>动态端口仅响应夹具声明的方法，避免新增依赖被默认返回值掩盖。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class MethodProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
}
