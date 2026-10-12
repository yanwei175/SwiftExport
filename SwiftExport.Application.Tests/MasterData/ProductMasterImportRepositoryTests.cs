using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Tests.MasterData;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>执行真实 Dapper 映射及参数绑定，验证无匹配行不会继承实体自动生成的标识；不连接 PostgreSQL。</summary>
[TestClass]
public sealed class ProductMasterImportRepositoryTests
{
    /// <summary>LEFT JOIN 全空实体列必须映射为新增，已有身份及版本保持不变，输入只出现在参数中。</summary>
    [TestMethod]
    public async Task Match_NullJoinedUidMeansCreateAndInputIsParameterized()
    {
        var uid = Guid.NewGuid();
        using var table = new DataTable();
        table.Columns.Add("SourceRowNumber", typeof(int)); table.Columns.Add("DuplicateCode", typeof(bool));
        table.Columns.Add("Uid", typeof(Guid)); table.Columns.Add("Version", typeof(int)); table.Columns.Add("MasterSku", typeof(string));
        table.Rows.Add(2, false, DBNull.Value, DBNull.Value, DBNull.Value);
        table.Rows.Add(3, false, uid, 4, "EXISTING");
        using var connection = new ReadConnection(table);
        var fixture = new ProductMasterImportExportTests.Fixture();
        using var session = new DbSession(new Factory(connection));
        var repository = new PostgreSqlProductMasterRepository(new RepositoryBaseContext(session,
            NullLoggerFactory.Instance, new AuditStampService(fixture), fixture));
        var matches = await repository.MatchForImportAsync([(2, "N' OR TRUE--"), (3, "EXISTING")]);
        Assert.IsNull(matches[0].Existing);
        Assert.AreEqual(uid, matches[1].Existing!.Uid);
        Assert.AreEqual(4, matches[1].Existing!.Version);
        Assert.IsFalse(connection.Sql.Contains("N' OR TRUE--", StringComparison.Ordinal));
        StringAssert.Contains((string)connection.Parameters["Inputs"]!, "OR TRUE");
        StringAssert.Contains(connection.Sql, "COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(connection.Sql, "pm.is_deleted=FALSE");
    }

    /// <summary>导出执行固定有界查询，共用分页筛选及转义参数，分类数组不拼接到 SQL。</summary>
    [TestMethod]
    public async Task Export_BindsFiltersAndReadsLimitPlusOne()
    {
        using var table = new DataTable(); table.Columns.Add("Uid", typeof(Guid));
        using var connection = new ReadConnection(table);
        var fixture = new ProductMasterImportExportTests.Fixture();
        using var session = new DbSession(new Factory(connection));
        var repository = new PostgreSqlProductMasterRepository(new RepositoryBaseContext(session,
            NullLoggerFactory.Instance, new AuditStampService(fixture), fixture));
        var category = Guid.NewGuid();
        await repository.GetForExportAsync(new ProductMasterSearchRequest { Keyword = "a%'", CategoryIds = [category], IncludeUncategorized = true }, 10000);
        Assert.AreEqual(10001, connection.Parameters["Limit"]);
        Assert.AreEqual("%a~%'%", connection.Parameters["Keyword"]);
        CollectionAssert.AreEqual(new[] { category }, (Guid[])connection.Parameters["Uids"]!);
        StringAssert.Contains(connection.Sql, "pm.category_id=ANY(@Uids) OR pm.category_id=@EmptyGuid");
        StringAssert.Contains(connection.Sql, "LIMIT @Limit");
        Assert.IsFalse(connection.Sql.Contains("OFFSET", StringComparison.Ordinal));
    }

    /// <summary>仅向会话提供本次内存连接。</summary>
    private sealed class Factory(ReadConnection connection) : IDbConnectionFactory
    {
        /// <inheritdoc />
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>只返回受控 reader 并记录 SQL；不具备任何网络或写入能力。</summary>
    private sealed class ReadConnection(DataTable table) : DbConnection
    {
        internal string Sql = string.Empty;
        internal Dictionary<string, object?> Parameters = [];
        /// <inheritdoc />
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        /// <inheritdoc />
        public override string Database => "offline";
        /// <inheritdoc />
        public override string DataSource => "none";
        /// <inheritdoc />
        public override string ServerVersion => "memory";
        /// <inheritdoc />
        public override ConnectionState State => ConnectionState.Open;
        /// <inheritdoc />
        public override void Open() { }
        /// <inheritdoc />
        public override void Close() { }
        /// <inheritdoc />
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <inheritdoc />
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        /// <inheritdoc />
        protected override DbCommand CreateDbCommand() => new ReadCommand(this, table);
    }

    /// <summary>使用实际 Npgsql 参数容器与内存 reader，确保测试经过 Dapper 执行边界。</summary>
    private sealed class ReadCommand(ReadConnection owner, DataTable table) : DbCommand
    {
        private readonly NpgsqlCommand _container = new();
        /// <inheritdoc />
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        /// <inheritdoc />
        public override int CommandTimeout { get; set; }
        /// <inheritdoc />
        public override CommandType CommandType { get; set; }
        /// <inheritdoc />
        public override bool DesignTimeVisible { get; set; }
        /// <inheritdoc />
        public override UpdateRowSource UpdatedRowSource { get; set; }
        /// <inheritdoc />
        protected override DbConnection? DbConnection { get; set; } = owner;
        /// <inheritdoc />
        protected override DbTransaction? DbTransaction { get; set; }
        /// <inheritdoc />
        protected override DbParameterCollection DbParameterCollection => _container.Parameters;
        /// <inheritdoc />
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        /// <inheritdoc />
        public override void Cancel() { }
        /// <inheritdoc />
        public override void Prepare() => throw new NotSupportedException();
        /// <inheritdoc />
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <inheritdoc />
        public override object ExecuteScalar() => throw new NotSupportedException();
        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            owner.Sql = CommandText;
            owner.Parameters = _container.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
            return table.CreateDataReader();
        }
        /// <inheritdoc />
        protected override void Dispose(bool disposing) { if (disposing) _container.Dispose(); base.Dispose(disposing); }
    }
}

