using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>真实售后服务的隔离端口测试，不代替SQL Server验证。</summary>
[TestClass]
public sealed class AfterSalesServiceTests
{
    /// <summary>混合行只把配件正数量交给来源，空和零只保留投诉。</summary>
    [TestMethod]
    public async Task MixedRowsAndStableReplay()
    {
        using var f=new Fixture();var c=f.Command();
        c.AddedLines.Add(f.Line(0));c.AddedLines.Add(f.Line(null));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);
        var replay=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(replay.Success,replay.Message);Assert.IsTrue(replay.Data.AlreadyApplied);Assert.AreEqual(1,f.NumberCalls);
        var confirmed=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(1,f.Received!.Items.Count);Assert.AreEqual(c.AddedLines[0].Uid,f.Received.Items[0].SourceLineId);
        Assert.AreEqual("CR",f.Received.SourceType);Assert.AreEqual(2m,f.Received.Items[0].DemandQty);
        Assert.AreEqual("Pcs",f.Received.Items[0].UnitSnapshot);Assert.AreEqual(confirmed.Data.Version,f.Received.SourceVersion);
        Assert.IsTrue(f.Received.Items[0].SkuSnapshot.StartsWith("非标件-"));
        c.AddedLines[0].Quantity=3;
        Assert.AreEqual("COMMAND_CONFLICT",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Code);
    }
    /// <summary>零数量无需确认，可独立结案。</summary>
    [TestMethod]
    public async Task ZeroComplaintCanClose()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines[0].Quantity=0;
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var confirmed=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);Assert.IsNull(f.Received);
        Assert.AreEqual(ConfirmationStatusEnum.待确认,confirmed.Data.ConfirmationStatus);
        var closed=await f.Service.CloseAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="已说明"});
        Assert.IsTrue(closed.Success,closed.Message);Assert.AreEqual(DocumentStatusEnum.Closed,closed.Data.DocumentStatus);
    }
    /// <summary>未定金额阻断确认，来源失败回滚确认及日志。</summary>
    [TestMethod]
    public async Task MissingAmountAndFailedReceptionRollback()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines[0].ServiceAmountCny=null;
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var blocked=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.AreEqual("VALIDATION",blocked.Error.Code);Assert.IsNull(f.Received);
        f.Rows[0].ServiceAmountCny=0;f.FailReception=true;
        var failed=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.AreEqual("SOURCE_EXIT_BLOCKED",failed.Error.Code);
        Assert.AreEqual(saved.Data.Version,f.Head!.Version);Assert.AreEqual(ConfirmationStatusEnum.待确认,f.Head.ConfirmationStatus);
        Assert.AreEqual(1,f.Logs.Count);
    }
    /// <summary>确认后锁定金额，说明可补充；撤回失败保持锁定，成功后可重确认。</summary>
    [TestMethod]
    public async Task LockSupplementCancelAndReconfirm()
    {
        using var f=new Fixture();var c=f.Command();
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var confirmed=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        var edit=f.Edit();edit.UpdatedLines[0].ServiceAmountCny=10;
        Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
        edit=f.Edit();edit.UpdatedLines[0].IssueDescription="补充描述";
        var updated=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.IsTrue(updated.Success,updated.Message);Assert.AreEqual(1,f.ReceiveCalls);
        f.FailCancellation=true;
        var failed=await f.Service.CancelDemandConfirmationAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version,Reason="修正"});
        Assert.IsFalse(failed.Success);Assert.AreEqual(ConfirmationStatusEnum.已确认,f.Head!.ConfirmationStatus);
        f.FailCancellation=false;
        var cancelled=await f.Service.CancelDemandConfirmationAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version,Reason="修正"});
        Assert.IsTrue(cancelled.Success,cancelled.Message);Assert.IsNull(f.Head.DemandConfirmedAt);
        edit=f.Edit();edit.UpdatedLines[0].Quantity=4;
        updated=await f.Service.SaveAsync(c.Uid,edit,false);
        var again=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version});
        Assert.IsTrue(again.Success,again.Message);Assert.AreEqual(c.AddedLines[0].Uid,f.Received!.Items[0].SourceLineId);
        Assert.AreEqual(4m,f.Received.Items[0].DemandQty);
    }
    /// <summary>版本冲突不残留部分修改；未完成的下游阻断结案。</summary>
    [TestMethod]
    public async Task ConflictAndClosureBlockerPreserveFacts()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines.Add(f.Line(0));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var edit=f.Edit();edit.UpdatedLines[0].IssueDescription="不能残留";edit.UpdatedLines[1].Version=999;
        Assert.AreEqual("VERSION_CONFLICT",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
        Assert.AreEqual("成品故障",f.Rows[0].IssueDescription);
        var confirmed=await f.Service.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        f.FailClosure=true;
        var closed=await f.Service.CloseAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="结案"});
        Assert.AreEqual("SOURCE_NOT_COMPLETED",closed.Error.Code);Assert.AreEqual(DocumentStatusEnum.Open,f.Head!.DocumentStatus);
    }
    /// <summary>无权限不能读取、写入或借幂等重放越权。</summary>
    [TestMethod]
    public async Task PermissionDeniesAllAccess()
    {
        using var f=new Fixture();f.Allowed=false;var c=f.Command();
        Assert.AreEqual("FORBIDDEN",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Code);
        Assert.AreEqual("FORBIDDEN",(await f.Service.GetAsync(c.Uid)).Error.Code);Assert.AreEqual(0,f.NumberCalls);
    }

    // 快照回滚模拟事务，不执行真实数据库写入。
    private sealed class Fixture:IDisposable
    {
        public AfterSalesHeader? Head;
        public List<AfterSalesLine> Rows=new();
        public List<BusinessOperationLog> Logs=new();
        public bool Allowed=true,FailReception,FailCancellation,FailClosure;
        public int NumberCalls,ReceiveCalls;
        public SourceDemandReceptionCommand? Received;
        public AfterSalesService Service {get;}
        private readonly DbSession session;
        private readonly Guid customer=Guid.NewGuid(),actor=Guid.NewGuid();
        public Fixture()
        {
            var connection=new MemoryConnection();
            connection.Begin=()=>{var h=Clone(Head);var r=Clone(Rows)!;var l=Clone(Logs)!;return ()=>{Head=h;Rows=r;Logs=l;};};
            session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,connection);
            var repo=Port<IAfterSalesRepository>((n,a)=>n switch {
                "GetHeaderAsync"=>Task.FromResult(Clone(Head)),
                "GetLinesAsync"=>Task.FromResult<IReadOnlyList<AfterSalesLine>>(Clone(Rows)!),
                "LockCommandAsync"=>Task.CompletedTask,
                "WriteHeaderAsync"=>WriteHead((AfterSalesHeader)a[0]!, (bool)a[1]!),
                "WriteLineAsync"=>WriteLine((AfterSalesLine)a[0]!, (bool)a[1]!),
                "ReferenceAsync"=>Task.FromResult<AfterSalesLookupDto?>(new(){Uid=(Guid)a[1]!,Code="CODE",Name="名称"}),
                _=>throw new NotSupportedException(n)});
            var reception=Port<ISourceDemandReceptionService>((n,a)=>{
                if(n=="ReceiveAsync"){if(FailReception)throw new SourceDemandReceptionException("SOURCE_EXIT_BLOCKED","模拟来源阻断");ReceiveCalls++;Received=(SourceDemandReceptionCommand)a[0]!;return Task.FromResult(new SourceDemandReceptionResult());}
                if(n=="CancelConfirmationAsync"){if(FailCancellation)throw new SourceDemandReceptionException("SOURCE_EXIT_BLOCKED","模拟已有执行");return Task.FromResult(new SourceDemandExitResult());}
                if(n=="HasActiveAsync")return Task.FromResult(false);throw new NotSupportedException(n);});
            var logs=Port<IBusinessOperationLogRepository>((n,a)=>n switch {
                "GetByCommandAsync"=>Task.FromResult(Logs.SingleOrDefault(x=>x.CommandId==(Guid)a[2]!)),
                "InsertAsync"=>AppendLog((BusinessOperationLog)a[0]!),_=>throw new NotSupportedException(n)});
            var closure=Port<ISourceDemandClosureService>((_,_)=>FailClosure?throw new SourceDemandClosureException("SOURCE_NOT_COMPLETED","尚未完成"):Task.CompletedTask);
            var numbering=Port<IDocumentNoService>((_,_)=>{NumberCalls++;return Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new(){DocumentNo="CR2026090001"}));});
            var permission=Port<IUserPermissionService>((_,_)=>Task.FromResult(Allowed));
            var user=Port<ICurrentUser>((n,_)=>n switch {"get_UserId"=>(Guid?)actor,"get_UserName"=>"tester","get_IsAuthenticated"=>true,_=>throw new NotSupportedException(n)});
            Service=new(new(session,NullLoggerFactory.Instance),repo,reception,closure,numbering,logs,permission,user);
        }
        public SaveAfterSalesCommand Command()=>new(){CommandId=Guid.NewGuid(),Uid=Guid.NewGuid(),Header=new(){CustomerId=customer,AcceptedDate=new(2026,9,8)},AddedLines=[Line(2)]};
        public AfterSalesEditLineDto Line(decimal? q)=>new(){Uid=Guid.NewGuid(),IssueDescription="成品故障",IssueSkuSnapshot="成品001",Quantity=q,IsNonStandard=true,DescriptionSnapshot="补发螺丝",ResponsibleSupplierId=Guid.NewGuid(),ServiceAmountCny=0,UnitSnapshot=""};
        public SaveAfterSalesCommand Edit()=>new(){CommandId=Guid.NewGuid(),Uid=Head!.Uid,Version=Head.Version,Header=Head.Adapt<AfterSalesEditHeaderDto>(),UpdatedLines=Rows.Where(x=>!x.IsDeleted).Adapt<List<AfterSalesEditLineDto>>()};
        private Task<bool> WriteHead(AfterSalesHeader h,bool insert){if(!insert)h.Version++;Head=Clone(h);return Task.FromResult(true);}
        private Task<bool> WriteLine(AfterSalesLine l,bool insert){if(!insert)l.Version++;Rows.RemoveAll(x=>x.Uid==l.Uid);Rows.Add(Clone(l)!);return Task.FromResult(true);}
        private Task<int> AppendLog(BusinessOperationLog l){Logs.Add(Clone(l)!);return Task.FromResult(1);}
        public void Dispose()=>session.Dispose();
    }
    private static T? Clone<T>(T value)=>value is null?default:JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
    private static T Port<T>(Func<string,object?[],object?> invoke) where T:class {var p=DispatchProxy.Create<T,TestPort>();((TestPort)(object)p).Handler=invoke;return p;}
    /// <summary>只模拟当前测试实际访问的依赖端口。</summary>
    public class TestPort:DispatchProxy
    {
        public Func<string,object?[],object?> Handler {get;set;}=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!.Name,args??[]);
    }
    private sealed class MemoryConnection:IDbConnection
    {
        public Func<Action> Begin {get;set;}=null!;
        [System.Diagnostics.CodeAnalysis.AllowNull] public string ConnectionString {get;set;}="";
        public int ConnectionTimeout=>0;public string Database=>"memory";public ConnectionState State=>ConnectionState.Open;
        public IDbTransaction BeginTransaction()=>new MemoryTransaction(this,Begin());
        public IDbTransaction BeginTransaction(IsolationLevel level)=>BeginTransaction();
        public void ChangeDatabase(string name)=>throw new NotSupportedException();
        public void Close(){} public void Open(){} public void Dispose(){}
        public IDbCommand CreateCommand()=>throw new NotSupportedException();
    }
    private sealed class MemoryTransaction(IDbConnection connection,Action undo):IDbTransaction
    {
        private bool ended;
        public IDbConnection Connection=>connection;public IsolationLevel IsolationLevel=>IsolationLevel.Serializable;
        public void Commit()=>ended=true;
        public void Rollback(){if(!ended)undo();ended=true;}
        public void Dispose(){if(!ended)Rollback();}
    }
}

