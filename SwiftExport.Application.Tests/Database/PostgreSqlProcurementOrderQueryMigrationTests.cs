using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>从公开入口和实际Dapper命令验证采购基础查询阶段、类型、DTO及事务；内存返回不证明PG业务事实。</summary>
[TestClass]
public sealed class PostgreSqlProcurementOrderQueryMigrationTests
{
    /// <summary>独立count与当前页在同一连接顺序执行；页为空不能丢失总数，也不接管外层事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Search_KeepIndependentCountAndNullableTransaction(bool transaction)
    {
        using var f=new Fixture(transaction);using var c=new CancellationTokenSource();QueueSearch(f,17,false);
        IProcurementOrderQueryRepository repository=f.Repository;
        var result=await repository.SearchAsync(new(){PageIndex=99,PageSize=1},c.Token);
        Assert.AreEqual(17,result.TotalCount);Assert.HasCount(0,result.Items);Assert.AreEqual(99,result.PageIndex);
        Assert.HasCount(2,f.Connection.Commands);StringAssert.Contains(f.Connection.Commands[0].Sql,"COUNT(*)::integer");
        StringAssert.Contains(f.Connection.Commands[1].Sql,"h.\"order_date\" DESC NULLS LAST");
        StringAssert.Contains(f.Connection.Commands[1].Sql,"get_byte(uuid_send(h.\"uid\"),10)");
        Assert.IsFalse(f.Connection.Commands[1].Sql.Contains("JOIN",StringComparison.Ordinal));
        AssertOwnership(f,c.Token,transaction);
    }

    /// <summary>页参数原样保留，不应用销售Manager默认值或服务以外的新上限。</summary>
    [TestMethod]
    [DataRow(0,0,0)] [DataRow(1,500,0)] [DataRow(3,7,14)]
    public async Task Search_KeepRawPaging(int page,int size,int skip)
    {
        using var f=new Fixture(false);QueueSearch(f,0,false);var result=await f.Repository.SearchAsync(new(){PageIndex=page,PageSize=size},default);
        Assert.AreEqual(page,result.PageIndex);Assert.AreEqual(size,result.PageSize);
        Assert.AreEqual(skip,f.Connection.Commands[1].Values["Skip"]);Assert.AreEqual(size,f.Connection.Commands[1].Values["Take"]);
    }

    /// <summary>nullable标量与Guid.Empty具有不同值；NULL参数仍显式uuid/integer/text，未知枚举不在仓储新增校验。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Search_BindNullableTypesAndEmptyIdentity(bool empty)
    {
        using var f=new Fixture(false);QueueSearch(f,0,false);
        await f.Repository.SearchAsync(new(){CustomerId=empty?Guid.Empty:null,SupplierId=empty?Guid.Empty:null,ConfirmationStatus=empty?(ConfirmationStatusEnum)99:null},default);
        var command=f.Connection.Commands[0];
        foreach(var name in new[]{"CustomerId","SupplierId"}){Assert.AreEqual(NpgsqlDbType.Uuid,command.Types[name]);Assert.AreEqual(empty?(object)Guid.Empty:DBNull.Value,command.Values[name]);}
        Assert.AreEqual(NpgsqlDbType.Integer,command.Types["ConfirmationStatus"]);Assert.AreEqual(empty?(object)99:DBNull.Value,command.Values["ConfirmationStatus"]);
        Assert.AreEqual(NpgsqlDbType.Text,command.Types["Keyword"]);Assert.AreEqual(DBNull.Value,command.Values["Keyword"]);
        foreach(var cmd in f.Connection.Commands){Assert.IsFalse(cmd.Sql.Contains("OriginKind",StringComparison.Ordinal));Assert.IsFalse(cmd.Sql.Contains("lifecycle_status=",StringComparison.Ordinal));}
    }

    /// <summary>本查询只Trim及转义限定字面文本；内空格/问号保持字面，不能采用销售通配构造器。</summary>
    [TestMethod]
    [DataRow(" a b? ","%a b?%")] [DataRow("[ab]","%~[ab]%")] [DataRow("~%_","%~~~%~_%")]
    [DataRow("x';DROP TABLE test;--","%x';DROP TABLE test;--%")]
    public async Task Search_KeepLiteralPattern(string keyword,string expected)
    {
        using var f=new Fixture(false);QueueSearch(f,0,false);await f.Repository.SearchAsync(new(){Keyword=keyword},default);
        Assert.AreEqual(expected,f.Connection.Commands[0].Values["Keyword"]);Assert.IsFalse(f.Connection.Commands[0].Sql.Contains(keyword,StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Commands[0].Sql,"COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
    }

    /// <summary>每个列表DTO字段具有显式别名；头历史客户名不被当前主数据替换。</summary>
    [TestMethod]
    public async Task Search_MapSnapshotAndAllAliases()
    {
        using var f=new Fixture(false);QueueSearch(f,1,true);var row=(await f.Repository.SearchAsync(new(),default)).Items.Single();
        Assert.AreEqual("历史客户",row.CustomerName);Assert.AreEqual(ConfirmationStatusEnum.已确认,row.ConfirmationStatus);Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Closed,row.LifecycleStatus);
        var sql=f.Connection.Commands[1].Sql;foreach(var property in typeof(ProcurementOrderListItemDto).GetProperties())StringAssert.Contains(sql,"AS \""+property.Name+"\"");
    }

    /// <summary>空头仍读取完整行阶段；正常头沿源装入缓冲行，NULL价格与非标映射保持。</summary>
    [TestMethod]
    [DataRow(false,false)] [DataRow(false,true)] [DataRow(true,false)] [DataRow(true,true)]
    public async Task Detail_KeepTwoStagesEvenWhenHeadMissing(bool head,bool transaction)
    {
        using var f=new Fixture(transaction);using var c=new CancellationTokenSource();QueueDetail(f,head);
        var response=await f.Repository.GetDetailAsync(Guid.Empty,c.Token);Assert.HasCount(2,f.Connection.Commands);Assert.HasCount(0,f.Connection.Results);
        if(!head)Assert.IsNull(response);
        else{Assert.IsNotNull(response);Assert.AreEqual(10m,response.TotalQuantity);Assert.AreEqual(3m,response.CnyTotalAmount);var line=response.Lines.Single();Assert.IsTrue(line.IsNonStandard);Assert.AreEqual(Guid.Empty,line.ProductId);Assert.IsNull(line.UnitPrice);Assert.IsNull(line.MainImage);Assert.AreEqual(10m,line.Quantity);}
        AssertOwnership(f,c.Token,transaction);Assert.AreEqual(Guid.Empty,f.Connection.Commands[0].Values["Uid"]);Assert.AreEqual(NpgsqlDbType.Uuid,f.Connection.Commands[0].Types["Uid"]);
        StringAssert.Contains(f.Connection.Commands[0].Sql,"COALESCE(SUM(l.\"unit_amount\"),0)");Assert.IsFalse(f.Connection.Commands[0].Sql.Contains("SUM(l.\"tax_amount\")",StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Commands[1].Sql,"l.\"quantity\" AS \"Quantity\"");Assert.IsFalse(f.Connection.Commands[1].Sql.Contains("l.\"qty\"",StringComparison.Ordinal));
    }

    /// <summary>完整源DTO别名、旧字段映射和客供件CASE均保留；BaseDto.IsDeleted本来未投影，默认false。</summary>
    [TestMethod]
    public async Task Detail_KeepEverySourceProjectionAndCase()
    {
        using var f=new Fixture(false);QueueDetail(f,true);await f.Repository.GetDetailAsync(Guid.NewGuid(),default);
        foreach(var property in typeof(PurchaseOrderDetailDto).GetProperties().Where(p=>p.Name!="Lines"))StringAssert.Contains(f.Connection.Commands[0].Sql,"AS \""+property.Name+"\"");
        foreach(var property in typeof(PurchaseOrderLineDetailDto).GetProperties().Where(p=>p.Name!="IsDeleted"))StringAssert.Contains(f.Connection.Commands[1].Sql,"AS \""+property.Name+"\"");
        StringAssert.Contains(f.Connection.Commands[0].Sql,"h.\"inletr_hose_type\" AS \"InletHoseType\"");
        StringAssert.Contains(f.Connection.Commands[1].Sql,"THEN l.\"delivery_supplier_code_snapshot\" ELSE '' END");
        StringAssert.Contains(f.Connection.Commands[1].Sql,"THEN l.\"delivery_target_supplier_id\" END");
    }

    /// <summary>预取消和两查询边界均停止后续命令，异常原样传播且调用者事务仍由调用者拥有。</summary>
    [TestMethod]
    [DataRow(false,-1)] [DataRow(false,0)] [DataRow(false,1)] [DataRow(true,-1)] [DataRow(true,0)] [DataRow(true,1)]
    public async Task Cancellation_KeepBothMethodsAndBoundaries(bool detail,int boundary)
    {
        using var f=new Fixture(true);using var c=new CancellationTokenSource();if(detail)QueueDetail(f,true);else QueueSearch(f,0,false);
        if(boundary<0)c.Cancel();else f.Connection.BeforeCommand=i=>{if(i==boundary)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>{if(detail)await f.Repository.GetDetailAsync(Guid.NewGuid(),c.Token);else await f.Repository.SearchAsync(new(),c.Token);});
        Assert.AreEqual(Math.Max(boundary,0),f.Connection.Commands.Count);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>记录同一token/可空事务和零仓储事务操作，不把夹具创建外层事务算成仓储动作。</summary>
    private static void AssertOwnership(Fixture f,CancellationToken token,bool transaction)
    {foreach(var command in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,command.Transaction);Assert.AreEqual(token,f.Connection.LastCancellation);Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);}

    /// <summary>队列只模拟源结果集，COUNT始终独立于页行。</summary>
    private static void QueueSearch(Fixture f,int count,bool row)
    {
        f.Connection.Results.Enqueue(Table(("Count",count)));
        var page=Table(("PurchaseOrderId",Guid.NewGuid()),("CustomerName","历史客户"),("ConfirmationStatus",1),("LifecycleStatus",8));if(!row)page.Rows.Clear();f.Connection.Results.Enqueue(page);
    }

    /// <summary>即使头缺失也提供行结果，以验证仓储不会跳过本应完整消费的第二阶段。</summary>
    private static void QueueDetail(Fixture f,bool head)
    {
        var header=Table(("PurchaseOrderId",Guid.NewGuid()),("TotalQuantity",10m),("CnyTotalAmount",3m));if(!head)header.Rows.Clear();f.Connection.Results.Enqueue(header);
        f.Connection.Results.Enqueue(Table(("Uid",Guid.NewGuid()),("Quantity",10m),("ProductId",DBNull.Value),("IsNonStandard",true),("UnitPrice",DBNull.Value),("MainImage",DBNull.Value)));
    }

    /// <summary>让实际Dapper消费带CLR名称及数据库NULL的数据表，不计算业务筛选。</summary>
    private static DataTable Table(params (string Name,object Value)[] values)
    {var table=new DataTable();foreach(var value in values)table.Columns.Add(value.Name,value.Value is DBNull?typeof(object):value.Value.GetType());table.Rows.Add(values.Select(x=>x.Value).ToArray());return table;}

    /// <summary>每例独享内存会话与可空事务，不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlProcurementOrderQueryRepository Repository;
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
        public string UserName => "采购基础查询离线夹具";
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
            /// <summary>用真实Dapper标量通道读取独立count返回表，不伪造异常分类。</summary>
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
