using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际Dapper多结果入口验证exists早返及释放、顺序缓冲、DTO和.NET Guid组装，不证明PG商业SQL执行。</summary>
[TestClass]
public sealed class PostgreSqlPurchaseOrderProgressDetailMigrationTests
{
    /// <summary>全局Guard后一次三语句，正常缓冲全部结果并借用可空事务及同token。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task StagesAndOwnership(bool transaction)
    {
        using var f=new Fixture(transaction);using var c=new CancellationTokenSource();Queue(f,true);
        IPurchaseOrderProgressQueryRepository repository=f.Repository;var d=await repository.GetDetailAsync(f.Uid,c.Token);
        Assert.IsNotNull(d);Assert.AreEqual(f.Uid,d.PurchaseOrderId);Assert.HasCount(1,d.Lines);Assert.HasCount(2,d.Lines.Single().ProgressHistory);
        Assert.HasCount(2,f.Connection.Commands);Assert.AreEqual(false,f.Connection.Commands[0].Values["Scoped"]);Assert.HasCount(3,f.Connection.ReadCounts);
        Assert.IsTrue(f.Connection.ReadCounts.All(x=>x>0));Assert.AreEqual(3,f.Connection.NextResults);Assert.AreEqual(1,f.Connection.Disposals);
        foreach(var cmd in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,cmd.Transaction);
        Assert.AreEqual(c.Token,f.Connection.LastToken);Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
        var command=f.Connection.Commands[1];Assert.AreEqual(f.Uid,command.Values["PurchaseOrderId"]);Assert.AreEqual(NpgsqlDbType.Uuid,command.Types["PurchaseOrderId"]);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,command.Types["FormalPostingStatuses"]);CollectionAssert.AreEqual(new[]{1,8},(int[])command.Values["FormalPostingStatuses"]!);
        Assert.AreEqual(10,command.Values["PurchaseReceipt"]);Assert.AreEqual(NpgsqlDbType.Integer,command.Types["PurchaseReceipt"]);
    }

    /// <summary>exists=false仍发生源ReadSingle的第一次NextResult，但不读取行或报告，并释放reader。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task MissingHeader_ReturnsNullAndDisposes(bool transaction)
    {
        using var f=new Fixture(transaction);Queue(f,false);Assert.IsNull(await f.Repository.GetDetailAsync(Guid.Empty));
        Assert.HasCount(2,f.Connection.Commands);Assert.AreEqual(1,f.Connection.NextResults);Assert.AreEqual(0,f.Connection.ReadCounts[1]);Assert.AreEqual(0,f.Connection.ReadCounts[2]);
        Assert.AreEqual(1,f.Connection.Disposals);Assert.IsTrue(f.Connection.ReaderClosed);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>空头释放不主动进入第三结果；第一次推进失败仍是可见驱动异常，不能把null吞掉它。</summary>
    [TestMethod]
    [DataRow(1,true)] [DataRow(2,false)]
    public async Task MissingHeader_AdvanceBoundary(int stage,bool throws)
    {
        using var f=new Fixture(false);Queue(f,false);var failure=new InvalidOperationException("结果推进失败");
        f.Connection.ResultAction=i=>{if(i==stage)throw failure;};
        if(throws){var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetDetailAsync(f.Uid));Assert.AreSame(failure,ex);}
        else Assert.IsNull(await f.Repository.GetDetailAsync(f.Uid));
        Assert.AreEqual(1,f.Connection.Disposals);
        // 只证明Dapper消费/Dispose，不把内存reader当成Npgsql服务器后续错误排空证据。
    }

    /// <summary>合法服务范围外的空Uid仍原样绑定，不新增默认值；正常空行仍缓冲完整报告。</summary>
    [TestMethod]
    public async Task EmptyLines_StillBuffersReportResult()
    {
        using var f=new Fixture(false);Queue(f,true);f.Connection.Results[1].Rows.Clear();
        var d=await f.Repository.GetDetailAsync(Guid.Empty);Assert.IsNotNull(d);Assert.AreEqual(Guid.Empty,d.PurchaseOrderId);Assert.HasCount(0,d.Lines);
        Assert.IsTrue(f.Connection.ReadCounts[2]>0);Assert.AreEqual(Guid.Empty,f.Connection.Commands[1].Values["PurchaseOrderId"]);
    }

    /// <summary>SourceLineId正式收货、当前图像及全部别名保留，报告不复验头或冻结行UID。</summary>
    [TestMethod]
    public async Task ProjectionAndSqlBoundaries()
    {
        using var f=new Fixture(false);Queue(f,true);var d=await f.Repository.GetDetailAsync(f.Uid);var sql=f.Connection.Commands[1].Sql;
        foreach(var property in typeof(PurchaseOrderProgressLineDto).GetProperties().Where(x=>x.Name is not ("LatestProgress" or "ProgressHistory")))StringAssert.Contains(sql,"AS \""+property.Name+"\"");
        foreach(var property in typeof(SupplierProgressReportDto).GetProperties())StringAssert.Contains(sql,"AS \""+property.Name+"\"");
        StringAssert.Contains(sql,"l.\"quantity\" AS \"Quantity\"");StringAssert.Contains(sql,"receiveLine.\"source_line_id\"");StringAssert.Contains(sql,"receiveHead.\"posting_status\"=1");
        StringAssert.Contains(sql,"ANY(@FormalPostingStatuses)");StringAssert.Contains(sql,"COALESCE(product.\"main_image\",'')");
        var reports=sql[(sql.LastIndexOf("SELECT r.",StringComparison.Ordinal))..];Assert.IsFalse(reports.Contains("purchase_order_head",StringComparison.Ordinal));
        Assert.IsFalse(reports.Contains("@LineIds",StringComparison.Ordinal));Assert.IsFalse(sql.Contains("demand_execution_link",StringComparison.Ordinal));Assert.IsFalse(sql.Contains("unit_snapshot",StringComparison.Ordinal));
        Assert.AreEqual(10m,d!.Lines.Single().Quantity);Assert.AreEqual(3m,d.Lines.Single().ReceivedQty);Assert.AreEqual("",d.Lines.Single().MainImage);
        Assert.IsNull(d.Lines.Single().SupplierSku);Assert.IsNull(d.Lines.Single().PlannedDeliveryDate);Assert.IsNull(d.Lines.Single().LatestProgress.ReadyQty);
    }

    /// <summary>Sequence相同的内存合法协议结果独立验证.NET Guid而非SQL Guid；真实索引不允许制造同序号夹具。</summary>
    [TestMethod]
    public async Task History_UsesDotNetGuidAndWholeLatest()
    {
        using var f=new Fixture(false);Queue(f,true);
        var a=Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001");var b=Guid.Parse("00000000-0000-0000-0000-000000000002");
        var table=f.Connection.Results[2];table.Rows[0]["Uid"]=b;table.Rows[1]["Uid"]=a;table.Rows[0]["Sequence"]=7;table.Rows[1]["Sequence"]=7;
        var line=(await f.Repository.GetDetailAsync(f.Uid))!.Lines.Single();CollectionAssert.AreEqual(new[]{a,b},line.ProgressHistory.Select(x=>x.Uid).ToArray());
        Assert.AreEqual(a,line.LatestProgress.Uid);Assert.AreSame(line.ProgressHistory[0],line.LatestProgress);
    }

    /// <summary>第三阶段报告可有第二阶段尚未列出的当前行；不冻结行身份，组装只关联已返回行。</summary>
    [TestMethod]
    public async Task IndependentReports_DoNotInventLines()
    {
        using var f=new Fixture(false);Queue(f,true);foreach(DataRow row in f.Connection.Results[2].Rows)row["PurchaseOrderLineId"]=Guid.NewGuid();
        var line=(await f.Repository.GetDetailAsync(f.Uid))!.Lines.Single();Assert.HasCount(0,line.ProgressHistory);Assert.IsNull(line.LatestProgress);Assert.IsTrue(f.Connection.ReadCounts[2]>0);
    }

    /// <summary>全局Guard先拒绝，不创建多结果reader，保持公共异常类别。</summary>
    [TestMethod]
    public async Task GlobalGuardFailure()
    {
        using var f=new Fixture(true);Queue(f,true);f.Connection.Invalid=true;
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(f.Uid));Assert.HasCount(1,f.Connection.Commands);Assert.AreEqual(0,f.Connection.Disposals);
    }

    /// <summary>Guard及多结果命令技术失败原样上交，无仓储回滚或空详情降级。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)]
    public async Task CommandFailure(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);var failure=new InvalidOperationException("技术失败");f.Connection.CommandAction=i=>{if(i==stage)throw failure;};
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetDetailAsync(f.Uid));Assert.AreSame(failure,ex);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>多结果读取技术失败保留，已建立reader必须释放。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public async Task ResultFailure(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);var failure=new InvalidOperationException("读取失败");f.Connection.ResultAction=i=>{if(i==stage)throw failure;};
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetDetailAsync(f.Uid));Assert.AreSame(failure,ex);Assert.AreEqual(1,f.Connection.Disposals);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>预取消和命令/各结果边界透传原token，读取取消后释放grid。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task Cancellation_AllBoundaries(int stage)
    {
        using var f=new Fixture(true);Queue(f,true);using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else if(stage<2)f.Connection.CommandAction=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        else f.Connection.ResultAction=i=>{if(i==stage-2)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetDetailAsync(f.Uid,c.Token));Assert.AreEqual(0,f.Connection.Rollbacks);Assert.AreEqual(stage>=2?1:0,f.Connection.Disposals);
    }

    /// <summary>数据表只提供源结果形状，Guard独立标量，商业SQL不在内存执行。</summary>
    private static void Queue(Fixture f,bool exists)
    {
        var e=new DataTable();e.Columns.Add("Exists",typeof(bool));e.Rows.Add(exists);
        var l=new DataTable();l.Columns.Add("PurchaseOrderLineId",typeof(Guid));l.Columns.Add("Version",typeof(int));l.Columns.Add("LineNumber",typeof(int));l.Columns.Add("MainImage",typeof(string));
        l.Columns.Add("Quantity",typeof(decimal));l.Columns.Add("ReceivedQty",typeof(decimal));l.Columns.Add("SupplierSku",typeof(string));l.Columns.Add("PlannedDeliveryDate",typeof(DateTime));
        l.Rows.Add(f.LineId,3,1,"",10m,3m,DBNull.Value,DBNull.Value);
        var r=new DataTable();r.Columns.Add("Uid",typeof(Guid));r.Columns.Add("PurchaseOrderLineId",typeof(Guid));r.Columns.Add("Sequence",typeof(int));r.Columns.Add("ReadyQty",typeof(decimal));
        r.Rows.Add(Guid.NewGuid(),f.LineId,1,7m);r.Rows.Add(Guid.NewGuid(),f.LineId,2,DBNull.Value);
        f.Connection.Results=[e,l,r];
    }

    /// <summary>独立离线会话，构造时仅返回内存连接，Dapper仍执行真实ADO消费路径。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly Guid Uid=Guid.NewGuid(),LineId=Guid.NewGuid();
        public readonly RecordingConnection Connection=new();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderProgressQueryRepository Repository;
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
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"采购进度详情离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public bool Invalid,ReaderClosed;
        public int Begins,Commits,Rollbacks,NextResults,Disposals;
        public int[] ReadCounts=new int[3];
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
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){Record();return new RecordingReader(owner,new DataTableReader(owner.Results));}
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(ExecuteDbDataReader(behavior));}
            protected override void Dispose(bool disposing){if(disposing)container.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class RecordingReader(RecordingConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            public override bool NextResult(){owner.NextResults++;if(phase<2)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
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
