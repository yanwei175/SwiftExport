using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证统一来源需求接收的幂等、来源身份、版本和下游处置边界。</summary>
[TestClass]
public sealed class SourceDemandReceptionServiceTests
{
    [TestMethod]
    public async Task RepeatedCommandReplaysAndChangedPayloadIsRejected()
    {
        using var fixture = new ReceptionFixture();
        var command = fixture.Command("SO", 1);
        var first = await fixture.Service.ReceiveAsync(command);
        var replay = await fixture.Service.ReceiveAsync(command);

        Assert.AreEqual(1, first.CreatedCount);
        Assert.AreEqual(first.CreatedCount, replay.CreatedCount);
        Assert.AreEqual(1, fixture.Repository.Demands.Count);
        Assert.AreEqual(1, fixture.Logs.Items.Count);

        command.Items[0].DemandQty = 2;
        var error = await Assert.ThrowsAsync<SourceDemandReceptionException>(() => fixture.Service.ReceiveAsync(command));
        Assert.AreEqual("COMMAND_REUSED", error.Code);
    }

    [TestMethod]
    public async Task SourceLineUsesVersionRulesWithoutCreatingSecondActiveDemand()
    {
        using var fixture = new ReceptionFixture();
        await fixture.Service.ReceiveAsync(fixture.Command("SO", 1));

        var unchanged = await fixture.Service.ReceiveAsync(fixture.Command("SO", 1));
        Assert.AreEqual(1, unchanged.UnchangedCount);

        var conflict = fixture.Command("SO", 1);
        conflict.Items[0].DemandQty = 2;
        Assert.AreEqual("SOURCE_VERSION_CONFLICT",
            (await Assert.ThrowsAsync<SourceDemandReceptionException>(() => fixture.Service.ReceiveAsync(conflict))).Code);

        var updated = fixture.Command("SO", 2);
        updated.Items[0].DemandQty = 3;
        Assert.AreEqual(1, (await fixture.Service.ReceiveAsync(updated)).UpdatedCount);
        Assert.AreEqual(1, fixture.Repository.Demands.Count);
        Assert.AreEqual(3m, fixture.Repository.Demands[0].DemandQty);

        Assert.AreEqual("STALE_SOURCE_VERSION",
            (await Assert.ThrowsAsync<SourceDemandReceptionException>(() => fixture.Service.ReceiveAsync(fixture.Command("SO", 1)))).Code);
    }

    [TestMethod]
    public async Task NewVersionWithDownstreamFactsRequiresDisposition()
    {
        using var fixture = new ReceptionFixture();
        await fixture.Service.ReceiveAsync(fixture.Command("SO", 1));
        fixture.Repository.HasBlocker = true;
        var changed = fixture.Command("SO", 2);
        changed.Items[0].DemandQty = 4;

        var error = await Assert.ThrowsAsync<SourceDemandReceptionException>(() => fixture.Service.ReceiveAsync(changed));
        Assert.AreEqual("SOURCE_CHANGE_REQUIRES_DISPOSITION", error.Code);
        Assert.AreEqual(1m, fixture.Repository.Demands[0].DemandQty);
    }

    [TestMethod]
    public async Task SourceTypesAreIsolatedByFullSourceIdentity()
    {
        using var fixture = new ReceptionFixture();
        await fixture.Service.ReceiveAsync(fixture.Command("SO", 1));
        await fixture.Service.ReceiveAsync(fixture.Command("ComplaintReplenishment", 1));

        Assert.AreEqual(2, fixture.Repository.Demands.Count);
        CollectionAssert.AreEquivalent(
            new[] { "SO", "ComplaintReplenishment" },
            fixture.Repository.Demands.Select(item => item.SourceType).ToArray());
    }

    [TestMethod]
    public async Task FullSubmissionRejectsUnsubmittedActiveLine()
    {
        using var fixture = new ReceptionFixture();
        var existing = fixture.Command("SO", 1);
        existing.Items = [existing.Items[0], fixture.Item(Guid.NewGuid())];
        await fixture.Service.ReceiveAsync(existing);

        var incomplete = fixture.Command("SO", 2);
        incomplete.RejectUnsubmittedActiveLines = true;
        var error = await Assert.ThrowsAsync<SourceDemandReceptionException>(() => fixture.Service.ReceiveAsync(incomplete));
        Assert.AreEqual("UNSUBMITTED_ACTIVE_SOURCE", error.Code);
    }

    /// <summary>组合内存仓储与显式事务会话，使测试只验证协议而不依赖数据库。</summary>
    private sealed class ReceptionFixture : IDisposable
    {
        private readonly FakeConnection _connection = new();
        public ReceptionFixture()
        {
            // DbSession 的正式构造会打开 SQL 连接；此处只需要非空事务标记来隔离验证协议逻辑。
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            Session.Transaction = _connection.BeginTransaction();
            Service = new SourceDemandReceptionService(Session, Repository, Logs, new Correlation(), new User());
        }

        public Guid SourceUid { get; } = Guid.NewGuid();
        public Guid SourceLineId { get; } = Guid.NewGuid();
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public FakeGrossDemandRepository Repository { get; } = new();
        public FakeOperationLogs Logs { get; } = new();
        public DbSession Session { get; }
        public SourceDemandReceptionService Service { get; }

        public SourceDemandReceptionCommand Command(string sourceType, int sourceVersion) => new()
        {
            CommandId = Guid.NewGuid(), SourceType = sourceType, SourceUid = SourceUid, SourceNo = "SOURCE-001",
            SourceVersion = sourceVersion, CustomerId = CustomerId, Items = [Item(SourceLineId)]
        };

        public SourceDemandReceptionItem Item(Guid lineId) => new()
        {
            SourceLineId = lineId, SourceLineNumber = 1, ProductId = ProductId, SkuSnapshot = "SKU-001",
            UnitSnapshot = "PCS", DemandQty = 1, RequiredDate = new DateTime(2026, 9, 30)
        };

        public void Dispose() => Session.Dispose();
    }

    /// <summary>仅实现接收协议使用的仓储成员，其余成员禁止测试误调用。</summary>
    private sealed class FakeGrossDemandRepository : IGrossDemandRepository
    {
        public List<GrossDemand> Demands { get; } = [];
        public bool HasBlocker { get; set; }
        public Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid)
            => Task.FromResult<IReadOnlyList<GrossDemand>>(Demands.Where(item => item.SourceType == sourceType && item.SourceUid == sourceUid && !item.IsDeleted && item.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active).ToList());
        public Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands)
        {
            var added = demands.ToList();
            foreach (var demand in added) { demand.Version = 1; Demands.Add(demand); }
            return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>(added.Select(item => RepositoryWriteResult.Ok(item.Uid, null, 1)).ToList());
        }
        public Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int expectedVersion)
        {
            demand.Version = expectedVersion + 1;
            return Task.FromResult(RepositoryWriteResult.Ok(demand.Uid, expectedVersion, demand.Version));
        }
        public Task<bool> HasSourceCancellationBlockerAsync(string sourceType, Guid sourceUid, IEnumerable<Guid> sourceLineIds) => Task.FromResult(HasBlocker);
        public Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid) => throw new NotSupportedException();
        public Task<RepositoryWriteResult> UpdateDecompositionStateAsync(Guid uid, int expectedVersion, GrossDemandDecompositionStatusEnum status, int decompositionRevision, DateTime? decomposedAt) => throw new NotSupportedException();
        public Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion) => throw new NotSupportedException();
        public Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason) => throw new NotSupportedException();
        public Task<bool> HasInvalidSourceCancellationHistoryAsync(IEnumerable<Guid> grossDemandIds) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> GetNetDemandHistoryCountsForUpdateAsync(IEnumerable<Guid> grossDemandIds) => throw new NotSupportedException();
        public Task<int> HardDeleteNeverDecomposedAsync(IEnumerable<Guid> grossDemandIds) => throw new NotSupportedException();
        public Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds, string reason) => throw new NotSupportedException();
        public Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason) => throw new NotSupportedException();
    }

    /// <summary>以内存字典模拟业务操作日志的命令唯一性。</summary>
    private sealed class FakeOperationLogs : IBusinessOperationLogRepository
    {
        public List<BusinessOperationLog> Items { get; } = [];
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId)
            => Task.FromResult(Items.SingleOrDefault(item => item.Module == module && item.OperationType == operationType && item.CommandId == commandId));
        public Task<int> InsertAsync(BusinessOperationLog log) { Items.Add(log); return Task.FromResult(1); }
        public Task<IReadOnlyList<SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Correlation : ICorrelationContext { public string CorrelationId => "source-demand-test"; }
    private sealed class User : ICurrentUser { public string UserName => "tester"; public Guid? UserId => Guid.NewGuid(); public bool IsAuthenticated => true; }

    /// <summary>为 DbSession 提供不访问网络的已打开连接和事务标记。</summary>
    private sealed class FakeConnection : IDbConnection
    {
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "Tests";
        public ConnectionState State { get; private set; } = ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new FakeTransaction(this);
        public IDbTransaction BeginTransaction(IsolationLevel il) => new FakeTransaction(this);
        public void ChangeDatabase(string databaseName) { }
        public void Close() => State = ConnectionState.Closed;
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() => State = ConnectionState.Open;
        public void Dispose() => Close();
    }

    private sealed class FakeTransaction(IDbConnection connection) : IDbTransaction
    {
        public IDbConnection Connection { get; } = connection;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }
}
