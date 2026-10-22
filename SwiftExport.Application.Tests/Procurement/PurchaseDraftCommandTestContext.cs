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
    /// <summary>仅授予指定细权限；未指定时保持原测试的统一授权开关。</summary>
    internal HashSet<string>? AllowedPermissions {get;set;}
    /// <summary>记录实际细权限检查顺序，证明追加和重放没有跳过授权。</summary>
    internal List<string> PermissionChecks {get;}=[];
    /// <summary>记录公共回执锁的读取次数，鉴权拒绝不得进入回执或事务。</summary>
    internal int ReceiptLocks {get;private set;}
    private readonly User user=new();
    /// <summary>提供与执行器、支持服务一致的账号身份。</summary>
    internal ICurrentUser CurrentUser=>user;
    /// <summary>模拟原账号会话失效及恢复，不改变回执所属用户。</summary>
    internal bool IsAuthenticated {get=>user.Authenticated;set=>user.Authenticated=value;}

    /// <summary>将测试账号、细权限和审计替身接入实际公共命令执行器。</summary>
    internal PurchaseDraftCommandTestContext(ServiceBaseContext context)
    {
        var receipt=PurchaseDraftSourceOrchestrationTests.Proxy<ICommandReceiptRepository>.New((method,args)=>method.Name switch
        {
            "LockAsync"=>LockReceipt(),
            "FindAsync"=>Task.FromResult(Receipts.GetValueOrDefault((Guid)args[0]!)),
            "InsertAsync"=>Insert((CommandReceipt)args[0]!),
            _=>throw new InvalidOperationException(method.Name)
        });
        Executor=new(context,receipt,user);
        var permission=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>method.Name=="HasPermissionAsync"
            ?CheckPermission((string)args[1]!):throw new InvalidOperationException(method.Name));
        var logs=PurchaseDraftSourceOrchestrationTests.Proxy<IBusinessOperationLogRepository>.New((method,_)=>method.Name=="InsertAsync"
            ?Task.FromResult(RejectAudit?0:1):throw new InvalidOperationException(method.Name));
        Support=new(permission,user,logs,new Correlation(),NullLogger<ProcurementOrderCommandSupport>.Instance);
    }

    /// <summary>只保存成功回执；业务失败在插入之前退出，替身不模拟PG物理回滚。</summary>
    private Task Insert(CommandReceipt receipt){Receipts.Add(receipt.CommandId,receipt);return Task.CompletedTask;}
    /// <summary>只观察回执进入时机，不模拟数据库锁语义。</summary>
    private Task<int> LockReceipt(){ReceiptLocks++;return Task.FromResult(0);}
    /// <summary>按实际权限键返回当前授权，保留拒绝时的检查证据。</summary>
    private Task<bool> CheckPermission(string key){PermissionChecks.Add(key);return Task.FromResult(Allow && (AllowedPermissions is null || AllowedPermissions.Contains(key)));}
    /// <summary>离线请求的稳定身份进入真实命令指纹。</summary>
    private sealed class User:ICurrentUser
    {internal bool Authenticated=true;public Guid? UserId=>new Guid("d6bfbb81-2874-4113-9a57-501f43caf826");public string UserName=>"采购草稿测试";public bool IsAuthenticated=>Authenticated;}
    /// <summary>审计关联标识不进入业务内容指纹。</summary>
    private sealed class Correlation:ICorrelationContext{public string CorrelationId=>"purchase-draft-test";}
}
