using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际Dapper两入口的投影、批次阶段、参数和借用生命周期；ADO脚本不解释SQL商业算法。</summary>
[TestClass]
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>两入口完整DTO由真实Dapper还原，业务量保留SQL输出，父事务可空且不结束。</summary>
    [TestMethod, DataRow(false,false), DataRow(false,true), DataRow(true,false), DataRow(true,true)]
    public async Task Offline_MapAllFieldsAndBorrow(bool detail, bool transactional)
    {
        using var f = new ReadFixture(transactional); using var cts = new CancellationTokenSource();
        if (detail)
        {
            var head = Sample<InboundHeaderDetailDto>(); var line = Sample<InboundLineDetailDto>(); line.IsDeleted=false;
            var rows = Rows(line); rows.Columns.Remove("IsDeleted"); f.Connection.Results=[Rows(head),rows];
            var result = await f.Repository.GetDetailAsync(Guid.NewGuid(),cts.Token); Assert.IsNotNull(result);
            Same(head,result.Header,f.Connection.Sql); Same(line,result.Lines.Single(),f.Connection.Sql, "IsDeleted");
        }
        else
        {
            var row=Sample<InboundManagerListItemDto>(); f.Connection.Results=[Count(31),Rows(row)];
            var result=await f.Repository.SearchAsync(new(){PageIndex=5,PageSize=7},cts.Token);
            Assert.AreEqual(31,result.TotalCount);Assert.AreEqual(5,result.PageIndex);Assert.AreEqual(7,result.PageSize);Same(row,result.Items.Single(),f.Connection.Sql);
        }
        Assert.AreEqual(detail?2:1,f.Connection.Commands); Assert.AreSame(f.Transaction,f.Session.Transaction);
        Assert.AreSame(f.Transaction,f.Connection.LastTransaction); Assert.AreEqual(cts.Token,f.Connection.Token);
        Assert.AreEqual(0,f.Connection.EndTransactions);Assert.AreEqual(0,f.Connection.Closes);Assert.IsTrue(f.Connection.Reader!.IsClosed);
        foreach(var sql in f.Connection.Sqls) foreach(var word in new[]{"FOR UPDATE","CREATE ","INSERT ","UPDATE ","DELETE ","DROP "}) Assert.IsFalse(sql.Contains(word,StringComparison.Ordinal));
    }

    /// <summary>分页保留int先减一和long乘法、原始响应，不把非法Take或超大页裁剪。</summary>
    [TestMethod,DataRow(0,20),DataRow(-2,5),DataRow(1,0),DataRow(1,-7),DataRow(int.MinValue,1),DataRow(int.MaxValue,200),DataRow(3,201)]
    public async Task Offline_RawPaging(int index,int size)
    {
        using var f=new ReadFixture(false);f.Connection.Results=[Count(17),Rows(new InboundManagerListItemDto()).Clone()];
        var result=await f.Repository.SearchAsync(new(){PageIndex=index,PageSize=size},default);
        Assert.AreEqual((long)unchecked(index-1)*size,f.Connection.Values["Skip"]);Assert.AreEqual(size,f.Connection.Values["Take"]);
        Assert.AreEqual(index,result.PageIndex);Assert.AreEqual(size,result.PageSize);Assert.AreEqual(17,result.TotalCount);Assert.HasCount(0,result.Items);
        StringAssert.Contains(f.Connection.Sql,"LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip");
        Assert.AreEqual(NpgsqlDbType.Bigint,f.Connection.Types["Skip"]);Assert.AreEqual(NpgsqlDbType.Integer,f.Connection.Types["Take"]);
    }

    /// <summary>旧可空枚举规范化为显式整数数组，字面搜索仍只使用原三个字段且不执行历史门禁。</summary>
    [TestMethod,DataRow(null),DataRow(" \t "),DataRow("  a~%_[  ")]
    public async Task Offline_SearchParameters(string? keyword)
    {
        using var f=new ReadFixture(false);f.Connection.Results=[Count(0),Rows(new InboundManagerListItemDto()).Clone()];
        await f.Repository.SearchAsync(new(){Keyword=keyword!,DocumentType=(InboundDocumentTypeEnum)999,PostingStatus=null},default);
        CollectionAssert.AreEqual(new[]{999},(int[])f.Connection.Values["DocumentTypes"]!);
        Assert.IsEmpty((int[])f.Connection.Values["PostingStatuses"]!);
        Assert.AreEqual(string.IsNullOrWhiteSpace(keyword)?DBNull.Value:(object)"%a~~~%~_~[%",f.Connection.Values["Keyword"]);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,f.Connection.Types["PostingStatuses"]);Assert.AreEqual(NpgsqlDbType.Text,f.Connection.Types["Keyword"]);
        Assert.AreEqual(1,f.Connection.Commands);Assert.IsFalse(f.Connection.Sql.Contains("inventory_ledger_entry",StringComparison.Ordinal));
        foreach(var word in new[]{"h.lifecycle_status=","h.creation_mode=","product.","h.remark COLLATE"}) Assert.IsFalse(f.Connection.Sql.Contains(word,StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Sql,"ESCAPE '~'");StringAssert.Contains(f.Connection.Sql,"ORDER BY h.receipt_date DESC,ARRAY[get_byte(uuid_send(h.uid),10)");
    }

    /// <summary>历史检查先限定目标头来源；缺头不受无关历史阻断，相关异常仍返回专用信号且不读详情。</summary>
    [TestMethod]
    public async Task Offline_ScopedHistoryPreservesMissingHeaderAndRejectsRelatedEvidence()
    {
        using var f=new ReadFixture(true);
        f.Connection.Results=[Rows(new InboundHeaderDetailDto()).Clone(),Rows(new InboundLineDetailDto()).Clone()];
        Assert.IsNull(await f.Repository.GetDetailAsync(Guid.Empty,default));Assert.AreEqual(2,f.Connection.Commands);
        StringAssert.Contains(f.Connection.Sqls[0],"target.uid=@Uid");
        StringAssert.Contains(f.Connection.Sqls[0],"target.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Sqls[0],"receipt_head_reviews");
        f.Connection.InvalidHistory=true;
        var error=await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(Guid.NewGuid(),default));
        Assert.AreEqual(DatabaseFailureKind.UnverifiedReceiptHistory,f.Session.Errors.Classify(error));Assert.AreEqual(3,f.Connection.Commands);
        f.Connection.Results=[Count(0),Rows(new InboundManagerListItemDto()).Clone()];await f.Repository.SearchAsync(new(),default);Assert.AreEqual(4,f.Connection.Commands);
        Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>即使头空也读取行，非法行数量暴露Dapper技术错误并释放reader，不把缺头提前返回。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_MissingHeaderStillBuffersLines(bool broken)
    {
        using var f=new ReadFixture(false);var line=new DataTable();line.Columns.Add("Quantity",typeof(string));line.Rows.Add(broken?"不是数量":"12.3456");
        f.Connection.Results=[Rows(new InboundHeaderDetailDto()).Clone(),line];
        if(broken)await Assert.ThrowsAsync<DataException>(()=>f.Repository.GetDetailAsync(Guid.NewGuid(),default));
        else Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));
        Assert.AreEqual(2,f.Connection.Commands);Assert.IsTrue(f.Connection.Reader!.IsClosed);
    }

    /// <summary>空来源NULL沿DTO默认，不补SQL空串；数量与草稿仍由LATERAL/CASE，历史JOIN不排删。</summary>
    [TestMethod]
    public async Task Offline_NullsAndSqlBoundaries()
    {
        using var f=new ReadFixture(true);var line=Rows(new InboundLineDetailDto());foreach(var c in new[]{"SourceVersion","OriginalQty","SourceNo","BoNo","LinkSku","ImagePath","DescriptionSnapshot"})line.Rows[0][c]=DBNull.Value;
        f.Connection.Results=[Rows(new InboundHeaderDetailDto()),line];var result=await f.Repository.GetDetailAsync(Guid.Empty,default);Assert.IsNotNull(result);
        var actual=result.Lines.Single();Assert.AreEqual(0,actual.SourceVersion);Assert.AreEqual(0m,actual.OriginalQty);Assert.AreEqual("",actual.SourceNo);Assert.AreEqual("",actual.ImagePath);
        StringAssert.Contains(f.Connection.Sql,"p.quantity AS \"OriginalQty\"");StringAssert.Contains(f.Connection.Sql,"LEFT JOIN LATERAL");StringAssert.Contains(f.Connection.Sql,"COUNT(DISTINCT dh.uid)::integer");
        StringAssert.Contains(f.Connection.Sql,"dh.uid<>@Uid");StringAssert.Contains(f.Connection.Sql,"receipt_head_reviews");
        StringAssert.Contains(f.Connection.Sql,"CASE WHEN p.quantity>COALESCE(received.received_qty,0) THEN p.quantity-COALESCE(received.received_qty,0) ELSE 0 END");
        foreach(var word in new[]{"product.is_deleted","product.is_active","p.source_type","l.is_deleted AS"})Assert.IsFalse(f.Connection.Sql.Contains(word,StringComparison.Ordinal),word);
        Assert.AreEqual(Guid.Empty,f.Connection.Values["Uid"]);Assert.AreEqual(NpgsqlDbType.Uuid,f.Connection.Types["Uid"]);
    }

    /// <summary>预取消入口不执行SQL；执行等待取消/数据库技术错误不被吞掉或结束父事务。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_CancellationAndFailure(bool detail)
    {
        using var f=new ReadFixture(true);await Assert.ThrowsAsync<OperationCanceledException>(()=>Read(f,detail,new CancellationToken(true)));Assert.AreEqual(0,f.Connection.Commands);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cts=new CancellationTokenSource();
        f.Connection.Before=async token=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);};var pending=Read(f,detail,cts.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);
        var error=new InvalidOperationException("查询故障");f.Connection.Before=_=>throw error;Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(f,detail,default)));
        Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>使用原方法参数调用，取消验证不模拟业务。</summary>
    private static Task Read(ReadFixture f,bool detail,CancellationToken token)=>detail?f.Repository.GetDetailAsync(Guid.NewGuid(),token):f.Repository.SearchAsync(new(),token);
    /// <summary>比较完整DTO字段及显式quoted别名，允许源行IsDeleted未投影。</summary>
    private static void Same(object expected,object actual,string sql,string? omitted=null)
    {foreach(var p in expected.GetType().GetProperties()){Assert.AreEqual(p.GetValue(expected),p.GetValue(actual),p.Name);if(p.Name!=omitted)StringAssert.Contains(sql,"AS \""+p.Name+"\"");}}
    /// <summary>独立count结果与页数量刻意不同，不由测试脚本计算SQL。</summary>
    private static DataTable Count(int value){var table=new DataTable();table.Columns.Add("Count",typeof(int));table.Rows.Add(value);return table;}
    /// <summary>未知结构和第二批技术异常原样传播，历史检查成功不能把缺头或后续失败吞掉。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_DetailBatchFailureAfterGuard(bool schema)
    {
        using var f=new ReadFixture(true);Exception error=schema?new PostgresException("缺列","ERROR","ERROR","42703"):new InvalidOperationException("详情批次失败");
        f.Connection.Before=_=>{if(f.Connection.Commands==2)throw error;return Task.CompletedTask;};
        try{await f.Repository.GetDetailAsync(Guid.Empty,default);Assert.Fail("应暴露第二批失败");}catch(Exception actual){Assert.AreSame(error,actual);}
        Assert.AreEqual(2,f.Connection.Commands);Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(0,f.Connection.EndTransactions);
        Assert.AreEqual(schema?DatabaseFailureKind.SchemaMismatch:DatabaseFailureKind.Unknown,f.Session.Errors.Classify(error));
    }

    /// <summary>仓储直调用不引入服务分页/长度校验，空request仍是技术失败。</summary>
    [TestMethod]
    public async Task Offline_DirectRequestKeepsTechnicalBoundary()
    {
        using var f=new ReadFixture(false);await Assert.ThrowsAsync<NullReferenceException>(()=>f.Repository.SearchAsync(null!,default));Assert.AreEqual(0,f.Connection.Commands);
        f.Connection.Results=[Count(0),Rows(new InboundManagerListItemDto()).Clone()];var keyword=new string('a',201);
        await f.Repository.SearchAsync(new(){Keyword=keyword,DocumentType=null,PageSize=int.MaxValue},default);
        Assert.AreEqual("%"+keyword+"%",f.Connection.Values["Keyword"]);Assert.IsEmpty((int[])f.Connection.Values["DocumentTypes"]!);Assert.AreEqual(int.MaxValue,f.Connection.Values["Take"]);
    }

    /// <summary>有效集合绑定为 integer[]，空选覆盖旧单值；计数和当前页使用同一集合谓词。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2),DataRow(3)]
    public async Task Offline_MultiFiltersBindTypedArraysBeforePaging(int mode)
    {
        using var f=new ReadFixture(true);f.Connection.Results=[Count(7),Rows(new InboundManagerListItemDto()).Clone()];
        var request=new InboundManagerSearchRequest{DocumentType=InboundDocumentTypeEnum.ProductionReceipt,PostingStatus=InboundPostingStatusEnum.Posted,PageIndex=2,PageSize=3};
        if(mode==1){request.DocumentTypes=[];request.PostingStatuses=[];}
        if(mode>=2)request.DocumentTypes=[InboundDocumentTypeEnum.PurchaseReceipt,InboundDocumentTypeEnum.OpeningBalance];
        if(mode==2)request.PostingStatuses=[InboundPostingStatusEnum.Unposted,InboundPostingStatusEnum.Reversed];
        var result=await f.Repository.SearchAsync(request,default);
        CollectionAssert.AreEqual(mode==0?new[]{20}:mode==1?[]:new[]{10,100},(int[])f.Connection.Values["DocumentTypes"]!);
        CollectionAssert.AreEqual(mode==1?[]:mode==2?new[]{0,8}:new[]{1},(int[])f.Connection.Values["PostingStatuses"]!);
        foreach(var name in new[]{"DocumentTypes","PostingStatuses"})Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,f.Connection.Types[name]);
        Assert.AreEqual(2,f.Connection.Sql.Split("h.document_type=ANY(@DocumentTypes)").Length-1);
        Assert.AreEqual(2,f.Connection.Sql.Split("h.posting_status=ANY(@PostingStatuses)").Length-1);
        Assert.AreEqual(3L,f.Connection.Values["Skip"]);Assert.AreEqual(3,f.Connection.Values["Take"]);
        Assert.AreEqual(7,result.TotalCount);Assert.AreEqual(2,result.PageIndex);Assert.AreSame(f.Transaction,f.Session.Transaction);
    }
    /// <summary>丰富原始字段样本，数值刻意不协调，验证仓储不重新计算对账或Available。</summary>
    private static T Sample<T>() where T:new()
    {
        var item=new T();foreach(var p in typeof(T).GetProperties()){var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;object value=t==typeof(Guid)?Guid.NewGuid():t==typeof(string)?" 原快照 ":t==typeof(DateTime)?new DateTime(2026,10,5,12,34,56):t==typeof(decimal)?12.3456m:t==typeof(long)?1234567890123L:t==typeof(bool)?true:t.IsEnum?Enum.ToObject(t,900):7;p.SetValue(item,value);}return item;
    }
    /// <summary>提供真正ADO列结构，不解释SQL或业务CASE。</summary>
    private static DataTable Rows(object row)
    {
        var table=new DataTable();var props=row.GetType().GetProperties();foreach(var p in props){var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;table.Columns.Add(p.Name,t.IsEnum?typeof(int):t);}table.Rows.Add(props.Select(p=>{var v=p.GetValue(row);return v is Enum?Convert.ToInt32(v):v??DBNull.Value;}).ToArray());return table;
    }
    /// <summary>独占无网络夹具，不修改或共享旧测试的可变结果集。</summary>
    private sealed class ReadFixture : IDisposable
    {
        public ReadConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlInboundQueryRepository Repository { get; }
        public PostgreSqlInboundSourceRepository Sources { get; }
        /// <summary>构建既有上下文和调用者可空事务。</summary>
        public ReadFixture(bool transactional)
        {
            Session = new(new ReadFactory(Connection)); if (transactional) Session.Transaction = Transaction = Connection.BeginTransaction();
            var user = new WorkspaceUser(); var context = new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            Repository = new(context); Sources = new(context);
        }
        /// <summary>夹具结束才释放会话。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>记录实际命令/参数，脚本结果不解释SQL。</summary>
    private sealed class ReadConnection : DbConnection
    {
        public List<string> Sqls=[]; public List<CancellationToken> Tokens=[]; public string Sql = ""; public DataTable[] Results = []; public bool InvalidHistory; public DbDataReader? Reader;
        public Dictionary<string, object?> Values = []; public Dictionary<string, NpgsqlDbType> Types = [];
        public DbTransaction? LastTransaction; public CancellationToken Token;
        public int Commands; public int Begins; public int EndTransactions; public int Closes;
        public Func<CancellationToken, Task>? Before;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "script";
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>没有网络打开。</summary>
        public override void Open() { }
        /// <summary>记录越权关闭。</summary>
        public override void Close() => Closes++;
        /// <summary>禁止切库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录调用者事务。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new ReadTransaction(this); }
        /// <summary>建立真实Dapper可执行的命令。</summary>
        protected override DbCommand CreateDbCommand() => new ReadCommand(this);
    }

    /// <summary>仅维护事务身份及结束计数。</summary>
    private sealed class ReadTransaction(ReadConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        /// <summary>记录提交。</summary>
        public override void Commit() => connection.EndTransactions++;
        /// <summary>记录回滚。</summary>
        public override void Rollback() => connection.EndTransactions++;
        /// <summary>记录释放。</summary>
        protected override void Dispose(bool disposing) { if (disposing) connection.EndTransactions++; base.Dispose(disposing); }
    }

    /// <summary>参数使用真实Npgsql容器；只读执行由本例脚本提供。</summary>
    private sealed class ReadCommand(ReadConnection connection) : DbCommand
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
        /// <summary>取消由传入令牌控制。</summary>
        public override void Cancel() { }
        /// <summary>无需提供者准备。</summary>
        public override void Prepare() { }
        /// <summary>禁止写入。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>禁止同步历史查询，异步标量入口负责记录令牌及专用门禁结果。</summary>
        public override object? ExecuteScalar() => throw new NotSupportedException();
        /// <summary>真实历史EXISTS只返回本例明确命中信号，不解释证明SQL。</summary>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql=CommandText; connection.Sqls.Add(CommandText); connection.Token=token; connection.Tokens.Add(token); connection.LastTransaction=DbTransaction; connection.Values=parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>(object?)p.Value); connection.Types=parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.NpgsqlDbType); if(connection.Before is not null)await connection.Before(token); return connection.InvalidHistory; }
        /// <summary>禁止丢失令牌的同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>捕获参数和令牌，等待/异常仅注入测试命令边界。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql = CommandText; connection.Sqls.Add(CommandText); connection.Tokens.Add(token); connection.Token = token; connection.LastTransaction = DbTransaction;
            connection.Values = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
            connection.Types = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType);
            if (connection.Before is not null) await connection.Before(token);
            return connection.Reader = new DataTableReader(connection.Results);
        }
        /// <summary>只释放当前参数容器。</summary>
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>不读取配置或连接网络，错误分类沿现有PG实现。</summary>
    private sealed class ReadFactory(ReadConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>返回本例连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>夹具审计身份。</summary>
    private sealed class WorkspaceUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_inbound_query__";
        public bool IsAuthenticated => false;
    }
}


