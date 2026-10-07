using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>经公开销售行入口和真实Dapper检验UID、审计、版本、枚举与Stage两命令边界；不模拟PG约束。</summary>
[TestClass]
public sealed class PostgreSqlSalesOrderLineMigrationTests
{
    /// <summary>有效行完整映射、字段别名与源SqlGuid尾键；普通可空事务不额外开事务，更新锁借用外层。</summary>
    [TestMethod,DataRow(false,false),DataRow(true,false),DataRow(true,true)]
    public async Task Offline_GetLinesMapsAndBorrows(bool transactional,bool locking)
    {
        using var f=new Fixture(transactional); var line=Line(); f.Connection.Outcomes.Enqueue(Rows(line));
        var actual=(await f.Repository.GetLinesByOrderIdAsync(line.SalesOrderId,locking)).Single();
        foreach(var column in EntityMetadataCache.GetMetadata(typeof(SalesOrderLine)).Columns)
        { Assert.AreEqual(column.Property.GetValue(line),column.Property.GetValue(actual),column.PropertyName); StringAssert.Contains(f.Connection.Sqls[0],"AS \""+column.PropertyName+"\""); }
        StringAssert.Contains(f.Connection.Sqls[0],"is_deleted=FALSE"); StringAssert.Contains(f.Connection.Sqls[0],"ORDER BY line_number,ARRAY[get_byte(uuid_send(uid),10)");
        Assert.AreEqual(locking,f.Connection.Sqls[0].Contains("FOR UPDATE",StringComparison.Ordinal)); Assert.AreEqual(line.SalesOrderId,f.Connection.Values[0]["OrderId"]);
        AssertBorrowed(f,1); Assert.IsTrue(f.Connection.Readers.All(r=>r.IsClosed));
    }
    /// <summary>更新锁无外层事务在执行前拒绝；正常空结果仍执行一次读取。</summary>
    [TestMethod]
    public async Task Offline_LockNeedsOuterTransactionAndEmptyRead()
    {
        using var f=new Fixture(false); await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetLinesByOrderIdAsync(Guid.NewGuid(),true));
        Assert.HasCount(0,f.Connection.Sqls); f.Connection.Outcomes.Enqueue(Rows()); Assert.HasCount(0,await f.Repository.GetLinesByOrderIdAsync(Guid.Empty)); AssertBorrowed(f,1);
    }
    /// <summary>销售专属Add保留非空UID/正Version，空UID才生成，非正版本置1；同次审计补缺不覆盖显式值。</summary>
    [TestMethod,DataRow(false,-1),DataRow(false,0),DataRow(false,7),DataRow(true,-1),DataRow(true,0),DataRow(true,7)]
    public async Task Offline_AddKeepsIdentityVersionAndAudit(bool emptyUid,int version)
    {
        using var f=new Fixture(true); var line=Line(); var original=line.Uid; line.Uid=emptyUid?Guid.Empty:original; line.Version=version; line.IsDeleted=true;
        line.CreatedDate=new(2026,9,1); line.CreatedBy="显式建立"; line.UpdatedDate=null; line.UpdatedBy=null;
        f.Connection.Outcomes.Enqueue(1); var before=DateTime.Now; var result=await f.Repository.AddAsync(line);
        Assert.IsTrue(result.Success); Assert.AreEqual(emptyUid?line.Uid:original,result.Uid); Assert.AreNotEqual(Guid.Empty,line.Uid);
        Assert.AreEqual(version<=0?1:version,line.Version); Assert.AreEqual(line.Version,result.NewVersion); Assert.IsNull(result.OldVersion);
        Assert.AreEqual("新增销售订单行成功。",result.Message); Assert.AreEqual(1,result.AffectedRows); Assert.IsFalse(line.IsDeleted);
        Assert.AreEqual(new DateTime(2026,9,1),line.CreatedDate); Assert.AreEqual("显式建立",line.CreatedBy); Assert.AreEqual(UserName,line.UpdatedBy); Assert.IsTrue(line.UpdatedDate>=before);
        Assert.AreEqual(line.Uid,f.Connection.Values[0]["Uid"]); Assert.AreEqual(false,f.Connection.Values[0]["IsDeleted"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp,f.Connection.Types[0]["UpdatedDate"]); Assert.AreEqual(DateTimeKind.Unspecified,((DateTime)f.Connection.Values[0]["UpdatedDate"]!).Kind); AssertBorrowed(f,1);
    }
    /// <summary>非一行插入保留原失败结果，null输入先拒绝而不执行。</summary>
    [TestMethod,DataRow(0),DataRow(2)]
    public async Task Offline_AddFailureAndNull(int affected)
    {
        using var f=new Fixture(false); await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Repository.AddAsync(null!)); Assert.HasCount(0,f.Connection.Sqls);
        var line=Line(); f.Connection.Outcomes.Enqueue(affected); var result=await f.Repository.AddAsync(line);
        Assert.IsFalse(result.Success); Assert.AreEqual(line.Uid,result.Uid); Assert.AreEqual("INSERT_FAILED",result.ErrorCode); Assert.AreEqual("新增销售订单行失败。",result.Message); Assert.AreEqual(0,result.AffectedRows); AssertBorrowed(f,1);
    }
    /// <summary>实际MAX与UPDATE两个命令、long上界、空集合门禁；Stage不改版本/审计也不使用UID缓存。</summary>
    [TestMethod,DataRow(2,2,5,false),DataRow(1073741822,2,1073741825,false),DataRow(1073741822,3,0,true),
     DataRow(1073741823,2,0,true),DataRow(0,2,3,false),DataRow(0,int.MaxValue,0,true),DataRow(int.MaxValue,1,0,true)]
    public async Task Offline_StageTwoCommandsAndOverflow(int maximum,int finalCount,int offset,bool overflow)
    {
        using var f=new Fixture(true); var order=Guid.NewGuid(); f.Connection.Outcomes.Enqueue(maximum); f.Connection.Outcomes.Enqueue(3);
        if(overflow) { var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.StageLineNumbersAsync(order,finalCount)); Assert.AreEqual("销售订单行号暂存超出 int 范围。",error.Message);
            Assert.AreEqual(DatabaseFailureKind.Unknown,f.Session.Errors.Classify(error)); AssertBorrowed(f,1); }
        else {
            Assert.AreEqual(3,await f.Repository.StageLineNumbersAsync(order,finalCount)); AssertBorrowed(f,2); Assert.AreEqual(offset,f.Connection.Values[1]["Offset"]);
            StringAssert.Contains(f.Connection.Sqls[1],"line_number=line_number+@Offset"); Assert.IsFalse(f.Connection.Sqls[1].Contains("version",StringComparison.Ordinal));
            Assert.IsFalse(f.Connection.Sqls[1].Contains("updated_",StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Sqls[1].Contains("uid=",StringComparison.Ordinal));
        }
        StringAssert.Contains(f.Connection.Sqls[0],"COALESCE(MAX(line_number),0)"); Assert.AreEqual(order,f.Connection.Values[0]["OrderId"]);
    }
    /// <summary>无外层事务仍按原两阶段Stage执行，空UPDATE返回0；技术失败不转换为0/业务并发。</summary>
    [TestMethod,DataRow(1),DataRow(2),DataRow(0)]
    public async Task Offline_StageFailureAndOptionalTransaction(int failStage)
    {
        using var f=new Fixture(false); f.Connection.Outcomes.Enqueue(0); f.Connection.Outcomes.Enqueue(0); var error=new InvalidOperationException("数据库失败");
        f.Connection.Before=stage=> { if(stage==failStage)throw error; };
        if(failStage==0)Assert.AreEqual(0,await f.Repository.StageLineNumbersAsync(Guid.NewGuid(),1));
        else Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.StageLineNumbersAsync(Guid.NewGuid(),1)));
        AssertBorrowed(f,failStage==1?1:2);
    }
    /// <summary>三个批量入口普通失败继续，结果/身份与惰性枚举同序；技术异常时停止读取后续输入。</summary>
    [TestMethod,DataRow("add",false),DataRow("update",false),DataRow("delete",false),DataRow("add",true),DataRow("update",true),DataRow("delete",true)]
    public async Task Offline_BatchKeepsOrderAndFailureBehavior(string operation,bool technical)
    {
        using var f=new Fixture(true); var lines=new[]{Line(),Line(),Line()}; var enumerated=new List<Guid>();
        IEnumerable<SalesOrderLine> Input() { foreach(var line in lines) { enumerated.Add(line.Uid); yield return line; } }
        if(operation=="add") { f.Connection.Outcomes.Enqueue(1); f.Connection.Outcomes.Enqueue(0); f.Connection.Outcomes.Enqueue(1); }
        else { f.Connection.Outcomes.Enqueue(Versions(2)); f.Connection.Outcomes.Enqueue(Versions()); f.Connection.Outcomes.Enqueue(Versions(2)); }
        var error=new InvalidOperationException("第二行技术失败"); if(technical)f.Connection.Before=stage=> { if(stage==2)throw error; };
        Task<IReadOnlyList<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>> read=operation switch {
            "add"=>f.Repository.AddLinesAsync(Input()),"update"=>f.Repository.UpdateLinesAsync(Input()),
            _=>f.Repository.DeleteLinesAsync(Input().Select(line=>new SalesOrderDeletedLineDto { Uid=line.Uid,Version=line.Version })) };
        if(technical) { Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>read)); Assert.HasCount(2,enumerated); AssertBorrowed(f,2); }
        else { var result=await read; CollectionAssert.AreEqual(lines.Select(l=>l.Uid).ToArray(),result.Select(r=>r.Uid).ToArray());
            CollectionAssert.AreEqual(new[]{true,false,true},result.Select(r=>r.Success).ToArray()); Assert.HasCount(3,enumerated); AssertBorrowed(f,3); }
        CollectionAssert.AreEqual(enumerated.ToArray(),f.Connection.Values.Select(v=>(Guid)v["Uid"]!).ToArray());
    }
    /// <summary>空批量无数据库操作，不额外开事务。</summary>
    [TestMethod]
    public async Task Offline_EmptyBatches()
    {
        using var f=new Fixture(false); Assert.HasCount(0,await f.Repository.AddLinesAsync([])); Assert.HasCount(0,await f.Repository.UpdateLinesAsync([]));
        Assert.HasCount(0,await f.Repository.DeleteLinesAsync([])); AssertBorrowed(f,0);
    }
    /// <summary>继承Update保留执行前实体版本推进和历史OldVersion返回，失败不自动还原内存版本。</summary>
    [TestMethod,DataRow(true,0),DataRow(true,4),DataRow(false,0),DataRow(false,4)]
    public async Task Offline_UpdateKeepsInheritedVersionContract(bool success,int version)
    {
        using var f=new Fixture(true); var line=Line(); line.Version=version; var original=version<=0?1:version; var next=original+1;
        f.Connection.Outcomes.Enqueue(success?Versions(next):Versions()); var result=await f.Repository.UpdateAsync(line);
        Assert.AreEqual(next,line.Version); Assert.AreEqual(next,result.OldVersion); Assert.AreEqual(success?next:null,result.NewVersion); Assert.AreEqual(success,result.Success);
        Assert.AreEqual(original,f.Connection.Values[0]["OriginalVersion"]); Assert.AreEqual(UserName,line.UpdatedBy); AssertBorrowed(f,1);
    }
    /// <summary>专属软删按原Version并推进1，原销售结果文案及无时区审计保持；失败不是新增语义码。</summary>
    [TestMethod,DataRow(true),DataRow(false)]
    public async Task Offline_DeleteLineVersionAndResult(bool success)
    {
        using var f=new Fixture(true); var id=Guid.NewGuid(); f.Connection.Outcomes.Enqueue(success?Versions(8):Versions());
        var result=(await f.Repository.DeleteLinesAsync([new() { Uid=id,Version=7 }])).Single(); Assert.AreEqual(id,result.Uid); Assert.AreEqual(7,result.OldVersion);
        Assert.AreEqual(success?8:null,result.NewVersion); Assert.AreEqual(success,result.Success); Assert.AreEqual(success?"删除销售订单行成功。":"删除销售订单行失败，数据已被其他用户修改。",result.Message);
        if(!success)Assert.AreEqual("CONCURRENCY_CONFLICT",result.ErrorCode); StringAssert.Contains(f.Connection.Sqls[0],"version=version+1"); StringAssert.Contains(f.Connection.Sqls[0],"uid=@Uid AND version=@Version AND is_deleted=FALSE RETURNING version");
        Assert.AreEqual(UserName,f.Connection.Values[0]["UserName"]); Assert.AreEqual(NpgsqlDbType.Timestamp,f.Connection.Types[0]["Now"]); AssertBorrowed(f,1);
    }
    /// <summary>整单删除返回真实数量且不要求单行版本，不物理删除或修改来源。</summary>
    [TestMethod,DataRow(false,0),DataRow(true,3)]
    public async Task Offline_DeleteOrderCountAndNullableTransaction(bool transactional,int count)
    {
        using var f=new Fixture(transactional); var order=Guid.NewGuid(); f.Connection.Outcomes.Enqueue(count);
        Assert.AreEqual(count,await f.Repository.DeleteByOrderIdAsync(order)); StringAssert.Contains(f.Connection.Sqls[0],"sales_order_id=@OrderId AND is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Sqls[0],"version=version+1"); Assert.IsFalse(f.Connection.Sqls[0].Contains("DELETE FROM",StringComparison.Ordinal));
        Assert.AreEqual(order,f.Connection.Values[0]["OrderId"]); AssertBorrowed(f,1);
    }
    private const string UserName="__pgtest_sales_line_offline__";
    /// <summary>包含稳定身份及nullable/小数/墙上时间的合法行。</summary>
    private static SalesOrderLine Line()=>new() { SalesOrderId=Guid.NewGuid(),OrderNo=" SO ",LineNumber=1,IsNonStandard=true,Sku=" RAW ",Qty=12.3456m,UnitSnapshot=" PCS ",ExpectedDeliveryDate=new(2026,10,5,9,10,11),CustomerSku=" CS ",DescriptionEn="描述" };
    /// <summary>按实际元数据生成完整实体CLR列，不解释业务SQL。</summary>
    private static DataTable Rows(params SalesOrderLine[] lines)
    {
        var table=new DataTable(); var columns=EntityMetadataCache.GetMetadata(typeof(SalesOrderLine)).Columns;
        foreach(var column in columns) { var type=Nullable.GetUnderlyingType(column.Property.PropertyType)??column.Property.PropertyType; table.Columns.Add(column.PropertyName,type.IsEnum?typeof(int):type); }
        foreach(var line in lines)table.Rows.Add(columns.Select(c=> { var value=c.Property.GetValue(line); return value is null?DBNull.Value:value.GetType().IsEnum?Convert.ToInt32(value):value; }).ToArray()); return table;
    }
    /// <summary>UPDATE RETURNING单列结果，不命中时零行。</summary>
    private static DataTable Versions(params int[] versions) { var t=new DataTable(); t.Columns.Add("version",typeof(int)); foreach(var v in versions)t.Rows.Add(v); return t; }
    /// <summary>每个命令借用事务、释放reader，没有提交/回滚/连接关闭或DDL。</summary>
    private static void AssertBorrowed(Fixture f,int count)
    {
        Assert.HasCount(count,f.Connection.Sqls); Assert.AreSame(f.Transaction,f.Session.Transaction); Assert.IsTrue(f.Connection.Transactions.All(t=>ReferenceEquals(t,f.Transaction)));
        Assert.AreEqual(0,f.Connection.EndTransactions); Assert.AreEqual(0,f.Connection.Closes); Assert.AreEqual(f.Transaction is null?0:1,f.Connection.Begins); Assert.IsTrue(f.Connection.Readers.All(r=>r.IsClosed));
        foreach(var sql in f.Connection.Sqls) foreach(var forbidden in new[]{"CREATE ","DROP ","LOCK TABLE","WITH (","HOLDLOCK"})Assert.IsFalse(sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
    }
    /// <summary>每例独立ADO脚本、会话及可空调用方事务。</summary>
    private sealed class Fixture:IDisposable
    {
        public ConnectionScript Connection { get; }=new(); public DbSession Session { get; } public DbTransaction? Transaction { get; }
        public PostgreSqlSalesOrderLineRepository Repository { get; }
        /// <summary>复用现有上下文，事务由测试调用者创建。</summary>
        public Fixture(bool transactional) { Session=new(new Factory(Connection)); if(transactional)Session.Transaction=Transaction=Connection.BeginTransaction(); var user=new User(); Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user)); }
        /// <summary>夹具结束才释放会话。</summary>
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>真实Dapper消费的脚本连接，仅记录命令与返回外部安排的结果。</summary>
    private sealed class ConnectionScript:DbConnection
    {
        public Queue<object> Outcomes=[]; public List<string> Sqls=[]; public List<Dictionary<string,object?>> Values=[];
        public List<Dictionary<string,NpgsqlDbType>> Types=[]; public List<DbTransaction?> Transactions=[]; public List<DbDataReader> Readers=[];
        public Action<int>? Before; public int Begins,EndTransactions,Closes;
        [AllowNull] public override string ConnectionString { get; set; }="";
        public override string Database=>"offline"; public override string DataSource=>"none"; public override string ServerVersion=>"script"; public override ConnectionState State=>ConnectionState.Open;
        /// <summary>不访问网络。</summary>
        public override void Open() { }
        /// <summary>记录越权关闭。</summary>
        public override void Close()=>Closes++;
        /// <summary>禁止切库。</summary>
        public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        /// <summary>调用者拥有事务身份。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) { Begins++; return new ScriptTransaction(this); }
        /// <summary>创建真实Dapper参数命令。</summary>
        protected override DbCommand CreateDbCommand()=>new ScriptCommand(this);
    }
    /// <summary>记录事务所有权，不模拟提交。</summary>
    private sealed class ScriptTransaction(ConnectionScript connection):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted; protected override DbConnection DbConnection=>connection;
        /// <summary>记录提交。</summary>
        public override void Commit()=>connection.EndTransactions++;
        /// <summary>记录回滚。</summary>
        public override void Rollback()=>connection.EndTransactions++;
        /// <summary>记录释放。</summary>
        protected override void Dispose(bool disposing) { if(disposing)connection.EndTransactions++; base.Dispose(disposing); }
    }
    /// <summary>真实Npgsql参数容器由Dapper绑定，脚本结果按实际命令执行顺序消费。</summary>
    private sealed class ScriptCommand(ConnectionScript connection):DbCommand
    {
        private readonly NpgsqlCommand parameters=new();
        [AllowNull] public override string CommandText { get; set; }="";
        public override int CommandTimeout { get; set; } public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; } public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }=connection; protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
        /// <summary>本接口没有取消入口。</summary>
        public override void Cancel() { }
        /// <summary>不准备网络命令。</summary>
        public override void Prepare() { }
        /// <summary>捕获真实SQL、类型化参数与事务，再返回外部脚本结果。</summary>
        private object Capture()
        {
            connection.Sqls.Add(CommandText); connection.Transactions.Add(DbTransaction);
            connection.Values.Add(parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>(object?)p.Value));
            connection.Types.Add(parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.NpgsqlDbType));
            connection.Before?.Invoke(connection.Sqls.Count); return connection.Outcomes.Dequeue();
        }
        /// <summary>执行写入的实际ADO路径。</summary>
        public override int ExecuteNonQuery()=>(int)Capture();
        /// <summary>提供真实MAX标量路径。</summary>
        public override object? ExecuteScalar()=>Capture();
        /// <summary>返回完整缓冲reader供Dapper映射。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) { var reader=((DataTable)Capture()).CreateDataReader(); connection.Readers.Add(reader); return reader; }
        /// <summary>只释放命令参数容器。</summary>
        protected override void Dispose(bool disposing) { if(disposing)parameters.Dispose(); base.Dispose(disposing); }
    }
    /// <summary>无网络会话工厂，错误分类沿既有PG实现。</summary>
    private sealed class Factory(ConnectionScript connection):IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
        /// <summary>返回本例连接。</summary>
        public IDbConnection CreateConnection()=>connection;
    }
    /// <summary>独立审计身份，不使用真实账号。</summary>
    private sealed class User:ICurrentUser { public Guid? UserId=>null; public string UserName=>PostgreSqlSalesOrderLineMigrationTests.UserName; public bool IsAuthenticated=>false; }
}
