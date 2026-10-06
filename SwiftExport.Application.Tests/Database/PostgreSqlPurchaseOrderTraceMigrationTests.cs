using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实Dapper离线ADO验证scope/门禁/六结果协议和原C#组装；不把内存返回当成PG商业SQL事实。</summary>
[TestClass]
public sealed class PostgreSqlPurchaseOrderTraceMigrationTests
{
    /// <summary>scope仅用于一次ScopedGuard，正常六结果完整缓冲，借用同一连接/可空事务/token。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ProtocolAndOwnership(bool transaction)
    {
        using var f=new Fixture(transaction);Queue(f,true);using var c=new CancellationTokenSource();
        IPurchaseOrderTraceRepository repo=f.Repository;var d=await repo.GetAsync(f.Uid,c.Token);
        Assert.IsNotNull(d);Assert.HasCount(3,f.Connection.Commands);Assert.AreEqual(6,f.Connection.NextResults);Assert.IsTrue(f.Connection.ReadCounts.All(x=>x>0));Assert.AreEqual(1,f.Connection.Disposals);
        var scope=f.Connection.Commands[0];StringAssert.Contains(scope.Sql,"scopeLine");Assert.IsFalse(scope.Sql.Contains("is_deleted",StringComparison.Ordinal));Assert.IsFalse(scope.Sql.Contains("purchase_order_head",StringComparison.Ordinal));
        var guard=f.Connection.Commands[1];Assert.AreEqual(true,guard.Values["Scoped"]);CollectionAssert.AreEqual(f.Connection.Scope,(Guid[])guard.Values["PurchaseLineIds"]!);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,guard.Types["PurchaseLineIds"]);
        foreach(var cmd in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,cmd.Transaction);
        var batch=f.Connection.Commands[2];Assert.AreEqual(f.Uid,batch.Values["PurchaseOrderId"]);Assert.AreEqual(NpgsqlDbType.Uuid,batch.Types["PurchaseOrderId"]);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,batch.Types["FormalPostingStatuses"]);CollectionAssert.AreEqual(new[]{1,8},(int[])batch.Values["FormalPostingStatuses"]!);
        Assert.AreEqual(10,batch.Values["PurchaseReceipt"]);Assert.AreEqual(NpgsqlDbType.Integer,batch.Types["PurchaseReceipt"]);Assert.IsFalse(batch.Values.ContainsKey("PurchaseLineIds"));
        Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.AreEqual(0,f.Connection.Commits);
    }

    /// <summary>缺头仍先查全部行scope和Guard；ReadSingleOrDefault推进一次后早返，不缓冲尾集合。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task MissingHeaderDisposes(bool transaction)
    {
        using var f=new Fixture(transaction);Queue(f,false);Assert.IsNull(await f.Repository.GetAsync(Guid.Empty));
        Assert.HasCount(3,f.Connection.Commands);Assert.AreEqual(Guid.Empty,f.Connection.Commands[0].Values["PurchaseOrderId"]);Assert.AreEqual(1,f.Connection.NextResults);Assert.IsTrue(f.Connection.ReadCounts.Skip(1).All(x=>x==0));Assert.AreEqual(1,f.Connection.Disposals);Assert.IsTrue(f.Connection.ReaderClosed);
    }

    /// <summary>空头首次推进技术错误仍上交；未读更后阶段不由业务主动读取，不代替Npgsql排空验证。</summary>
    [TestMethod]
    [DataRow(1,true)] [DataRow(5,false)]
    public async Task MissingHeaderAdvance(int stage,bool throws)
    {
        using var f=new Fixture(false);Queue(f,false);var failure=new InvalidOperationException("推进失败");f.Connection.ResultAction=i=>{if(i==stage)throw failure;};
        if(throws)Assert.AreSame(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetAsync(f.Uid)));
        else Assert.IsNull(await f.Repository.GetAsync(f.Uid));
        Assert.AreEqual(1,f.Connection.Disposals);
    }

    /// <summary>六阶段投影完整quoted aliases，历史条件不增加软删/生命周期过滤；原CASE与独立Latest条件保持。</summary>
    [TestMethod]
    public async Task SqlAndAliases()
    {
        using var f=new Fixture(false);Queue(f,true);await f.Repository.GetAsync(f.Uid);var sql=f.Connection.Commands[2].Sql;var stages=sql.Split(';',StringSplitOptions.RemoveEmptyEntries);Assert.HasCount(6,stages);
        Type[] dtos=[typeof(PurchaseOrderTraceDto),typeof(PurchaseOrderTraceLineDto),typeof(PurchaseOrderTraceLinkDto),typeof(PurchaseOrderTraceProgressDto),typeof(PurchaseOrderTraceReceiptDto),typeof(PurchaseOrderTraceOperationDto)];
        string[] computed=["Lines","Operations","Links","ProgressHistory","Receipts","FormalReceivedQty","ReviewReason"];
        for(int i=0;i<dtos.Length;i++)foreach(var prop in dtos[i].GetProperties().Where(x=>!computed.Contains(x.Name)||i==2))StringAssert.Contains(stages[i],"AS \""+prop.Name+"\"");
        StringAssert.Contains(stages[1],"l.\"quantity\" AS \"Quantity\"");StringAssert.Contains(stages[1],"LEFT JOIN LATERAL");StringAssert.Contains(stages[1],"LIMIT 1");Assert.IsFalse(stages[1].Contains("l.\"is_deleted\"=",StringComparison.Ordinal));
        StringAssert.Contains(stages[2],"n.\"source_demand_version_snapshot\"<>g.\"version\"");StringAssert.Contains(stages[2],"n.\"parent_net_demand_id\" IS NULL");Assert.IsFalse(stages[2].Contains("n.\"unit_snapshot\"",StringComparison.Ordinal));
        foreach(var x in new[]{"link","n","g"})Assert.IsFalse(stages[2].Contains(x+".\"is_deleted\"=",StringComparison.Ordinal));
        Assert.IsFalse(stages[2].Contains("lifecycle_status",StringComparison.Ordinal));Assert.IsFalse(stages[2].Contains("link_status\"=",StringComparison.Ordinal));
        StringAssert.Contains(stages[2],"g.\"source_type\" AS \"SourceType\"");StringAssert.Contains(stages[2],"g.\"demand_basis_fingerprint\" AS \"DemandBasisFingerprint\"");
        StringAssert.Contains(stages[2],"erp_dictionary_ci");StringAssert.Contains(stages[2],"btrim(p.\"unit\",' ')");StringAssert.Contains(stages[2],"link.\"target_head_id\"<>l.\"purchase_order_id\"");
        StringAssert.Contains(stages[4],"l.\"source_line_id\"");StringAssert.Contains(stages[4],"ANY(@FormalPostingStatuses)");StringAssert.Contains(stages[4],"THEN l.\"quantity\" ELSE 0");
        Assert.IsFalse(stages[5].Contains("module",StringComparison.Ordinal));StringAssert.Contains(stages[5],"log.\"aggregate_uid\"=@PurchaseOrderId");StringAssert.Contains(stages[5],"DESC NULLS LAST");
        foreach(var stage in stages.Skip(1).Take(4)){Assert.IsFalse(stage.Contains("@PurchaseLineIds",StringComparison.Ordinal));Assert.IsFalse(stage.Contains("purchase_order_head",StringComparison.Ordinal));}
    }

    /// <summary>原.NET单位区分大小写、尾空格及Unicode空白，不能按SQL字典等价掩盖；不一致覆盖其他理由并保持NULL。</summary>
    [TestMethod]
    [DataRow(null,true)] [DataRow("",true)] [DataRow(" ",true)] [DataRow("\u3000",true)] [DataRow("pcs",true)] [DataRow("PCS ",true)] [DataRow("KG",true)] [DataRow("PCS",false)]
    public async Task ReceiptUnitUsesDotNet(string? unit,bool mismatch)
    {
        using var f=new Fixture(false);Queue(f,true);f.Connection.Results[4].Rows[0]["UnitSnapshot"]=unit is null?DBNull.Value:unit;
        var line=(await f.Repository.GetAsync(f.Uid))!.Lines.Single();
        if(mismatch){Assert.IsNull(line.FormalReceivedQty);Assert.AreEqual("正式入库单位与采购行不一致，净收货量需核实。",line.ReviewReason);}
        else{Assert.AreEqual(4m,line.FormalReceivedQty);Assert.AreEqual("当前采购行未找到需求 Link，请核实来源。",line.ReviewReason);}
    }

    /// <summary>独立采购与已删行无Link不造缺来源错误；其他有效无Link保留原中文，空收货Sum为0。</summary>
    [TestMethod]
    [DataRow(2,false,false)] [DataRow(1,true,false)] [DataRow(1,false,true)]
    public async Task NoLinkReviewBoundary(int origin,bool deleted,bool review)
    {
        using var f=new Fixture(false);Queue(f,true);f.Connection.Results[0].Rows[0]["OriginKind"]=origin;f.Connection.Results[1].Rows[0]["IsDeleted"]=deleted;f.Connection.Results[4].Rows.Clear();
        var line=(await f.Repository.GetAsync(f.Uid))!.Lines.Single();Assert.AreEqual(0m,line.FormalReceivedQty);
        Assert.AreEqual(review?"当前采购行未找到需求 Link，请核实来源。":null,line.ReviewReason);
    }

    /// <summary>历史软删Link保留，NeedsReview仍影响摘要；行Latest与稍后History独立，不排序或覆盖源已缓冲值。</summary>
    [TestMethod]
    public async Task CompleteHistoryAndIndependentLatest()
    {
        using var f=new Fixture(false);Queue(f,true);var link=f.Connection.Results[2];link.Rows.Add(Guid.NewGuid(),f.LineId,true,true,5m,2m,3m,"原始指纹尾空格 ");
        var line=(await f.Repository.GetAsync(f.Uid))!.Lines.Single();
        Assert.HasCount(1,line.Links);Assert.IsTrue(line.Links[0].IsDeleted);Assert.AreEqual(3m,line.Links[0].EffectiveQty);Assert.AreEqual("原始指纹尾空格 ",line.Links[0].DemandBasisFingerprint);
        Assert.AreEqual("部分需求来源身份、版本、指纹或贡献数量需核实。",line.ReviewReason);
        Assert.AreEqual(8m,line.LatestReadyQty);Assert.HasCount(2,line.ProgressHistory);Assert.AreEqual(2,line.ProgressHistory[0].Sequence);Assert.IsNull(line.ProgressHistory[0].ReadyQty);Assert.AreEqual(4m,line.FormalReceivedQty);Assert.HasCount(2,line.Receipts);
    }

    /// <summary>后续阶段可含scope或行集合外身份，全部读取但不发明采购行；日志归头且保持结果顺序。</summary>
    [TestMethod]
    public async Task IndependentStageIdentities()
    {
        using var f=new Fixture(false);Queue(f,true);f.Connection.Scope=[Guid.NewGuid()];
        foreach(var idx in new[]{2,3,4})foreach(DataRow row in f.Connection.Results[idx].Rows)row["PurchaseOrderLineId"]=Guid.NewGuid();
        var d=(await f.Repository.GetAsync(f.Uid))!;var line=d.Lines.Single();Assert.HasCount(0,line.Links);Assert.HasCount(0,line.ProgressHistory);Assert.HasCount(0,line.Receipts);Assert.AreEqual(0m,line.FormalReceivedQty);Assert.HasCount(2,d.Operations);Assert.AreEqual("最新",d.Operations[0].OperationName);Assert.IsTrue(f.Connection.ReadCounts.All(x=>x>0));
    }

    /// <summary>Guard只接scope集合，命中保持公共异常且不打开六结果reader。</summary>
    [TestMethod]
    public async Task ScopedGuardFailure()
    {
        using var f=new Fixture(true);Queue(f,false);f.Connection.Invalid=true;
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetAsync(f.Uid));Assert.HasCount(2,f.Connection.Commands);Assert.AreEqual(0,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>scope/Guard/六结果入口技术错误保持，不仓储回滚、不降级空头。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public async Task CommandFailure(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);var failure=new InvalidOperationException("命令失败");f.Connection.CommandAction=i=>{if(i==stage)throw failure;};
        Assert.AreSame(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetAsync(f.Uid)));Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>六结果各阶段技术错误原样可见，reader必释放。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task ResultFailure(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);var failure=new InvalidOperationException("结果失败");f.Connection.ResultAction=i=>{if(i==stage)throw failure;};
        Assert.AreSame(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetAsync(f.Uid)));Assert.AreEqual(1,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>预取消/三命令/六结果边界传播原取消类别；无仓储事务接管。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
    public async Task Cancellation(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else if(stage<3)f.Connection.CommandAction=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        else f.Connection.ResultAction=i=>{if(i==stage-3)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetAsync(f.Uid,c.Token));Assert.AreEqual(stage>=3?1:0,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>仅提供独立结果形状，SQL商业谓词不在内存解释；最新整行值故意与后来报告不同。</summary>
    private static void Queue(Fixture f,bool exists)
    {
        DataTable Table(params (string Name,Type Type)[] cols){var t=new DataTable();foreach(var (n,type) in cols)t.Columns.Add(n,type);return t;}
        var h=Table(("PurchaseOrderId",typeof(Guid)),("OriginKind",typeof(int)));if(exists)h.Rows.Add(f.Uid,1);
        var l=Table(("PurchaseOrderLineId",typeof(Guid)),("LineNumber",typeof(int)),("IsDeleted",typeof(bool)),("UnitSnapshot",typeof(string)),("Quantity",typeof(decimal)),("LatestReadyQty",typeof(decimal)),("UnlinkedQty",typeof(decimal)));
        l.Rows.Add(f.LineId,1,false,"PCS",10m,8m,DBNull.Value);
        var links=Table(("LinkId",typeof(Guid)),("PurchaseOrderLineId",typeof(Guid)),("IsDeleted",typeof(bool)),("NeedsReview",typeof(bool)),("AllocatedQty",typeof(decimal)),("ReleasedQty",typeof(decimal)),("EffectiveQty",typeof(decimal)),("DemandBasisFingerprint",typeof(string)));
        var r=Table(("Uid",typeof(Guid)),("PurchaseOrderLineId",typeof(Guid)),("Sequence",typeof(int)),("ReadyQty",typeof(decimal)));r.Rows.Add(Guid.NewGuid(),f.LineId,2,DBNull.Value);r.Rows.Add(Guid.NewGuid(),f.LineId,1,7m);
        var receipts=Table(("InboundLineId",typeof(Guid)),("PurchaseOrderLineId",typeof(Guid)),("UnitSnapshot",typeof(string)),("Quantity",typeof(decimal)),("NetQuantity",typeof(decimal)));receipts.Rows.Add(Guid.NewGuid(),f.LineId,"PCS",4m,4m);receipts.Rows.Add(Guid.NewGuid(),f.LineId,"PCS",7m,0m);
        var log=Table(("Uid",typeof(Guid)),("OperationName",typeof(string)),("OccurredDate",typeof(DateTime)));log.Rows.Add(Guid.NewGuid(),"最新",new DateTime(2026,10,5));log.Rows.Add(Guid.NewGuid(),"旧操作",new DateTime(2026,10,4));
        f.Connection.Scope=[f.LineId,Guid.NewGuid()];f.Connection.Results=[h,l,links,r,receipts,log];
    }

    /// <summary>独立离线会话，构造时仅返回内存连接，Dapper仍执行真实ADO消费路径。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly Guid Uid=Guid.NewGuid(),LineId=Guid.NewGuid();
        public readonly RecordingConnection Connection=new();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderTraceRepository Repository;
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
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"采购Trace离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public Guid[] Scope=[];
        public bool Invalid,ReaderClosed;
        public int Begins,Commits,Rollbacks,NextResults,Disposals;
        public int[] ReadCounts=new int[6];
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
        /// <summary>借用真实Npgsql参数容器，并以数据表返回三个结果。</summary>
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
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                Record();
                if(CommandText.Contains("scopeLine",StringComparison.Ordinal))
                {var scope=new DataTable();scope.Columns.Add("PurchaseLineId",typeof(Guid));foreach(var id in owner.Scope)scope.Rows.Add(id);return scope.CreateDataReader();}
                return new RecordingReader(owner,new DataTableReader(owner.Results));
            }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(ExecuteDbDataReader(behavior));}
            protected override void Dispose(bool disposing){if(disposing)container.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class RecordingReader(RecordingConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            public override bool NextResult(){owner.NextResults++;if(phase<5)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
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
