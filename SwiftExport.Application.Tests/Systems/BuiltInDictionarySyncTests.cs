using System.Data;
using System.Reflection;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Systems.Dictionaries;
using SwiftExport.Application.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.WebApi.Controllers.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证显式同步的事务、身份、幂等和权限边界；仓储替身不连接运行数据库。</summary>
[TestClass]
public sealed class BuiltInDictionarySyncTests
{
    /// <summary>空库存入完整代码目录，第二次同步不写记录，也不改变版本和创建审计。</summary>
    [TestMethod]
    public async Task EmptyStorage_PersistsCatalogAndRepeatedSyncDoesNotWrite()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.ExpectedToken = cancellation.Token;
        var first = await fixture.Service.SyncAsync(cancellation.Token);
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(BuiltInDictionaryCatalog.Types.Count, first.Data.AddedTypeCount);
        Assert.AreEqual(BuiltInDictionaryCatalog.Data.Count, first.Data.AddedDataCount);
        CollectionAssert.AreEquivalent(BuiltInDictionaryCatalog.Types.Select(item => item.Uid).ToArray(), fixture.Types.Select(item => item.Uid).ToArray());
        CollectionAssert.AreEquivalent(BuiltInDictionaryCatalog.Data.Select(item => item.Uid).ToArray(), fixture.Data.Select(item => item.Uid).ToArray());
        Assert.IsTrue(fixture.Types.All(item => item.Version == 1 && item.IsSystem && !item.IsDeleted));
        Assert.IsTrue(fixture.Data.All(item => item.Version == 1 && item.IsSystem && item.IsEnabled && !item.IsDeleted));
        CollectionAssert.AreEqual(new[] { "mutex", "types", "data" }, fixture.Events.Take(3).ToArray());
        Assert.IsTrue(fixture.Events.LastIndexOf("write-type") < fixture.Events.IndexOf("write-data"));
        var writes = fixture.Writes;
        var second = await fixture.Service.SyncAsync(cancellation.Token);
        Assert.IsTrue(second.Success, second.Message);
        Assert.AreEqual(0, second.Data.AddedTypeCount + second.Data.UpdatedTypeCount + second.Data.AddedDataCount + second.Data.UpdatedDataCount);
        Assert.AreEqual(writes, fixture.Writes);
        Assert.IsTrue(fixture.Types.All(item => item.Version == 1 && item.CreatedBy == "同步测试"));
        Assert.IsTrue(fixture.Data.All(item => item.Version == 1 && item.CreatedBy == "同步测试"));
        Assert.AreEqual(2, fixture.Commits);
        Assert.AreEqual(2, fixture.Disposals);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>同编码旧身份优先，恢复有效标记时仍保留旧名称、排序、分组、层级、样式及创建审计；目录外记录不变。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingRecords_KeepIdentityAndConfiguration(bool deleted)
    {
        var fixture = new Fixture();
        var type = new SysDictType { Uid = Guid.NewGuid(), Version = 7, DictCode = " unit ", DictName = "旧单位名称",
            IsDeleted = deleted, ParentId = Guid.NewGuid(), GroupCode = "Custom", GroupName = "旧分组", SortOrder = 88,
            Remark = "旧备注", CreatedBy = "旧创建者", CreatedDate = new DateTime(2020, 1, 1) };
        var definition = BuiltInDictionaryCatalog.Data[0];
        var data = new SysDictData { Uid = Guid.NewGuid(), Version = 5, DictCode = definition.DictCode,
            DataNameEn = definition.DataNameEn, DataNameZh = "用户名称", IsDeleted = deleted, IsEnabled = false,
            SortOrder = 99, CssClass = "custom", Remark = "用户备注", CreatedBy = "旧创建者", CreatedDate = type.CreatedDate };
        fixture.Types.Add(type.Adapt<SysDictType>());
        fixture.Data.Add(data.Adapt<SysDictData>());
        var custom = new SysDictData { Uid = Guid.NewGuid(), Version = 4, DictCode = "Custom", DataNameEn = "External", IsDeleted = true };
        fixture.Data.Add(custom.Adapt<SysDictData>());
        var result = await fixture.Service.SyncAsync();
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.UpdatedTypeCount);
        Assert.AreEqual(1, result.Data.UpdatedDataCount);
        var savedType = fixture.Types.Single(item => item.Uid == type.Uid);
        Assert.AreEqual(type.DictCode, savedType.DictCode);
        Assert.AreEqual(type.DictName, savedType.DictName);
        Assert.AreEqual(type.GroupCode, savedType.GroupCode);
        Assert.AreEqual(type.GroupName, savedType.GroupName);
        Assert.AreEqual(type.ParentId, savedType.ParentId);
        Assert.AreEqual(type.SortOrder, savedType.SortOrder);
        Assert.AreEqual(type.Remark, savedType.Remark);
        Assert.AreEqual(type.CreatedDate, savedType.CreatedDate);
        Assert.AreEqual(8, savedType.Version);
        Assert.IsTrue(savedType.IsSystem && !savedType.IsDeleted);
        var savedData = fixture.Data.Single(item => item.Uid == data.Uid);
        Assert.AreEqual(data.DataNameZh, savedData.DataNameZh);
        Assert.AreEqual(data.Remark, savedData.Remark);
        Assert.AreEqual(data.SortOrder, savedData.SortOrder);
        Assert.AreEqual(data.CssClass, savedData.CssClass);
        Assert.AreEqual(data.CreatedBy, savedData.CreatedBy);
        Assert.AreEqual(data.CreatedDate, savedData.CreatedDate);
        Assert.AreEqual(6, savedData.Version);
        Assert.IsTrue(savedData.IsSystem && savedData.IsEnabled && !savedData.IsDeleted);
        Assert.IsFalse(fixture.Types.Any(item => item.Uid == BuiltInDictionaryCatalog.FindType("Unit")!.Uid));
        Assert.IsFalse(fixture.Data.Any(item => item.Uid == definition.Uid));
        Assert.AreEqual(4, fixture.Data.Single(item => item.Uid == custom.Uid).Version);
        Assert.IsTrue(fixture.Data.Single(item => item.Uid == custom.Uid).IsDeleted);
    }

    /// <summary>重复编码和固定身份被其他编码占用均在所有写入前拒绝，不擅自合并历史身份。</summary>
    [TestMethod]
    [DataRow("type-duplicate")]
    [DataRow("data-duplicate")]
    [DataRow("type-identity")]
    [DataRow("data-identity")]
    public async Task InvalidIdentity_RejectsBeforeAnyWrite(string mode)
    {
        var fixture = new Fixture();
        var type = BuiltInDictionaryCatalog.Types[0];
        var data = BuiltInDictionaryCatalog.Data[0];
        if (mode == "type-duplicate")
            for (var i = 0; i < 2; i++) fixture.Types.Add(new() { Uid = Guid.NewGuid(), Version = 1, DictCode = type.DictCode, DictName = "重复" });
        if (mode == "data-duplicate")
            for (var i = 0; i < 2; i++) fixture.Data.Add(new() { Uid = Guid.NewGuid(), Version = 1, DictCode = data.DictCode, DataNameEn = data.DataNameEn });
        if (mode == "type-identity") fixture.Types.Add(new() { Uid = type.Uid, Version = 1, DictCode = "Custom", DictName = "占用" });
        if (mode == "data-identity") fixture.Data.Add(new() { Uid = data.Uid, Version = 1, DictCode = "Custom", DataNameEn = "Other" });
        var result = await fixture.Service.SyncAsync();
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Commits);
        Assert.AreEqual(1, fixture.Rollbacks);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>在类型已写入后模拟明细冲突、异常或取消；整组回滚且释放事务，再次调用可以成功补齐。</summary>
    [TestMethod]
    [DataRow("conflict")]
    [DataRow("exception")]
    [DataRow("cancel")]
    public async Task FailedDataWrite_RollsBackAllChangesAndAllowsRetry(string mode)
    {
        var fixture = new Fixture { Failure = mode };
        using var cancellation = new CancellationTokenSource();
        fixture.ExpectedToken = cancellation.Token;
        fixture.Cancel = cancellation;
        if (mode == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.SyncAsync(cancellation.Token));
        else
        {
            var result = await fixture.Service.SyncAsync(cancellation.Token);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(mode == "conflict" ? ServiceErrorCodes.ConcurrencyConflict : ServiceErrorCodes.Unexpected, result.Error.Code);
            Assert.IsFalse(result.Message.Contains("SQL 细节", StringComparison.Ordinal));
        }
        Assert.AreEqual(0, fixture.Types.Count + fixture.Data.Count);
        Assert.AreEqual(0, fixture.Commits);
        Assert.AreEqual(1, fixture.Rollbacks);
        Assert.AreEqual(1, fixture.Disposals);
        Assert.IsNull(fixture.Session.Transaction);
        fixture.Failure = null;
        fixture.ExpectedToken = default;
        Assert.IsTrue((await fixture.Service.SyncAsync()).Success);
    }

    /// <summary>未登录和没有编辑权限时，连同步锁及数据库读取都不能执行。</summary>
    [TestMethod]
    [DataRow(false, true, ServiceErrorCodes.Unauthenticated)]
    [DataRow(true, false, ServiceErrorCodes.Forbidden)]
    public async Task Unauthorized_DoesNotOpenTransactionOrRead(bool authenticated, bool allowed, string code)
    {
        var fixture = new Fixture { Authenticated = authenticated, Allowed = allowed };
        var result = await fixture.Service.SyncAsync();
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(0, fixture.Begins);
        Assert.HasCount(0, fixture.Events);
    }

    /// <summary>调用方已有事务时明确拒绝；不提交、回滚或释放该事务。</summary>
    [TestMethod]
    public async Task ExistingTransaction_RemainsOwnedByCaller()
    {
        var fixture = new Fixture();
        var transaction = Proxy<IDbTransaction>((_, _) => throw new AssertFailedException("不能操作调用方事务。"));
        fixture.Session.Transaction = transaction;
        var result = await fixture.Service.SyncAsync();
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreSame(transaction, fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Begins);
        Assert.HasCount(0, fixture.Events);
    }

    /// <summary>Controller 与 Refit 保持显式 POST、相同响应和取消参数，Controller 仅转发同步用例。</summary>
    [TestMethod]
    public async Task ApiContract_UsesExplicitPostAndForwardsCancellation()
    {
        var action = typeof(SysDictTypeController).GetMethod(nameof(SysDictTypeController.SyncBuiltInAsync))!;
        var api = typeof(ISysDictTypeApi).GetMethod(action.Name)!;
        Assert.AreEqual("sync-built-in", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.AreEqual("/sync-built-in", api.GetCustomAttribute<PostAttribute>()!.Path);
        Assert.IsNotNull(action.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.AreEqual(api.ReturnType, action.ReturnType);
        Assert.AreEqual(typeof(CancellationToken), action.GetParameters().Single().ParameterType);
        using var cancellation = new CancellationTokenSource();
        var expected = ServiceResult<BuiltInDictionarySyncResponse>.Ok(new());
        var sync = Proxy<IBuiltInDictionarySyncService>((_, args) =>
        {
            Assert.AreEqual(cancellation.Token, args[0]);
            return Task.FromResult(expected);
        });
        var types = Proxy<ISysDictTypeService>((_, _) => throw new AssertFailedException("同步不能转入普通保存。"));
        Assert.AreSame(expected, await new SysDictTypeController(types, sync).SyncBuiltInAsync(cancellation.Token));
    }

    /// <summary>用可观察端口执行真实同步服务，回滚恢复两个表的快照，不复制目录或身份合并算法。</summary>
    private sealed class Fixture
    {
        internal List<SysDictType> Types { get; } = [];
        internal List<SysDictData> Data { get; } = [];
        internal List<string> Events { get; } = [];
        internal bool Authenticated = true, Allowed = true;
        internal int Begins, Commits, Rollbacks, Disposals, Writes;
        internal string? Failure;
        internal CancellationToken ExpectedToken;
        internal CancellationTokenSource? Cancel;
        internal IDbSession Session { get; }
        internal BuiltInDictionarySyncService Service { get; }

        internal Fixture()
        {
            SysDictType[] typeSnapshot = [];
            SysDictData[] dataSnapshot = [];
            IDbTransaction? current = null;
            var transaction = Proxy<IDbTransaction>((method, _) =>
            {
                if (method.Name == "Commit") Commits++;
                else if (method.Name == "Dispose") Disposals++;
                else if (method.Name == "Rollback")
                {
                    Rollbacks++;
                    Types.Clear(); Types.AddRange(typeSnapshot);
                    Data.Clear(); Data.AddRange(dataSnapshot);
                }
                else throw new NotSupportedException(method.Name);
                return null;
            });
            var connection = Proxy<IDbConnection>((method, args) =>
            {
                Assert.AreEqual("BeginTransaction", method.Name);
                Assert.AreEqual(IsolationLevel.ReadCommitted, args[0]);
                Begins++;
                typeSnapshot = Types.Select(item => item.Adapt<SysDictType>()).ToArray();
                dataSnapshot = Data.Select(item => item.Adapt<SysDictData>()).ToArray();
                return transaction;
            });
            Session = Proxy<IDbSession>((method, args) => method.Name switch
            {
                "get_Connection" => connection, "get_Transaction" => current,
                "set_Transaction" => current = (IDbTransaction?)args[0],
                _ => throw new NotSupportedException(method.Name)
            });
            var types = Proxy<ISysDictTypeRepository>((method, args) =>
            {
                Assert.AreEqual(ExpectedToken, args.Last());
                Assert.AreSame(transaction, current);
                if (method.Name == nameof(ISysDictTypeRepository.LockBuiltInSyncAsync))
                { Events.Add("mutex"); return Task.CompletedTask; }
                if (method.Name == nameof(ISysDictTypeRepository.GetAllIncludingDeletedAsync))
                {
                    Assert.AreEqual(true, args[0]); Events.Add("types");
                    return Task.FromResult<IReadOnlyList<SysDictType>>(Types.Select(item => item.Adapt<SysDictType>()).ToArray());
                }
                Assert.AreEqual(nameof(ISysDictTypeRepository.SaveBuiltInAsync), method.Name);
                Events.Add("write-type"); Writes++;
                var entity = ((SysDictType)args[0]!).Adapt<SysDictType>();
                var version = (int)args[1]!;
                entity.Version = version + 1; entity.IsSystem = true; entity.IsDeleted = false;
                entity.CreatedBy ??= "同步测试";
                Types.RemoveAll(item => item.Uid == entity.Uid); Types.Add(entity);
                return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, version, entity.Version));
            });
            var data = Proxy<ISysDictDataRepository>((method, args) =>
            {
                Assert.AreEqual(ExpectedToken, args.Last());
                Assert.AreSame(transaction, current);
                if (method.Name == nameof(ISysDictDataRepository.GetForBuiltInMergeAsync))
                {
                    Assert.IsNull(args[0]); Assert.AreEqual(true, args[1]); Events.Add("data");
                    return Task.FromResult<IReadOnlyList<SysDictData>>(Data.Select(item => item.Adapt<SysDictData>()).ToArray());
                }
                Assert.AreEqual(nameof(ISysDictDataRepository.SaveBuiltInOverrideAsync), method.Name);
                Events.Add("write-data"); Writes++;
                var entity = ((SysDictData)args[0]!).Adapt<SysDictData>();
                var version = (int)args[1]!;
                if (Failure == "exception") throw new InvalidOperationException("SQL 细节不可返回。");
                if (Failure == "cancel") { Cancel!.Cancel(); ExpectedToken.ThrowIfCancellationRequested(); }
                if (Failure == "conflict") return Task.FromResult(RepositoryWriteResult.Fail(entity.Uid, version, ServiceErrorCodes.ConcurrencyConflict, "已变化。"));
                entity.Version = version + 1; entity.IsSystem = true; entity.IsDeleted = false; entity.IsEnabled = true;
                entity.CreatedBy ??= "同步测试";
                Data.RemoveAll(item => item.Uid == entity.Uid); Data.Add(entity);
                return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, version, entity.Version));
            });
            var user = Proxy<ICurrentUser>((method, _) => method.Name switch
            {
                "get_IsAuthenticated" => Authenticated, "get_UserId" => (Guid?)Guid.Parse("b681f13a-8f0d-42dd-a7d4-85bc88204670"),
                _ => throw new NotSupportedException(method.Name)
            });
            var permissions = Proxy<IPermissionEvaluationService>((method, args) =>
            {
                Assert.AreEqual(nameof(IPermissionEvaluationService.HasPermissionAsync), method.Name);
                Assert.AreEqual(PermissionKeys.SystemDictionaryEdit, args[1]);
                Assert.AreEqual(ExpectedToken, args[2]);
                return Task.FromResult(Allowed);
            });
            Service = new(types, data, user, permissions, new(Session, NullLoggerFactory.Instance));
        }
    }

    /// <summary>复用定向接口代理，未声明调用立即失败，防止意外访问外部资源。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handle) where T : class
    {
        var proxy = DispatchProxy.Create<T, BuiltInDictionaryServiceTests.Port>();
        ((BuiltInDictionaryServiceTests.Port)(object)proxy).Handle = handle;
        return proxy;
    }
}
