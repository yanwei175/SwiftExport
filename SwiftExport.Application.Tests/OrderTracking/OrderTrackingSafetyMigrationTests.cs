using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>将旧聚合追踪仍有效的失败、取消及外层事务保护迁入独立交付查询；历史事实由只读PG验证。</summary>
[TestClass]
public sealed class OrderTrackingSafetyMigrationTests
{
    /// <summary>事实或证据阶段失败立即传播，不结束调用方事务或释放连接。</summary>
    [TestMethod,DataRow(0,false),DataRow(1,false),DataRow(0,true),DataRow(1,true)]
    public async Task DeliveryStageFailure_PreservesOwnerAndStops(int stage,bool outer)
    {
        using var f=new OrderTrackingReadFixture(outer);var failure=new InvalidOperationException("阶段失败");var reads=0;
        f.Connection.ReadAsync=(_,_)=>reads++==stage?throw failure:Task.FromResult<System.Data.Common.DbDataReader>(new System.Data.DataTable().CreateDataReader());
        var transaction=f.Session.Transaction;
        Assert.AreSame(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgreSqlDemandTrackingDeliveryRepository(f.Context).GetDeliveriesAsync(Guid.NewGuid(),OrderTrackingReadFixture.BusinessDate)));
        Assert.AreEqual(stage+1,reads);Assert.AreSame(transaction,f.Session.Transaction);
        Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);Assert.AreEqual(System.Data.ConnectionState.Open,f.Connection.State);
    }

    /// <summary>执行中取消在每个阶段保留原信号，不执行后续阶段或代替调用方结束事务。</summary>
    [TestMethod,DataRow(0),DataRow(1)]
    public async Task DeliveryStageCancellation_PreservesOriginalSignal(int stage)
    {
        using var f=new OrderTrackingReadFixture(true);using var cancellation=new CancellationTokenSource();var reads=0;
        f.Connection.ReadAsync=(_,token)=>
        {
            if(reads++==stage)cancellation.Cancel();token.ThrowIfCancellationRequested();
            return Task.FromResult<System.Data.Common.DbDataReader>(new System.Data.DataTable().CreateDataReader());
        };
        await Assert.ThrowsAsync<OperationCanceledException>(()=>new PostgreSqlDemandTrackingDeliveryRepository(f.Context).GetDeliveriesAsync(Guid.NewGuid(),OrderTrackingReadFixture.BusinessDate,cancellation.Token));
        Assert.AreEqual(stage+1,reads);Assert.AreEqual(cancellation.Token,f.Connection.Token);Assert.IsNotNull(f.Session.Transaction);
        Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);
    }
}
