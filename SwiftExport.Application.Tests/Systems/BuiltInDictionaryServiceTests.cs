using System.Data;
using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Application.Services.Systems.Dictionaries.Data;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证真实明细服务的首次覆盖、统一保护和整批回滚；仓储与事务均为无网络替身。</summary>
[TestClass]
public sealed class BuiltInDictionaryServiceTests
{
    /// <summary>各读取入口一致且不写入，分页总数包含代码默认项。</summary>
    [TestMethod]
    public async Task EmptyStorage_ReadsAndPaginatesWithoutWriting()
    {
        var fixture = new Fixture();
        var all = await fixture.Service.GetAllAsync();
        Assert.IsTrue(all.Success);
        Assert.AreEqual(2, all.Data.Count);
        var page = await fixture.Service.GetPagedAsync(new() { Keyword = "不限流", PageSize = 1 });
        Assert.AreEqual(1, page.Data.TotalCount);
        var byCode = await fixture.Service.GetByCodeAsync(" FlowLimiterSpecification ");
        var byId = await fixture.Service.GetByIdAsync(byCode.Data.Single().Uid);
        Assert.AreEqual(page.Data.Items.Single().Uid, byId.Data.Uid);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Session.Begins);
    }

    /// <summary>单条首次保存身份不变，陈旧零版本拒绝覆盖，后续正版本保存正常递增。</summary>
    [TestMethod]
    public async Task FirstOverride_KeepsIdentityAndRejectsStaleVersion()
    {
        var fixture = new Fixture();
        var item = (await fixture.Service.GetAllAsync()).Data.First();
        item.DataNameZh = "我的默认项";
        var first = await fixture.Service.UpdateAsync(Update(item));
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(item.Uid, first.Data.Uid);
        Assert.AreEqual(0, first.Data.OldVersion);
        Assert.AreEqual(1, first.Data.NewVersion);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await fixture.Service.UpdateAsync(Update(item))).Error.Code);
        var saved = (await fixture.Service.GetByIdAsync(item.Uid)).Data;
        Assert.AreEqual("我的默认项", saved.DataNameZh);
        Assert.AreEqual(1, saved.Version);
        saved.DataNameZh = "第二次覆盖";
        Assert.IsTrue((await fixture.Service.UpdateAsync(Update(saved))).Success);
        Assert.AreEqual(2, fixture.Store.Single().Version);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>旧 Guid、创建审计和真实版本必须保留，客户端字段不能改变服务器身份。</summary>
    [TestMethod]
    public async Task LegacyOverride_PreservesGuidAndServerFields()
    {
        var fixture = new Fixture();
        var definition = BuiltInDictionaryCatalog.Data[0];
        var original = new SysDictData
        {
            Uid = Guid.NewGuid(), Version = 3, DictCode = definition.DictCode, DataNameEn = definition.DataNameEn,
            DataNameZh = "旧名称", IsEnabled = true, CreatedBy = "original", CreatedDate = new DateTime(2020, 1, 2)
        };
        fixture.Store.Add(original);
        var dto = (await fixture.Service.GetByIdAsync(original.Uid)).Data;
        dto.CreatedBy = "forged";
        dto.CreatedDate = DateTime.Now;
        dto.IsSystem = false;
        dto.DataNameZh = "新名称";
        var request = Update(dto);
        dto.Uid = Guid.NewGuid();
        Assert.IsTrue((await fixture.Service.UpdateAsync(request)).Success);
        Assert.AreEqual(original.Uid, fixture.Store.Single().Uid);
        Assert.AreEqual("original", fixture.Store.Single().CreatedBy);
        Assert.AreEqual(new DateTime(2020, 1, 2), fixture.Store.Single().CreatedDate);
        Assert.IsTrue(fixture.Store.Single().IsSystem);
    }

    /// <summary>单条新增、修改、删除均执行保护；普通零版本及伪造内置标记不能绕过身份。</summary>
    [TestMethod]
    public async Task SingleRoutes_RejectProtectedChangesAndForgedIdentity()
    {
        var fixture = new Fixture();
        var item = (await fixture.Service.GetAllAsync()).Data.First();
        Assert.IsFalse((await fixture.Service.AddAsync(new() { Data = item })).Success);
        item.IsEnabled = false;
        Assert.IsFalse((await fixture.Service.UpdateAsync(Update(item))).Success);
        item.IsEnabled = true;
        item.DataNameEn = "changed";
        Assert.IsFalse((await fixture.Service.UpdateAsync(Update(item))).Success);
        Assert.IsFalse((await fixture.Service.DeleteAsync(new() { Uid = item.Uid, Version = 1 })).Success);
        var custom = new SysDictDataDto { Uid = Guid.NewGuid(), Version = 0, DictCode = "Unit", DataNameEn = "Pcs", DataNameZh = "个", IsEnabled = true, IsBuiltIn = true };
        Assert.IsFalse((await fixture.Service.UpdateAsync(Update(custom))).Success);
        Assert.AreEqual(0, fixture.Writes);
    }

    /// <summary>只有代码类型也能新增用户建议或单位；不创建类型记录，不授予客户端伪造的系统标记。</summary>
    [TestMethod]
    [DataRow("Unit")]
    [DataRow("IncotermLocation")]
    [DataRow("IncotermSortRemark")]
    public async Task CustomItem_CanBeAddedUnderUnpersistedBuiltInType(string dictCode)
    {
        var fixture = new Fixture();
        var result = await fixture.Service.AddAsync(new()
        {
            Data = new() { DictCode = dictCode, DataNameEn = "Pcs", DataNameZh = "个", IsEnabled = true, IsSystem = true, IsBuiltIn = true }
        });
        Assert.IsTrue(result.Success, result.Message);
        var data = (await fixture.Service.GetByCodeAsync(dictCode)).Data.Single();
        Assert.IsFalse(data.IsSystem || data.IsBuiltIn);
        Assert.AreEqual("Pcs", data.DataNameEn);
        Assert.AreEqual(1, fixture.Store.Count);
    }

    /// <summary>异常已删除内置记录读时不恢复，显式保存才按旧身份和版本恢复。</summary>
    [TestMethod]
    public async Task DeletedBuiltIn_IsRestoredOnlyByExplicitVersionedSave()
    {
        var fixture = new Fixture();
        var definition = BuiltInDictionaryCatalog.Data[1];
        fixture.Store.Add(new() { Uid = Guid.NewGuid(), Version = 5, DictCode = definition.DictCode,
            DataNameEn = definition.DataNameEn, DataNameZh = "异常", IsDeleted = true, IsEnabled = false });
        var item = (await fixture.Service.GetByCodeAsync(definition.DictCode)).Data.Single();
        Assert.AreEqual("不限流", item.DataNameZh);
        Assert.IsTrue(fixture.Store.Single().IsDeleted);
        Assert.IsTrue((await fixture.Service.UpdateAsync(Update(item))).Success);
        Assert.IsFalse(fixture.Store.Single().IsDeleted);
        Assert.IsTrue(fixture.Store.Single().IsEnabled);
        Assert.AreEqual(6, fixture.Store.Single().Version);
    }

    /// <summary>混合批次的后续仓储失败必须撤销先前新增，并清理本用例事务。</summary>
    [TestMethod]
    public async Task FailedBatch_RollsBackEarlierWriteAndReturnsNoPartialSuccess()
    {
        var fixture = new Fixture { FailWriteNumber = 2 };
        var item = (await fixture.Service.GetAllAsync()).Data.First();
        var result = await fixture.Service.BatchSaveAsync(new()
        {
            CreateItems = [new() { Data = new() { DictCode = "Unit", DataNameEn = "Pcs", DataNameZh = "个", IsEnabled = true } }],
            UpdateItems = [Update(item)]
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Store.Count);
        Assert.AreEqual(1, fixture.Session.Rollbacks);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>另一类型占用默认 Guid 时所有入口都应拒绝，不产生指向其他记录的虚拟选项。</summary>
    [TestMethod]
    public async Task OccupiedDefaultIdentity_IsRejectedByCodeAndAllReads()
    {
        var fixture = new Fixture();
        var definition = BuiltInDictionaryCatalog.Data[0];
        fixture.Store.Add(new() { Uid = definition.Uid, DictCode = "Unit", DataNameEn = "Pcs", DataNameZh = "个", IsEnabled = true });
        Assert.IsFalse((await fixture.Service.GetAllAsync()).Success);
        Assert.IsFalse((await fixture.Service.GetByCodeAsync(definition.DictCode)).Success);
        Assert.AreEqual(0, fixture.Writes);
    }

    /// <summary>被主型号或组关联引用的模板不得删除，拒绝路径没有业务写入。</summary>
    [TestMethod]
    public async Task ReferencedProfile_DeleteShouldRejectWithoutWrites()
    {
        var fixture = new Fixture { ProfileReferences = 2 };
        var profile = Profile();
        fixture.Store.Add(profile);
        var result = await fixture.Service.DeleteAsync(new() { Uid = profile.Uid, Version = profile.Version, IsSoftDelete = true });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes);
        Assert.IsFalse(fixture.Store.Single().IsDeleted);
    }

    /// <summary>停用和移出模板类型均会使业务引用失效，必须原子阻断。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReferencedProfile_DisableOrMoveShouldReject(bool move)
    {
        var fixture = new Fixture { ProfileReferences = 3 };
        var profile = Profile();
        fixture.Store.Add(profile);
        var dto = profile.Adapt<SysDictDataDto>();
        if (move) dto.DictCode = DictCodes.Unit;
        else dto.IsEnabled = false;
        var result = await fixture.Service.UpdateAsync(Update(dto));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes);
        Assert.IsTrue(fixture.Store.Single().IsEnabled);
        Assert.AreEqual(DictCodes.ProductAttributeProfile, fixture.Store.Single().DictCode);
    }

    /// <summary>引用按 Uid 保存，修改模板名称和英文代码仍保留既有身份并允许保存。</summary>
    [TestMethod]
    public async Task ReferencedProfile_RenameShouldPreserveUid()
    {
        var fixture = new Fixture { ProfileReferences = 3 };
        var profile = Profile();
        fixture.Store.Add(profile);
        var dto = profile.Adapt<SysDictDataDto>();
        dto.DataNameEn = "KitchenFaucet";
        dto.DataNameZh = "厨房龙头";
        var result = await fixture.Service.UpdateAsync(Update(dto));
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(profile.Uid, fixture.Store.Single().Uid);
        Assert.AreEqual("厨房龙头", fixture.Store.Single().DataNameZh);
        Assert.AreEqual(2, fixture.Store.Single().Version);
    }

    /// <summary>系列身份已被主型号使用时阻止删除、停用、移出类型；改名保持原身份。</summary>
    [TestMethod]
    [DataRow("delete")]
    [DataRow("disable")]
    [DataRow("move")]
    [DataRow("rename")]
    public async Task ReferencedSeries_ProtectsIdentityAndAllowsRename(string operation)
    {
        var fixture = new Fixture { SeriesReferences = 2 };
        var series = Profile();
        series.DictCode = DictCodes.ProductSeries;
        fixture.Store.Add(series);
        var dto = series.Adapt<SysDictDataDto>();
        var result = operation == "delete"
            ? await fixture.Service.DeleteAsync(new() { Uid = series.Uid, Version = series.Version, IsSoftDelete = true })
            : await fixture.Service.UpdateAsync(Update(Prepare()));
        Assert.AreEqual(operation == "rename", result.Success, result.Message);
        Assert.AreEqual(series.Uid, fixture.Store.Single().Uid);
        if (operation == "rename")
        {
            Assert.AreEqual("改名后的系列", fixture.Store.Single().DataNameZh);
            Assert.AreEqual(2, fixture.Store.Single().Version);
        }
        else
        {
            Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
            Assert.AreEqual(0, fixture.Writes);
            Assert.IsTrue(fixture.Store.Single().IsEnabled);
            Assert.AreEqual(DictCodes.ProductSeries, fixture.Store.Single().DictCode);
        }
        return;
        SysDictDataDto Prepare()
        {
            if (operation == "disable") dto.IsEnabled = false;
            if (operation == "move") dto.DictCode = DictCodes.Unit;
            if (operation == "rename") dto.DataNameZh = "改名后的系列";
            return dto;
        }
    }

    /// <summary>创建普通用户模板，类型内置并不将具体明细标记为系统项。</summary>
    private static SysDictData Profile() => new()
    {
        Uid = Guid.NewGuid(), Version = 1, DictCode = DictCodes.ProductAttributeProfile,
        DataNameEn = "BasinFaucet", DataNameZh = "面盆龙头", IsEnabled = true
    };

    private static UpdateItemRequest<SysDictDataDto> Update(SysDictDataDto item) =>
        new() { Uid = item.Uid, Version = item.Version, Data = item, ClientKey = item.Uid.ToString("N") };

    /// <summary>观察服务真正调用的仓储方法，模拟身份和版本持久化以及原子回滚。</summary>
    private sealed class Fixture
    {
        internal List<SysDictData> Store { get; } = [];
        internal int Writes { get; private set; }
        internal int FailWriteNumber { get; set; }
        internal int ProfileReferences { get; set; }
        internal int SeriesReferences { get; set; }
        internal Session Session { get; }
        internal SysDictDataService Service { get; }

        internal Fixture()
        {
            Session = new(Store);
            var data = DispatchProxy.Create<ISysDictDataRepository, Port>();
            ((Port)(object)data).Handle = (method, args) =>
            {
                if (method.Name == nameof(ISysDictDataRepository.CountProductSeriesReferencesAsync))
                    return Task.FromResult(SeriesReferences);
                if (method.Name == "GetForBuiltInMergeAsync")
                    return Task.FromResult<IReadOnlyList<SysDictData>>(Store.Where(item => args[0] is not string code || item.DictCode == code)
                        .Select(item => item.Adapt<SysDictData>()).ToArray());
                if (method.Name == "GetByIdsForUpdateAsync")
                    return Task.FromResult<IReadOnlyList<SysDictData>>(Store.Where(item => ((IEnumerable<Guid>)args[0]!).Contains(item.Uid))
                        .Select(item => item.Adapt<SysDictData>()).ToArray());
                if (method.Name is "AddAsync" or "SaveBuiltInOverrideAsync" or "UpdateAsync")
                {
                    var entity = ((SysDictData)args[0]!).Adapt<SysDictData>();
                    var expected = method.Name == "SaveBuiltInOverrideAsync" ? (int)args[1]! : entity.Version;
                    Writes++;
                    if (Writes == FailWriteNumber)
                        return Task.FromResult(RepositoryWriteResult.Fail(entity.Uid, expected, ServiceErrorCodes.Unexpected, "模拟写入失败。"));
                    var old = Store.SingleOrDefault(item => item.Uid == entity.Uid);
                    if (method.Name == "AddAsync")
                    {
                        entity.Uid = Guid.NewGuid(); entity.Version = 1; Store.Add(entity);
                        return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, null, 1));
                    }
                    if (expected == 0 && old is not null || expected > 0 && (old is null || old.Version != expected))
                        return Task.FromResult(RepositoryWriteResult.Fail(entity.Uid, expected, ServiceErrorCodes.VersionConflict, "版本冲突。"));
                    entity.Version = expected + 1;
                    entity.IsDeleted = false;
                    if (old is not null) Store.Remove(old);
                    Store.Add(entity);
                    return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, expected == 0 ? 0 : entity.Version, entity.Version));
                }
                throw new NotSupportedException(method.Name);
            };
            var types = DispatchProxy.Create<ISysDictTypeRepository, Port>();
            ((Port)(object)types).Handle = (_, _) => Task.FromResult<IReadOnlyList<SysDictType>>([]);
            var attributes = DispatchProxy.Create<IProductAttributeRepository, Port>();
            ((Port)(object)attributes).Handle = (method, _) => method.Name == nameof(IProductAttributeRepository.CountProfileReferencesAsync)
                ? Task.FromResult(ProfileReferences) : throw new NotSupportedException(method.Name);
            Service = new(data, types, new(Session, NullLoggerFactory.Instance), attributes);
        }
    }

    /// <summary>把接口调用交给定向测试处理器；不连接数据库且未声明的方法立即失败。</summary>
    public class Port : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Handle { get; set; } = null!;
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handle(targetMethod!, args ?? []);
    }

    /// <summary>测试会话只记录事务边界，回滚时恢复开始前的数据快照。</summary>
    private sealed class Session(List<SysDictData> store) : IDbSession, IDbConnection, IDbTransaction
    {
        private SysDictData[] _baseline = [];
        internal int Begins { get; private set; }
        internal int Rollbacks { get; private set; }
        public IDbConnection Connection => this;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "dictionary-test";
        public ConnectionState State => ConnectionState.Open;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <inheritdoc />
        public IDbTransaction BeginTransaction() { Begins++; _baseline = store.Select(item => item.Adapt<SysDictData>()).ToArray(); return this; }
        /// <inheritdoc />
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
        /// <inheritdoc />
        public void Rollback() { Rollbacks++; store.Clear(); store.AddRange(_baseline); }
        /// <inheritdoc />
        public void Commit() { }
        /// <inheritdoc />
        public void Dispose() { }
        /// <inheritdoc />
        public void Open() { }
        /// <inheritdoc />
        public void Close() { }
        /// <inheritdoc />
        public void ChangeDatabase(string name) => throw new NotSupportedException();
        /// <inheritdoc />
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }
}
