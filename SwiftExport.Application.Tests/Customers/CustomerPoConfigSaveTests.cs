using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.Customers.PoConfigs;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Systems.Currencies;

namespace SwiftExport.Application.Tests.Customers;

/// <summary>真实配置服务的身份、兼容、折扣和原子保存离线回归；事务替身不验证 PG 并发锁。</summary>
[TestClass]
public sealed class CustomerPoConfigSaveTests
{
    /// <summary>单条基类及批量调用均优先按身份取当前编码，兼容无身份的唯一编码调用。</summary>
    [TestMethod]
    [DataRow("add", false)] [DataRow("update", false)] [DataRow("batch", false)]
    [DataRow("add", true)] [DataRow("update", true)] [DataRow("batch", true)]
    public async Task Save_ResolvesBeforeValidationAndKeepsCurrentCodes(string entry, bool legacy)
    {
        var f = new Fixture();
        var dto = f.Dto();
        if (legacy) { dto.CustomerId = null; dto.SupplierId = null; dto.CustomerCode = " current-c "; dto.SupplierCode = " current-s "; }
        else { dto.CustomerCode = "OLD-C"; dto.SupplierCode = "OLD-S"; }
        dto.DefaultDiscountRate = 1.05m; dto.DiscountRemark = "  说明  ";
        var result = await f.Save(entry, dto);
        Assert.IsTrue(result, "合法身份或旧编码输入应保存。");
        var saved = f.Rows.Values.Single();
        Assert.AreEqual(f.CustomerId, saved.CustomerId); Assert.AreEqual(f.SupplierId, saved.SupplierId);
        Assert.AreEqual("CURRENT-C", saved.CustomerCode); Assert.AreEqual("CURRENT-S", saved.SupplierCode);
        Assert.AreEqual(1.05m, saved.DefaultDiscountRate); Assert.AreEqual("说明", saved.DiscountRemark);
        Assert.AreEqual(1, f.Commits); Assert.AreEqual(0, f.Rollbacks); Assert.IsNull(f.Transaction);
        Assert.AreEqual("lock", f.Trace[0]);
    }

    /// <summary>更名后的不同编码不能生成第二份同身份配置，单项和批量新增/更新均须在写入前拒绝。</summary>
    [TestMethod]
    [DataRow("add")] [DataRow("update")] [DataRow("batch")]
    public async Task Save_RenamedCodesCannotBypassIdUniqueness(string entry)
    {
        var f = new Fixture();
        var occupied = Guid.NewGuid(); f.Rows[occupied] = f.Entity(occupied, "OLD-C", "OLD-S");
        var dto = f.Dto();
        if (entry == "update") f.Rows[f.TargetId] = f.Entity(f.TargetId, "OTHER", "OTHER", Guid.NewGuid());
        var before = f.Snapshot();
        Assert.IsFalse(await f.Save(entry, dto));
        Assert.AreEqual(0, f.Writes); Assert.AreEqual(before, f.Snapshot()); Assert.AreEqual(1, f.Rollbacks);
    }

    /// <summary>显式空、失效或已删除身份不能退回旧编码匹配；双方无唯一解析也不能保存。</summary>
    [TestMethod]
    [DataRow("add")] [DataRow("update")] [DataRow("batch")]
    public async Task Save_InvalidMasterDataIsRejectedWithoutWrites(string entry)
    {
        foreach (var problem in new[] { "empty", "missing", "deleted", "ambiguous" })
        {
            var f = new Fixture(); var dto = f.Dto();
            if (problem == "empty") dto.CustomerId = Guid.Empty;
            if (problem == "missing") dto.SupplierId = Guid.NewGuid();
            if (problem == "deleted") f.PartiesValid = false;
            if (problem == "ambiguous") { dto.CustomerId = null; dto.CustomerCode = "AMBIGUOUS"; }
            Assert.IsFalse(await f.Save(entry, dto)); Assert.AreEqual(0, f.Writes); Assert.IsNull(f.Transaction);
        }
    }

    /// <summary>同批多个不同编码解析为同一身份组合也要拒绝，不能等第二次写入才碰数据库约束。</summary>
    [TestMethod]
    public async Task Save_DuplicateResolvedPairsInBatchAreRejected()
    {
        var f = new Fixture(); var first = f.Dto(); var second = f.Dto(); second.CustomerCode = "HISTORICAL";
        var result = await f.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = first }, new() { Data = second }] });
        Assert.IsFalse(result.Success); Assert.AreEqual(0, f.Writes); Assert.HasCount(0, f.Rows);
    }

    /// <summary>折扣上限、非负和精度统一覆盖单条/批量入口，空值及涨价倍率不被误拒。</summary>
    [TestMethod]
    [DataRow("add")] [DataRow("update")] [DataRow("batch")]
    public async Task Save_DiscountNumericRangeAndScaleAreEnforced(string entry)
    {
        foreach (var rate in new decimal?[] { null, 0m, 1.05m, 999.999999m, -0.000001m, 1000m, 0.1234567m })
        {
            var f = new Fixture(); var dto = f.Dto(); dto.DefaultDiscountRate = rate;
            Assert.AreEqual(rate is null or >= 0m and <= 999.999999m && (rate is null || decimal.Round(rate.Value, 6) == rate), await f.Save(entry, dto));
        }
    }

    /// <summary>版本冲突与后序失败不能留下先写行；自有事务最终回滚并释放。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Save_StaleVersionAndLaterWriteFailureAreAtomic(bool laterFailure)
    {
        var f = new Fixture(); f.Rows[f.TargetId] = f.Entity(f.TargetId);
        var dto = f.Dto(); dto.Note = "修改";
        var before = f.Snapshot();
        var request = new CustomerPoConfigBatchSaveRequest
        { UpdateItems = [new() { Uid = f.TargetId, Version = laterFailure ? 4 : 3, Data = dto }] };
        if (laterFailure)
        {
            var created = f.Dto(); created.CustomerId = f.OtherCustomerId; f.FailWrite = 2;
            request.CreateItems.Add(new() { Data = created });
        }
        var result = await f.Service.BatchSaveAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(before, f.Snapshot()); Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks); Assert.IsNull(f.Transaction);
    }

    /// <summary>调用方已有事务时零仓储操作，不能替调用方提交或清空会话。</summary>
    [TestMethod]
    public async Task Save_DoesNotOwnExistingTransaction()
    {
        var f = new Fixture(); var outer = f.Begin(); f.Transaction = outer;
        Assert.IsFalse((await f.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = f.Dto() }] })).Success);
        Assert.AreSame(outer, f.Transaction); Assert.HasCount(0, f.Trace);
        Assert.AreEqual(0, f.Commits); Assert.AreEqual(0, f.Rollbacks);
    }

    /// <summary>Edit 不能靠 IsDeleted 绕过唯一性或删除权限，客户端系统字段也不能改写创建审计。</summary>
    [TestMethod]
    public async Task Save_UpdatePreservesDeletionAndCreationAudit()
    {
        var f = new Fixture(); var old = f.Entity(f.TargetId); old.CreatedBy = "原创建人"; old.CreatedDate = new DateTime(2026, 1, 2);
        f.Rows[f.TargetId] = old;
        var dto = f.Dto(); dto.IsDeleted = true; dto.CreatedBy = "伪造人"; dto.CreatedDate = DateTime.MaxValue;
        Assert.IsTrue(await f.Save("update", dto));
        Assert.IsFalse(f.Rows[f.TargetId].IsDeleted); Assert.AreEqual(old.CreatedBy, f.Rows[f.TargetId].CreatedBy);
        Assert.AreEqual(old.CreatedDate, f.Rows[f.TargetId].CreatedDate);
    }

    /// <summary>新增时客户端身份、版本和审计不参与持久化，仓储负责生成和打戳。</summary>
    [TestMethod]
    public async Task Save_CreateClearsClientSystemFields()
    {
        var f = new Fixture(); var dto = f.Dto(); dto.CreatedBy = "伪造创建人"; dto.UpdatedBy = "伪造更新人";
        dto.CreatedDate = DateTime.MaxValue; dto.UpdatedDate = DateTime.MaxValue; dto.Version = 999; dto.IsDeleted = true;
        Assert.IsTrue(await f.Save("add", dto));
        var saved = f.Rows.Values.Single();
        Assert.IsFalse(saved.IsDeleted); Assert.AreEqual(1, saved.Version);
        Assert.IsNull(saved.CreatedBy); Assert.IsNull(saved.CreatedDate); Assert.IsNull(saved.UpdatedBy); Assert.IsNull(saved.UpdatedDate);
    }

    /// <summary>记录仓储与事务，返回完整独立对象；主数据身份及旧编码解析由场景显式控制。</summary>
    private sealed class Fixture : IDbSession, IDbConnection, IDatabaseExceptionClassifier
    {
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid OtherCustomerId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid TargetId { get; } = Guid.NewGuid();
        public Dictionary<Guid, CustomerPoConfig> Rows { get; } = [];
        public List<string> Trace { get; } = [];
        public bool PartiesValid = true;
        public int Writes, FailWrite, Commits, Rollbacks;
        public CustomerPoConfigService Service { get; }
        public IDbTransaction? Transaction { get; set; }
        public IDbConnection Connection => this;
        public IDatabaseExceptionClassifier Errors => this;
        /// <summary>让真实服务调用可观察仓储与同一事务会话，不创建真实连接。</summary>
        public Fixture()
        {
            Service = new(Proxy<ICustomerPoConfigRepository>(Call), Proxy<ISysDicCurrencyRepository>((_, _) => Task.FromResult<IReadOnlyList<SysDicCurrency>>([])), new ServiceBaseContext(this, NullLoggerFactory.Instance));
        }
        /// <summary>构造双方有效的基本保存输入，场景可指定旧编码或异常身份。</summary>
        public CustomerPoConfigDto Dto() => new() { CustomerId = CustomerId, CustomerCode = "CURRENT-C", SupplierId = SupplierId, SupplierCode = "CURRENT-S" };
        /// <summary>建立已有持久配置及固定并发版本，供更新与冲突场景使用。</summary>
        public CustomerPoConfig Entity(Guid id, string customerCode = "CURRENT-C", string supplierCode = "CURRENT-S", Guid? customer = null)
            => new() { Uid = id, Version = 4, CustomerId = customer ?? CustomerId, CustomerCode = customerCode, SupplierId = SupplierId, SupplierCode = supplierCode };
        /// <summary>比较持久状态的所有字段，不能只按行数判断回滚。</summary>
        public string Snapshot() => JsonSerializer.Serialize(Rows.OrderBy(row => row.Key));
        /// <summary>经真实单项基类引用或批量入口调用，防止测试绕开原有消费者路径。</summary>
        public async Task<bool> Save(string entry, CustomerPoConfigDto dto)
        {
            BaseService<CustomerPoConfig, CustomerPoConfigDto> baseService = Service;
            if (entry == "update" && !Rows.ContainsKey(TargetId)) Rows[TargetId] = Entity(TargetId);
            return entry == "add" ? (await baseService.AddAsync(new() { Data = dto })).Success
                : entry == "update" ? (await baseService.UpdateAsync(new() { Uid = TargetId, Version = 4, Data = dto })).Success
                : (await Service.BatchSaveAsync(new() { CreateItems = [new() { Data = dto }] })).Success;
        }
        /// <summary>配置显式仓储事实并记录写入，任何未声明的调用直接失败。</summary>
        private object Call(string name, object?[] args)
        {
            Assert.IsNotNull(Transaction, "全部配置解析和校验必须在保存事务内。"); Trace.Add(name == "LockForWriteAsync" ? "lock" : name);
            switch (name)
            {
                case "LockForWriteAsync": return Task.CompletedTask;
                case "GetByIdListAsync": return Task.FromResult<IReadOnlyList<CustomerPoConfig>>(Rows.Values.Where(row => ((IEnumerable<Guid>)args[0]!).Contains(row.Uid)).Select(Clone).ToArray());
                case "GetByPartiesAsync": return Task.FromResult(Rows.Values.FirstOrDefault(row => !row.IsDeleted && row.CustomerId == (Guid)args[0]! && row.SupplierId == (Guid)args[1]!)?.Adapt<CustomerPoConfigDto>());
                case "PopulateRelationIdsAsync":
                    var entity = (CustomerPoConfig)args[0]!;
                    if (!PartiesValid || entity.CustomerId.HasValue && entity.CustomerId != CustomerId && entity.CustomerId != OtherCustomerId
                        || entity.SupplierId.HasValue && entity.SupplierId != SupplierId
                        || !entity.CustomerId.HasValue && !string.Equals(entity.CustomerCode, "CURRENT-C", StringComparison.OrdinalIgnoreCase)
                        || !entity.SupplierId.HasValue && !string.Equals(entity.SupplierCode, "CURRENT-S", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
                    entity.CustomerId ??= CustomerId; entity.SupplierId ??= SupplierId;
                    entity.CustomerCode = "CURRENT-C"; entity.SupplierCode = "CURRENT-S"; return Task.FromResult(true);
                case "AddAsync": case "UpdateAsync":
                    var row = Clone((CustomerPoConfig)args[0]!); Writes++;
                    if (Writes == FailWrite) return Task.FromResult(RepositoryWriteResult.Fail(row.Uid, row.Version, "CONCURRENCY_CONFLICT", "受控失败"));
                    if (name == "AddAsync") row.Uid = Guid.NewGuid();
                    var old = name == "AddAsync" ? (int?)null : row.Version; row.Version = old.GetValueOrDefault() + 1;
                    Rows[row.Uid] = row; return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, old, row.Version));
                default: throw new AssertFailedException("未授权仓储调用：" + name);
            }
        }
        /// <summary>捕获本次保存前全部数据，回滚可恢复后序故障之前的状态。</summary>
        public MemoryTransaction Begin() => new(this);
        public IDbTransaction BeginTransaction() => Begin();
        public IDbTransaction BeginTransaction(IsolationLevel level) { Assert.AreEqual(IsolationLevel.ReadCommitted, level); return Begin(); }
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "offline-config";
        public ConnectionState State => ConnectionState.Open;
        public void Open() { } public void Close() { } public void Dispose() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }
    /// <summary>回滚恢复完整持久状态，不声称模拟数据库隔离和索引竞争。</summary>
    private sealed class MemoryTransaction(Fixture fixture) : IDbTransaction
    {
        private readonly Dictionary<Guid, CustomerPoConfig> _before = fixture.Rows.ToDictionary(row => row.Key, row => Clone(row.Value));
        public IDbConnection Connection => fixture;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() => fixture.Commits++;
        public void Rollback() { fixture.Rollbacks++; fixture.Rows.Clear(); foreach (var pair in _before) fixture.Rows[pair.Key] = Clone(pair.Value); }
        public void Dispose() { }
    }
    /// <summary>只配置用例需要的接口成员，任何越界调用立即失败。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>把实际接口调用交给当前用例，不返回默认成功值。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args ?? []);
    }
    /// <summary>建立按成员名配置的接口替身，不替代服务中的业务判断。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    { var proxy = DispatchProxy.Create<T, RepositoryProxy>(); ((RepositoryProxy)(object)proxy).Call = call; return proxy; }
    private static CustomerPoConfig Clone(CustomerPoConfig row) => JsonSerializer.Deserialize<CustomerPoConfig>(JsonSerializer.Serialize(row))!;
}
