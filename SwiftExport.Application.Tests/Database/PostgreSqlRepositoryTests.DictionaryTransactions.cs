using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.Dictionaries.Types;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用现有字典服务验证PG通用写入的事务编排；仓储读取与写入均走真实Dapper，数据由ADO替身提供，不冒充实库并发验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>服务按父子顺序保存，并把逻辑父标识转换为通用仓储实际生成的Uid。</summary>
    [TestMethod]
    public async Task DictionaryBatch_CommitsSharedTransactionAndResolvesParent()
    {
        using var fixture = new Fixture();
        var service = DictionaryService(fixture);
        var result = await service.BatchSaveAsync(DictionaryCreates());
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.IsNull(fixture.Session.Transaction);
        var writes = fixture.Connection.Commands.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(2, writes);
        Assert.AreEqual("CustomParent", writes[0].Values["DictCode"]);
        Assert.AreEqual(writes[0].Values["Uid"], writes[1].Values["ParentId"]);
        Assert.IsNotNull(writes[0].Transaction);
        Assert.AreSame(writes[0].Transaction, writes[1].Transaction);
    }

    /// <summary>第二次写入失败时调用回滚而非提交，并释放事务；不声称替身能验证数据库数据回滚。</summary>
    [TestMethod]
    public async Task DictionaryBatch_FailedSecondWriteRollsBackAndClearsSession()
    {
        using var fixture = new Fixture();
        var service = DictionaryService(fixture);
        var writes = 0;
        fixture.Connection.ExecuteWrite = () => ++writes == 2 ? 0 : 1;
        var result = await service.BatchSaveAsync(DictionaryCreates());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(2, writes);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(1, fixture.Connection.Rollbacks);
        Assert.IsNull(fixture.Session.Transaction);
        Assert.AreEqual(2, fixture.Connection.TransactionDisposals); // 夹具初始事务及业务事务各一次。
    }

    /// <summary>业务校验拒绝重复编码时，事务回滚且没有任何INSERT命令。</summary>
    [TestMethod]
    public async Task DictionaryBatch_DuplicateCodeRejectsBeforeWrite()
    {
        using var fixture = new Fixture();
        var service = DictionaryService(fixture);
        var request = DictionaryCreates();
        request.CreateItems[0].Data.DictCode = "customparent";
        var result = await service.BatchSaveAsync(request);
        Assert.IsFalse(result.Success);
        Assert.IsFalse(fixture.Connection.Commands.Any(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)));
        Assert.AreEqual(1, fixture.Connection.Commands.Count);
        Assert.AreEqual(1, fixture.Connection.Rollbacks);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>为真实应用服务提供共享会话和PG通用写入，读取与写入使用本批PG专用仓储。</summary>
    private static SysDictTypeService DictionaryService(Fixture fixture)
    {
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var user = new User();
        var context = new RepositoryBaseContext(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        return new SysDictTypeService(new PostgreSqlSysDictTypeRepository(context), new PostgreSqlSysDictDataRepository(context),
            new ServiceBaseContext(fixture.Session, NullLoggerFactory.Instance));
    }

    /// <summary>故意以子在前的顺序输入，验证服务重新排序与逻辑标识重映射。</summary>
    private static SysDictTypeBatchSaveRequest DictionaryCreates()
    {
        var parent = Guid.NewGuid();
        return new SysDictTypeBatchSaveRequest { CreateItems =
        [
            new() { Data = new SysDictTypeDto { Uid = Guid.NewGuid(), ParentId = parent, DictCode = "CustomChild", DictName = "子项" } },
            new() { Data = new SysDictTypeDto { Uid = parent, DictCode = "CustomParent", DictName = "父项" } }
        ] };
    }

}
