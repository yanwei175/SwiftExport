using System.Data;
using System.Reflection;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.QualityControl.FlowManagement;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl.FlowManagement;

/// <summary>调用真实流量服务验证头行原子性、快照、身份、字典、版本及权限；内存事务不替代真实 PostgreSQL 验收。</summary>
[TestClass]
public sealed class FlowServiceTests
{
    /// <summary>空字典存储直接采用代码默认项；流量保存只写头行，不要求先插入类型或明细。</summary>
    [TestMethod]
    public async Task EmptyDictionaryUsesBuiltInDefaultsWithoutInitializingStorage()
    {
        var f = new Fixture();
        f.Dictionary.Clear();
        var function = BuiltInDictionaryCatalog.Data.Single(x => x.DictCode == DictCodes.FlowTestFunction);
        var limiter = BuiltInDictionaryCatalog.Data.Single(x => x.DictCode == DictCodes.FlowLimiterSpecification);
        var dto = f.Edit();
        dto.Lines = [f.Line(function.Uid, limiter.Uid)];
        var result = await f.Service.CreateAsync(new() { Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        var line = result.Data.Detail.Lines.Single();
        Assert.AreEqual(function.Uid, line.FunctionId);
        Assert.AreEqual(limiter.Uid, line.LimiterSpecificationId);
        Assert.AreEqual("Default", line.FunctionNameSnapshot);
        Assert.AreEqual("不限流", line.LimiterSpecificationNameSnapshot);
        Assert.HasCount(0, f.Dictionary);
        Assert.AreEqual(2, f.Writes);
        Assert.AreEqual(1, f.Commits);
    }

    /// <summary>内置业务键存在旧 Guid 时保留原身份和显示配置，过期代码默认 Guid 必须拒绝，不能静默改关联。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task StoredBuiltInIdentityWinsAndStaleDefaultIdentityIsRejected(bool stale)
    {
        var f = new Fixture();
        f.Dictionary[f.Function].DataNameZh = "现存默认功能";
        f.Dictionary[f.Limiter].DataNameEn = "Unlimited";
        f.Dictionary[f.Limiter].DataNameZh = "现存不限流规格";
        var dto = f.Edit();
        if (stale)
            dto.Lines[0].FunctionId = BuiltInDictionaryCatalog.Data.Single(x => x.DictCode == DictCodes.FlowTestFunction).Uid;
        var result = await f.Service.CreateAsync(new() { Data = dto });
        Assert.AreEqual(!stale, result.Success, result.Message);
        if (stale)
        {
            Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
            Assert.AreEqual(0, f.Writes);
            return;
        }
        var line = result.Data.Detail.Lines.Single();
        Assert.AreEqual(f.Function, line.FunctionId);
        Assert.AreEqual(f.Limiter, line.LimiterSpecificationId);
        Assert.AreEqual("现存默认功能", line.FunctionNameSnapshot);
        Assert.AreEqual("现存不限流规格", line.LimiterSpecificationNameSnapshot);
    }

    /// <summary>停用或软删除的内置记录通过目录回退供采用，保留旧身份；读取不恢复数据库标记。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task InvalidStoredBuiltInFlagsUseDefaultsWithoutMutatingStorage(bool deleted)
    {
        var f = new Fixture();
        f.Dictionary[f.Function].IsDeleted = deleted;
        f.Dictionary[f.Function].IsEnabled = false;
        f.Dictionary[f.Function].DataNameZh = "旧异常配置";
        f.Dictionary[f.Limiter].DataNameEn = "Unlimited";
        f.Dictionary[f.Limiter].IsEnabled = false;
        var result = await f.Service.CreateAsync(new() { Data = f.Edit() });
        Assert.IsTrue(result.Success, result.Message);
        var line = result.Data.Detail.Lines.Single();
        Assert.AreEqual(f.Function, line.FunctionId);
        Assert.AreEqual(f.Limiter, line.LimiterSpecificationId);
        Assert.AreEqual("Default", line.FunctionNameSnapshot);
        Assert.AreEqual("不限流", line.LimiterSpecificationNameSnapshot);
        Assert.AreEqual(deleted, f.Dictionary[f.Function].IsDeleted);
        Assert.IsFalse(f.Dictionary[f.Function].IsEnabled);
        Assert.AreEqual("旧异常配置", f.Dictionary[f.Function].DataNameZh);
    }

    /// <summary>同物料允许多组，组内不同功能可选不同限流器，两套压力列与零值都准确保留。</summary>
    [TestMethod]
    public async Task MultipleGroupsAndBothPressureFamilies()
    {
        var f = new Fixture();
        var dto = f.Edit();
        foreach (var property in typeof(FlowLineEditDto).GetProperties().Where(x => x.PropertyType == typeof(decimal?)))
            property.SetValue(dto.Lines[0], 0.125m);
        dto.Lines[0].Flow1BarLMin = 0;
        dto.Lines.Add(f.Line(f.SecondFunction, f.SecondLimiter));
        var first = await f.Service.CreateAsync(new() { Data = dto, ClientKey = "first" });
        var second = await f.Service.CreateAsync(new() { Data = f.Edit() });
        Assert.IsTrue(first.Success, first.Message);
        Assert.IsTrue(second.Success, second.Message);
        Assert.AreNotEqual(first.Data.Detail.Uid, second.Data.Detail.Uid);
        Assert.AreEqual("SKU-A", first.Data.Detail.SkuSnapshot);
        Assert.AreEqual(2, first.Data.Detail.Lines.Count);
        Assert.AreEqual(0m, first.Data.Detail.Lines.Single(x => x.FunctionId == f.Function).Flow1BarLMin);
        Assert.AreEqual(0.125m, first.Data.Detail.Lines.Single(x => x.FunctionId == f.Function).Flow100PsiGpm);
        Assert.AreEqual(1, first.Data.HeaderResult.NewVersion);
        Assert.AreEqual("first", first.Data.HeaderResult.ClientKey);
        Assert.AreEqual(2, f.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>非法结构在写入前拒绝，测量值未填、重复功能、客户端身份和精度错误均不能成为测试结果。</summary>
    [TestMethod]
    [DataRow("empty")] [DataRow("null-line")] [DataRow("duplicate")] [DataRow("missing-measurement")]
    [DataRow("function")] [DataRow("limiter")] [DataRow("sort")] [DataRow("material")]
    [DataRow("date")] [DataRow("new-id")] [DataRow("new-version")] [DataRow("precision")] [DataRow("overflow")]
    public async Task InvalidInputsAreRejectedBeforeWrites(string kind)
    {
        var f = new Fixture();
        var dto = f.Edit();
        switch (kind)
        {
            case "empty": dto.Lines.Clear(); break;
            case "null-line": dto.Lines.Add(null!); break;
            case "duplicate": dto.Lines.Add(f.Line(f.Function, f.SecondLimiter)); break;
            case "missing-measurement": dto.Lines[0].Flow1BarLMin = null; break;
            case "function": dto.Lines[0].FunctionId = Guid.Empty; break;
            case "limiter": dto.Lines[0].LimiterSpecificationId = Guid.Empty; break;
            case "sort": dto.Lines[0].SortNo = -1; break;
            case "material": dto.MaterialId = Guid.Empty; break;
            case "date": dto.TestDate = default; break;
            case "new-id": dto.Lines[0].Uid = Guid.NewGuid(); break;
            case "new-version": dto.Lines[0].Version = 1; break;
            case "precision": dto.Lines[0].Flow1BarLMin = 1.00001m; break;
            case "overflow": dto.Lines[0].Flow1BarLMin = 100000000000000m; break;
        }
        var result = await f.Service.CreateAsync(new() { Data = dto });
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual(0, f.Begins);
    }

    /// <summary>全部固定压力列都禁止负值，不能只检查第一列。</summary>
    [TestMethod]
    [DataRow("Flow1BarLMin")]
    [DataRow("Flow2BarLMin")]
    [DataRow("Flow3BarLMin")]
    [DataRow("Flow4BarLMin")]
    [DataRow("Flow5BarLMin")]
    [DataRow("Flow6BarLMin")]
    [DataRow("Flow7BarLMin")]
    [DataRow("Flow8BarLMin")]
    [DataRow("Flow20PsiGpm")]
    [DataRow("Flow40PsiGpm")]
    [DataRow("Flow60PsiGpm")]
    [DataRow("Flow80PsiGpm")]
    [DataRow("Flow100PsiGpm")]
    public async Task EachPressureColumnRejectsNegativeValues(string property)
    {
        var f = new Fixture();
        var dto = f.Edit();
        typeof(FlowLineEditDto).GetProperty(property)!.SetValue(dto.Lines[0], -0.01m);
        var result = await f.Service.CreateAsync(new() { Data = dto });
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
    }

    /// <summary>新增必须核实物料活动资格及字典分类、存在性和启用状态。</summary>
    [TestMethod]
    [DataRow("inactive")] [DataRow("deleted-material")] [DataRow("unknown-material")]
    [DataRow("wrong-code")] [DataRow("disabled")] [DataRow("deleted-dictionary")] [DataRow("unknown-dictionary")]
    public async Task InvalidReferencesRollBackWithoutWrites(string kind)
    {
        var f = new Fixture();
        var dto = f.Edit();
        switch (kind)
        {
            case "inactive": f.Material.IsActive = false; break;
            case "deleted-material": f.Material.IsDeleted = true; break;
            case "unknown-material": dto.MaterialId = Guid.NewGuid(); break;
            case "wrong-code": f.Dictionary[f.Function].DictCode = DictCodes.FlowLimiterSpecification; break;
            case "disabled": f.Dictionary[f.Limiter].IsEnabled = false; break;
            case "deleted-dictionary": f.Dictionary[f.Limiter].IsDeleted = true; break;
            case "unknown-dictionary": dto.Lines[0].FunctionId = Guid.NewGuid(); break;
        }
        var result = await f.Service.CreateAsync(new() { Data = dto });
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual(1, f.Rollbacks);
    }

    /// <summary>更名及停用不会刷新已采用快照；允许继续编辑既有结果，更换限流器时才生成新快照。</summary>
    [TestMethod]
    public async Task SnapshotsSurviveRenameAndDisable()
    {
        var f = new Fixture();
        var first = await f.Create();
        f.Material.Sku = "SKU-RENAMED";
        f.Material.IsActive = false;
        f.Dictionary[f.Function].DataNameZh = "改名功能";
        f.Dictionary[f.Function].IsEnabled = false;
        f.Dictionary[f.Limiter].DataNameZh = "改名规格";
        f.Dictionary[f.Limiter].IsEnabled = false;
        var request = f.Update(first);
        request.Data.Lines[0].Flow1BarLMin = 3m;
        var saved = await f.Service.UpdateAsync(request);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual("SKU-A", saved.Data.Detail.SkuSnapshot);
        Assert.AreEqual("Default", saved.Data.Detail.Lines[0].FunctionNameSnapshot);
        Assert.AreEqual("不限流", saved.Data.Detail.Lines[0].LimiterSpecificationNameSnapshot);
        var next = f.Update(saved.Data.Detail);
        next.Data.Lines[0].LimiterSpecificationId = f.SecondLimiter;
        var changed = await f.Service.UpdateAsync(next);
        Assert.IsTrue(changed.Success, changed.Message);
        Assert.AreEqual("1.8GPM", changed.Data.Detail.Lines[0].LimiterSpecificationNameSnapshot);
        Assert.AreEqual("Default", changed.Data.Detail.Lines[0].FunctionNameSnapshot);
        Assert.AreEqual(2, saved.Data.HeaderResult.NewVersion);
        Assert.AreEqual(1, saved.Data.HeaderResult.OldVersion);
    }

    /// <summary>完整集合更新同时增加、修改和移除行，返回当前版本，移除行保持软删历史。</summary>
    [TestMethod]
    public async Task UpdateAddsEditsAndRemovesLines()
    {
        var f = new Fixture();
        var dto = f.Edit();
        dto.Lines.Add(f.Line(f.SecondFunction, f.SecondLimiter));
        var first = (await f.Service.CreateAsync(new() { Data = dto })).Data.Detail;
        var removed = first.Lines.Single(x => x.FunctionId == f.SecondFunction);
        var request = f.Update(first);
        request.Data.Lines.RemoveAll(x => x.Uid == removed.Uid);
        request.Data.Lines[0].Flow1BarLMin = 5;
        request.Data.Lines.Add(f.Line(f.ThirdFunction, f.Limiter));
        var result = await f.Service.UpdateAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.Detail.Lines.Count);
        Assert.IsTrue(f.Lines.Single(x => x.Uid == removed.Uid).IsDeleted);
        Assert.AreEqual(2, result.Data.Detail.Lines.Single(x => x.FunctionId == f.Function).Version);
        Assert.AreEqual(1, result.Data.Detail.Lines.Single(x => x.FunctionId == f.ThirdFunction).Version);
    }

    /// <summary>换功能和交换功能按替换处理，保留旧行历史，最终唯一键不因中间态冲突而失败。</summary>
    [TestMethod]
    public async Task FunctionSwapReplacesOldRows()
    {
        var f = new Fixture();
        var dto = f.Edit();
        dto.Lines.Add(f.Line(f.SecondFunction, f.SecondLimiter));
        var first = (await f.Service.CreateAsync(new() { Data = dto })).Data.Detail;
        var request = f.Update(first);
        (request.Data.Lines[0].FunctionId, request.Data.Lines[1].FunctionId) =
            (request.Data.Lines[1].FunctionId, request.Data.Lines[0].FunctionId);
        var result = await f.Service.UpdateAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, f.Lines.Count(x => x.IsDeleted));
        Assert.IsFalse(result.Data.Detail.Lines.Any(x => first.Lines.Any(old => old.Uid == x.Uid)));
    }

    /// <summary>过期头或行版本、跨组行、重复身份以及更换物料均在任何写入前拒绝。</summary>
    [TestMethod]
    [DataRow("header-version")] [DataRow("line-version")] [DataRow("foreign-line")]
    [DataRow("duplicate-id")] [DataRow("material")]
    public async Task InvalidUpdateDoesNotWrite(string kind)
    {
        var f = new Fixture();
        var first = await f.Create();
        var foreign = await f.Create();
        var request = f.Update(first);
        switch (kind)
        {
            case "header-version": request.Version++; break;
            case "line-version": request.Data.Lines[0].Version++; break;
            case "foreign-line": request.Data.Lines[0].Uid = foreign.Lines[0].Uid; break;
            case "duplicate-id": var duplicate = Clone(request.Data.Lines[0]); duplicate.FunctionId = f.SecondFunction; request.Data.Lines.Add(duplicate); break;
            case "material": request.Data.MaterialId = Guid.NewGuid(); break;
        }
        f.ResetActivity();
        var result = await f.Service.UpdateAsync(request);
        Assert.AreEqual(kind.EndsWith("version") ? ServiceErrorCodes.ConcurrencyConflict : ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
    }

    /// <summary>新增或更新的每个写阶段失败均撤销整组，不能留下删除行、头版本或部分新增。</summary>
    [TestMethod]
    [DataRow("create", 1)] [DataRow("create", 2)] [DataRow("create", 3)]
    [DataRow("update", 1)] [DataRow("update", 2)] [DataRow("update", 3)] [DataRow("update", 4)]
    public async Task EveryWriteStageRollsBack(string operation, int failAt)
    {
        var f = new Fixture();
        if (operation == "create")
        {
            var dto = f.Edit();
            dto.Lines.Add(f.Line(f.SecondFunction, f.SecondLimiter));
            f.FailAt = failAt;
            var result = await f.Service.CreateAsync(new() { Data = dto });
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, f.Headers.Count);
            Assert.AreEqual(0, f.Lines.Count);
        }
        else
        {
            var dto = f.Edit();
            dto.Lines.Add(f.Line(f.SecondFunction, f.SecondLimiter));
            var initial = (await f.Service.CreateAsync(new() { Data = dto })).Data.Detail;
            var before = JsonSerializer.Serialize(new { f.Headers, f.Lines });
            var request = f.Update(initial);
            request.Data.Lines.RemoveAll(x => x.FunctionId == f.SecondFunction);
            request.Data.Lines[0].Flow1BarLMin = 8;
            request.Data.Lines.Add(f.Line(f.ThirdFunction, f.Limiter));
            f.ResetActivity();
            f.FailAt = failAt;
            Assert.IsFalse((await f.Service.UpdateAsync(request)).Success);
            Assert.AreEqual(before, JsonSerializer.Serialize(new { f.Headers, f.Lines }));
        }
        Assert.AreEqual(1, f.Rollbacks);
        Assert.AreEqual(0, f.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>删除固定软删，旧版本不能删除，删头失败会恢复先前删行。</summary>
    [TestMethod]
    [DataRow("success")] [DataRow("stale")] [DataRow("failure")]
    public async Task DeleteIsAtomicAndVersioned(string kind)
    {
        var f = new Fixture();
        var initial = await f.Create();
        f.ResetActivity();
        if (kind == "failure") f.FailAt = 2;
        var result = await f.Service.DeleteAsync(new() { Uid = initial.Uid, Version = kind == "stale" ? 2 : 1, IsSoftDelete = false });
        Assert.AreEqual(kind == "success", result.Success);
        Assert.AreEqual(kind == "success", f.Headers.Single().IsDeleted);
        Assert.AreEqual(kind == "success", f.Lines.Single().IsDeleted);
        if (kind == "success") Assert.AreEqual(ServiceErrorCodes.NotFound, (await f.Service.GetByIdAsync(initial.Uid)).Error.Code);
    }

    /// <summary>五个入口区分身份缺失及细粒度权限不足，拒绝时零仓储访问、零写入和零事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task UnauthorizedRequestsDoNotTouchPersistence(bool authenticated)
    {
        var f = new Fixture { Authenticated = authenticated };
        f.AllowedKeys.Clear();
        var expected = authenticated ? ServiceErrorCodes.Forbidden : ServiceErrorCodes.Unauthenticated;
        Assert.AreEqual(expected, (await f.Service.GetPagedAsync(new())).Error.Code);
        Assert.AreEqual(expected, (await f.Service.GetByIdAsync(Guid.NewGuid())).Error.Code);
        Assert.AreEqual(expected, (await f.Service.CreateAsync(new() { Data = f.Edit() })).Error.Code);
        Assert.AreEqual(expected, (await f.Service.UpdateAsync(new() { Uid = Guid.NewGuid(), Version = 1, Data = f.Edit() })).Error.Code);
        Assert.AreEqual(expected, (await f.Service.DeleteAsync(new() { Uid = Guid.NewGuid(), Version = 1 })).Error.Code);
        Assert.AreEqual(0, f.Reads);
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual(0, f.Begins);
        if (authenticated) CollectionAssert.AreEqual(new[] { PermissionKeys.QualityControlFlowView, PermissionKeys.QualityControlFlowView,
            PermissionKeys.QualityControlFlowCreate, PermissionKeys.QualityControlFlowEdit, PermissionKeys.QualityControlFlowDelete }, f.RequestedKeys);
    }

    /// <summary>查询空组和错误分页有稳定结果，现存查询不打开维护事务。</summary>
    [TestMethod]
    public async Task ReadAndPagingBoundaries()
    {
        var f = new Fixture();
        Assert.AreEqual(ServiceErrorCodes.NotFound, (await f.Service.GetByIdAsync(Guid.NewGuid())).Error.Code);
        Assert.AreEqual(0, (await f.Service.GetPagedAsync(new())).Data.TotalCount);
        foreach (var search in new[] { new FlowSearchRequest { PageIndex = 0 }, new() { PageSize = 501 },
            new() { MaterialId = Guid.Empty }, new() { TestDateFrom = new(2026, 10, 9), TestDateTo = new(2026, 10, 8) } })
            Assert.AreEqual(ServiceErrorCodes.Validation, (await f.Service.GetPagedAsync(search)).Error.Code);
        Assert.AreEqual(0, f.Begins);
    }

    /// <summary>外层事务不能被接管；写入期间取消会整体回滚并向上传递取消。</summary>
    [TestMethod]
    public async Task ExternalTransactionAndCancellationAreSafe()
    {
        var f = new Fixture();
        using (var transaction = f.Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            f.Session.Transaction = transaction;
            Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, (await f.Service.CreateAsync(new() { Data = f.Edit() })).Error.Code);
            Assert.AreSame(transaction, f.Session.Transaction);
            Assert.AreEqual(0, f.Writes);
            f.Session.Transaction = null;
            transaction.Rollback();
        }
        f.ResetActivity();
        using var cancellation = new CancellationTokenSource();
        f.AfterWrite = _ => cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.CreateAsync(new() { Data = f.Edit() }, cancellation.Token));
        Assert.AreEqual(0, f.Headers.Count);
        Assert.AreEqual(1, f.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>无法确认提交结果时返回独立错误码，避免让调用方误以为可以安全重复新增。</summary>
    [TestMethod]
    public async Task CommitFailureIsOutcomeUnknown()
    {
        var f = new Fixture { FailCommit = true };
        var result = await f.Service.CreateAsync(new() { Data = f.Edit() });
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.IsNull(f.Session.Transaction);
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    /// <summary>依赖代理只记录真实服务调用，不复制业务校验。</summary>
    public class Port : DispatchProxy
    {
        public Func<string, object?[], object?> Action { get; set; } = null!;
        /// <summary>将仓储或身份调用转发到测试夹具。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Action(method!.Name, args ?? []);
    }

    private static T Proxy<T>(Func<string, object?[], object?> action) where T : class
    {
        var proxy = DispatchProxy.Create<T, Port>();
        ((Port)(object)proxy).Action = action;
        return proxy;
    }

    /// <summary>只模拟持久化与事务，真实业务逻辑始终由 FlowService 执行。</summary>
    private sealed class Fixture
    {
        public List<FlowHeader> Headers { get; set; } = [];
        public List<FlowLine> Lines { get; set; } = [];
        public Dictionary<Guid, SysDictData> Dictionary { get; } = [];
        public Product Material { get; } = new() { Sku = "SKU-A", IsActive = true };
        public Guid Function { get; } = Guid.NewGuid();
        public Guid SecondFunction { get; } = Guid.NewGuid();
        public Guid ThirdFunction { get; } = Guid.NewGuid();
        public Guid Limiter { get; } = Guid.NewGuid();
        public Guid SecondLimiter { get; } = Guid.NewGuid();
        public HashSet<string> AllowedKeys { get; } = [PermissionKeys.QualityControlFlowView, PermissionKeys.QualityControlFlowCreate,
            PermissionKeys.QualityControlFlowEdit, PermissionKeys.QualityControlFlowDelete];
        public List<string> RequestedKeys { get; } = [];
        public bool Authenticated { get; set; } = true;
        public bool FailCommit { get; set; }
        public int Reads, Writes, Begins, Commits, Rollbacks, FailAt;
        public Action<int>? AfterWrite { get; set; }
        public TestSession Session { get; }
        public FlowService Service { get; }

        /// <summary>建立字典、权限和可观察会话；所有数据均位于内存。</summary>
        public Fixture()
        {
            foreach (var (id, code, name) in new[] { (Function, DictCodes.FlowTestFunction, "Default"),
                (SecondFunction, DictCodes.FlowTestFunction, "按摩"), (ThirdFunction, DictCodes.FlowTestFunction, "雨淋"),
                (Limiter, DictCodes.FlowLimiterSpecification, "不限流"), (SecondLimiter, DictCodes.FlowLimiterSpecification, "1.8GPM") })
                Dictionary[id] = new() { Uid = id, DictCode = code, DataNameZh = name, DataNameEn = name, IsEnabled = true };
            Session = new(new Connection(this));
            var user = Proxy<ICurrentUser>((name, _) => name switch
            {
                "get_IsAuthenticated" => Authenticated, "get_UserId" => (Guid?)Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "get_UserName" => "flow-test", _ => throw new NotSupportedException(name)
            });
            var permissions = Proxy<IPermissionEvaluationService>((name, args) =>
            {
                if (name != "HasPermissionAsync") throw new NotSupportedException(name);
                var key = (string)args[1]!;
                RequestedKeys.Add(key);
                return Task.FromResult(AllowedKeys.Contains(key));
            });
            var repo = Proxy<IFlowRepository>(ReadOrWrite);
            Service = new(repo, new ServiceBaseContext(Session, NullLoggerFactory.Instance), permissions, user);
        }

        public FlowLineEditDto Line(Guid function, Guid limiter) => new() { FunctionId = function, LimiterSpecificationId = limiter, Flow1BarLMin = 1m };
        public FlowEditDto Edit() => new() { MaterialId = Material.Uid, TestDate = new(2026, 10, 8), Lines = [Line(Function, Limiter)] };
        public async Task<FlowDetailDto> Create() => (await Service.CreateAsync(new() { Data = Edit() })).Data.Detail;
        public UpdateItemRequest<FlowEditDto> Update(FlowDetailDto detail) => new()
        {
            Uid = detail.Uid, Version = detail.Version,
            Data = new() { MaterialId = detail.MaterialId, TestDate = detail.TestDate, Lines = detail.Lines.Adapt<List<FlowLineEditDto>>() }
        };
        public void ResetActivity() { Reads = Writes = Begins = Commits = Rollbacks = 0; RequestedKeys.Clear(); }

        /// <summary>读取返回隔离副本；写入必须有事务，失败注入发生在真实服务的指定阶段。</summary>
        private object ReadOrWrite(string name, object?[] args)
        {
            var token = (CancellationToken)args[^1]!;
            token.ThrowIfCancellationRequested();
            if (name is "AddHeaderAsync" or "UpdateHeaderAsync" or "DeleteHeaderAsync" or "AddLineAsync" or "UpdateLineAsync" or "DeleteLineAsync")
            {
                Assert.IsNotNull(Session.Transaction);
                Writes++;
                if (Writes == FailAt) return Task.FromResult(RepositoryWriteResult.Fail(Guid.Empty, null, ServiceErrorCodes.ConcurrencyConflict, "模拟写入失败"));
                var result = name switch
                {
                    "AddHeaderAsync" => Save(Headers, (FlowHeader)args[0]!, true),
                    "UpdateHeaderAsync" => Save(Headers, (FlowHeader)args[0]!, false),
                    "AddLineAsync" => Save(Lines, (FlowLine)args[0]!, true),
                    "UpdateLineAsync" => Save(Lines, (FlowLine)args[0]!, false),
                    "DeleteHeaderAsync" => Delete(Headers, (Guid)args[0]!, (int)args[1]!),
                    _ => Delete(Lines, (Guid)args[0]!, (int)args[1]!)
                };
                AfterWrite?.Invoke(Writes);
                return Task.FromResult(result);
            }
            Reads++;
            return name switch
            {
                "ReadMaterialAsync" => Task.FromResult((Guid)args[0]! == Material.Uid && !Material.IsDeleted ? Clone(Material) : null),
                "ReadDictionaryItemsAsync" => Task.FromResult<IReadOnlyList<SysDictData>>(Dictionary.Values
                    .Where(item => ((IEnumerable<Guid>)args[0]!).Contains(item.Uid)
                        || BuiltInDictionaryCatalog.IsBuiltIn(item)).Select(Clone).ToArray()),
                "ReadHeaderAsync" => Task.FromResult(Clone(Headers.SingleOrDefault(x => x.Uid == (Guid)args[0]! && !x.IsDeleted))),
                "ReadLinesAsync" => Task.FromResult<IReadOnlyList<FlowLine>>(Clone(Lines.Where(x => x.HeaderId == (Guid)args[0]! && !x.IsDeleted).OrderBy(x => x.Uid).ToArray())),
                "GetDetailAsync" => Task.FromResult(ReadDetail((Guid)args[0]!)),
                "GetPagedAsync" => Task.FromResult(new PagedResponseBase<FlowListItemDto> { PageIndex = 1, PageSize = 20, Items = Headers.Where(x => !x.IsDeleted).Adapt<List<FlowListItemDto>>(), TotalCount = Headers.Count(x => !x.IsDeleted) }),
                _ => throw new NotSupportedException(name)
            };
        }

        private FlowDetailDto? ReadDetail(Guid id)
        {
            var header = Headers.SingleOrDefault(x => x.Uid == id && !x.IsDeleted);
            if (header is null) return null;
            var result = header.Adapt<FlowDetailDto>();
            result.Lines = Lines.Where(x => x.HeaderId == id && !x.IsDeleted).Adapt<List<FlowLineDto>>();
            return result;
        }

        /// <summary>只模拟版本和数据库功能唯一键，便于检验服务的写入顺序。</summary>
        private RepositoryWriteResult Save<T>(List<T> rows, T entity, bool create) where T : SwiftExport.Core.Common.BaseEntity
        {
            var oldVersion = create ? (int?)null : entity.Version;
            if (entity is FlowLine line && Lines.Any(x => !x.IsDeleted && x.Uid != line.Uid && x.HeaderId == line.HeaderId && x.FunctionId == line.FunctionId))
                throw new InvalidOperationException("模拟唯一键冲突");
            if (create) { entity.Uid = Guid.NewGuid(); entity.Version = 1; }
            else { rows.RemoveAll(x => x.Uid == entity.Uid); entity.Version++; }
            rows.Add(Clone(entity));
            return RepositoryWriteResult.Ok(entity.Uid, oldVersion, entity.Version);
        }

        private static RepositoryWriteResult Delete<T>(List<T> rows, Guid id, int version) where T : SwiftExport.Core.Common.BaseEntity
        {
            var row = rows.Single(x => x.Uid == id && !x.IsDeleted);
            Assert.AreEqual(version, row.Version);
            row.IsDeleted = true;
            return RepositoryWriteResult.Ok(id, version, version);
        }
    }

    /// <summary>内存会话仅保留事务和公共数据库异常分类。</summary>
    private sealed class TestSession(IDbConnection connection) : IDbSession
    {
        public IDbConnection Connection => connection;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
    }

    /// <summary>开启事务时保存头行快照，回滚恢复全部数据。</summary>
    private sealed class Connection(Fixture fixture) : IDbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "flow-memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => BeginTransaction(IsolationLevel.ReadCommitted);
        /// <summary>按真实用例隔离级别记录事务及完整持久化快照。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel level)
        {
            Assert.AreEqual(IsolationLevel.ReadCommitted, level);
            fixture.Begins++;
            return new Transaction(this, fixture, Clone(fixture.Headers), Clone(fixture.Lines));
        }
        public void ChangeDatabase(string name) => throw new NotSupportedException();
        public void Close() { }
        public void Open() { }
        public void Dispose() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }

    /// <summary>事务只结束一次，未提交释放也恢复数据，支持模拟提交结果未知。</summary>
    private sealed class Transaction(IDbConnection connection, Fixture fixture, List<FlowHeader> headers, List<FlowLine> lines) : IDbTransaction
    {
        private bool _ended;
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <summary>模拟成功提交或提交阶段断连。</summary>
        public void Commit() { if (fixture.FailCommit) throw new IOException("模拟提交断连"); fixture.Commits++; _ended = true; }
        /// <summary>回滚恢复头行和版本。</summary>
        public void Rollback() { if (_ended) return; fixture.Headers = headers; fixture.Lines = lines; fixture.Rollbacks++; _ended = true; }
        public void Dispose() { if (!_ended) Rollback(); }
    }
}

