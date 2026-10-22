using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Association;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Association.Validation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>离线验证关联命令鉴权先于回执重放；会话或权限失效不能推断此前请求未提交。</summary>
[TestClass]
public sealed class ProcurementDemandAssociationReplayTests
{
    /// <summary>模拟原响应丢失但回执已提交；鉴权拒绝不读回执，恢复同账号权限后原命令重放已有结果。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuthorizationRefusalDoesNotDisproveEarlierCommitAndOriginalCommandCanReplay(bool unauthenticated)
    {
        var connection=new PurchaseDraftSourceOrchestrationTests.ConnectionStub();
        var session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        typeof(DbSession).GetField("<Connection>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,connection);
        var context=new ServiceBaseContext(session,NullLoggerFactory.Instance);
        var commands=new PurchaseDraftCommandTestContext(context);
        var uid=Guid.NewGuid();
        var command=new AssociateProcurementDemandsCommand
        {
            CommandId=Guid.NewGuid(),ExpectedVersion=1,PreviewToken="frozen-preview",Reason="  原关联意图  ",
            Items=[new(){CommandItemId=Guid.NewGuid(),PurchaseOrderLineId=Guid.NewGuid(),NetDemandId=Guid.NewGuid(),
                ExpectedLineVersion=1,ExpectedDemandVersion=1,ExpectedSourceDemandVersion=1,Quantity=1.2345m}]
        };
        var original=new ProcurementOrderCommandResult
        {
            PurchaseOrderId=uid,OrderNo="PO-ASSOCIATION",Version=2,
            Lines=[new(){PurchaseOrderLineId=command.Items[0].PurchaseOrderLineId,Version=2}]
        };
        // 通过真实执行器保存提交回执，代表客户端未收到的先前成功；不在本测试模拟PG业务写入。
        await commands.Executor.ExecuteAsync(command.CommandId,"Procurement.Order.AssociateDemands",uid,
            new{command.ExpectedVersion,command.PreviewToken,Reason=command.Reason.Trim(),Items=command.Items.OrderBy(x=>x.CommandItemId).ToArray()},
            _=>Task.FromResult(original),default);
        connection.Begins=connection.Commits=connection.Rollbacks=0;
        var locks=commands.ReceiptLocks;var payload=JsonSerializer.Serialize(command);
        var receipt=commands.Receipts[command.CommandId];
        using var validators=new ServiceCollection().AddSingleton<IValidator<AssociateProcurementDemandsCommand>,AssociateProcurementDemandsCommandValidator>().BuildServiceProvider();
        var correlation=PurchaseDraftSourceOrchestrationTests.Proxy<ICorrelationContext>.New((_,_)=>"association-replay-test");
        var pipeline=new CommandPipeline(new CommandRequestValidator(validators),correlation,commands.CurrentUser,NullLogger<CommandPipeline>.Instance);
        // 业务端口必须不被访问，避免重放时偷偷重建贡献、读取新预览或写入审计。
        var repository=PurchaseDraftSourceOrchestrationTests.Proxy<IProcurementDemandAssociationRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
        var facts=PurchaseDraftSourceOrchestrationTests.Proxy<IProcurementOrderFactLockRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
        var links=PurchaseDraftSourceOrchestrationTests.Proxy<INetDemandExecutionLinkRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
        var logs=PurchaseDraftSourceOrchestrationTests.Proxy<IBusinessOperationLogRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
        var service=new ProcurementDemandAssociationService(repository,facts,links,null!,null!,commands.Support,pipeline,commands.Executor,logs,commands.CurrentUser,correlation);
        commands.AllowedPermissions=[PermissionKeys.ProcurementOrderRead,PermissionKeys.ProcurementOrderCreate];
        commands.IsAuthenticated=!unauthenticated;
        var refused=await service.AssociateAsync(uid,command);
        Assert.IsFalse(refused.Success);
        Assert.AreEqual(unauthenticated?ServiceErrorCodes.Unauthenticated:ServiceErrorCodes.Forbidden,refused.Error.Code);
        Assert.AreEqual(0,connection.Begins);Assert.AreEqual(locks,commands.ReceiptLocks);
        Assert.HasCount(1,commands.Receipts);Assert.AreSame(receipt,commands.Receipts[command.CommandId]);
        commands.IsAuthenticated=true;commands.AllowedPermissions.Add(PermissionKeys.ProcurementOrderEdit);
        var recovered=await service.AssociateAsync(uid,command);
        Assert.IsTrue(recovered.Success,recovered.Message);
        Assert.AreEqual(original.PurchaseOrderId,recovered.Data.PurchaseOrderId);
        Assert.AreEqual(original.Version,recovered.Data.Version);
        Assert.AreEqual(original.Lines[0].Version,recovered.Data.Lines.Single().Version);
        Assert.AreEqual(1,connection.Begins);Assert.AreEqual(1,connection.Commits);
        Assert.AreEqual(locks+1,commands.ReceiptLocks);Assert.HasCount(1,commands.Receipts);
        Assert.AreEqual(payload,JsonSerializer.Serialize(command));
        Assert.IsNull(session.Transaction);
    }
}
