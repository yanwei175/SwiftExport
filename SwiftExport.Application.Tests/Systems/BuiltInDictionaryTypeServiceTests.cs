using System.Data;
using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Application.Services.Systems.Dictionaries.Types;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证真实类型服务的内置读取、只读保护及虚拟父节点，接口替身不连接数据库。</summary>
[TestClass]
public sealed class BuiltInDictionaryTypeServiceTests
{
    /// <summary>空类型存储的 all、树、身份与分页一致，读取不启动事务或产生写入。</summary>
    [TestMethod]
    public async Task EmptyStorage_ReadsSameBuiltInTypesAcrossAllRoutes()
    {
        var fixture = new Fixture();
        var all = await fixture.Service.GetAllAsync();
        var tree = await fixture.Service.GetDictTypeGroupTreeAsync();
        var page = await fixture.Service.GetPagedAsync(new() { PageIndex = 2, PageSize = 3 });
        var filtered = await fixture.Service.GetPagedAsync(new()
        {
            BatchKeywords = [" Unit ", "FlowTestFunction"], PageSize = 1
        });
        var byId = await fixture.Service.GetByIdAsync(all.Data.First().Uid);

        Assert.IsTrue(all.Success && tree.Success && page.Success && filtered.Success && byId.Success);
        Assert.AreEqual(11, all.Data.Count);
        Assert.IsTrue(all.Data.All(type => type.IsBuiltIn && type.IsSystem && type.Version == 0));
        CollectionAssert.AreEquivalent(all.Data.Select(type => type.Uid).ToArray(),
            tree.Data.SelectMany(group => Flatten(group.Children)).Select(type => type.Uid).ToArray());
        Assert.AreEqual(11, page.Data.TotalCount);
        Assert.HasCount(3, page.Data.Items);
        Assert.AreEqual(2, filtered.Data.TotalCount);
        Assert.HasCount(1, filtered.Data.Items);
        Assert.AreEqual(all.Data.First().Uid, byId.Data.Uid);
        Assert.AreEqual(0, fixture.Begins);
        Assert.HasCount(0, fixture.Writes);
    }

    /// <summary>软删除内置类型仍保留旧 Guid 和版本，读取采用默认名称且不恢复数据库标记。</summary>
    [TestMethod]
    public async Task DeletedBuiltInType_PreservesLegacyIdentityWithoutWriting()
    {
        var fixture = new Fixture();
        var definition = BuiltInDictionaryCatalog.FindType("Unit")!;
        var legacy = new SysDictType { Uid = Guid.NewGuid(), Version = 5, DictCode = definition.DictCode,
            DictName = "被删除的名称", IsDeleted = true };
        fixture.Store.Add(legacy);

        var item = (await fixture.Service.GetByIdAsync(legacy.Uid)).Data;
        Assert.AreEqual(legacy.Uid, item.Uid);
        Assert.AreEqual(5, item.Version);
        Assert.AreEqual(definition.DictName, item.DictName);
        Assert.IsTrue(item.IsBuiltIn);
        Assert.IsTrue(legacy.IsDeleted);
        Assert.IsFalse((await fixture.Service.GetByIdAsync(definition.Uid)).Success,
            "默认 Guid 不能在已有不同身份时被静默映射到旧记录。");
        Assert.HasCount(0, fixture.Writes);
    }

    /// <summary>单条新增、改名和删除经过真实批量校验，即使旧 IsSystem 标记错误也不能修改内置类型。</summary>
    [TestMethod]
    public async Task SingleCrudRoutes_RejectBuiltInTypeChanges()
    {
        var fixture = new Fixture();
        var definition = BuiltInDictionaryCatalog.FindType("CustomerType")!;
        fixture.Store.Add(new() { Uid = Guid.NewGuid(), Version = 2, DictCode = definition.DictCode,
            DictName = definition.DictName, IsSystem = false });
        var item = (await fixture.Service.GetAllAsync()).Data.Single(type => type.DictCode == definition.DictCode);

        Assert.IsFalse((await fixture.Service.AddAsync(new() { Data = item })).Success);
        item.DictName = "不允许的改名";
        Assert.IsFalse((await fixture.Service.UpdateAsync(new() { Uid = item.Uid, Version = item.Version, Data = item })).Success);
        Assert.IsFalse((await fixture.Service.DeleteAsync(new() { Uid = item.Uid, Version = item.Version })).Success);
        Assert.HasCount(0, fixture.Writes);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>自定义类型可以引用尚未持久化的内置父节点，保存只写用户新增项。</summary>
    [TestMethod]
    public async Task CustomType_CanUseUnpersistedBuiltInParent()
    {
        var fixture = new Fixture();
        var parent = BuiltInDictionaryCatalog.FindType("Unit")!;
        var result = await fixture.Service.AddAsync(new()
        {
            Data = new() { DictCode = "CustomUnitChild", DictName = "自定义下级", ParentId = parent.Uid,
                IsSystem = true, IsBuiltIn = true }
        });

        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(parent.Uid, fixture.Writes[0].ParentId);
        Assert.IsFalse(fixture.Writes[0].IsSystem, "客户端伪造标记不能授予系统身份。");
        Assert.IsFalse(fixture.Store.Any(type => type.DictCode == parent.DictCode));
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>遍历实际服务返回的树节点，用于比较不同读取入口的身份口径。</summary>
    private static IEnumerable<DictTypeTreeDto> Flatten(IEnumerable<DictTypeTreeDto> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    /// <summary>复用明细测试的窄接口代理，记录类型服务使用的事务与写入，不复制合并算法。</summary>
    private sealed class Fixture
    {
        internal List<SysDictType> Store { get; } = [];
        internal List<SysDictType> Writes { get; } = [];
        internal int Begins { get; private set; }
        internal IDbSession Session { get; }
        internal SysDictTypeService Service { get; }

        internal Fixture()
        {
            IDbTransaction? current = null;
            var transaction = Proxy<IDbTransaction>((_, _) => null);
            var connection = Proxy<IDbConnection>((method, _) =>
            {
                if (method.Name != nameof(IDbConnection.BeginTransaction)) throw new NotSupportedException(method.Name);
                Begins++;
                return transaction;
            });
            Session = Proxy<IDbSession>((method, args) => method.Name switch
            {
                "get_Connection" => connection,
                "get_Transaction" => current,
                "set_Transaction" => current = (IDbTransaction?)args[0],
                _ => throw new NotSupportedException(method.Name)
            });
            var types = Proxy<ISysDictTypeRepository>((method, args) =>
            {
                if (method.Name == nameof(ISysDictTypeRepository.GetAllIncludingDeletedAsync))
                    return Task.FromResult<IReadOnlyList<SysDictType>>(Store.Select(type => type.Adapt<SysDictType>()).ToArray());
                if (method.Name != nameof(ISysDictTypeRepository.AddAsync)) throw new NotSupportedException(method.Name);
                var entity = ((SysDictType)args[0]!).Adapt<SysDictType>();
                entity.Uid = Guid.NewGuid(); entity.Version = 1;
                Store.Add(entity); Writes.Add(entity);
                return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, null, 1));
            });
            var data = Proxy<ISysDictDataRepository>((method, _) =>
                method.Name == nameof(ISysDictDataRepository.GetDictCodesWithDataAsync)
                    ? Task.FromResult<IReadOnlyList<string>>([])
                    : throw new NotSupportedException(method.Name));
            Service = new(types, data, new(Session, NullLoggerFactory.Instance));
        }
    }

    /// <summary>复用已存在的代理类型，只为本测试声明实际调用的接口行为。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handle) where T : class
    {
        var proxy = DispatchProxy.Create<T, BuiltInDictionaryServiceTests.Port>();
        ((BuiltInDictionaryServiceTests.Port)(object)proxy).Handle = handle;
        return proxy;
    }
}
