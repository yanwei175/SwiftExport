using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;
using SwiftExport.Core.Entities.Shipping;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实Dapper映射27字段及私有空页包，内存ADO仅验传输/资源协议，不冒充PG资格/执行计划证据。</summary>
[TestClass]
public sealed class PostgreSqlSourceDeliveryQueryMigrationTests
{
    /// <summary>第四方法补齐后验证完整接口，原三个普通方法的行为断言继续保留。</summary>
    [TestMethod]
    public void CompleteInterfaceContract()
    {var type=typeof(PostgreSqlSourceDeliveryQueryRepository);Assert.IsTrue(typeof(SwiftExport.Core.Interfaces.Shipping.ISourceDeliveryQueryRepository).IsAssignableFrom(type));CollectionAssert.AreEquivalent(new[]{"IsSchemaReadyAsync","SearchAsync","GetAsync","GetForUpdateAsync"},type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Select(x=>x.Name).ToArray());}

    /// <summary>目录bool原样、表/三列来自元数据；假ADO只证参数及目录谓词，缺目录实库不造DDL。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SchemaParameters(bool ready)
    {using var f=new Fixture(true);f.Connection.ScalarResult=ready;Assert.AreEqual(ready,await f.Repository.IsSchemaReadyAsync());var c=f.Connection.Commands.Single();Assert.HasCount(7,c.Values);Assert.AreEqual("public",c.Values["Schema"]);Assert.AreEqual("gross_demand",c.Values["GrossTable"]);Assert.AreEqual("packing_list_head",c.Values["HeadTable"]);Assert.AreEqual("packing_list_line",c.Values["LineTable"]);Assert.AreEqual("is_non_standard_snapshot",c.Values["ModeColumn"]);Assert.AreEqual("source_type_snapshot",c.Values["TypeColumn"]);Assert.AreEqual("source_document_no_snapshot",c.Values["NumberColumn"]);Assert.IsTrue(c.Types.Values.All(x=>x==NpgsqlDbType.Text));StringAssert.Contains(c.Sql,"t.relkind IN ('r','p')");StringAssert.Contains(c.Sql,"a.attnum>0 AND NOT a.attisdropped");Assert.IsFalse(c.Sql.Contains("product"));Assert.AreSame(f.Session.Transaction,c.Transaction);}

    /// <summary>真实私有行映射全部27字段，原值/NULL/负余额及SQL给定总数页序保持，token/可空父事务不接管。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SearchFullMapping(bool parent)
    {using var f=new Fixture(parent);using var cancel=new CancellationTokenSource();var first=Item();first.SourceNo="Z 原文 ";first.RemainingQty=-2.125m;first.DemandQty=1m;first.DeliveredQty=3.125m;var second=Item();second.SourceNo="A";second.CustomerId=null;second.ProductId=null;second.SourceVersion=null;second.SourceLineNumber=null;second.RequiredDate=null;second.Description=null!;var table=Rows(true);Add(table,first,27,true);Add(table,second,27,true);f.Connection.Results=[table];var q=new SourceDemandDeliverySearchRequest{PageIndex=3,PageSize=2};var d=await f.Repository.SearchAsync(q,cancel.Token);Assert.AreEqual(27,d.TotalCount);Assert.HasCount(2,d.Items);Assert.AreEqual(3,d.PageIndex);Assert.AreEqual(2,d.PageSize);Same(first,d.Items[0]);Same(second,d.Items[1]);Assert.AreEqual(cancel.Token,f.Connection.LastToken);Assert.AreSame(f.Session.Transaction,f.Connection.Commands.Single().Transaction);Assert.AreEqual(1,f.Connection.Disposals);Assert.AreEqual(1,f.Connection.NextResults);Assert.IsTrue(f.Connection.ReaderClosed);Assert.AreEqual(parent?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.AreEqual(0,f.Connection.Commits);}

    /// <summary>HasSource false是唯一占位判据；假身份非NULL也不成为候选，超末页仍传总数。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(29)]
    public async Task EmptyPage(int total)
    {using var f=new Fixture(false);var t=Rows(true);Add(t,Item(),total,false);f.Connection.Results=[t];var d=await f.Repository.SearchAsync(new(){PageIndex=8,PageSize=20});Assert.AreEqual(total,d.TotalCount);Assert.HasCount(0,d.Items);Assert.AreEqual(8,d.PageIndex);}

    /// <summary>真实HasSource的Empty身份不能被C#误作占位；资格仅来自SQL。</summary>
    [TestMethod]
    public async Task EmptyIdentityIsReal()
    {using var f=new Fixture(false);var t=Rows(true);var row=Item();row.GrossDemandId=Guid.Empty;Add(t,row);f.Connection.Results=[t];Assert.AreEqual(Guid.Empty,(await f.Repository.SearchAsync(new())).Items.Single().GrossDemandId);}

    /// <summary>Get全部字段及零负剩余/Closed沿源，不额外过滤或重新计算。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(-1)]
    public async Task GetFullMapping(int remaining)
    {using var f=new Fixture(true);var row=Item();row.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;row.IsActive=false;row.RemainingQty=remaining;row.CustomerId=null;var t=Rows(false);Add(t,row);f.Connection.Results=[t];Same(row,(await f.Repository.GetAsync(row.GrossDemandId))!);var c=f.Connection.Commands.Single();Assert.HasCount(3,c.Values);Assert.AreEqual(row.GrossDemandId,c.Values["GrossDemandId"]);Assert.AreEqual(NpgsqlDbType.Uuid,c.Types["GrossDemandId"]);Assert.AreEqual(NpgsqlDbType.Integer,c.Types["Posted"]);Assert.AreEqual(NpgsqlDbType.Integer,c.Types["Active"]);Assert.AreSame(f.Session.Transaction,c.Transaction);StringAssert.Contains(c.Sql,"WHERE \"GrossDemandId\"=@GrossDemandId");}

    /// <summary>Empty与普通身份均发SQL，缺行null，Get非业务空身份验证入口。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task GetMissing(bool empty)
    {using var f=new Fixture(false);f.Connection.Results=[Rows(false)];var id=empty?Guid.Empty:Guid.NewGuid();Assert.IsNull(await f.Repository.GetAsync(id));Assert.AreEqual(id,f.Connection.Commands.Single().Values["GrossDemandId"]);}

    /// <summary>九参数显式类型、nullable uuid与Empty不同，原枚举及分页保持；过滤请求不被修改。</summary>
    [TestMethod]
    [DataRow("null")] [DataRow("empty")] [DataRow("id")]
    public async Task SearchParameters(string mode)
    {using var f=new Fixture(false);f.Empty();Guid? customer=mode=="null"?null:mode=="empty"?Guid.Empty:Guid.NewGuid();var q=new SourceDemandDeliverySearchRequest{CustomerId=customer,SourceType="  SO ",SourceNo=" ~%_[ ",Sku=" X ",Keyword=" Y ",PageIndex=4,PageSize=20};await f.Repository.SearchAsync(q);var c=f.Connection.Commands.Single();Assert.HasCount(9,c.Values);Assert.AreEqual((object?)customer??DBNull.Value,c.Values["CustomerId"]);Assert.AreEqual("SO",c.Values["SourceType"]);Assert.AreEqual("%~~~%~_~[%",c.Values["SourceNo"]);Assert.AreEqual("%X%",c.Values["Sku"]);Assert.AreEqual("%Y%",c.Values["Keyword"]);Assert.AreEqual(60L,c.Values["Skip"]);Assert.AreEqual(20,c.Values["Take"]);Assert.AreEqual(1,c.Values["Posted"]);Assert.AreEqual(0,c.Values["Active"]);foreach(var n in new[]{"SourceType","SourceNo","Sku","Keyword"})Assert.AreEqual(NpgsqlDbType.Text,c.Types[n]);Assert.AreEqual(NpgsqlDbType.Uuid,c.Types["CustomerId"]);Assert.AreEqual(NpgsqlDbType.Bigint,c.Types["Skip"]);foreach(var n in new[]{"Take","Posted","Active"})Assert.AreEqual(NpgsqlDbType.Integer,c.Types[n]);Assert.AreEqual("  SO ",q.SourceType);}

    /// <summary>分页保留源int减一回绕再long乘；假reader不能模拟PG拒绝，真实非法页另验。</summary>
    [TestMethod]
    [DataRow(0,20)] [DataRow(-1,20)] [DataRow(1,0)] [DataRow(2,-1)] [DataRow(int.MinValue,20)] [DataRow(int.MaxValue,int.MaxValue)]
    public async Task OriginalPageArithmetic(int index,int size)
    {using var f=new Fixture(false);f.Empty();var d=await f.Repository.SearchAsync(new(){PageIndex=index,PageSize=size});var c=f.Connection.Commands.Single();Assert.AreEqual((long)unchecked(index-1)*size,c.Values["Skip"]);Assert.AreEqual(size,c.Values["Take"]);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);}

    /// <summary>空白/Unicode Trim与四转义所有文本参数一致，SourceType只Clean不LIKE。</summary>
    [TestMethod]
    [DataRow(null,null)] [DataRow(" 	 ",null)] [DataRow(" ~%_[ ","%~~~%~_~[%")] [DataRow(" 中文 ","%中文%")]
    public async Task TextBinding(string? input,string? expected)
    {using var f=new Fixture(false);f.Empty();await f.Repository.SearchAsync(new(){SourceType=input,SourceNo=input,Sku=input,Keyword=input});var c=f.Connection.Commands.Single();foreach(var n in new[]{"SourceNo","Sku","Keyword"})Assert.AreEqual((object?)expected??DBNull.Value,c.Values[n]);Assert.AreEqual((object?)(string.IsNullOrWhiteSpace(input)?null:input.Trim())??DBNull.Value,c.Values["SourceType"]);}

    /// <summary>缺COUNT包或真实行必需字段NULL是技术错误，reader释放及父事务所有权保持。</summary>
    [TestMethod]
    [DataRow("no-row")] [DataRow("GrossDemandId")] [DataRow("DemandVersion")] [DataRow("SourceUid")] [DataRow("DemandQty")] [DataRow("DeliveredQty")] [DataRow("RemainingQty")] [DataRow("IsActive")]
    public async Task InvalidProjection(string field)
    {using var f=new Fixture(true);var t=Rows(true);if(field!="no-row"){Add(t,Item());t.Rows[0][field]=DBNull.Value;}f.Connection.Results=[t];await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.SearchAsync(new()));Assert.AreEqual(1,f.Connection.Disposals);Assert.IsNotNull(f.Session.Transaction);Assert.AreEqual(0,f.Connection.Rollbacks);}

    /// <summary>null请求在SQL前失败，不新增业务Validation。</summary>
    [TestMethod]
    public async Task NullRequest()
    {using var f=new Fixture(false);await Assert.ThrowsAsync<NullReferenceException>(()=>f.Repository.SearchAsync(null!));Assert.HasCount(0,f.Connection.Commands);}

    /// <summary>标量/查询派发原技术错误不包装，reader读取失败也释放，不清理父事务。</summary>
    [TestMethod]
    [DataRow("schema",false)] [DataRow("search",false)] [DataRow("get",false)] [DataRow("search",true)] [DataRow("get",true)]
    public async Task FailureOwnership(string method,bool reader)
    {using var f=new Fixture(true);f.Empty();var error=new InvalidOperationException("明确注入");if(reader)f.Connection.ResultAction=_=>throw error;else f.Connection.CommandAction=_=>throw error;Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>Call(f,method,default)));Assert.IsNotNull(f.Session.Transaction);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.AreEqual(reader?1:0,f.Connection.Disposals);}

    /// <summary>各方法预取消及查询读取取消沿原token释放，仓储不回滚父事务。</summary>
    [TestMethod]
    [DataRow("schema",-1)] [DataRow("search",-1)] [DataRow("get",-1)] [DataRow("search",0)] [DataRow("get",0)] [DataRow("search",1)] [DataRow("get",1)]
    public async Task CancellationOwnership(string method,int stage)
    {using var f=new Fixture(true);f.Empty();using var c=new CancellationTokenSource();if(stage<0)c.Cancel();else if(stage==0)f.Connection.CommandAction=_=>c.Cancel();else f.Connection.ResultAction=_=>c.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>Call(f,method,c.Token));Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.IsNotNull(f.Session.Transaction);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.AreEqual(stage<0?0:1,f.Connection.Disposals);}

    /// <summary>SQL边界只是补充证据，不把关键词存在当资格/物化实库通过。</summary>
    [TestMethod]
    public async Task SqlBoundary()
    {using var f=new Fixture(false);f.Empty();await f.Repository.SearchAsync(new());var sql=f.Connection.Commands.Single().Sql;StringAssert.Contains(sql,"candidates AS MATERIALIZED");StringAssert.Contains(sql,"COUNT(*)::integer");StringAssert.Contains(sql,"FROM totals t LEFT JOIN page pg ON TRUE");StringAssert.Contains(sql,"COALESCE(pg.\"HasSource\",FALSE)");StringAssert.Contains(sql,"LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip");var qty=PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListLine)).Columns.Single(x=>x.PropertyName=="Qty").ColumnName;StringAssert.Contains(sql,"SUM(l.\""+qty+"\")");StringAssert.Contains(sql,"COALESCE(s.\"SourceLineNumber\",2147483647)");Assert.IsFalse(sql.Contains("FOR UPDATE"));Assert.IsFalse(sql.Contains("ILIKE"));Assert.IsFalse(sql.Contains("outbound_document"));Assert.IsFalse(sql.Contains("c.\"is_active\""));Assert.IsFalse(sql.Contains("p.\"is_active\""));Assert.IsFalse(sql.Contains("h.\"lifecycle_status\""));}

    /// <summary>统一调度只用于异常/取消测试，不改变生产实现。</summary>
    private static async Task Call(Fixture f,string method,CancellationToken token)
    {if(method=="schema")await f.Repository.IsSchemaReadyAsync(token);else if(method=="get")await f.Repository.GetAsync(Guid.NewGuid(),token);else await f.Repository.SearchAsync(new(),token);}
    /// <summary>独立字段列表从契约反射，不共享生产私有字段数组。</summary>
    private static readonly PropertyInfo[] Fields=typeof(SourceDemandDeliveryItemDto).GetProperties();
    /// <summary>真实数据表列类型模拟驱动投影，SQL资格不由夹具解释。</summary>
    private static DataTable Rows(bool search)
    {var t=new DataTable();if(search){t.Columns.Add("TotalCount",typeof(int));t.Columns.Add("HasSource",typeof(bool));}foreach(var p in Fields){var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;t.Columns.Add(p.Name,type.IsEnum?typeof(int):type);}return t;}
    /// <summary>填入源DTO值，空文本及nullable值沿DBNull传输。</summary>
    private static void Add(DataTable t,SourceDemandDeliveryItemDto row,int total=1,bool source=true)
    {var r=t.NewRow();if(t.Columns.Contains("HasSource")){r["TotalCount"]=total;r["HasSource"]=source;}foreach(var p in Fields){var v=p.GetValue(row);r[p.Name]=v is Enum?Convert.ToInt32(v):v??DBNull.Value;}t.Rows.Add(r);}
    /// <summary>所有字段逐一比较，保持快照/NULL和数量原值。</summary>
    private static void Same(SourceDemandDeliveryItemDto expected,SourceDemandDeliveryItemDto actual)
    {Assert.HasCount(27,Fields);foreach(var p in Fields)Assert.AreEqual(p.GetValue(expected),p.GetValue(actual),p.Name);}
    /// <summary>原始完整DTO样本只给传输测试，不冒充真实资格关系。</summary>
    private static SourceDemandDeliveryItemDto Item()=>new(){GrossDemandId=Guid.NewGuid(),DemandVersion=7,SourceUid=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),SourceType=" SO ",SourceNo=" 原单号 ",SourceLineNumber=3,SourceVersion=9,CustomerId=Guid.NewGuid(),CustomerCode=" 原客户 ",CustomerName=null!,ProductId=Guid.NewGuid(),IsNonStandard=false,Sku=" 快照SKU ",LinkSku=null!,CustomerSku="客户型号",Description="说明",Color="颜色",Specification=null!,ImagePath="图片",Unit=" PCS ",DemandQty=9.125m,DeliveredQty=2m,RemainingQty=7.125m,LifecycleStatus=GrossDemandLifecycleStatusEnum.Active,IsActive=true,RequiredDate=new DateTime(2026,10,5)};
    /// <summary>无网络离线会话，真实Dapper实例化生产私有模型。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly RecordingConnection Connection=new();public readonly DbSession Session;public readonly PostgreSqlSourceDeliveryQueryRepository Repository;
        /// <summary>父事务仅观察所有权，不模拟真实PG隔离。</summary>
        public Fixture(bool parent){Session=new(new Factory(Connection));if(parent)Session.Transaction=Connection.BeginTransaction();var user=new Identity();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
        /// <summary>COUNT占位27字段全部NULL。</summary>
        public void Empty(){var t=Rows(true);var r=t.NewRow();r["TotalCount"]=0;r["HasSource"]=false;t.Rows.Add(r);Connection.Results=[t];}
        /// <summary>释放本人离线资源，无数据库清理。</summary>
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>只创建指定内存连接，沿PG异常分类不接触网络。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>离线只读用户，仓储不调用打戳。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"来源交付离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public bool ScalarResult;
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
            public override object ExecuteScalar(){Record();return owner.ScalarResult;}
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
