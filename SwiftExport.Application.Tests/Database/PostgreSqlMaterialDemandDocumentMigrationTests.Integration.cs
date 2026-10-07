using System.Data;
using System.Data.SqlTypes;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>严格failure门禁下的真实七方法验收；默认父回滚，提交例仅清理登记的随机Net/Gross身份，不修改约束或DDL。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlMaterialDemandDocumentMigrationIntegrationTests
{
    private static readonly DateTime Wall=new(2026,10,5,8,9,10,DateTimeKind.Unspecified);

    /// <summary>39属性逐项读取核对，输入删除标志不被改写；非正版本与空身份只在逐行新增时补齐。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(-9)] [DataRow(7)]
    public async Task AddFullProjectionAuditAndIdentity(int version)
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);
        var a=Net(g.Uid);a.Version=version;a.Uid=Guid.Empty;a.IsDeleted=true;
        var b=Net(g.Uid,1,2);b.CreatedDate=null;b.UpdatedDate=null;b.CreatedBy=null;b.UpdatedBy=null;
        var before=DateTime.Now;var results=await f.Repository.AddDecompositionAsync([a,b]);var after=DateTime.Now;
        Assert.HasCount(2,results);Assert.AreNotEqual(Guid.Empty,a.Uid);Assert.AreEqual(version<=0?1:version,a.Version);
        for(var i=0;i<2;i++){var input=i==0?a:b;Assert.IsTrue(results[i].Success);Assert.AreEqual(input.Uid,results[i].Uid);Assert.AreEqual(input.Version,results[i].NewVersion);Assert.AreEqual("新增成功。",results[i].Message);}
        Assert.IsTrue(a.IsDeleted);var expected=Clone(a);expected.IsDeleted=false;Same(expected,(await f.Read(a.Uid))!);
        Assert.AreEqual("",a.CreatedBy);Assert.AreEqual("原更新",a.UpdatedBy);Assert.AreEqual("PG净需求验收",b.CreatedBy);Assert.AreEqual(b.CreatedDate,b.UpdatedDate);Assert.IsTrue(b.CreatedDate>=before&&b.CreatedDate<=after);
        Same(b,(await f.Read(b.Uid))!);Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>全部可空快照原样保留，不从现行产品或来源补值；客供送达规则也保持原输入。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task AddNullableSnapshotsAndDelivery(bool customerSupply)
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);var a=Net(g.Uid);
        foreach(var property in typeof(NetDemand).GetProperties().Where(p=>Nullable.GetUnderlyingType(p.PropertyType)!=null||p.PropertyType==typeof(string)))
            if(property.Name is not "SupplyTypeSnapshot" and not "SkuSnapshot")property.SetValue(a,null);
        if(customerSupply){a.DeliveryMode=(DemandDeliveryModeEnum)1;a.ParentNetDemandId=Guid.NewGuid();a.DeliveryTargetNetDemandId=Guid.NewGuid();}
        Assert.IsTrue((await f.Repository.AddDecompositionAsync([a])).Single().Success);Same(a,(await f.Read(a.Uid))!);
    }

    /// <summary>原活动/未删过滤与Revision/Line/SqlGuid顺序；无Gross资格过滤，关闭来源仍可读取活动Net。</summary>
    [TestMethod]
    public async Task ActiveFilteringProjectionAndSqlGuidOrder()
    {
        await using var f=new Fixture();var g=Gross();g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;await f.PrepareBegin([g]);await f.Insert(g);
        var a=Net(g.Uid,2,1);var b=Net(g.Uid,1,2);var c=Net(g.Uid,1,1);
        var rolled=Net(g.Uid,3,1);rolled.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;
        var deleted=Net(g.Uid,4,1);deleted.IsDeleted=true;var voided=Net(g.Uid,5,1);voided.LifecycleStatus=NetDemandLifecycleStatusEnum.Voided;
        foreach(var row in new[]{a,b,c,rolled,deleted,voided})await f.Insert(row);
        var result=await f.Repository.GetActiveByGrossDemandForUpdateAsync(g.Uid);Assert.HasCount(3,result);
        var expected=new[]{a,b,c}.OrderBy(x=>x.DecompositionRevision).ThenBy(x=>x.LineNumber).ThenBy(x=>new SqlGuid(x.Uid)).ToArray();
        for(var i=0;i<3;i++)Same(expected[i],result[i]);
    }

    /// <summary>MAX包含全部生命周期和软删历史；空历史为1，最大int由C#加一回绕，不改成SQL或bigint运算。</summary>
    [TestMethod]
    [DataRow("empty",1)] [DataRow("rolled",18)] [DataRow("voided",18)] [DataRow("deleted",18)] [DataRow("max",int.MinValue)]
    public async Task RevisionAllHistory(string mode,int expected)
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);
        if(mode!="empty"){var a=Net(g.Uid,mode=="max"?int.MaxValue:17);a.LifecycleStatus=mode=="voided"?NetDemandLifecycleStatusEnum.Voided:NetDemandLifecycleStatusEnum.RolledBack;a.IsDeleted=mode=="deleted";await f.Insert(a);}
        Assert.AreEqual(expected,await f.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid));
    }

    /// <summary>退出包含关闭/软删Gross、所有历史，缺Gross不补行；无历史及未删8/9安全，活动或软删历史不安全。</summary>
    [TestMethod]
    [DataRow("none",0,false,true)] [DataRow("active",1,true,false)] [DataRow("rolled",1,false,true)] [DataRow("voided",1,false,true)] [DataRow("deleted",1,false,false)] [DataRow("mixed",2,true,false)]
    public async Task AssessmentTruthTable(string mode,int count,bool active,bool complete)
    {
        await using var f=new Fixture();var g=Gross();g.IsDeleted=true;g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;var missing=Gross();await f.PrepareBegin([g,missing]);await f.Insert(g);
        if(mode!="none"){var row=Net(g.Uid);row.LifecycleStatus=mode=="active"?NetDemandLifecycleStatusEnum.Active:mode=="voided"?NetDemandLifecycleStatusEnum.Voided:NetDemandLifecycleStatusEnum.RolledBack;row.IsDeleted=mode=="deleted";await f.Insert(row);if(mode=="mixed")await f.Insert(Net(g.Uid,2));}
        var result=await f.Repository.AssessSourceExitForUpdateAsync([missing.Uid,g.Uid,g.Uid]);Assert.HasCount(1,result);var a=result.Single();Assert.AreEqual(g.Uid,a.GrossDemandId);Assert.AreEqual(count,a.HistoryCount);Assert.AreEqual(active,a.HasActiveRevision);Assert.AreEqual(complete,a.IsHistoryComplete);
    }

    /// <summary>输出身份使用SqlGuid序，不跟随输入CLR顺序；不同修订的同Line家族行也按SqlGuid排序。</summary>
    [TestMethod]
    public async Task AssessmentAndFamilySqlGuidOrder()
    {
        await using var f=new Fixture();var a=Gross();var b=Gross();var bytes=Guid.NewGuid().ToByteArray();bytes[0]=1;bytes[10]=2;a.Uid=new(bytes);bytes=(byte[])bytes.Clone();bytes[0]=2;bytes[10]=1;b.Uid=new(bytes);
        Assert.AreNotEqual(Math.Sign(a.Uid.CompareTo(b.Uid)),Math.Sign(new SqlGuid(a.Uid).CompareTo(new SqlGuid(b.Uid))));await f.PrepareBegin([a,b]);await f.Insert(a);await f.Insert(b);
        var assessment=await f.Repository.AssessSourceExitForUpdateAsync([a.Uid,b.Uid]);CollectionAssert.AreEqual(new[]{a.Uid,b.Uid}.OrderBy(x=>new SqlGuid(x)).ToArray(),assessment.Select(x=>x.GrossDemandId).ToArray());
        var x=Net(a.Uid,1,1);var y=Net(a.Uid,2,1);x.Uid=a.Uid;y.Uid=b.Uid;y.RootNetDemandId=x.RootNetDemandId;await f.Insert(x);await f.Insert(y);
        var family=await f.Repository.GetActiveFamilyForUpdateAsync(x.Uid);CollectionAssert.AreEqual(new[]{x.Uid,y.Uid}.OrderBy(z=>new SqlGuid(z)).ToArray(),family.Select(z=>z.Uid).ToArray());
    }

    /// <summary>推荐供应商Nullable/Empty原样，版本及活动删除资格不扩大；只四列和版本改变。</summary>
    [TestMethod]
    [DataRow("null",true,false,1)] [DataRow("empty",true,false,1)] [DataRow("id",true,false,1)] [DataRow("id",false,false,0)] [DataRow("id",true,true,0)] [DataRow("rolled",true,false,0)] [DataRow("voided",true,false,0)]
    public async Task SupplierQualificationAndOnlyWriteColumns(string mode,bool matching,bool deleted,int changed)
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);var a=Net(g.Uid);a.IsDeleted=deleted;
        if(mode=="rolled")a.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;if(mode=="voided")a.LifecycleStatus=NetDemandLifecycleStatusEnum.Voided;await f.Insert(a);
        Guid? supplier=mode=="null"?null:mode=="empty"?Guid.Empty:Guid.NewGuid();var before=DateTime.Now;
        Assert.AreEqual(changed,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(a.Uid,matching?a.Version:a.Version+1,supplier));var after=DateTime.Now;var actual=(await f.Read(a.Uid))!;
        var expected=Clone(a);if(changed==1){expected.SuggestedPurchaseSupplierId=supplier;expected.Version++;expected.UpdatedDate=actual.UpdatedDate;expected.UpdatedBy="PG净需求验收";Assert.IsTrue(actual.UpdatedDate>=before&&actual.UpdatedDate<=after);}Same(expected,actual);
        Assert.AreEqual(0,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(Guid.NewGuid(),1,null));
    }

    /// <summary>回退只影响对应活动未删修订；关闭Gross不阻断，原因不Trim，审计同一时间并逐行版本加一。</summary>
    [TestMethod]
    [DataRow(" 原因 ")] [DataRow(null)]
    public async Task RollbackQualificationAndOnlyWriteColumns(string? reason)
    {
        await using var f=new Fixture();var g=Gross();g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;await f.PrepareBegin([g]);await f.Insert(g);
        var a=Net(g.Uid,1,1);var b=Net(g.Uid,1,2);var deleted=Net(g.Uid,1,3);deleted.IsDeleted=true;var rolled=Net(g.Uid,1,4);rolled.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;var later=Net(g.Uid,2,1);
        foreach(var row in new[]{a,b,deleted,rolled,later})await f.Insert(row);var before=DateTime.Now;
        Assert.AreEqual(2,await f.Repository.RollbackRevisionAsync(g.Uid,1,reason!));var after=DateTime.Now;DateTime? first=null;
        foreach(var row in new[]{a,b}){var actual=(await f.Read(row.Uid))!;var expected=Clone(row);expected.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;expected.Version++;expected.UpdatedBy=expected.RolledBackBy="PG净需求验收";expected.RolledBackReason=reason;expected.RolledBackAt=expected.UpdatedDate=actual.UpdatedDate;Assert.AreEqual(actual.UpdatedDate,actual.RolledBackAt);Assert.IsTrue(actual.UpdatedDate>=before&&actual.UpdatedDate<=after);first??=actual.UpdatedDate;Assert.AreEqual(first,actual.UpdatedDate);Same(expected,actual);}
        foreach(var row in new[]{deleted,rolled,later})Same(row,(await f.Read(row.Uid))!);Assert.AreEqual(0,await f.Repository.RollbackRevisionAsync(g.Uid,1,"重复"));
    }

    /// <summary>家族三观察保留原资格与完整投影，缺定位或无效Gross为空，不引入目标存在及Root验证。</summary>
    [TestMethod]
    [DataRow("valid",2)] [DataRow("missing-net",0)] [DataRow("deleted-net",0)] [DataRow("rolled-net",0)] [DataRow("closed-gross",0)] [DataRow("deleted-gross",0)] [DataRow("missing-gross",0)]
    public async Task FamilyObservationsAndFiltering(string mode,int count)
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);if(mode=="closed-gross")g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;if(mode=="deleted-gross")g.IsDeleted=true;if(mode!="missing-gross")await f.Insert(g);
        var a=Net(g.Uid);a.IsDeleted=mode=="deleted-net";if(mode=="rolled-net")a.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;
        var b=Net(g.Uid,1,2);b.RootNetDemandId=a.RootNetDemandId;var other=Net(g.Uid,1,3);var history=Net(g.Uid,1,4);history.RootNetDemandId=a.RootNetDemandId;history.LifecycleStatus=NetDemandLifecycleStatusEnum.Voided;
        foreach(var row in new[]{a,b,other,history})await f.Insert(row);
        var result=await f.Repository.GetActiveFamilyForUpdateAsync(mode=="missing-net"?Guid.NewGuid():a.Uid);Assert.HasCount(count,result);if(count==2){Same(a,result[0]);Same(b,result[1]);}
    }

    /// <summary>父事务缺租约拒绝实际存在Net及直接Gross操作，但缺定位仍返回原空零。</summary>
    [TestMethod]
    [DataRow("active")] [DataRow("revision")] [DataRow("assess")] [DataRow("supplier")] [DataRow("rollback")] [DataRow("family")] [DataRow("add")]
    public async Task ParentMissingLease(string operation)
    {
        await using var f=new Fixture();f.Begin();var g=Gross();await f.Insert(g);var a=Net(g.Uid);await f.Insert(a);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async()=>{switch(operation){case "active":await f.Repository.GetActiveByGrossDemandForUpdateAsync(g.Uid);break;case "revision":await f.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid);break;case "assess":await f.Repository.AssessSourceExitForUpdateAsync([g.Uid]);break;case "supplier":await f.Repository.UpdateSuggestedPurchaseSupplierAsync(a.Uid,a.Version,null);break;case "rollback":await f.Repository.RollbackRevisionAsync(g.Uid,1,"x");break;case "family":await f.Repository.GetActiveFamilyForUpdateAsync(a.Uid);break;default:await f.Repository.AddDecompositionAsync([Net(g.Uid,2)]);break;}});
        Assert.AreEqual(0,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(Guid.NewGuid(),1,null));Assert.HasCount(0,await f.Repository.GetActiveFamilyForUpdateAsync(Guid.NewGuid()));Assert.IsNotNull(f.Session.Transaction);Same(a,(await f.Read(a.Uid))!);
    }

    /// <summary>定位改绑到未覆盖Gross时拒绝，不能持U晚补另一个Gross；缺失Net仍保持空零。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ReboundNetNotCovered(bool family)
    {
        await using var f=new Fixture();var held=Gross();var outside=Gross();await f.PrepareBegin([held]);await f.Insert(held);await f.Insert(outside);var a=Net(outside.Uid);await f.Insert(a);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async()=>{if(family)await f.Repository.GetActiveFamilyForUpdateAsync(a.Uid);else await f.Repository.UpdateSuggestedPurchaseSupplierAsync(a.Uid,1,null);});Same(a,(await f.Read(a.Uid))!);
    }

    /// <summary>真实父事务中的新增、建议和回退均可回滚；仓储不提交父事务或归还父租约。</summary>
    [TestMethod]
    public async Task ParentRollbackAndBorrowedLeaseLifetime()
    {
        await using var f=new Fixture();await using var observer=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);var a=Net(g.Uid);await f.Repository.AddDecompositionAsync([a]);
        Assert.AreEqual(1,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(a.Uid,a.Version,null));Assert.AreEqual(1,await f.Repository.RollbackRevisionAsync(g.Uid,1,"回退"));Assert.IsTrue(await LockCount(observer,f)>0);
        f.End(false);Assert.IsNull(await observer.Read(a.Uid));Assert.IsTrue(await LockCount(observer,f)>0);await f.Release();Assert.AreEqual(0L,await LockCount(observer,f));
    }

    /// <summary>方法局部租约不新增事务；逐行第二条失败时首条已提交，连接和租约恢复后仍可使用。</summary>
    [TestMethod]
    [DataRow("duplicate")] [DataRow("quantity")]
    public async Task AutocommitPartialSuccess(string mode)
    {
        await using var f=new Fixture();await using var observer=new Fixture();var g=Gross();await f.InsertCommitted(g);var a=Net(g.Uid);var b=Net(g.Uid,1,2);if(mode=="duplicate")b.Uid=a.Uid;else b.GrossQty=0;f.Track(a);f.Track(b);
        var error=await Assert.ThrowsExactlyAsync<PostgresException>(()=>f.Repository.AddDecompositionAsync([a,b]));Assert.AreEqual(mode=="duplicate"?PostgresErrorCodes.UniqueViolation:PostgresErrorCodes.CheckViolation,error.SqlState);Same(a,(await observer.Read(a.Uid))!);Assert.IsNull(f.Session.Transaction);Assert.AreEqual(0L,await LockCount(observer,f));Assert.AreEqual(2,await f.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid));Assert.AreEqual(0L,await LockCount(observer,f));
        if(mode!="duplicate")Assert.IsNull(await observer.Read(b.Uid));
    }

    /// <summary>事务内后条失败保留原异常与输入副作用；父回滚撤销前条，不由仓储吞异常或重试。</summary>
    [TestMethod]
    public async Task ParentLaterInsertFailureRollback()
    {
        await using var f=new Fixture();var g=Gross();await f.PrepareBegin([g]);await f.Insert(g);var a=Net(g.Uid);var b=Net(g.Uid,2);b.NetQty=-1;
        var error=await Assert.ThrowsExactlyAsync<PostgresException>(()=>f.Repository.AddDecompositionAsync([a,b]));Assert.AreEqual(PostgresErrorCodes.CheckViolation,error.SqlState);Assert.IsNotNull(f.Session.Transaction);f.End(false);Assert.IsNull(await f.Read(a.Uid));Assert.IsNull(await f.Read(b.Uid));
    }

    /// <summary>两个真实backend争同Gross空范围；B在事务外实际尝试锁，A提交新增后B才开快照并取得新修订。</summary>
    [TestMethod]
    [DataRow("RR")] [DataRow("SERIALIZABLE")]
    public async Task ConcurrentMaxThenEstablish(string isolation)
    {
        await using var a=new Fixture();await using var b=new Fixture();await using var observer=new Fixture();var g=Gross();await a.InsertCommitted(g);await a.PrepareBegin([g]);Assert.AreEqual(1,await a.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));var waiting=b.Repository.AcquireMaterialBeforeTransactionAsync([], [g.Uid], [], [],timeout.Token);
        IAsyncDisposable? acquired=null;var adopted=false;
        try
        {
            await ObserveAttempt(observer,b,timeout.Token);Assert.IsFalse(waiting.IsCompleted);Assert.IsNull(b.Session.Transaction);var first=Net(g.Uid);a.Track(first);await a.Repository.AddDecompositionAsync([first]);a.End(true);await a.Release();
            acquired=await waiting;Assert.IsNotNull(acquired);b.Adopt(acquired);adopted=true;acquired=null;b.Begin(isolation=="RR"?IsolationLevel.RepeatableRead:IsolationLevel.Serializable);
            Assert.AreEqual(2,await b.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid));Assert.AreEqual(1,await b.Repository.RollbackRevisionAsync(g.Uid,1,"竞争回退"));var second=Net(g.Uid,2);await b.Repository.AddDecompositionAsync([second]);Assert.HasCount(1,await b.Repository.GetActiveByGrossDemandForUpdateAsync(g.Uid));b.End(false);Same(first,(await observer.Read(first.Uid))!);Assert.IsNull(await observer.Read(second.Uid));
        }
        finally {timeout.Cancel();await b.Release();if(!adopted)try{acquired=await waiting;if(acquired is not null)await acquired.DisposeAsync();}catch(OperationCanceledException){}}
    }

    /// <summary>另一backend回退提交后才开启B的RR快照，B不得把旧活动修订当作仍可执行的家族。</summary>
    [TestMethod]
    public async Task ConcurrentRollbackBeforeFamilySnapshot()
    {
        await using var a=new Fixture();await using var b=new Fixture();await using var observer=new Fixture();var g=Gross();await a.InsertCommitted(g);var row=Net(g.Uid);await a.InsertCommitted(row);await a.PrepareBegin([g]);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));var waiting=b.Repository.AcquireMaterialBeforeTransactionAsync([],[],[row.Uid],[],deadline.Token);var adopted=false;
        try
        {
            await ObserveAttempt(observer,b,deadline.Token);Assert.IsFalse(waiting.IsCompleted);Assert.IsNull(b.Session.Transaction);
            Assert.AreEqual(1,await a.Repository.RollbackRevisionAsync(g.Uid,1,"先回退"));a.End(true);await a.Release();var lease=await waiting;Assert.IsNotNull(lease);b.Adopt(lease);adopted=true;b.Begin(IsolationLevel.RepeatableRead);
            Assert.HasCount(0,await b.Repository.GetActiveFamilyForUpdateAsync(row.Uid));Assert.AreEqual(2,await b.Repository.GetNextDecompositionRevisionForUpdateAsync(g.Uid));Assert.AreEqual(0,await b.Repository.UpdateSuggestedPurchaseSupplierAsync(row.Uid,row.Version,null));Assert.AreEqual(NetDemandLifecycleStatusEnum.RolledBack,(await observer.Read(row.Uid))!.LifecycleStatus);
        }
        finally{deadline.Cancel();await b.Release();if(!adopted)try{var lease=await waiting;if(lease is not null)await lease.DisposeAsync();}catch(OperationCanceledException){}}
    }

    /// <summary>从已提交Net发现Gross及来源，覆盖后父事务可消费；已结束租约不能被后续父事务使用。</summary>
    [TestMethod]
    public async Task NetDiscoveryAndExpiredLease()
    {
        await using var owner=new Fixture();await using var f=new Fixture();var g=Gross();await owner.InsertCommitted(g);var a=Net(g.Uid);await owner.InsertCommitted(a);
        var lease=await f.Repository.AcquireMaterialBeforeTransactionAsync([],[],[a.Uid],[]);Assert.IsNotNull(lease);f.Adopt(lease);f.Begin();Assert.HasCount(1,await f.Repository.GetActiveFamilyForUpdateAsync(a.Uid));await f.Release();f.Begin();await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.UpdateSuggestedPurchaseSupplierAsync(a.Uid,a.Version,null));
    }

    /// <summary>真实锁竞争取消只发生在准备阶段，无父事务或业务副作用，所有部分资源释放后连接仍能执行查询。</summary>
    [TestMethod]
    public async Task PreparationCancellationReleasesResources()
    {
        await using var a=new Fixture();await using var b=new Fixture();await using var observer=new Fixture();var g=Gross();await a.PrepareBegin([g]);
        using var cancel=new CancellationTokenSource();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));var waiting=b.Repository.AcquireMaterialBeforeTransactionAsync([], [g.Uid], [], [],cancel.Token);
        try{await ObserveAttempt(observer,b,deadline.Token);Assert.IsFalse(waiting.IsCompleted);cancel.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>{await waiting;});Assert.IsNull(b.Session.Transaction);Assert.AreEqual(0L,await LockCount(observer,b));Assert.AreEqual(1,await b.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));}
        finally{cancel.Cancel();try{var lease=await waiting;if(lease is not null)await lease.DisposeAsync();}catch(OperationCanceledException){}}
    }

    /// <summary>取锁前后Net改绑，共同准备必须整组归还并重新发现新Gross与来源，不能持旧U追加新S。</summary>
    [TestMethod]
    public async Task PreparationRechecksChangedBinding()
    {
        await using var owner=new Fixture();await using var b=new Fixture();await using var observer=new Fixture();var first=Gross();var second=Gross();await owner.InsertCommitted(first);await owner.InsertCommitted(second);var row=Net(first.Uid);await owner.InsertCommitted(row);await owner.PrepareBegin([first,second]);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));var waiting=b.Repository.AcquireMaterialBeforeTransactionAsync([],[],[row.Uid],[],deadline.Token);IAsyncDisposable? acquired=null;var adopted=false;
        try
        {
            await ObserveAttempt(observer,b,deadline.Token);Assert.IsFalse(waiting.IsCompleted);
            Assert.AreEqual(1,await owner.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.net_demand SET gross_demand_id=@Gross WHERE uid=@Uid",new{Gross=second.Uid,row.Uid},owner.Session.Transaction)));
            owner.End(true);await owner.Release();acquired=await waiting;Assert.IsNotNull(acquired);b.Adopt(acquired);adopted=true;acquired=null;b.Begin();
            Assert.HasCount(1,await b.Repository.GetActiveFamilyForUpdateAsync(row.Uid));Assert.HasCount(1,await b.Repository.GetActiveByGrossDemandForUpdateAsync(second.Uid));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>b.Repository.GetActiveByGrossDemandForUpdateAsync(first.Uid));
        }
        finally{deadline.Cancel();await b.Release();if(!adopted)try{acquired=await waiting;if(acquired is not null)await acquired.DisposeAsync();}catch(OperationCanceledException){}}
    }

    /// <summary>七方法局部调用后的真实advisory归还与自动提交副作用；已有事务外租约只借用不归还。</summary>
    [TestMethod]
    public async Task MethodLocalAndBorrowedOutsideTransaction()
    {
        await using var f=new Fixture();await using var observer=new Fixture();var g=Gross();await f.InsertCommitted(g);var row=Net(g.Uid);await f.InsertCommitted(row);
        Assert.HasCount(1,await f.Repository.GetActiveByGrossDemandForUpdateAsync(g.Uid));Assert.HasCount(1,await f.Repository.AssessSourceExitForUpdateAsync([g.Uid]));Assert.HasCount(1,await f.Repository.GetActiveFamilyForUpdateAsync(row.Uid));Assert.AreEqual(0L,await LockCount(observer,f));
        Assert.AreEqual(1,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(row.Uid,row.Version,null));Assert.IsNull((await observer.Read(row.Uid))!.SuggestedPurchaseSupplierId);Assert.AreEqual(0L,await LockCount(observer,f));
        var lease=await f.Repository.AcquireMaterialBeforeTransactionAsync([], [g.Uid], [], []);Assert.IsNotNull(lease);f.Adopt(lease);var count=await LockCount(observer,f);Assert.IsTrue(count>0);
        Assert.AreEqual(1,await f.Repository.RollbackRevisionAsync(g.Uid,1,"方法局部"));Assert.AreEqual(count,await LockCount(observer,f));Assert.IsNull(f.Session.Transaction);await f.Release();Assert.AreEqual(0L,await LockCount(observer,f));
        Assert.AreEqual(NetDemandLifecycleStatusEnum.RolledBack,(await observer.Read(row.Uid))!.LifecycleStatus);
    }

    /// <summary>合法随机毛需求只用于本例逻辑引用，不创建产品、客户或单据历史。</summary>
    private static GrossDemand Gross()=>new(){Uid=Guid.NewGuid(),Version=3,SourceType="SO",SourceUid=Guid.NewGuid(),SourceNo="PGND"+Guid.NewGuid().ToString("N"),SourceVersion=1,SourceLineNumber=1,DemandBasisFingerprint="ND-basis",IsNonStandard=true,SkuSnapshot="PGND"+Guid.NewGuid().ToString("N"),DemandQty=10,UnitSnapshot="PCS",CreatedDate=Wall,UpdatedDate=Wall,CreatedBy="原创建",UpdatedBy="原更新"};
    /// <summary>完整快照包含NULL、空串、原文本和可选逻辑引用；不依赖其他业务表的真实数据。</summary>
    private static NetDemand Net(Guid gross,int revision=1,int line=1)
    {
        var uid=Guid.NewGuid();return new(){Uid=uid,Version=3,GrossDemandId=gross,SourceDemandVersionSnapshot=5,SourceVersionSnapshot=null,SourceDemandBasisFingerprintSnapshot=" 原指纹 ",DecompositionRevision=revision,LineNumber=line,RootNetDemandId=uid,DeliveryMode=DemandDeliveryModeEnum.Standard,ProductId=null,SuggestedPurchaseSupplierId=Guid.NewGuid(),BomLineId=Guid.NewGuid(),BomVersion=2,BomPath="原路径",MaterialTypeSnapshot="原物料",SupplyTypeSnapshot="采购",SkuSnapshot="原SKU",ParentSkuSnapshot="",CustomerSkuSnapshot="原客户型号",DescriptionSnapshot="原描述",ColorSnapshot="原颜色",SpecificationSnapshot="原规格",UnitSnapshot=" PCS ",GrossQty=3.125m,NetQty=0.125m,RequiredDate=Wall,Remark=" 原备注 ",RolledBackAt=null,RolledBackBy="原回退人",RolledBackReason="原回退原因",CreatedDate=Wall,CreatedBy="",UpdatedDate=Wall.AddDays(1),UpdatedBy="原更新"};
    }
    /// <summary>复制预期值用于逐项核对写入之外的所有属性。</summary>
    private static NetDemand Clone(NetDemand row){var copy=new NetDemand();foreach(var p in typeof(NetDemand).GetProperties().Where(p=>p.CanRead&&p.CanWrite))p.SetValue(copy,p.GetValue(row));return copy;}
    /// <summary>全部39属性独立比较；PG时间精度为微秒，原输入更细时间只允许舍弃末位精度。</summary>
    private static void Same(NetDemand expected,NetDemand actual)
    {
        Assert.IsNotNull(actual);var properties=typeof(NetDemand).GetProperties().Where(p=>p.CanRead&&p.CanWrite).ToArray();Assert.HasCount(39,properties);
        foreach(var p in properties){var x=p.GetValue(expected);var y=p.GetValue(actual);if(x is DateTime d&&y is DateTime e)Assert.IsTrue(Math.Abs(d.Ticks-e.Ticks)<10,p.Name);else Assert.AreEqual(x,y,p.Name);}
    }
    /// <summary>实际服务器查询证明目标backend已经尝试生产try-lock，禁止以固定延时替代锁竞争证据。</summary>
    private static async Task ObserveAttempt(Fixture observer,Fixture target,CancellationToken token)
    {while(!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%')",new{target.Pid},cancellationToken:token)))await Task.Delay(25,token);}
    /// <summary>按真实PID核对会话advisory清理，不依赖逻辑连接关闭或伪造lease。</summary>
    private static Task<long> LockCount(Fixture observer,Fixture target)=>observer.Session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'",new{target.Pid});
    /// <summary>只提供本例审计身份，不建立用户记录。</summary>
    private sealed class User:ICurrentUser{public string UserName=>"PG净需求验收";public Guid? UserId=>null;public bool IsAuthenticated=>true;}
    /// <summary>公开真实准备入口与原生Npgsql；父回滚为主，跨连接提交必须先登记精确身份。</summary>
    private sealed class Fixture:IAsyncDisposable
    {
        private IAsyncDisposable? lease;private readonly HashSet<Guid> nets=[];private readonly HashSet<Guid> grosses=[];
        public DbSession Session{get;}public PostgreSqlMaterialDemandDocumentRepository Repository{get;}public int Pid=>((NpgsqlConnection)Session.Connection).ProcessID;
        /// <summary>固定故障库门禁先于建立连接，不打印凭据。</summary>
        public Fixture(){Session=new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));var user=new User();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
        /// <summary>共同能力返回真实租约后才允许测试建立父事务。</summary>
        public async Task PrepareBegin(GrossDemand[] rows){var value=await Repository.AcquireMaterialBeforeTransactionAsync(rows.Select(x=>x.SourceUid).ToArray(),rows.Select(x=>x.Uid).ToArray(),[],[]);Assert.IsNotNull(value);Adopt(value);Begin();}
        /// <summary>接管本例公开入口取得的真实租约，不覆盖其他资源。</summary>
        public void Adopt(IAsyncDisposable value){Assert.IsNull(lease);lease=value;}
        /// <summary>事务属于测试，明确无准备用例也由测试创建。</summary>
        public void Begin(IsolationLevel level=IsolationLevel.ReadCommitted){Assert.IsNull(Session.Transaction);Session.Transaction=Session.Connection.BeginTransaction(level);}
        /// <summary>结束测试父事务并清空共享引用后才允许释放资源。</summary>
        public void End(bool commit){if(Session.Transaction is not {} tx)return;try{if(commit)tx.Commit();else tx.Rollback();}finally{Session.Transaction=null;tx.Dispose();}}
        /// <summary>租约释放不会接管父事务；本例先结束父事务。</summary>
        public async Task Release(){End(false);if(lease is {} value){lease=null;await value.DisposeAsync();}}
        /// <summary>直接夹具插入保留软删与历史状态，不修改约束。</summary>
        public async Task Insert(object row){var map=EntityMetadataCache.GetMetadata(row.GetType());Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',',map.Columns.Select(c=>"@"+c.ParameterName))})",PostgreSqlParameters.Entity(row,map),Session.Transaction)));}
        /// <summary>只登记本人本例随机Net，清理不使用Gross范围。</summary>
        public void Track(NetDemand row){Assert.AreNotEqual(Guid.Empty,row.Uid);nets.Add(row.Uid);}
        /// <summary>跨连接专用Gross自动提交种子先登记。</summary>
        public async Task InsertCommitted(GrossDemand row){Assert.IsNull(Session.Transaction);grosses.Add(row.Uid);await Insert(row);}
        /// <summary>跨连接专用Net自动提交种子先登记。</summary>
        public async Task InsertCommitted(NetDemand row){Assert.IsNull(Session.Transaction);Track(row);await Insert(row);}
        /// <summary>不按资格过滤的完整读取，用于核对未受影响的行。</summary>
        public Task<NetDemand?> Read(Guid uid){var map=EntityMetadataCache.GetMetadata(typeof(NetDemand));return Session.Connection.QuerySingleOrDefaultAsync<NetDemand>(new CommandDefinition($"SELECT {string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)+" AS "+PostgreSqlNames.Quote(c.PropertyName)))} FROM public.net_demand WHERE uid=@Uid",new{Uid=uid},Session.Transaction));}
        /// <summary>先释放全部父事务与租约，再精确删除本人登记Net/Gross；不碰旧数据、回执或任何目录。</summary>
        public async ValueTask DisposeAsync()
        {
            try{await Release();if(nets.Count+grosses.Count>0){using var cleanup=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));if(nets.Count>0)await cleanup.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.net_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(nets),commandTimeout:10));if(grosses.Count>0)await cleanup.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.gross_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(grosses),commandTimeout:10));if(nets.Count>0)Assert.AreEqual(0L,await cleanup.Connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT count(*) FROM public.net_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(nets))));if(grosses.Count>0)Assert.AreEqual(0L,await cleanup.Connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT count(*) FROM public.gross_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(grosses))));}}
            finally{Session.Dispose();}
        }
    }
}
