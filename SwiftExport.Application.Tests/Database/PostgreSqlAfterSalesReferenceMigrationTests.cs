using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>用真实 Dapper 与离线 ADO 核对引用切片的参数、映射、单行异常及父事务边界，不冒充 PG 资格证据。</summary>
[TestClass]
public sealed class PostgreSqlAfterSalesReferenceMigrationTests
{
    /// <summary>完整候选接口已交付，保留引用签名并确认分页入口可由现有接口解析。</summary>
    [TestMethod]
    public void CompleteLookupContract()
    {
        var type=typeof(PostgreSqlAfterSalesLookupRepository);
        Assert.IsTrue(typeof(IAfterSalesLookupRepository).IsAssignableFrom(type));
        CollectionAssert.AreEqual(new[]{"LookupAsync","ReferenceAsync"},type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Select(m=>m.Name).OrderBy(name=>name,StringComparer.Ordinal).ToArray());
        Assert.AreEqual(4,type.GetMethod("ReferenceAsync")!.GetParameters().Length);
    }

    /// <summary>精确 kind 白名单拒绝 NULL、大小写、空白和 SQL 文本，沿源异常且零命令。</summary>
    [TestMethod]
    [DataRow(null)] [DataRow("")] [DataRow("Customers")] [DataRow(" customers")] [DataRow("customers ")]
    [DataRow("unknown")] [DataRow("products;DROP TABLE product")]
    public async Task InvalidKind(string? kind)
    {
        using var f=new Fixture(false);
        // 源白名单的异常文本和参数名已封存；退出旧仓储后仍逐字保护原异常，不降低为仅检查异常类型。
        var expected=new ArgumentException("不支持的售后候选类型。","kind");
        var actual=await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Repository.ReferenceAsync(kind!,Guid.Empty,null,true));
        Assert.AreEqual(expected.ParamName,actual.ParamName);Assert.AreEqual(expected.Message,actual.Message);
        Assert.HasCount(0,f.Connection.Commands);
    }

    /// <summary>七分支均只发单个参数化 SELECT，Empty 身份仍查询，两个参数显式 uuid，NULL 客户不短路。</summary>
    [TestMethod]
    [DataRow("customers",false)] [DataRow("customers",true)]
    [DataRow("suppliers",false)] [DataRow("suppliers",true)]
    [DataRow("owners",false)] [DataRow("owners",true)]
    [DataRow("colors",false)] [DataRow("colors",true)]
    [DataRow("quality-categories",false)] [DataRow("quality-categories",true)]
    [DataRow("sales-order-lines",false)] [DataRow("sales-order-lines",true)]
    [DataRow("products",false)] [DataRow("products",true)]
    public async Task SevenKindsParameters(string kind,bool active)
    {
        using var f=new Fixture(true);f.Connection.Rows=Rows(kind=="products");
        Assert.IsNull(await f.Repository.ReferenceAsync(kind,Guid.Empty,null,active));
        var c=f.Connection.Commands.Single();
        Assert.AreEqual(Guid.Empty,c.Values["uid"]);Assert.AreEqual(DBNull.Value,c.Values["CustomerId"]);
        Assert.HasCount(2,c.Values);Assert.IsTrue(c.Types.Values.All(t=>t==NpgsqlDbType.Uuid));
        Assert.AreSame(f.Session.Transaction,c.Transaction);Assert.AreEqual(CancellationToken.None,f.Connection.Token);
        Assert.AreEqual(1,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
        Assert.IsTrue(f.Connection.Reader!.IsClosed);Assert.IsFalse(c.Sql.Contains("FOR UPDATE"));Assert.IsFalse(c.Sql.Contains(';'));
        StringAssert.Contains(c.Sql,"WHERE q.\"Uid\"=@uid");
        var tables=new Dictionary<string,string>{{"customers","customer"},{"suppliers","supplier"},{"owners","sys_user"},{"colors","product_color"},{"quality-categories","after_sales_quality_category"},{"sales-order-lines","sales_order_line"},{"products","product"}};
        StringAssert.Contains(c.Sql,"\"public\".\""+tables[kind]+"\"");
        Assert.IsFalse(c.Sql.Contains("IS NULL"),"原软删资格只接受 FALSE，不将 NULL 当未删。");
        if(kind=="colors") Assert.IsFalse(c.Sql.Contains("is_active"));
        else if(kind=="sales-order-lines") {Assert.IsFalse(c.Sql.Contains("document_status"));Assert.IsFalse(c.Sql.Contains("is_active"));StringAssert.Contains(c.Sql,"CONCAT(");StringAssert.Contains(c.Sql,"INNER JOIN");}
        else if(kind=="quality-categories") {Assert.AreEqual(active,c.Sql.Contains("\"is_enabled\"=TRUE"));StringAssert.Contains(c.Sql,"d.\"category_name\" AS \"Name\"");StringAssert.Contains(c.Sql,"NULLIF(btrim(d.\"category_code\"),'')");Assert.IsFalse(c.Sql.Contains("dict_code"));Assert.IsFalse(c.Sql.Contains("is_system"));}
        else if(kind=="products")
        {
            Assert.AreEqual(active,c.Sql.Contains("p.\"is_active\"=TRUE"));StringAssert.Contains(c.Sql,"r.\"is_active\"=TRUE");
            StringAssert.Contains(c.Sql,"LEFT JOIN LATERAL");StringAssert.Contains(c.Sql,"LIMIT 1");
            Assert.IsFalse(c.Sql.Contains("c.\"is_deleted\""));
            StringAssert.Contains(c.Sql,"get_byte(uuid_send(r.\"uid\"),10)");StringAssert.Contains(c.Sql,"get_byte(uuid_send(r.\"uid\"),0)");
        }
        else Assert.AreEqual(active,c.Sql.Contains("\"is_active\"=TRUE"));
    }

    /// <summary>完整九字段原值包含空串、尾空格与可空颜色，同一调用只读一结果。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ProductNineFields(bool parent)
    {
        using var f=new Fixture(parent);var uid=Guid.NewGuid();var customer=Guid.NewGuid();var color=Guid.NewGuid();
        f.Connection.Rows=Rows(true);f.Connection.Rows.Rows.Add(uid," SKU ",""," 客户 "," AQ "," PCS ",color,""," 规格 ");
        var d=await f.Repository.ReferenceAsync("products",uid,customer,false);
        Assert.IsNotNull(d);Assert.AreEqual(uid,d.Uid);Assert.AreEqual(" SKU ",d.Code);Assert.AreEqual("",d.Name);
        Assert.AreEqual(" 客户 ",d.CustomerSku);Assert.AreEqual(" AQ ",d.LinkSku);Assert.AreEqual(" PCS ",d.Unit);
        Assert.AreEqual(color,d.ColorId);Assert.AreEqual("",d.ColorName);Assert.AreEqual(" 规格 ",d.Specification);
        Assert.AreEqual(customer,f.Connection.Commands.Single().Values["CustomerId"]);
        Assert.AreEqual(parent?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);
    }

    /// <summary>用已封存的源九字段默认值检查真实 Dapper 的三列缺省及九列 NULL 映射，不修改全局空值设置。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task DefaultsAndNullsMatchSource(bool product)
    {
        using var f=new Fixture(false);var uid=Guid.NewGuid();var table=Rows(product);var row=table.NewRow();row["Uid"]=uid;row["Code"]="CODE";table.Rows.Add(row);f.Connection.Rows=table;
        var kind=product?"products":"customers";
        // 源 DTO 字符串初始化为 Empty，Dapper 默认不把 DBNull 写回这些字段；显式期望防止未来默认值漂移。
        var source=new AfterSalesLookupDto {Uid=uid,Code="CODE",Name="",CustomerSku="",LinkSku="",Unit="",ColorId=null,ColorName="",Specification=""};
        var target=await f.Repository.ReferenceAsync(kind,uid,null,false);
        Assert.IsNotNull(target);
        foreach(var property in typeof(AfterSalesLookupDto).GetProperties()) Assert.AreEqual(property.GetValue(source),property.GetValue(target),property.Name);
        Assert.IsNull(target.ColorId);
    }

    /// <summary>QuerySingleOrDefault 保留重复匹配异常而非任取一行，缺失普通身份仍返回 null。</summary>
    [TestMethod]
    public async Task DuplicateResultIsNotHidden()
    {
        using var f=new Fixture(false);var uid=Guid.NewGuid();f.Connection.Rows=Rows(false);
        Assert.IsNull(await f.Repository.ReferenceAsync("customers",uid,null,true));
        f.Connection.Rows.Rows.Add(uid,"A","A");f.Connection.Rows.Rows.Add(uid,"B","B");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.ReferenceAsync("customers",uid,null,true));
        Assert.IsTrue(f.Connection.Reader!.IsClosed);
    }

    /// <summary>驱动错误原样返回，不自建事务或包装成引用不存在。</summary>
    [TestMethod]
    public async Task DriverFailurePropagates()
    {
        using var f=new Fixture(false);var error=new InvalidOperationException("离线命令失败");f.Connection.Error=error;
        var actual=await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.ReferenceAsync("owners",Guid.NewGuid(),null,true));
        Assert.AreSame(error,actual);Assert.AreEqual(0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);
    }

    /// <summary>只创建三或九列 DataTable，SQL 资格不由假 ADO 解释。</summary>
    private static DataTable Rows(bool product)
    {
        var t=new DataTable();foreach(var name in product?new[]{"Uid","Code","Name","CustomerSku","LinkSku","Unit","ColorId","ColorName","Specification"}:new[]{"Uid","Code","Name"}) t.Columns.Add(name,name is "Uid" or "ColorId"?typeof(Guid):typeof(string));return t;
    }
    /// <summary>共享无网络会话及真实审计依赖，观察父事务动作。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly RecordingConnection Connection=new();public readonly DbSession Session;public readonly RepositoryBaseContext Context;public readonly PostgreSqlAfterSalesLookupRepository Repository;
        /// <summary>父事务来自夹具，仓储只借用。</summary>
        public Fixture(bool parent){Session=new(new Factory(Connection));if(parent)Session.Transaction=Connection.BeginTransaction();var user=new Identity();Context=new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user);Repository=new(Context);}
        /// <summary>释放本人的离线资源。</summary>
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>只返回指定假连接，不连接数据库。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {
        /// <summary>提供离线连接。</summary>
        public IDbConnection CreateConnection()=>connection;
    }
    /// <summary>无认证的只读测试身份，不创建真实用户。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"售后引用离线";public bool IsAuthenticated=>false;}
    /// <summary>记录真实 Dapper 命令参数、事务与 reader，结果仅模拟传输。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable Rows=PostgreSqlAfterSalesReferenceMigrationTests.Rows(false);public DbDataReader? Reader;public Exception? Error;public CancellationToken Token;public int Begins,Commits,Rollbacks;
        public readonly List<(string Sql,Dictionary<string,object?> Values,Dictionary<string,NpgsqlDbType> Types,DbTransaction? Transaction)> Commands=[];
        [AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"none";public override string ServerVersion=>"test";public override ConnectionState State=>ConnectionState.Open;
        public override void Open(){}public override void Close(){}public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level){Begins++;return new Transaction(this);}
        protected override DbCommand CreateDbCommand()=>new Command(this);
        /// <summary>只观察父事务控制，不模拟 PG 锁。</summary>
        private sealed class Transaction(RecordingConnection owner):DbTransaction
        {protected override DbConnection DbConnection=>owner;public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;public override void Commit()=>owner.Commits++;public override void Rollback()=>owner.Rollbacks++;}
        /// <summary>真实 Npgsql 参数容器配合 DataTableReader，避免假 DTO 直接返回绕过 Dapper。</summary>
        private sealed class Command(RecordingConnection owner):DbCommand
        {
            private readonly NpgsqlCommand container=new();
            [AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>container.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel(){}public override void Prepare(){}public override int ExecuteNonQuery()=>throw new NotSupportedException();public override object ExecuteScalar()=>throw new NotSupportedException();
            /// <summary>只记录实际生成的 SQL，不执行或解释资格条件。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                owner.Commands.Add((CommandText,container.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>(object?)p.Value),container.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.NpgsqlDbType),DbTransaction));
                if(owner.Error is not null)throw owner.Error;owner.Reader=owner.Rows.CreateDataReader();return owner.Reader;
            }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.Token=token;return Task.FromResult(ExecuteDbDataReader(behavior));}
            protected override void Dispose(bool disposing){if(disposing)container.Dispose();base.Dispose(disposing);}
        }
    }
}
