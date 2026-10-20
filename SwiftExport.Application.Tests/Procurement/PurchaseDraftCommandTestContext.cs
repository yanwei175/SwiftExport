using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>离线草稿编排测试共用真实公共执行器与可观察回执；不替代真实PG锁或提交证据。</summary>
internal sealed class PurchaseDraftCommandTestContext
{
    internal BusinessCommandExecutor Executor {get;}
    internal ProcurementOrderCommandSupport Support {get;}
    internal Dictionary<Guid,CommandReceipt> Receipts {get;}=[];
    internal bool Allow {get;set;}=true;
    internal bool RejectAudit {get;set;}

    /// <summary>将测试账号、细权限和审计替身接入实际公共命令执行器。</summary>
    internal PurchaseDraftCommandTestContext(ServiceBaseContext context)
    {
        var user=new User();
        var receipt=PurchaseDraftSourceOrchestrationTests.Proxy<ICommandReceiptRepository>.New((method,args)=>method.Name switch
        {
            "LockAsync"=>Task.FromResult(0),
            "FindAsync"=>Task.FromResult(Receipts.GetValueOrDefault((Guid)args[0]!)),
            "InsertAsync"=>Insert((CommandReceipt)args[0]!),
            _=>throw new InvalidOperationException(method.Name)
        });
        Executor=new(context,receipt,user);
        var permission=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,_)=>method.Name=="HasPermissionAsync"
            ?Task.FromResult(Allow):throw new InvalidOperationException(method.Name));
        var logs=PurchaseDraftSourceOrchestrationTests.Proxy<IBusinessOperationLogRepository>.New((method,_)=>method.Name=="InsertAsync"
            ?Task.FromResult(RejectAudit?0:1):throw new InvalidOperationException(method.Name));
        Support=new(permission,user,logs,new Correlation(),NullLogger<ProcurementOrderCommandSupport>.Instance);
    }

    /// <summary>只保存成功回执；业务失败在插入之前退出，替身不模拟PG物理回滚。</summary>
    private Task Insert(CommandReceipt receipt){Receipts.Add(receipt.CommandId,receipt);return Task.CompletedTask;}
    /// <summary>离线请求的稳定身份进入真实命令指纹。</summary>
    private sealed class User:ICurrentUser
    {public Guid? UserId=>new Guid("d6bfbb81-2874-4113-9a57-501f43caf826");public string UserName=>"采购草稿测试";public bool IsAuthenticated=>true;}
    /// <summary>审计关联标识不进入业务内容指纹。</summary>
    private sealed class Correlation:ICorrelationContext{public string CorrelationId=>"purchase-draft-test";}
}
