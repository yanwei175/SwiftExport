using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>销售头真实PG切片验收；普通随机夹具回滚，独立竞争仅提交本例身份并按UID精准清理，不初始化结构。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSalesOrderHeadMigrationIntegrationTests
{
    /// <summary>关闭仅依赖开放/版本/未删，不额外要求已确认；失败统一源文案且不回填原因或改变审计。</summary>
    [TestMethod]
    [DataRow("pending")][DataRow("confirmed")][DataRow("closed")][DataRow("stale")][DataRow("deleted")][DataRow("missing")]
    public Task Close_KeepsSourcePredicateResultAndWallTime(string mode) => Rollback(async f =>
    {
        await f.Seed(mode);var before=DateTime.Now.AddMilliseconds(-1);
        var expected=mode=="stale"?0:1;var result=await f.Repository.CloseAsync(f.Row.Uid,expected," 原因原文 ");
        var after=DateTime.Now.AddMilliseconds(1);var success=mode is "pending" or "confirmed";
        CheckResult(result,f.Row.Uid,expected,success,"关闭销售订单");
        if(mode=="missing")return;
        var current=await f.Read();Assert.AreEqual(success?2:1,current.Version);
        if(success)
        {
            Assert.AreEqual(SalesOrderDocumentStatusEnum.Closed,current.DocumentStatus);Assert.AreEqual(" 原因原文 ",current.CloseReason);
            Assert.AreEqual(current.ClosedDate,current.UpdatedDate);Assert.AreEqual(UserName,current.ClosedBy);Assert.AreEqual(UserName,current.UpdatedBy);
            Assert.IsTrue(current.ClosedDate>=before&&current.ClosedDate<=after);Assert.AreEqual(DateTimeKind.Unspecified,current.ClosedDate!.Value.Kind);
        }
        else {Assert.IsNull(current.CloseReason);Assert.IsNull(current.ClosedDate);Assert.AreEqual(f.Row.UpdatedDate,current.UpdatedDate);}
        Assert.AreEqual(f.Row.CreatedDate,current.CreatedDate);Assert.AreEqual(f.Row.ConfirmationStatus,current.ConfirmationStatus);
        Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:f.Transaction));
    });

    /// <summary>确认写入没有源状态/生命周期限定，重复同状态也增版本；已删/缺失/旧版本只返回原并发失败。</summary>
    [TestMethod]
    [DataRow("pending")][DataRow("confirmed")][DataRow("closed")][DataRow("stale")][DataRow("deleted")][DataRow("missing")]
    public Task Confirmation_KeepsUnrestrictedLifecycleAndVersionIncrement(string mode) => Rollback(async f =>
    {
        await f.Seed(mode);var expected=mode=="stale"?0:1;
        var result=await f.Repository.SetConfirmationStatusAsync(f.Row.Uid,expected,ConfirmationStatusEnum.已确认);
        var success=mode is "pending" or "confirmed" or "closed";CheckResult(result,f.Row.Uid,expected,success,"更新销售订单状态");
        if(mode=="missing")return;
        var current=await f.Read();Assert.AreEqual(success?2:1,current.Version);Assert.AreEqual(f.Row.DocumentStatus,current.DocumentStatus);
        Assert.AreEqual(success?ConfirmationStatusEnum.已确认:f.Row.ConfirmationStatus,current.ConfirmationStatus);
        Assert.IsNull(current.CloseReason);Assert.IsNull(current.ClosedDate);
        if(success){Assert.AreEqual(UserName,current.UpdatedBy);Assert.IsNotNull(current.UpdatedDate);}
        else Assert.AreEqual(f.Row.UpdatedDate,current.UpdatedDate);
    });

    /// <summary>草稿删除只允许开放待确认未删指定版本；失败不软删或增版本，不反向清理明细。</summary>
    [TestMethod]
    [DataRow("pending")][DataRow("confirmed")][DataRow("closed")][DataRow("stale")][DataRow("deleted")][DataRow("missing")]
    public Task DeleteDraft_KeepsSourceStateVersionAndFailure(string mode) => Rollback(async f =>
    {
        await f.Seed(mode);var expected=mode=="stale"?0:1;
        var result=await f.Repository.DeleteDraftAsync(f.Row.Uid,expected);var success=mode=="pending";
        CheckResult(result,f.Row.Uid,expected,success,"删除销售订单草稿");
        if(mode=="missing")return;
        var current=await f.Read();Assert.AreEqual(success?2:1,current.Version);
        Assert.AreEqual(success||mode=="deleted",current.IsDeleted);Assert.AreEqual(f.Row.ConfirmationStatus,current.ConfirmationStatus);
        Assert.AreEqual(f.Row.DocumentStatus,current.DocumentStatus);
        if(success){Assert.AreEqual(UserName,current.UpdatedBy);Assert.IsNotNull(current.UpdatedDate);Assert.IsNull(await f.Repository.GetByIdAsync(f.Row.Uid));}
        else Assert.AreEqual(f.Row.UpdatedDate,current.UpdatedDate);
    });

    /// <summary>继承CRUD沿原审计/身份及版本规则，三个专属状态写入与泛型共享同一事务并由调用方整体回滚。</summary>
    [TestMethod]
    public async Task InheritedCrudAndStatusWrites_RollBackTogether()
    {
        using var session=Session();using var tx=session.Connection.BeginTransaction();session.Transaction=tx;
        var repo=Repository(session);var row=Row();var uid=row.Uid;
        try
        {
            var requestedUid=row.Uid;
            Assert.IsTrue((await repo.AddAsync(row)).Success);uid=row.Uid;
            Assert.AreNotEqual(requestedUid,uid,"源与PG泛型Add都重新生成UID，夹具须使用写入后身份。");
            Assert.AreEqual(1,row.Version);Assert.AreEqual(UserName,row.CreatedBy);
            Assert.IsTrue(await repo.ExistsIdAsync(uid));Assert.HasCount(1,await repo.GetByIdListAsync([uid]));
            var current=(await repo.GetByIdAsync(uid,true))!;current.Remark="泛型修改";
            Assert.AreEqual(2,(await repo.UpdateAsync(current)).NewVersion);
            CheckResult(await repo.SetConfirmationStatusAsync(uid,2,ConfirmationStatusEnum.已确认),uid,2,true,"更新销售订单状态");
            CheckResult(await repo.CloseAsync(uid,3,"原关闭"),uid,3,true,"关闭销售订单");
            Assert.AreSame(tx,session.Transaction);Assert.AreEqual(4,(await repo.GetByIdAsync(uid))!.Version);
            tx.Rollback();session.Transaction=null;
            Assert.AreEqual(0,await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid",new{Uid=uid}));
        }
        finally{if(session.Transaction is not null){tx.Rollback();session.Transaction=null;}}
    }

    /// <summary>两物理RC写连接竞争同一旧版本，第二生产UPDATE真实等锁后重验谓词而返回源并发失败，不重试/提交其事务。</summary>
    [TestMethod]
    [DataRow("close")][DataRow("confirmation")]
    public async Task CrossConnection_OldVersionWaitsAndOnlyOneWriterWins(string operation)
    {
        await using var observer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());await observer.OpenAsync();
        var row=Row();using var first=Session();using var second=Session();
        var seedRepo=Repository(first);Task<RepositoryWriteResult>? waiting=null;
        IDbTransaction? firstTx=null;IDbTransaction? secondTx=null;
        try
        {
            // 仅本例随机空头提交供两连接竞争；不执行服务/回执/取号，不触及既有身份。
            Assert.IsTrue((await seedRepo.AddAsync(row)).Success);
            firstTx=first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);first.Transaction=firstTx;
            secondTx=second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);second.Transaction=secondTx;
            var firstPid=await first.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()",transaction:firstTx);
            var secondPid=await second.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()",transaction:secondTx);
            Assert.AreNotEqual(firstPid,secondPid);Assert.AreNotEqual(observer.ProcessID,firstPid);Assert.AreNotEqual(observer.ProcessID,secondPid);
            var initial=await Write(Repository(first),operation,row.Uid);Assert.IsTrue(initial.Success);Assert.AreEqual(2,initial.NewVersion);
            waiting=Write(Repository(second),operation,row.Uid);
            var timer=Stopwatch.StartNew();var blocked=false;
            while(timer.Elapsed<TimeSpan.FromSeconds(10))
            {
                blocked=await observer.ExecuteScalarAsync<bool>("SELECT @Holder=ANY(pg_blocking_pids(@Waiter))",new{Holder=firstPid,Waiter=secondPid});
                if(blocked)break;if(waiting.IsCompleted)break;await Task.Delay(10);
            }
            Assert.IsTrue(blocked,"必须观察实际生产UPDATE的跨连接阻塞，不能凭时间或Task状态推定。");
            firstTx.Commit();first.Transaction=null;firstTx.Dispose();firstTx=null;
            var stale=await waiting.WaitAsync(TimeSpan.FromSeconds(10));
            CheckResult(stale,row.Uid,1,false,operation=="close"?"关闭销售订单":"更新销售订单状态");
            Assert.AreSame(secondTx,second.Transaction);Assert.AreEqual(1,await second.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:secondTx));
            Assert.AreEqual(2,await observer.ExecuteScalarAsync<int>("SELECT version FROM public.sales_order_header WHERE uid=@Uid",new{row.Uid}));
        }
        finally
        {
            if(firstTx is not null){firstTx.Rollback();firstTx.Dispose();first.Transaction=null;}
            if(waiting is not null&&!waiting.IsCompleted){try{await waiting.WaitAsync(TimeSpan.FromSeconds(10));}catch{}}
            if(secondTx is not null){secondTx.Rollback();secondTx.Dispose();second.Transaction=null;}
            await observer.ExecuteAsync("DELETE FROM public.sales_order_header WHERE uid=@Uid",new{row.Uid});
            Assert.AreEqual(0,await observer.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid",new{row.Uid}));
        }
    }

    /// <summary>三个源方法允许可空事务；真实自动提交仅本例两个随机头，结束按写入后UID清理并确认无残留。</summary>
    [TestMethod]
    public async Task NoOuterTransaction_ThreeMethodsKeepOriginalOwnership()
    {
        using var session=Session();var repo=Repository(session);var close=Row();var draft=Row();
        try
        {
            Assert.IsTrue((await repo.AddAsync(close)).Success);Assert.IsTrue((await repo.AddAsync(draft)).Success);
            Assert.IsNull(session.Transaction);
            CheckResult(await repo.SetConfirmationStatusAsync(close.Uid,1,ConfirmationStatusEnum.已确认),close.Uid,1,true,"更新销售订单状态");
            CheckResult(await repo.CloseAsync(close.Uid,2,"自动提交原文"),close.Uid,2,true,"关闭销售订单");
            CheckResult(await repo.DeleteDraftAsync(draft.Uid,1),draft.Uid,1,true,"删除销售订单草稿");
            Assert.IsNull(session.Transaction);Assert.AreEqual(3,(await repo.GetByIdAsync(close.Uid))!.Version);
            Assert.IsNull(await repo.GetByIdAsync(draft.Uid));Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally
        {
            foreach(var row in new[]{close,draft})
            {
                await session.Connection.ExecuteAsync("DELETE FROM public.sales_order_header WHERE uid=@Uid",new{row.Uid});
                Assert.AreEqual(0,await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid",new{row.Uid}));
            }
        }
    }

    /// <summary>仓储不把服务层必填原因校验移入基础设施；源允许NULL原因，绑定为数据库NULL并保持关闭结果。</summary>
    [TestMethod]
    public Task Close_NullReasonIsNotNewRepositoryValidation() => Rollback(async f =>
    {
        await f.Seed("pending");CheckResult(await f.Repository.CloseAsync(f.Row.Uid,1,null!),f.Row.Uid,1,true,"关闭销售订单");
        var current=await f.Read();Assert.AreEqual(SalesOrderDocumentStatusEnum.Closed,current.DocumentStatus);
        Assert.IsNull(current.CloseReason);Assert.AreEqual(2,current.Version);
    });

    /// <summary>竞争使用原版本1的生产命令，保持两个方法各自更新条件，不能换测试SQL代替实际入口。</summary>
    private static Task<RepositoryWriteResult> Write(PostgreSqlSalesOrderHeadRepository repository,string operation,Guid uid)
        => operation=="close"?repository.CloseAsync(uid,1,"竞争关闭"):repository.SetConfirmationStatusAsync(uid,1,ConfirmationStatusEnum.已确认);

    /// <summary>验证源每个成功/失败字段及完整文案；失败不二次读取并重新分类状态。</summary>
    private static void CheckResult(RepositoryWriteResult value,Guid uid,int oldVersion,bool success,string operation)
    {
        Assert.AreEqual(uid,value.Uid);Assert.AreEqual(oldVersion,value.OldVersion);Assert.AreEqual(success,value.Success);
        Assert.AreEqual(success?oldVersion+1:null,value.NewVersion);Assert.AreEqual(success?1:0,value.AffectedRows);
        Assert.AreEqual(success?null:"CONCURRENCY_CONFLICT",value.ErrorCode);
        Assert.AreEqual(success?$"{operation}成功。":$"{operation}失败，订单已被其他用户修改，请刷新后重试。",value.Message);
    }

    /// <summary>普通用例只在固定故障库单事务中写入随机头，结束回滚，不初始化结构或复用业务身份。</summary>
    private static async Task Rollback(Func<Fixture,Task> action)
    {
        using var session=Session();using var tx=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=tx;
        try{await action(new(session,tx));}
        finally{tx.Rollback();session.Transaction=null;}
    }

    /// <summary>创建独立PG请求会话，只从已授权且严格核验的故障测试库取得连接。</summary>
    private static DbSession Session()=>new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    /// <summary>复用真实审计依赖与会话，不修改全局DI或服务。</summary>
    private static PostgreSqlSalesOrderHeadRepository Repository(DbSession session)
    {var user=new HeadUser();return new(new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
    /// <summary>生成随机业务键与身份的开放待确认空订单，只用于本例事务。</summary>
    private static SalesOrderHeader Row()=>new(){Uid=Guid.NewGuid(),OrderNo="PGHEAD"+Guid.NewGuid().ToString("N"),OrderDate=new(2026,10,5),DeliveryDate=new(2026,11,5)};

    private const string UserName="__pgtest_sales_head__";
    /// <summary>不借用生产用户或权限的审计身份。</summary>
    private sealed class HeadUser:ICurrentUser
    {public string UserName=>PostgreSqlSalesOrderHeadMigrationIntegrationTests.UserName;public Guid? UserId=>null;public bool IsAuthenticated=>false;}

    /// <summary>一例一个随机头及调用方事务；直接改变本例状态仅用于构造合法边界，不影响通用仓储规则。</summary>
    private sealed class Fixture(DbSession session,IDbTransaction transaction)
    {
        public DbSession Session{get;}=session;public IDbTransaction Transaction{get;}=transaction;
        public PostgreSqlSalesOrderHeadRepository Repository{get;}=PostgreSqlSalesOrderHeadMigrationIntegrationTests.Repository(session);
        public SalesOrderHeader Row{get;}=PostgreSqlSalesOrderHeadMigrationIntegrationTests.Row();
        /// <summary>以正常Add取得审计和版本，再仅设置本例软删/关闭；不存在场景不插入。</summary>
        public async Task Seed(string mode)
        {
            if(mode=="missing")return;
            if(mode=="confirmed")Row.ConfirmationStatus=ConfirmationStatusEnum.已确认;
            if(mode=="closed")Row.DocumentStatus=SalesOrderDocumentStatusEnum.Closed;
            Assert.IsTrue((await Repository.AddAsync(Row)).Success);
            if(mode=="deleted")await Session.Connection.ExecuteAsync("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid",new{Row.Uid},Transaction);
            // 不拿内存100ns精度与PG微秒存储比较；以命令前已持久化的审计值精确断言命令不修改它们。
            var stored=await Read();Row.CreatedDate=stored.CreatedDate;Row.UpdatedDate=stored.UpdatedDate;
        }
        /// <summary>失败及已删除状态仍从本例真实表读取，避免泛型软删除过滤掩盖原值。</summary>
        public async Task<SalesOrderHeader> Read()
            => await Session.Connection.QuerySingleAsync<SalesOrderHeader>("""
                SELECT version AS "Version",document_status AS "DocumentStatus",confirmation_status AS "ConfirmationStatus",
                    is_deleted AS "IsDeleted",close_reason AS "CloseReason",closed_date AS "ClosedDate",closed_by AS "ClosedBy",
                    updated_date AS "UpdatedDate",updated_by AS "UpdatedBy",created_date AS "CreatedDate"
                FROM public.sales_order_header WHERE uid=@Uid
                """,new{Row.Uid},Transaction);
    }
}
