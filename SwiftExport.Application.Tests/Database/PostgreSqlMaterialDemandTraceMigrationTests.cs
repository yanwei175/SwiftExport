using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Core.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际调用生产Trace经Dapper记录六阶段、冻结数组和外层事务；替身不证明PG SQL执行。</summary>
[TestClass]
public sealed class PostgreSqlMaterialDemandTraceMigrationTests
{
    /// <summary>阶段顺序与token/事务均按冻结协议，空值数组不重查scope、不二次Guard。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Stages_FreezePairedCoverageAndBorrowCaller(bool transaction)
    {
        using var f = new Fixture(transaction); using var c = new CancellationTokenSource();
        var uid=Guid.NewGuid(); var link=Guid.NewGuid(); var line=Guid.NewGuid();
        Queue(f, true, link, line, null, 5m);
        var result=await f.Repository.GetTraceAsync(uid,c.Token);
        Assert.IsNotNull(result); Assert.AreEqual(5m,result.AssociatedPurchaseQty); Assert.AreEqual(0m,result.CurrentReceiptCoverageQty);
        Assert.HasCount(6,f.Connection.Commands);
        var sql=f.Connection.Commands.Select(x=>x.Sql).ToArray();
        StringAssert.Contains(sql[0],"SELECT DISTINCT target_line_id"); StringAssert.Contains(sql[1],"SELECT EXISTS");
        StringAssert.Contains(sql[2],"AS \"SourceDemandVersion\""); StringAssert.Contains(sql[3],"selected.gross_demand_id");
        StringAssert.Contains(sql[4],"WITH eligible AS"); StringAssert.Contains(sql[4],"ROWS UNBOUNDED PRECEDING");
        StringAssert.Contains(sql[4],"e.source_line_number NULLS LAST,ARRAY[get_byte(uuid_send(e.link_id),10)");
        Assert.IsFalse(sql[4].Contains("decomposition_revision",StringComparison.Ordinal));
        Assert.IsFalse(sql[4].Contains("@PurchaseLineIds",StringComparison.Ordinal));
        StringAssert.Contains(sql[5],"unnest(@CoverageLinkIds::uuid[], @CoverageTargetLineIds::uuid[], @CoverageQuantities::numeric[])");
        StringAssert.Contains(sql[5],"ORDER BY report.sequence DESC,ARRAY[get_byte(uuid_send(report.uid),10)");
        Assert.IsFalse(sql[5].Contains("WITH eligible",StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[]{link},(Guid[])f.Connection.Commands[5].Values["CoverageLinkIds"]!);
        CollectionAssert.AreEqual(new[]{line},(Guid[])f.Connection.Commands[5].Values["CoverageTargetLineIds"]!);
        Assert.IsNull(((decimal?[])f.Connection.Commands[5].Values["CoverageQuantities"]!)[0]);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Numeric,f.Connection.Commands[5].Types["CoverageQuantities"]);
        foreach(var command in f.Connection.Commands) Assert.AreSame(f.Session.Transaction,command.Transaction);
        Assert.AreEqual(c.Token,f.Connection.LastCancellation); Assert.AreEqual(transaction?1:0,f.Connection.Begins);
        Assert.AreEqual(0,f.Connection.Commits); Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>没有覆盖记录仍绑定三个空数组，不补伪造的零覆盖行。</summary>
    [TestMethod]
    public async Task EmptyCoverage_StaysEmptyAndKeepsTree()
    {
        using var f=new Fixture(false); Queue(f,true,Guid.NewGuid(),Guid.NewGuid(),0m,0m);
        f.Connection.Results.Clear(); Queue(f,true,Guid.NewGuid(),Guid.NewGuid(),0m,0m,false);
        var result=await f.Repository.GetTraceAsync(Guid.NewGuid());
        Assert.HasCount(1,result!.TreeItems); Assert.HasCount(0,(Guid[])f.Connection.Commands[5].Values["CoverageLinkIds"]!);
        Assert.HasCount(0,(decimal?[])f.Connection.Commands[5].Values["CoverageQuantities"]!);
    }

    /// <summary>空头仍先缓冲tree；不执行覆盖/采购，后续阶段失败也不会被早返回吞掉。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task MissingHeader_BuffersTreeBeforeNull(bool failTree)
    {
        using var f=new Fixture(true); Queue(f,false,Guid.NewGuid(),Guid.NewGuid(),0m,0m);
        if(failTree) f.Connection.BeforeCommand=i=>{if(i==3)throw new InvalidOperationException("tree阶段");};
        if(failTree) await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.GetTraceAsync(Guid.NewGuid()));
        else { Assert.IsNull(await f.Repository.GetTraceAsync(Guid.NewGuid())); Assert.HasCount(4,f.Connection.Commands); }
        Assert.IsNotNull(f.Session.Transaction); Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>源C#理由保持Ordinal、Unicode空白和优先级；超量保留原associated数值并使coverage未知。</summary>
    [TestMethod]
    [DataRow("over","采购承接超量需核实")] [DataRow("fingerprint","来源版本或需求指纹需核实")]
    [DataRow("md-case","来源版本或需求指纹需核实")] [DataRow("unit","物料单位需核实")]
    [DataRow("history","历史拆解修订")] [DataRow("purchase","采购目标、客户、单位或来源修订需核实")]
    public async Task SourceReasons_KeepOriginalSummary(string branch,string reason)
    {
        using var f=new Fixture(false); Queue(f,true,Guid.NewGuid(),Guid.NewGuid(),1m,branch=="over"?12m:5m);
        var tables=f.Connection.Results.ToArray(); var row=tables[2].Rows[0];
        if(branch=="fingerprint") row["SourceDemandBasisFingerprintSnapshot"]="a";
        if(branch=="md-case") row["SourceType"]="md";
        if(branch=="unit") row["Unit"]="\u2003";
        if(branch=="history") row["LifecycleStatus"]=8;
        if(branch=="purchase") tables[5].Rows[0]["ReviewReason"]="采购目标需核实";
        var result=await f.Repository.GetTraceAsync(Guid.NewGuid()); Assert.AreEqual(reason,result!.ReviewReason);
        if(branch=="over") Assert.AreEqual(12m,result.AssociatedPurchaseQty); else Assert.IsNull(result.AssociatedPurchaseQty);
        Assert.IsNull(result.CurrentReceiptCoverageQty);
    }

    /// <summary>各命令取消原样传播；仓储不提交/回滚已有事务，预取消不发命令。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task Cancellation_StopsAtEveryStage(int stage)
    {
        using var f=new Fixture(true); using var c=new CancellationTokenSource(); Queue(f,true,Guid.NewGuid(),Guid.NewGuid(),0m,1m);
        if(stage<0)c.Cancel(); else f.Connection.BeforeCommand=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetTraceAsync(Guid.NewGuid(),c.Token));
        Assert.AreEqual(0,f.Connection.Commits); Assert.AreEqual(0,f.Connection.Rollbacks); Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>共享typedGuard经实际服务分类，保留既有固定消息并阻止头/覆盖读取。</summary>
    [TestMethod]
    public async Task ScopedGuard_UsesOriginalServiceMessage()
    {
        using var f=new Fixture(true); f.Connection.Results.Enqueue(Table(("PurchaseLineId",Guid.NewGuid()))); f.Connection.Results.Enqueue(Table(("Invalid",true)));
        var service=new MaterialDemandQueryService(new ServiceBaseContext(f.Session,NullLoggerFactory.Instance),f.Repository);
        var result=await service.GetTraceAsync(Guid.NewGuid()); Assert.IsFalse(result.Success);
        Assert.AreEqual("正式入库反向历史需核实，收货量未知。",result.Message); Assert.HasCount(2,f.Connection.Commands);
        Assert.IsNotNull(f.Session.Transaction); Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>完整接口实际可解析；DTO每个字段由显式CLR别名承载，覆盖没有第二次查询。</summary>
    [TestMethod]
    public async Task PublicContractAndDtoAliases_AreComplete()
    {
        using var f=new Fixture(false);Queue(f,true,Guid.NewGuid(),Guid.NewGuid(),1m,3m);
        IMaterialDemandQueryRepository repository=f.Repository;await repository.GetTraceAsync(Guid.NewGuid());
        var purchase=f.Connection.Commands[5].Sql;foreach(var field in typeof(MaterialDemandTracePurchaseDto).GetProperties())StringAssert.Contains(purchase,"AS \""+field.Name+"\"");
        foreach(var field in typeof(MaterialDemandTraceTreeItemDto).GetProperties())StringAssert.Contains(f.Connection.Commands[3].Sql,"AS \""+field.Name+"\"");
        foreach(var command in f.Connection.Commands){Assert.IsFalse(command.Sql.Contains("#",StringComparison.Ordinal));Assert.IsFalse(command.Sql.Contains("DROP TABLE",StringComparison.Ordinal));}
    }

    /// <summary>构造各阶段原始返回表；不在替身计算分摊，只验证实际Dapper映射及C#汇总。</summary>
    private static void Queue(Fixture f,bool head,Guid link,Guid line,decimal? coverage,decimal associated,bool frozen=true)
    {
        f.Connection.Results.Enqueue(Table(("PurchaseLineId",line))); f.Connection.Results.Enqueue(Table(("Invalid",false)));
        var header=Table(("NetDemandId",Guid.NewGuid()),("GrossDemandId",Guid.NewGuid()),("NetQty",10m),("LifecycleStatus",0),("SourceLifecycleStatus",0),("CustomerId",Guid.NewGuid()),("SourceType","MD"),("SourceDemandVersion",1),("SourceDemandVersionSnapshot",1),("SourceVersion",(object)DBNull.Value),("SourceVersionSnapshot",DBNull.Value),("DemandBasisFingerprint","A"),("SourceDemandBasisFingerprintSnapshot","A"),("Unit","KG"));
        if(!head)header.Rows.Clear(); f.Connection.Results.Enqueue(header);
        f.Connection.Results.Enqueue(Table(("NetDemandId",Guid.NewGuid()),("Sku","树行")));
        var covers=Table(("LinkId",link),("TargetLineId",line),("CurrentReceiptCoverageQty",coverage??(object)DBNull.Value));
        if(!frozen)covers.Rows.Clear(); f.Connection.Results.Enqueue(covers);
        f.Connection.Results.Enqueue(Table(("LinkId",link),("PurchaseLineId",line),("IsActiveLink",true),("LinkedQty",associated),("CurrentReceiptCoverageQty",coverage??(object)DBNull.Value),("ReviewReason","")));
    }

    /// <summary>显式列类型及DBNull保持可空证据；单行返回并允许场景删除头行。</summary>
    private static DataTable Table(params (string Name,object Value)[] values)
    {
        var table=new DataTable(); foreach(var v in values) table.Columns.Add(v.Name,v.Value is DBNull?(v.Name.Contains("CoverageQty",StringComparison.Ordinal)?typeof(decimal):typeof(int)):v.Value.GetType());
        table.Rows.Add(values.Select(v=>v.Value).ToArray()); return table;
    }
    /// <summary>每例独享内存连接，使用生产会话和审计上下文但不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlMaterialDemandQueryRepository Repository;
        public Fixture(bool transaction)
        {
            Session = new(new Factory(Connection));
            if (transaction) Session.Transaction = Connection.BeginTransaction();
            var user = new User();
            Repository = new(new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>只返回指定离线连接。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    { public IDbConnection CreateConnection() => connection; public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance; }

    /// <summary>提供固定测试审计身份，查询不调用打戳。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料来源离线夹具";
        public bool IsAuthenticated => false;
    }

    /// <summary>记录实际查询命令、事务和参数，返回预先构造的数据表；没有提供者执行通道。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public readonly Queue<DataTable> Results = new();
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction, Dictionary<string, NpgsqlDbType> Types)> Commands = [];
        public CancellationToken LastCancellation;
        public int Begins, Commits, Rollbacks;
        public Action<int>? BeforeCommand;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new Transaction(this); }
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>仅记录外层事务动作。</summary>
        private sealed class Transaction(RecordingConnection connection) : DbTransaction
        {
            protected override DbConnection DbConnection => connection;
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => connection.Rollbacks++;
        }

        /// <summary>仅借用 Npgsql 参数容器；执行结果完全由内存数据表提供。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
        {
            private readonly NpgsqlCommand parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            /// <summary>用真实Dapper标量通道读取Guard返回表，不伪造异常分类。</summary>
            public override object ExecuteScalar() { using var reader = ExecuteDbDataReader(CommandBehavior.Default); return reader.Read() ? reader.GetValue(0) : DBNull.Value; }
            /// <summary>标量通道保留原token，命令与数据表由同一记录连接提供。</summary>
            public override Task<object?> ExecuteScalarAsync(CancellationToken token) { connection.LastCancellation=token; token.ThrowIfCancellationRequested(); return Task.FromResult<object?>(ExecuteScalar()); }
            /// <summary>记录执行时SQL、参数类型和外层事务，再返回本阶段数据表。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                connection.BeforeCommand?.Invoke(connection.Commands.Count);
                connection.Commands.Add((CommandText, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value), DbTransaction, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType)));
                return connection.Results.Dequeue().CreateDataReader();
            }
            /// <summary>异步查询在执行前观察取消并记录token，不创建数据库连接。</summary>
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
