using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证字典专用仓储的Dapper查询、投影和事务；SQL引擎比较语义由初始化脚本自检及后续实库测试负责。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>计数与分页共用筛选参数，文本数组不展开，投影别名正确还原DTO。</summary>
    [TestMethod]
    public async Task DictionaryPage_MapsDtoAndBindsStablePaging()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("DictCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("DictName", typeof(string));
        fixture.Connection.Rows.Rows.Add("Unit", "单位");
        var repository = new PostgreSqlSysDictTypeRepository(DictionaryContext(fixture));
        var result = await repository.GetPagedAsync(new SysDictTypeSearchRequest
        { PageIndex = 0, PageSize = 500, Keyword = "  单位\\%  ", BatchKeywords = [" unit ", "UNIT"] });
        Assert.AreEqual(1, result.PageIndex);
        Assert.AreEqual(200, result.PageSize);
        Assert.AreEqual("单位", result.Items.Single().DictName);
        Assert.AreEqual(2, fixture.Connection.Commands.Count);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.AreEqual("%单位\\%%", command.Values["Keyword"]);
            CollectionAssert.AreEqual(new[] { "unit" }, (string[])command.Values["BatchKeywords"]!);
            StringAssert.Contains(command.Sql, "rtrim(dict_code, ' ') = ANY(@BatchKeywords)");
            StringAssert.Contains(command.Sql, "LIKE @Keyword ESCAPE ''");
        }
        StringAssert.Contains(fixture.Connection.Text, "\"dict_name\" AS \"DictName\"");
        StringAssert.Contains(fixture.Connection.Text, "group_code NULLS FIRST");
        StringAssert.Contains(fixture.Connection.Text, "LIMIT @PageSize OFFSET @Offset");
    }

    /// <summary>明细分页与按编码读取采用相同尾空格等值规则，保留启用标记的读取契约。</summary>
    [TestMethod]
    public async Task DictionaryData_MapsProjectionAndCodeLookup()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("DataNameZh", typeof(string));
        fixture.Connection.Rows.Columns.Add("IsEnabled", typeof(bool));
        fixture.Connection.Rows.Rows.Add("个", false);
        var repository = new PostgreSqlSysDictDataRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new SysDictDataSearchRequest { BatchKeywords = ["Unit"] });
        Assert.AreEqual("个", page.Items.Single().DataNameZh);
        var rows = await repository.GetByCodeAsync(" Unit ");
        Assert.IsFalse(rows.Single().IsEnabled);
        Assert.AreEqual("Unit", fixture.Connection.Values["DictCode"]);
        StringAssert.Contains(fixture.Connection.Text, "rtrim(dict_code, ' ') = rtrim(@DictCode, ' ')");
    }

    /// <summary>维护先锁类型后锁明细，SQL按Uid取锁；明细去重在读取后执行，不生成非法DISTINCT FOR UPDATE。</summary>
    [TestMethod]
    public async Task DictionaryLocks_UseSharedTransactionAndOrderedRows()
    {
        using var fixture = new Fixture();
        var context = DictionaryContext(fixture);
        var types = new PostgreSqlSysDictTypeRepository(context);
        var data = new PostgreSqlSysDictDataRepository(context);
        await types.GetByDictCodesAsync([" Unit "], true);
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY uid FOR UPDATE");
        fixture.Connection.Rows.Columns.Add("DictCode", typeof(string));
        fixture.Connection.Rows.Rows.Add("Unit");
        fixture.Connection.Rows.Rows.Add("Unit");
        Assert.HasCount(1, await data.GetDictCodesWithDataAsync(["Unit"]));
        Assert.IsFalse(fixture.Connection.Text.Contains("SELECT DISTINCT", StringComparison.Ordinal));
        Assert.AreSame(fixture.Connection.Commands[0].Transaction, fixture.Connection.Commands[1].Transaction);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => types.GetAllForUpdateAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.GetDictCodesWithDataAsync(["Unit"]));
    }

    /// <summary>同一参数包可用于同时存活的两个命令，参数实例不被驱动重复认领。</summary>
    [TestMethod]
    public void DictionaryParameters_CloneForEachCommand()
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", ["Unit"]);
        using var first = new NpgsqlCommand();
        using var second = new NpgsqlCommand();
        parameters.AddParameters(first, null!);
        parameters.AddParameters(second, null!);
        Assert.AreNotSame(first.Parameters[0], second.Parameters[0]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, second.Parameters[0].NpgsqlDbType);
    }

    /// <summary>字典唯一索引和查询统一去尾空格；生成的ICU自检在任何表创建前执行。</summary>
    [TestMethod]
    public void DictionarySchema_ScopesCollationAndNormalizesUniqueKey()
    {
        var sql = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        StringAssert.Contains(sql, "locale = 'und-u-ks-level2', deterministic = false");
        StringAssert.Contains(sql, "字典比较规则未通过源库对照样例");
        StringAssert.Contains(sql, "ON \"public\".\"sys_dict_type\" (rtrim(\"dict_code\", ' ')) WHERE \"is_deleted\" = FALSE");
        Assert.IsTrue(sql.IndexOf("字典比较规则未通过", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE", StringComparison.Ordinal));
        var dictionaryColumn = sql.Split('\n').Where(line => line.Contains("COLLATE public.erp_dictionary_ci", StringComparison.Ordinal) && line.TrimStart().StartsWith('"')).ToArray();
        Assert.IsTrue(dictionaryColumn.Length > 0);
        Assert.IsTrue(dictionaryColumn.All(line => line.Contains("varchar", StringComparison.Ordinal) || line.Contains("text", StringComparison.Ordinal)));
    }

    /// <summary>提供与夹具会话完全相同的仓储上下文。</summary>
    private static RepositoryBaseContext DictionaryContext(Fixture fixture)
    {
        var user = new User();
        return new(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }
}
