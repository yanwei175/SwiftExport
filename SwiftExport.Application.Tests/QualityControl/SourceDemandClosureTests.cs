using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>验证售后使用的来源结案边界，正式交付净额与合法终止分别构成完成依据。</summary>
[TestClass]
public sealed class SourceDeliveryClosureTests
{
    /// <summary>部分交付拒绝，交付完成或合法关闭允许；反向净额和超额均拒绝。</summary>
    [TestMethod]
    public async Task DeliveryAndTerminationDetermineClosure()
    {
        var row=new GrossDemand {Uid=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),DemandQty=10};
        var expected=new Dictionary<Guid,decimal>{{row.SourceLineId,10}};
        var ex=await Assert.ThrowsAsync<SourceDeliveryException>(()=>Verify([row],expected,4));
        Assert.AreEqual("SOURCE_NOT_COMPLETED",ex.Code);
        await Verify([row],expected,10);
        row.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;
        await Verify([row],expected,4);
        foreach(var net in new[]{-1m,11m})
        {
            ex=await Assert.ThrowsAsync<SourceDeliveryException>(()=>Verify([row],expected,net));
            Assert.AreEqual("SOURCE_MISMATCH",ex.Code);
        }
    }

    /// <summary>缺失、重复、数量漂移阻断；已撤回作废历史不能充当完成依据。</summary>
    [TestMethod]
    public async Task MissingDuplicateAndVoidedSourcesCannotHideRemainingWork()
    {
        var row=new GrossDemand {Uid=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),DemandQty=10};
        var expected=new Dictionary<Guid,decimal>{{row.SourceLineId,10}};
        foreach(var rows in new List<GrossDemand>[] {[],[row,row]})
            Assert.AreEqual("SOURCE_MISMATCH",(await Assert.ThrowsAsync<SourceDeliveryException>(()=>Verify(rows,expected,10))).Code);
        row.DemandQty=9;
        Assert.AreEqual("SOURCE_MISMATCH",(await Assert.ThrowsAsync<SourceDeliveryException>(()=>Verify([row],expected,9))).Code);
        row.DemandQty=10;row.LifecycleStatus=GrossDemandLifecycleStatusEnum.Voided;
        Assert.AreEqual("SOURCE_MISMATCH",(await Assert.ThrowsAsync<SourceDeliveryException>(()=>Verify([row],expected,10))).Code);
        var current=new GrossDemand {Uid=Guid.NewGuid(),SourceLineId=row.SourceLineId,DemandQty=10};
        await Verify([row,current],expected,10);
    }

    // 仅替换来源读取端口，执行真实结案规则；事务标识确保测试遵循服务前置条件。
    private static async Task Verify(List<GrossDemand> rows,Dictionary<Guid,decimal> expected,decimal delivered)
    {
        using var session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        session.Transaction=Port<IDbTransaction>((_,_)=>null);
        var deliveryRows=rows.Select(row=>new SourceDemandDeliveryItemDto
        {
            GrossDemandId=row.Uid,SourceLineId=row.SourceLineId,DemandQty=row.DemandQty,
            DeliveredQty=delivered,LifecycleStatus=row.LifecycleStatus
        }).ToList();
        var repository=Port<ISourceDeliveryRepository>((name,_)=>name=="GetSourceForClosureForUpdateAsync"
            ? Task.FromResult<IReadOnlyList<SourceDemandDeliveryItemDto>>(deliveryRows)
            : throw new NotSupportedException(name));
        var service=new SourceDeliveryService(new ServiceBaseContext(session,NullLoggerFactory.Instance),repository);
        await service.VerifyClosureAsync("CR",Guid.NewGuid(),expected);
    }
    private static T Port<T>(Func<string,object?[],object?> invoke) where T:class
    {
        var port=DispatchProxy.Create<T,AfterSalesServiceTests.TestPort>();
        ((AfterSalesServiceTests.TestPort)(object)port).Handler=invoke;
        return port;
    }
}
