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
using SwiftExport.Core.Interfaces.Inventory.Outbounds;

using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>以实际Dapper内存ADO验证出库四查询协议/参数/DTO默认及异常；商业SQL和负LIMIT效果由真实PG另验。</summary>
[TestClass]
public sealed class PostgreSqlOutboundQueryMigrationTests
{
    /// <summary>第五方法接回后完整原接口的五签名映射到实际仓储；业务SQL分别由原四查询及新选品测试验证。</summary>
    [TestMethod]
    public void CompleteInterfaceIncludesFiveMethods()
    {
        Assert.IsTrue(typeof(IOutboundQueryRepository).IsAssignableFrom(typeof(PostgreSqlOutboundQueryRepository)));
        var mapping=typeof(PostgreSqlOutboundQueryRepository).GetInterfaceMap(typeof(IOutboundQueryRepository));
        Assert.AreEqual(5,mapping.TargetMethods.Length);
        CollectionAssert.AreEquivalent(new[]{"SearchAsync","GetDetailAsync","SearchItemsAsync","GetProductsAsync","IsProductActiveAsync"},
            mapping.TargetMethods.Select(x=>x.Name).ToArray());
        Assert.IsTrue(mapping.TargetMethods.All(x=>!x.IsAbstract&&x.DeclaringType==typeof(PostgreSqlOutboundQueryRepository)));
    }

    /// <summary>同一Session可空事务/token及两结果，原页值与13字段保留。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SearchOwnership(bool transaction)
    {
        using var f=new Fixture(transaction);SearchResults(f);using var c=new CancellationTokenSource();var q=new OutboundManagerSearchRequest{PageIndex=4,PageSize=20};
        var d=await f.Repository.SearchAsync(q,c.Token);Assert.AreEqual(9,d.TotalCount);Assert.HasCount(1,d.Items);Assert.AreEqual(4,d.PageIndex);Assert.AreEqual(20,d.PageSize);Assert.AreEqual(2,f.Connection.NextResults);Assert.AreEqual(1,f.Connection.Disposals);
        var cmd=f.Connection.Commands.Single();Assert.AreSame(f.Session.Transaction,cmd.Transaction);Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(60L,cmd.Values["Skip"]);Assert.AreEqual(NpgsqlDbType.Bigint,cmd.Types["Skip"]);Assert.AreEqual(20,cmd.Values["Take"]);Assert.AreEqual(NpgsqlDbType.Integer,cmd.Types["Take"]);
        foreach(var key in new[]{"DocumentType","PostingStatus"}){Assert.AreEqual(DBNull.Value,cmd.Values[key]);Assert.AreEqual(NpgsqlDbType.Integer,cmd.Types[key]);}
        Assert.AreEqual(DBNull.Value,cmd.Values["Keyword"]);Assert.AreEqual(NpgsqlDbType.Text,cmd.Types["Keyword"]);
        Assert.AreEqual("",d.Items[0].Remark);Assert.AreEqual("",d.Items[0].IssueReason);Assert.AreEqual(3,d.Items[0].LineCount);Assert.AreEqual(4.5m,d.Items[0].TotalQuantity);
        Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>原.NETTrim和字面转义按同序执行，nullable与枚举筛选按整数透传。</summary>
    [TestMethod]
    [DataRow(null,null)] [DataRow(" \u3000",null)] [DataRow("  %_[~  ","%~%~_~[~~%")] [DataRow(" AbC ","%AbC%")]
    public async Task LiteralKeyword(string? keyword,string? expected)
    {
        using var f=new Fixture(false);SearchResults(f);await f.Repository.SearchAsync(new(){Keyword=keyword!,DocumentType=OutboundDocumentTypeEnum.StockIssue,PostingStatus=OutboundPostingStatusEnum.Reversed},default);
        var cmd=f.Connection.Commands.Single();Assert.AreEqual(expected is null?DBNull.Value:expected,cmd.Values["Keyword"]);Assert.AreEqual(22,cmd.Values["DocumentType"]);Assert.AreEqual(8,cmd.Values["PostingStatus"]);
    }

    /// <summary>非法页仍绑定原值；静态验证局部SQL负LIMIT，内存结果不冒称数据库已经拒绝。</summary>
    [TestMethod]
    [DataRow(0,20)] [DataRow(1,0)] [DataRow(1,-1)] [DataRow(int.MinValue,20)] [DataRow(int.MaxValue,200)]
    public async Task RawPagination(int index,int size)
    {
        using var f=new Fixture(false);SearchResults(f);var q=new OutboundManagerSearchRequest{PageIndex=index,PageSize=size};var d=await f.Repository.SearchAsync(q,default);var cmd=f.Connection.Commands.Single();
        Assert.AreEqual((long)unchecked(index-1)*size,cmd.Values["Skip"]);Assert.AreEqual(size,cmd.Values["Take"]);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);StringAssert.Contains(cmd.Sql,"LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip");
    }

    /// <summary>管理计数与页独立当前表、完整别名、SUM0，未增加状态/自动单或主数据资格。</summary>
    [TestMethod]
    public async Task SearchSqlBoundary()
    {
        using var f=new Fixture(false);SearchResults(f);await f.Repository.SearchAsync(new(),default);var sql=f.Connection.Commands.Single().Sql;var parts=sql.Split(';',StringSplitOptions.RemoveEmptyEntries);Assert.HasCount(2,parts);
        foreach(var property in typeof(OutboundManagerListItemDto).GetProperties())StringAssert.Contains(parts[1],"AS \""+property.Name+"\"");
        foreach(var part in parts){StringAssert.Contains(part,"h.\"is_deleted\"=FALSE");StringAssert.Contains(part,"l.\"sku_snapshot\"");StringAssert.Contains(part,"@PostingStatus IS NULL");Assert.IsFalse(part.Contains("lifecycle_status\"=",StringComparison.Ordinal));Assert.IsFalse(part.Contains("trigger_packing_list_id",StringComparison.Ordinal));}
        StringAssert.Contains(sql,"COUNT(*)::integer");StringAssert.Contains(sql,"SUM(l.\"quantity\")");StringAssert.Contains(sql,"DESC NULLS LAST");
        Assert.IsFalse(sql.Contains("product",StringComparison.Ordinal));Assert.IsFalse(sql.Contains("inventory_balance",StringComparison.Ordinal));Assert.IsFalse(sql.Contains("MATERIALIZED",StringComparison.Ordinal));
    }

    /// <summary>头存在/缺失均读完整行集合后判null，保留真实Dapper推进与释放及父事务。</summary>
    [TestMethod]
    [DataRow(false,false)] [DataRow(false,true)] [DataRow(true,false)] [DataRow(true,true)]
    public async Task DetailBuffersLines(bool exists,bool transaction)
    {
        using var f=new Fixture(transaction);DetailResults(f,exists);using var c=new CancellationTokenSource();var d=await f.Repository.GetDetailAsync(f.Uid,c.Token);
        Assert.AreEqual(exists,d is not null);Assert.IsTrue(f.Connection.ReadCounts[1]>1);Assert.AreEqual(2,f.Connection.NextResults);Assert.AreEqual(1,f.Connection.Disposals);Assert.IsTrue(f.Connection.ReaderClosed);
        Assert.AreSame(f.Session.Transaction,f.Connection.Commands.Single().Transaction);Assert.AreEqual(NpgsqlDbType.Uuid,f.Connection.Commands.Single().Types["Uid"]);Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(0,f.Connection.Rollbacks);
        if(exists){Assert.AreEqual("",d!.Header.IssueReason);Assert.IsNull(d.Lines.Single().ImagePath);Assert.IsNull(d.Lines.Single().UnitSnapshot);Assert.AreEqual(-2m,d.Lines.Single().OnHandQty);Assert.IsFalse(d.Lines.Single().IsDeleted);}
    }

    /// <summary>24/18投影不补行IsDeleted；第二阶段不复核头未删或产品有效，不变历史快照和负余额。</summary>
    [TestMethod]
    public async Task DetailProjectionBoundary()
    {
        using var f=new Fixture(false);DetailResults(f,true);await f.Repository.GetDetailAsync(Guid.Empty,default);var cmd=f.Connection.Commands.Single();Assert.AreEqual(Guid.Empty,cmd.Values["Uid"]);var parts=cmd.Sql.Split(';',StringSplitOptions.RemoveEmptyEntries);
        foreach(var property in typeof(OutboundHeaderDetailDto).GetProperties())StringAssert.Contains(parts[0],"AS \""+property.Name+"\"");
        foreach(var property in typeof(OutboundLineDetailDto).GetProperties().Where(x=>x.Name!="IsDeleted"))StringAssert.Contains(parts[1],"AS \""+property.Name+"\"");
        Assert.IsFalse(parts[1].Contains("h.\"is_deleted\"",StringComparison.Ordinal));Assert.IsFalse(parts[1].Contains("p.\"is_deleted\"",StringComparison.Ordinal));Assert.IsFalse(parts[1].Contains("is_active",StringComparison.Ordinal));
        StringAssert.Contains(parts[1],"l.\"is_deleted\"=FALSE");StringAssert.Contains(parts[1],"b.\"is_deleted\"=FALSE");StringAssert.Contains(parts[1],"COALESCE(b.\"on_hand_qty\",0)");Assert.IsFalse(parts[1].Contains("on_hand_qty\">",StringComparison.Ordinal));StringAssert.Contains(parts[1],"ASC NULLS FIRST");
    }

    /// <summary>空头第二集合错误不能降级null，reader仍释放；正常第一/第二结果技术异常也原样保留。</summary>
    [TestMethod]
    [DataRow(false,1)] [DataRow(true,0)] [DataRow(true,1)]
    public async Task DetailResultFailure(bool exists,int stage)
    {
        using var f=new Fixture(true);DetailResults(f,exists);var error=new InvalidOperationException("结果技术异常");f.Connection.ResultAction=i=>{if(i==stage)throw error;};
        Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetDetailAsync(f.Uid,default)));Assert.AreEqual(1,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>空产品集合含预取消仍成功且没有SQL；NULL集合保持源技术错误。</summary>
    [TestMethod]
    public async Task ProductsEmptyAndNull()
    {
        using var f=new Fixture(false);using var c=new CancellationTokenSource();c.Cancel();Assert.HasCount(0,await f.Repository.GetProductsAsync([],c.Token));Assert.HasCount(0,f.Connection.Commands);
        await Assert.ThrowsAsync<NullReferenceException>(()=>f.Repository.GetProductsAsync(null!,default));Assert.HasCount(0,f.Connection.Commands);
    }

    /// <summary>非空产品身份只六投影，UUID数组不展开，不排停用、排序或填候选默认字段。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ProductsProjectionAndDefaults(bool transaction)
    {
        using var f=new Fixture(transaction);var t=Table(("ProductId",typeof(Guid)),("Sku",typeof(string)),("LinkSku",typeof(string)),("Description",typeof(string)),("ImagePath",typeof(string)),("Unit",typeof(string)));t.Rows.Add(f.LineId,"历史SKU",DBNull.Value,DBNull.Value,DBNull.Value," PCS ");f.Connection.Results=[t];
        var ids=new[]{f.LineId,f.LineId,Guid.Empty};var row=(await f.Repository.GetProductsAsync(ids,default)).Single();var cmd=f.Connection.Commands.Single();CollectionAssert.AreEqual(ids,(Guid[])cmd.Values["Ids"]!);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,cmd.Types["Ids"]);Assert.AreSame(f.Session.Transaction,cmd.Transaction);
        StringAssert.Contains(cmd.Sql,"ANY(@Ids)");Assert.IsFalse(cmd.Sql.Contains("is_active",StringComparison.Ordinal));Assert.IsFalse(cmd.Sql.Contains("ORDER BY",StringComparison.Ordinal));Assert.IsFalse(cmd.Sql.Contains("balance",StringComparison.Ordinal));
        Assert.AreEqual(Guid.Empty,row.Uid);Assert.AreEqual(Guid.Empty,row.WarehouseSiteId);Assert.AreEqual(0m,row.OnHandQty);Assert.IsNull(row.DefaultWarehouseName);Assert.IsNull(row.ImagePath);Assert.AreEqual(" PCS ",row.Unit);
    }

    /// <summary>单次EXISTS只判断身份/启用/未删，不引入余额或单位资格。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ProductActive(bool value)
    {
        using var f=new Fixture(true);f.Connection.Invalid=value;Assert.AreEqual(value,await f.Repository.IsProductActiveAsync(Guid.Empty,default));var cmd=f.Connection.Commands.Single();Assert.AreEqual(Guid.Empty,cmd.Values["ProductId"]);Assert.AreEqual(NpgsqlDbType.Uuid,cmd.Types["ProductId"]);StringAssert.Contains(cmd.Sql,"SELECT EXISTS");StringAssert.Contains(cmd.Sql,"p.\"is_active\"=TRUE");Assert.IsFalse(cmd.Sql.Contains("unit",StringComparison.Ordinal));Assert.AreSame(f.Session.Transaction,cmd.Transaction);
    }

    /// <summary>四方法命令技术异常原样传播，仓储不自行回滚或提供空成功。</summary>
    [TestMethod]
    [DataRow("search")] [DataRow("detail")] [DataRow("products")] [DataRow("active")]
    public async Task CommandFailure(string method)
    {
        using var f=new Fixture(true);DetailResults(f,true);var error=new InvalidOperationException("命令失败");f.Connection.CommandAction=_=>throw error;
        Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>Call(f,method,default)));Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>四非空方法预取消与读结果边界保留取消类别及reader释放，不接管外层事务。</summary>
    [TestMethod]
    [DataRow("search",-1)] [DataRow("search",0)] [DataRow("search",1)] [DataRow("detail",-1)] [DataRow("detail",0)] [DataRow("detail",1)] [DataRow("products",-1)] [DataRow("active",-1)]
    public async Task Cancellation(string method,int stage)
    {
        using var f=new Fixture(true);if(method=="search")SearchResults(f);else DetailResults(f,true);using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else f.Connection.ResultAction=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>Call(f,method,c.Token));Assert.AreEqual(stage<0?0:1,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>只封装四方法测试调用，避免给生产类添加接口或占位方法。</summary>
    private static async Task Call(Fixture f,string method,CancellationToken token)
    {
        switch(method){case "search":await f.Repository.SearchAsync(new(),token);break;case "detail":await f.Repository.GetDetailAsync(f.Uid,token);break;case "products":await f.Repository.GetProductsAsync([f.LineId],token);break;default:await f.Repository.IsProductActiveAsync(f.LineId,token);break;}
    }
    /// <summary>列类型明确，内存表只提供源结果形状，不解释业务SQL。</summary>
    private static DataTable Table(params (string Name,Type Type)[] cols){var t=new DataTable();foreach(var (n,type) in cols)t.Columns.Add(n,type);return t;}
    /// <summary>计数与页结果故意独立，不假定同快照。</summary>
    private static void SearchResults(Fixture f)
    {
        var count=Table(("Count",typeof(int)));count.Rows.Add(9);
        var items=Table(("Uid",typeof(Guid)),("LineCount",typeof(int)),("TotalQuantity",typeof(decimal)),("Remark",typeof(string)),("IssueReason",typeof(string)));items.Rows.Add(f.Uid,3,4.5m,DBNull.Value,DBNull.Value);f.Connection.Results=[count,items];
    }
    /// <summary>缺头仍返回模拟行结果以验证缓冲；负余额和NULL保持，不模拟PG关联条件。</summary>
    private static void DetailResults(Fixture f,bool exists)
    {
        var h=Table(("Uid",typeof(Guid)),("IssueReason",typeof(string)));if(exists)h.Rows.Add(f.Uid,DBNull.Value);
        var l=Table(("Uid",typeof(Guid)),("Quantity",typeof(decimal)),("OnHandQty",typeof(decimal)),("ImagePath",typeof(string)),("UnitSnapshot",typeof(string)));l.Rows.Add(f.LineId,3m,-2m,DBNull.Value,DBNull.Value);f.Connection.Results=[h,l];
    }

    /// <summary>独立离线会话，构造时仅返回内存连接，Dapper仍执行真实ADO消费路径。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly Guid Uid=Guid.NewGuid(),LineId=Guid.NewGuid();
        public readonly RecordingConnection Connection=new();
        public readonly DbSession Session;
        public readonly PostgreSqlOutboundQueryRepository Repository;
        /// <summary>创建本例独占内存会话，可选借用事务用于验证仓储不接管。</summary>
        public Fixture(bool transaction){Session=new(new Factory(Connection));if(transaction)Session.Transaction=Connection.BeginTransaction();var user=new Identity();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>只创建指定内存连接，沿PG异常分类不接触网络。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>离线只读用户，仓储不调用打戳。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"出库四查询离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public bool Invalid,ReaderClosed;
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
        /// <summary>借用真实Npgsql参数容器，并以数据表返回两个结果。</summary>
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
            public override object ExecuteScalar(){Record();return owner.Invalid;}
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
