using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;

using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际Dapper私有投影及参数验收；内存ADO只证明读取/映射协议，商业资格和物化执行由真PG另验。</summary>
[TestClass]
public sealed class PostgreSqlOutboundItemsQueryMigrationTests
{
    /// <summary>第五方法补齐原接口，私有投影不扩Contracts，不修改四查询方法的签名。</summary>
    [TestMethod]
    public void CompleteInterface()
    { Assert.IsTrue(typeof(SwiftExport.Core.Interfaces.Inventory.Outbounds.IOutboundQueryRepository).IsAssignableFrom(typeof(PostgreSqlOutboundQueryRepository))); Assert.HasCount(5,typeof(SwiftExport.Core.Interfaces.Inventory.Outbounds.IOutboundQueryRepository).GetMethods()); }

    /// <summary>一个命令/结果集借当前可空事务与token；每行SQL总数不是页大小，十字段原值和NULL不改写。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task OwnershipAndFullMapping(bool transaction)
    {
        using var f=new Fixture(transaction); using var c=new CancellationTokenSource(); var q=new OutboundItemSearchRequest{WarehouseSiteId=Guid.NewGuid(),PageIndex=3,PageSize=2};
        var t=Rows();var a=Guid.NewGuid();var b=Guid.NewGuid();t.Rows.Add(17,true,a,q.WarehouseSiteId,b," SKU ",DBNull.Value,"说明",DBNull.Value," PCS ",2.125m,DBNull.Value);
        t.Rows.Add(17,true,Guid.NewGuid(),q.WarehouseSiteId,Guid.NewGuid(),"A","关联",DBNull.Value,"图片","KG",1m,"默认仓");f.Connection.Results=[t];
        var d=await f.Repository.SearchItemsAsync(q,c.Token);Assert.AreEqual(17,d.TotalCount);Assert.HasCount(2,d.Items);Assert.AreEqual(3,d.PageIndex);Assert.AreEqual(2,d.PageSize);
        var r=d.Items[0];Assert.AreEqual(a,r.Uid);Assert.AreEqual(b,r.ProductId);Assert.AreEqual(q.WarehouseSiteId,r.WarehouseSiteId);Assert.AreEqual(" SKU ",r.Sku);Assert.IsNull(r.LinkSku);Assert.AreEqual("说明",r.Description);Assert.IsNull(r.ImagePath);Assert.AreEqual(" PCS ",r.Unit);Assert.AreEqual(2.125m,r.OnHandQty);Assert.IsNull(r.DefaultWarehouseName);
        Assert.AreEqual("A",d.Items[1].Sku,"不在C#重排页序。");Assert.IsNull(d.Items[1].Description);Assert.AreEqual("默认仓",d.Items[1].DefaultWarehouseName);
        var cmd=f.Connection.Commands.Single();Assert.AreSame(f.Session.Transaction,cmd.Transaction);Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(1,f.Connection.NextResults);Assert.AreEqual(1,f.Connection.Disposals);Assert.IsTrue(f.Connection.ReaderClosed);Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>SQL false占位不成为假DTO，即使其其他值意外非NULL也只认HasItem；总数0和超末页非0完整保持。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(29)]
    public async Task EmptyPageKeepsCount(int total)
    {
        using var f=new Fixture(false);var t=Rows();t.Rows.Add(total,false,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"不应组装",DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,1m,DBNull.Value);f.Connection.Results=[t];
        var d=await f.Repository.SearchItemsAsync(new(){PageIndex=8,PageSize=20},default);Assert.AreEqual(total,d.TotalCount);Assert.HasCount(0,d.Items);Assert.AreEqual(8,d.PageIndex);Assert.AreEqual(20,d.PageSize);Assert.AreEqual(1,f.Connection.Disposals);
    }

    /// <summary>真实HasItem行的Guid.Empty和零余额不能被客户端占位判定吞掉；资格只由SQL决定。</summary>
    [TestMethod]
    public async Task EmptyGuidIsRealItem()
    {
        using var f=new Fixture(false);var t=Rows();t.Rows.Add(1,true,Guid.Empty,Guid.Empty,Guid.Empty,"",DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,0m,DBNull.Value);f.Connection.Results=[t];
        var d=await f.Repository.SearchItemsAsync(new(),default);Assert.HasCount(1,d.Items);Assert.AreEqual(Guid.Empty,d.Items[0].Uid);Assert.AreEqual(0m,d.Items[0].OnHandQty);Assert.IsNull(d.Items[0].Unit);
    }

    /// <summary>局部null与空列表都绑定空uuid[]，非空保留重复及Empty，五参数类型明确不依赖驱动猜测。</summary>
    [TestMethod]
    [DataRow("null")] [DataRow("empty")] [DataRow("one")] [DataRow("duplicates")] [DataRow("empty-guid")]
    public async Task ExclusionParameters(string mode)
    {
        using var f=new Fixture(false);f.Empty();var id=Guid.NewGuid();var ids=mode switch{"null"=>null,"empty"=>new List<Guid>(),"one"=>[id],"duplicates"=>new List<Guid>{id,id,Guid.Empty},_=>[Guid.Empty]};var q=new OutboundItemSearchRequest{WarehouseSiteId=Guid.NewGuid(),ExcludedProductIds=ids!,PageIndex=4,PageSize=20};
        await f.Repository.SearchItemsAsync(q,default);var cmd=f.Connection.Commands.Single();CollectionAssert.AreEqual(ids?.ToArray()??[],(Guid[])cmd.Values["ExcludedProductIds"]!);Assert.AreSame(ids,q.ExcludedProductIds);
        Assert.HasCount(5,cmd.Values);Assert.AreEqual(q.WarehouseSiteId,cmd.Values["WarehouseSiteId"]);Assert.AreEqual(60L,cmd.Values["Skip"]);Assert.AreEqual(20,cmd.Values["Take"]);Assert.AreEqual(DBNull.Value,cmd.Values["Keyword"]);
        Assert.AreEqual(NpgsqlDbType.Uuid,cmd.Types["WarehouseSiteId"]);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,cmd.Types["ExcludedProductIds"]);Assert.AreEqual(NpgsqlDbType.Text,cmd.Types["Keyword"]);Assert.AreEqual(NpgsqlDbType.Bigint,cmd.Types["Skip"]);Assert.AreEqual(NpgsqlDbType.Integer,cmd.Types["Take"]);
    }

    /// <summary>分页原int减1和long乘保持，内存结果不模拟负LIMIT异常；真实PG另验技术拒绝。</summary>
    [TestMethod]
    [DataRow(0,20)] [DataRow(-1,20)] [DataRow(1,0)] [DataRow(2,-1)] [DataRow(int.MinValue,20)] [DataRow(int.MaxValue,int.MaxValue)]
    public async Task OriginalPageArithmetic(int index,int size)
    {
        using var f=new Fixture(false);f.Empty();var q=new OutboundItemSearchRequest{PageIndex=index,PageSize=size};var d=await f.Repository.SearchItemsAsync(q,default);var cmd=f.Connection.Commands.Single();Assert.AreEqual((long)unchecked(index-1)*size,cmd.Values["Skip"]);Assert.AreEqual(size,cmd.Values["Take"]);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);
        StringAssert.Contains(cmd.Sql,"LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip");
    }

    /// <summary>沿原.NET Trim与字面LIKE转义，不对来源列/数组增加预处理。</summary>
    [TestMethod]
    [DataRow(null,null)] [DataRow(" \t\u00a0",null)] [DataRow("  ~%_[  ","%~~~%~_~[%")] [DataRow("中文","%中文%")]
    public async Task KeywordBinding(string? input,string? expected)
    {using var f=new Fixture(false);f.Empty();await f.Repository.SearchItemsAsync(new(){Keyword=input},default);Assert.AreEqual((object?)expected??DBNull.Value,f.Connection.Commands.Single().Values["Keyword"]);}

    /// <summary>COUNT本应必有行，缺结果不静默返回0；真实行必需GUID/余额NULL暴露映射错误，reader仍完整释放。</summary>
    [TestMethod]
    [DataRow("no-row")] [DataRow("uid")] [DataRow("warehouse")] [DataRow("product")] [DataRow("balance")]
    public async Task InvalidPrivateProjectionIsTechnicalFailure(string field)
    {
        using var f=new Fixture(true);var t=Rows();if(field!="no-row"){var values=new object[]{1,true,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"S",DBNull.Value,DBNull.Value,DBNull.Value,"PCS",1m,DBNull.Value};values[field switch{"uid"=>2,"warehouse"=>3,"product"=>4,_=>10}]=DBNull.Value;t.Rows.Add(values);}f.Connection.Results=[t];
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.SearchItemsAsync(new(),default));Assert.AreEqual(1,f.Connection.Disposals);Assert.IsNotNull(f.Session.Transaction);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>原null请求成员访问仍在SQL前失败，不新增业务Validation。</summary>
    [TestMethod]
    public async Task NullRequestNoSql()
    {using var f=new Fixture(false);await Assert.ThrowsAsync<NullReferenceException>(()=>f.Repository.SearchItemsAsync(null!,default));Assert.HasCount(0,f.Connection.Commands);}

    /// <summary>命令和读取异常原样传播，父事务不被仓储回滚；当前reader按已打开阶段释放。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task FailureOwnership(bool reader)
    {
        using var f=new Fixture(true);f.Empty();var error=new InvalidOperationException("明确注入");if(reader)f.Connection.ResultAction=_=>throw error;else f.Connection.CommandAction=_=>throw error;
        Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.SearchItemsAsync(new(),default)));Assert.AreEqual(reader?1:0,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>预取消、命令派发和读取阶段保留原令牌与释放，不把空候选当无SQL早返。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)]
    public async Task CancellationOwnership(int stage)
    {
        using var f=new Fixture(true);f.Empty();using var c=new CancellationTokenSource();if(stage<0)c.Cancel();else if(stage==0)f.Connection.CommandAction=_=>c.Cancel();else f.Connection.ResultAction=_=>c.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchItemsAsync(new(),c.Token));Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(stage<0?0:1,f.Connection.Disposals);Assert.IsNotNull(f.Session.Transaction);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>静态只核对SQL边界，结合实际Dapper测试；不能把文本检查称为商业资格/物化真PG证据。</summary>
    [TestMethod]
    public async Task SqlContract()
    {
        using var f=new Fixture(false);f.Empty();await f.Repository.SearchItemsAsync(new(),default);var sql=f.Connection.Commands.Single().Sql;
        StringAssert.Contains(sql,"WITH candidates AS MATERIALIZED");StringAssert.Contains(sql,"COUNT(*)::integer");StringAssert.Contains(sql,"FROM totals t LEFT JOIN page pg ON TRUE");StringAssert.Contains(sql,"COALESCE(pg.\"HasItem\",FALSE)");
        StringAssert.Contains(sql,"b.\"product_id\"<>ALL(@ExcludedProductIds)");StringAssert.Contains(sql,"NULLIF(btrim(p.\"unit\",' '),'') IS NOT NULL");StringAssert.Contains(sql,"AND dw.\"is_deleted\"=FALSE");Assert.IsFalse(sql.Contains("dw.\"is_active\""));
        StringAssert.Contains(sql,"rtrim(c.\"Sku\",' ')");StringAssert.Contains(sql,"rtrim(pg.\"Sku\",' ')");Assert.IsFalse(sql.Contains("FOR UPDATE"));Assert.IsFalse(sql.Contains("CREATE TEMP"));Assert.IsFalse(sql.Contains("ILIKE"));
        foreach(var alias in new[]{"Uid","WarehouseSiteId","ProductId","Sku","LinkSku","Description","ImagePath","Unit","OnHandQty","DefaultWarehouseName"})StringAssert.Contains(sql,"pg.\""+alias+"\"");
        Assert.AreEqual(1,System.Text.RegularExpressions.Regex.Matches(sql,"FROM \\\"public\\\".\\\"inventory_balance\\\"").Count);
    }

    /// <summary>列类型对应私有行，不解释查询SQL。</summary>
    private static DataTable Rows(){var t=new DataTable();foreach(var (name,type) in new (string,Type)[]{("TotalCount",typeof(int)),("HasItem",typeof(bool)),("Uid",typeof(Guid)),("WarehouseSiteId",typeof(Guid)),("ProductId",typeof(Guid)),("Sku",typeof(string)),("LinkSku",typeof(string)),("Description",typeof(string)),("ImagePath",typeof(string)),("Unit",typeof(string)),("OnHandQty",typeof(decimal)),("DefaultWarehouseName",typeof(string))})t.Columns.Add(name,type);return t;}

    /// <summary>独立内存ADO会话，没有网络；真实Dapper实例化生产私有行。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly RecordingConnection Connection=new();public readonly DbSession Session;public readonly PostgreSqlOutboundQueryRepository Repository;
        /// <summary>父事务只用于验证所有权，不模拟PG隔离。</summary>
        public Fixture(bool transaction){Session=new(new Factory(Connection));if(transaction)Session.Transaction=Connection.BeginTransaction();var user=new Identity();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
        /// <summary>SQL占位行的身份和文本全部NULL，HasItem仍false。</summary>
        public void Empty(){var t=Rows();t.Rows.Add(0,false,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value,DBNull.Value);Connection.Results=[t];}
        /// <summary>释放本人离线会话，不执行任何清库。</summary>
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>只创建指定内存连接，沿PG异常分类不接触网络。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>离线只读用户，仓储不调用打戳。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"出库选品离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public bool ReaderClosed;
        public int Begins,Commits,Rollbacks,NextResults,Disposals;
        public int[] ReadCounts=new int[2];
        public CancellationToken LastToken;
        public Action<int>? CommandAction,ResultAction;
        public readonly List<(string Sql,Dictionary<string,object?> Values,Dictionary<string,NpgsqlDbType> Types,DbTransaction? Transaction)> Commands=[];
        [AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"none";public override string ServerVersion=>"test";public override ConnectionState State=>ConnectionState.Open;
        public override void Open(){}public override void Close(){}public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level){Begins++;return new RecordingTransaction(this);}
        protected override DbCommand CreateDbCommand()=>new RecordingCommand(this);
        /// <summary>只记录调用者事务动作。</summary>
        private sealed class RecordingTransaction(RecordingConnection owner):DbTransaction
        {protected override DbConnection DbConnection=>owner;public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;public override void Commit()=>owner.Commits++;public override void Rollback()=>owner.Rollbacks++;}
        /// <summary>借用真实Npgsql参数容器，并以数据表返回单个结果。</summary>
        private sealed class RecordingCommand(RecordingConnection owner):DbCommand
        {
            private readonly NpgsqlCommand container=new();
            [AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}
            public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}
            protected override DbParameterCollection DbParameterCollection=>container.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel(){}public override void Prepare(){}public override int ExecuteNonQuery()=>throw new NotSupportedException();
            /// <summary>执行前记录真实命令及独立参数类型。</summary>
            private void Record(){owner.CommandAction?.Invoke(owner.Commands.Count);owner.Commands.Add((CommandText,container.Parameters.Cast<NpgsqlParameter>().ToDictionary(x=>x.ParameterName,x=>(object?)x.Value),container.Parameters.Cast<NpgsqlParameter>().ToDictionary(x=>x.ParameterName,x=>x.NpgsqlDbType),DbTransaction));}
            public override object ExecuteScalar(){Record();throw new NotSupportedException();}
            public override Task<object?> ExecuteScalarAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult<object?>(ExecuteScalar());}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){Record();return new RecordingReader(owner,new DataTableReader(owner.Results));}
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(ExecuteDbDataReader(behavior));}
            protected override void Dispose(bool disposing){if(disposing)container.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class RecordingReader(RecordingConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            /// <summary>观察当前结果读取次数，原行和列仍由内部reader提供。</summary>
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            /// <summary>观察结果推进边界，不主动排空未读结果或改变驱动返回。</summary>
            public override bool NextResult(){owner.NextResults++;if(phase<1)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
            public override Task<bool> ReadAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(Read());}
            public override Task<bool> NextResultAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(NextResult());}
            public override void Close(){inner.Close();owner.ReaderClosed=true;}
            protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;owner.Disposals++;inner.Dispose();owner.ReaderClosed=true;}base.Dispose(disposing);}
            public override int FieldCount=>inner.FieldCount;public override bool HasRows=>inner.HasRows;public override bool IsClosed=>inner.IsClosed;public override int RecordsAffected=>inner.RecordsAffected;public override int Depth=>inner.Depth;
            public override object this[int ordinal]=>inner[ordinal];public override object this[string name]=>inner[name];
            public override string GetName(int ordinal)=>inner.GetName(ordinal);public override string GetDataTypeName(int ordinal)=>inner.GetDataTypeName(ordinal);public override Type GetFieldType(int ordinal)=>inner.GetFieldType(ordinal);
            public override object GetValue(int ordinal)=>inner.GetValue(ordinal);public override int GetValues(object[] values)=>inner.GetValues(values);public override int GetOrdinal(string name)=>inner.GetOrdinal(name);public override bool IsDBNull(int ordinal)=>inner.IsDBNull(ordinal);
            public override bool GetBoolean(int ordinal)=>inner.GetBoolean(ordinal);public override byte GetByte(int ordinal)=>inner.GetByte(ordinal);public override long GetBytes(int ordinal,long offset,byte[]? buffer,int bufferOffset,int length)=>inner.GetBytes(ordinal,offset,buffer,bufferOffset,length);
            public override char GetChar(int ordinal)=>inner.GetChar(ordinal);public override long GetChars(int ordinal,long offset,char[]? buffer,int bufferOffset,int length)=>inner.GetChars(ordinal,offset,buffer,bufferOffset,length);
            public override Guid GetGuid(int ordinal)=>inner.GetGuid(ordinal);public override short GetInt16(int ordinal)=>inner.GetInt16(ordinal);public override int GetInt32(int ordinal)=>inner.GetInt32(ordinal);public override long GetInt64(int ordinal)=>inner.GetInt64(ordinal);
            public override float GetFloat(int ordinal)=>inner.GetFloat(ordinal);public override double GetDouble(int ordinal)=>inner.GetDouble(ordinal);public override string GetString(int ordinal)=>inner.GetString(ordinal);public override decimal GetDecimal(int ordinal)=>inner.GetDecimal(ordinal);public override DateTime GetDateTime(int ordinal)=>inner.GetDateTime(ordinal);
            public override IEnumerator GetEnumerator()=>((IEnumerable)inner).GetEnumerator();public override DataTable? GetSchemaTable()=>inner.GetSchemaTable();
        }
    }
}
