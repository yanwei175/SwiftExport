using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.QualityControl.Categories;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl.Categories;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.QualityControl.Categories;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>仅在明确启用的保留 failure 库执行；不部署或重置结构，普通场景回滚，SSI 场景仅提交并精确清理本人随机夹具。</summary>
[TestClass]
public sealed class AfterSalesQualityCategoryPostgreSqlTests
{
    /// <summary>真实 SQL 验证父子均可引用、停用历史资格、版本冲突与售后头引用检测。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesQualityCategory")]
    public async Task TreeCandidatesVersionsAndReferences()
    {
        using var session=Open(); using var tx=session.Connection.BeginTransaction(IsolationLevel.Serializable); session.Transaction=tx;
        try
        {
            var context=Context(session); var repo=new PostgreSqlAfterSalesQualityCategoryRepository(context);
            var lookups=new PostgreSqlAfterSalesLookupRepository(context);
            var root=new AfterSalesQualityCategory{CategoryName=Prefix(),FakeName="父级别名"};
            Assert.IsTrue((await repo.AddAsync(root)).Success);
            var child=new AfterSalesQualityCategory{CategoryName=Prefix(),ParentId=root.Uid};Assert.IsTrue((await repo.AddAsync(child)).Success);
            foreach(var row in new[]{root,child}) Assert.IsNotNull(await lookups.ReferenceAsync("quality-categories",row.Uid,null,true));
            root.IsEnabled=false;Assert.IsTrue((await repo.UpdateAsync(root)).Success);
            Assert.IsNull(await lookups.ReferenceAsync("quality-categories",root.Uid,null,true));
            Assert.AreEqual("父级别名",(await lookups.ReferenceAsync("quality-categories",root.Uid,null,false))!.Code);
            Assert.IsNotNull(await lookups.ReferenceAsync("quality-categories",child.Uid,null,true));
            var all=await repo.ReadAllAsync(true,CancellationToken.None);
            Assert.IsTrue(all.Any(x=>x.Uid==root.Uid&&!x.IsEnabled));
            root.Version=1;Assert.AreEqual("CONCURRENCY_CONFLICT",(await repo.UpdateAsync(root)).ErrorCode);
            Assert.AreEqual("CONCURRENCY_CONFLICT",(await repo.DeleteAsync(root.Uid,1)).ErrorCode);
            var head=new AfterSalesHeader{AfterSalesNo=Prefix(),CustomerId=Guid.NewGuid(),CustomerCodeSnapshot="TEST",AcceptedDate=new DateTime(2026,10,7),IssueDescription="分类引用测试",QualityCategoryId=child.Uid};
            await Insert(context,head);
            CollectionAssert.AreEqual(new[]{child.Uid},(await repo.GetReferencedCategoryIdsAsync([root.Uid,child.Uid],CancellationToken.None)).ToArray());
            Assert.IsTrue((await repo.DeleteAsync(root.Uid,2)).Success);
            Assert.IsNull(await lookups.ReferenceAsync("quality-categories",root.Uid,null,false));
        }
        finally{tx.Rollback();session.Transaction=null;}
    }

    /// <summary>数据库唯一键实际拒绝大小写和尾普通空格重复，异常通过保存点恢复；软删记录不占用编码。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesQualityCategory")]
    [DataRow(false)] [DataRow(true)]
    public async Task CodeUniquenessUsesProductCategoryComparison(bool trailingSpace)
    {
        using var session=Open();using var tx=session.Connection.BeginTransaction();session.Transaction=tx;
        try
        {
            var repo=new PostgreSqlAfterSalesQualityCategoryRepository(Context(session));var code=Prefix();
            var first=new AfterSalesQualityCategory{CategoryName=code,CategoryCode=code};Assert.IsTrue((await repo.AddAsync(first)).Success);
            await session.Connection.ExecuteAsync("SAVEPOINT code_conflict",transaction:tx);
            var duplicate=new AfterSalesQualityCategory{CategoryName=Prefix(),CategoryCode=trailingSpace?code+" ":code.ToLowerInvariant()};
            var error=await Assert.ThrowsExactlyAsync<PostgresException>(()=>repo.AddAsync(duplicate));Assert.AreEqual("23505",error.SqlState);
            await session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT code_conflict",transaction:tx);
            Assert.IsTrue((await repo.DeleteAsync(first.Uid,1)).Success);Assert.IsTrue((await repo.AddAsync(duplicate)).Success);
        }
        finally{tx.Rollback();session.Transaction=null;}
    }

    /// <summary>真实服务第二次写入故障必须回滚已完成 SQL；另连接核对随机前缀零残留。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesQualityCategory")]
    public async Task ServiceMidBatchFailureRollsBackRealWrites()
    {
        using var session=Open(); var context=Context(session);var real=new PostgreSqlAfterSalesQualityCategoryRepository(context);
        var proxy=DispatchProxy.Create<IAfterSalesQualityCategoryRepository,FailSecondWrite>();
        var handler=(FailSecondWrite)(object)proxy;handler.Inner=real;
        var permissions=DispatchProxy.Create<IPermissionEvaluationService,AllowPermission>();
        var service=new AfterSalesQualityCategoryService(proxy,new ServiceBaseContext(session,NullLoggerFactory.Instance),permissions,new User());
        var prefix=Prefix();var result=await service.BatchSaveAsync(new(){CreateItems=[new(){Data=new(){CategoryName=prefix+"A"}},new(){Data=new(){CategoryName=prefix+"B"}}]});
        Assert.IsFalse(result.Success);Assert.AreEqual(2,handler.AddCount);Assert.IsNull(session.Transaction);
        using var verification=Open();
        Assert.AreEqual(0,await verification.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.after_sales_quality_category WHERE category_name LIKE @Pattern",new{Pattern=prefix+"%"}));
    }

    /// <summary>两个同时存续的 Serializable 事务形成双向读写依赖；任一提交顺序都不能留下分类删除后的新引用。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesQualityCategory")]
    [DataRow(false)] [DataRow(true)]
    public async Task SerializableCategoryDeleteVersusNewReferenceCannotBothCommit(bool deleteCommitsFirst)
    {
        using var setup=Open();
        var category=new AfterSalesQualityCategory{CategoryName=Prefix()};
        var head=new AfterSalesHeader{AfterSalesNo=Prefix(),CustomerId=Guid.NewGuid(),CustomerCodeSnapshot="TEST",
            AcceptedDate=new DateTime(2026,10,7),IssueDescription="SSI分类引用竞争",QualityCategoryId=category.Uid};
        try
        {
            // 分类必须先提交，才能被两个独立事务共同看到；仅此随机身份属于本测试。
            await Insert(Context(setup),category);
            using var deletion=Open();using var reference=Open();
            using var deleteTx=deletion.Connection.BeginTransaction(IsolationLevel.Serializable);
            using var referenceTx=reference.Connection.BeginTransaction(IsolationLevel.Serializable);
            deletion.Transaction=deleteTx;reference.Transaction=referenceTx;
            try
            {
                await deletion.Connection.ExecuteAsync("SET LOCAL statement_timeout='10s'",transaction:deleteTx);
                await reference.Connection.ExecuteAsync("SET LOCAL statement_timeout='10s'",transaction:referenceTx);
                var categories=new PostgreSqlAfterSalesQualityCategoryRepository(Context(deletion));
                var lookups=new PostgreSqlAfterSalesLookupRepository(Context(reference));
                var failures=new List<string>();

                // A 读完整分类图并锁行、检查无引用；B 的普通引用读不会被 A 的行锁阻塞。
                Assert.IsTrue((await categories.ReadAllAsync(true,CancellationToken.None)).Any(x=>x.Uid==category.Uid));
                Assert.HasCount(0,await categories.GetReferencedCategoryIdsAsync([category.Uid],CancellationToken.None));
                Assert.IsNotNull(await lookups.ReferenceAsync("quality-categories",category.Uid,null,true));
                // B 插入头形成 A读→B写；A 删除分类形成 B读→A写，禁止用先提交一侧规避竞争。
                await Insert(Context(reference),head);
                var deleteReady=await Attempt(async()=>
                {
                    Assert.IsTrue((await categories.DeleteAsync(category.Uid,category.Version)).Success);
                },deleteTx);
                var deleteCommitted=false;var referenceCommitted=false;
                if(deleteCommitsFirst)
                {
                    if(deleteReady)deleteCommitted=await Attempt(()=>Commit(deleteTx),deleteTx,true);
                    referenceCommitted=await Attempt(()=>Commit(referenceTx),referenceTx,true);
                }
                else
                {
                    referenceCommitted=await Attempt(()=>Commit(referenceTx),referenceTx,true);
                    if(deleteReady)deleteCommitted=await Attempt(()=>Commit(deleteTx),deleteTx,true);
                }
                Assert.IsFalse(deleteCommitted&&referenceCommitted,"分类删除和基于旧快照的新引用不能同时提交。");
                Assert.IsTrue(failures.Count>0,"必须观测到真实 PostgreSQL 40001，普通业务失败不能冒充 SSI 防护。");
                Assert.IsTrue(failures.All(code=>code=="40001"));

                // 独立已提交视图同时核对两侧结果与孤儿事实，不依赖发生冲突的快照。
                Assert.AreEqual(referenceCommitted?1:0,await setup.Connection.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM public.after_sales_header WHERE uid=@Uid",new{head.Uid}));
                Assert.AreEqual(deleteCommitted,await setup.Connection.ExecuteScalarAsync<bool>(
                    "SELECT is_deleted FROM public.after_sales_quality_category WHERE uid=@Uid",new{category.Uid}));
                Assert.AreEqual(0,await setup.Connection.ExecuteScalarAsync<int>("""
                    SELECT count(*) FROM public.after_sales_header h
                    LEFT JOIN public.after_sales_quality_category c ON c.uid=h.quality_category_id AND c.is_deleted=FALSE
                    WHERE h.uid=@Uid AND h.is_deleted=FALSE AND c.uid IS NULL
                    """,new{head.Uid}));

                // 只接纳明确的序列化失败；死锁、超时、断连和断言失败直接使测试失败。
                async Task<bool> Attempt(Func<Task> action,IDbTransaction transaction,bool committing=false)
                {
                    try{await action();return true;}
                    catch(PostgresException error)when(error.SqlState=="40001")
                    {
                        failures.Add(error.SqlState);
                        // COMMIT 的 40001 已终结事务，驱动仍保留 Connection 不能用来判断活动状态。
                        // DML 发生 40001 时事务仍处于失败态，需要显式回滚。
                        if(!committing)transaction.Rollback();
                        return false;
                    }
                }
                static Task Commit(IDbTransaction transaction){transaction.Commit();return Task.CompletedTask;}
            }
            finally{deletion.Transaction=null;reference.Transaction=null;}
        }
        finally
        {
            // 内层事务和连接已释放；按本人随机 Uid 先清头再清分类，绝不清空表或触碰其他夹具。
            using var cleanup=setup.Connection.BeginTransaction();
            await setup.Connection.ExecuteAsync("DELETE FROM public.after_sales_header WHERE uid=@Uid",new{head.Uid},cleanup);
            await setup.Connection.ExecuteAsync("DELETE FROM public.after_sales_quality_category WHERE uid=@Uid",new{category.Uid},cleanup);
            cleanup.Commit();
            Assert.AreEqual(0,await setup.Connection.ExecuteScalarAsync<int>("""
                SELECT (SELECT count(*) FROM public.after_sales_header WHERE uid=@HeadId)
                     + (SELECT count(*) FROM public.after_sales_quality_category WHERE uid=@CategoryId)
                """,new{HeadId=head.Uid,CategoryId=category.Uid}));
        }
    }
    /// <summary>只使用既有保留专用库入口；结构不存在应失败并交由主控部署，不自动初始化。</summary>
    private static DbSession Open()=>new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    private static string Prefix()=>"QC"+Guid.NewGuid().ToString("N");
    private static RepositoryBaseContext Context(DbSession session){var user=new User();return new(session,NullLoggerFactory.Instance,new AuditStampService(user),user);}
    /// <summary>头部随机夹具使用共享元数据绑定，在调用方事务内插入，无现存业务行修改。</summary>
    private static async Task Insert(RepositoryBaseContext context,BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);var map=EntityMetadataCache.GetMetadata(entity.GetType());
        var columns=string.Join(",",map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(",",map.Columns.Select(x=>"@"+x.ParameterName));
        await context.Session.Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,map),context.Session.Transaction);
    }
    /// <summary>认证身份仅用于分类服务及随机审计字段，不改变数据库权限。</summary>
    private sealed class User:ICurrentUser{public Guid? UserId=>Guid.Parse("00000000-0000-0000-0000-000000000001");public string UserName=>"__qc_category_test__";public bool IsAuthenticated=>true;}
    /// <summary>本测试只放行服务权限，不模拟身份数据库。</summary>
    public class AllowPermission:DispatchProxy
    {
        /// <summary>只允许固定权限检查方法。</summary>
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name=="HasPermissionAsync"?Task.FromResult(true):throw new NotSupportedException();
    }
    /// <summary>第一笔使用真实仓储写入，第二笔才注入故障，其余读取沿真实 PostgreSQL。</summary>
    public class FailSecondWrite:DispatchProxy
    {
        public IAfterSalesQualityCategoryRepository Inner{get;set;}=null!;public int AddCount{get;private set;}
        /// <summary>故障发生在真实事务内部，让回滚测试覆盖实际已执行的第一条 INSERT。</summary>
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name=="AddAsync"&&++AddCount==2)return Task.FromResult(RepositoryWriteResult.Fail(Guid.Empty,null,"CONCURRENCY_CONFLICT","定向回滚故障"));
            return method.Invoke(Inner,args);
        }
    }
}
