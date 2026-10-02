using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在唯一命名的隔离 LocalDB 验证仓储中途返回冲突后，公共执行器回滚头行和引用。</summary>
[TestClass, DoNotParallelize]
public sealed class ProcurementConcurrencySqlTests
{
    /// <summary>故意向仓储传入后续项旧版本，验证已成功写入的前序项也不会提交；不修改业务库。</summary>
    [TestMethod]
    [DataRow("Save")]
    [DataRow("Confirm")]
    [DataRow("Delete")]
    [DataRow("VoidLinks")]
    public async Task RepositoryConflictRollsBackEarlierWrites(string operation)
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var create = db.CreateCommand();
        create.Lines.Add(new() { ProductId = db.ProductId, Quantity = 3m, UnitPrice = 1m, TaxPrice = 1m });
        create.Lines.Add(new() { ProductId = db.ProductId, Quantity = 4m, UnitPrice = 1m, TaxPrice = 1m });
        create.Lines.Add(new() { ProductId = db.ProductId, Quantity = 5m, UnitPrice = 1m, TaxPrice = 1m });
        var created = await db.CreateAsync(create);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        var version = created.Data.Version;
        var lineId = created.Data.Lines[0].PurchaseOrderLineId;
        if (operation == "VoidLinks") await SeedLinksAsync(db, uid, lineId);
        var commandId = Guid.NewGuid();

        var result = await db.InScopeAsync(async provider =>
        {
            var session = provider.GetRequiredService<DbSession>();
            var support = provider.GetRequiredService<ProcurementOrderCommandSupport>();
            var executor = provider.GetRequiredService<BusinessCommandExecutor>();
            var failure = await support.RunAsync(async () =>
            {
                var execution = await executor.ExecuteAsync(commandId, "Procurement.ConcurrencySqlTest", uid,
                    new { operation }, async ct =>
                    {
                        var document = provider.GetRequiredService<IProcurementOrderDocumentRepository>();
                        var reversal = provider.GetRequiredService<IProcurementReversalRepository>();
                        var facts = ProcurementOrderCommandSupport.RequireLockedFacts(await reversal.LockAsync(uid, ct));
                        var head = facts.Head!;
                        var lines = facts.Lines;
                        var first = lines[0];
                        var second = lines[1];
                        var third = lines[2];
                        var firstVersion = first.Version;
                        var thirdVersion = third.Version;
                        var links = facts.Links.ToList();
                        var originalLinkVersion = links.FirstOrDefault()?.Version ?? 0;
                        SwiftExport.Contracts.Wrappers.RepositoryWriteResult write;

                        // Application 的正常前置检查会提前拒绝旧版本；此处直达仓储以复现后续 SQL 条件不匹配。
                        if (operation == "Save")
                        {
                            head.Remark = "不得提交的备注";
                            second.Version++;
                            write = await document.SaveAsync(head, lines, ct);
                        }
                        else if (operation == "Confirm")
                        {
                            head.Version++;
                            write = await document.ConfirmAsync(head, ct);
                        }
                        else if (operation == "Delete")
                        {
                            second.Version++;
                            write = await reversal.ApplyAsync(facts, ProcurementReversalOperation.DeleteLines,
                                new[] { first.Uid, second.Uid, third.Uid }, "隔离回滚验证", ct);
                        }
                        else
                        {
                            links[1].Version++;
                            write = await reversal.ApplyAsync(facts, ProcurementReversalOperation.Void,
                                lines.Select(x => x.Uid).ToArray(), "隔离回滚验证", ct);
                        }

                        Assert.IsFalse(write.Success);
                        Assert.AreEqual("CONCURRENCY_CONFLICT", write.ErrorCode);
                        Assert.AreEqual(operation switch
                        {
                            "Save" or "Delete" => second.Uid,
                            "Confirm" => head.Uid,
                            _ => links[1].Uid
                        }, write.Uid);
                        // 在回滚前观察真实数据库：前序写入成功，冲突后的项没有继续执行。
                        Assert.AreEqual(version + (operation == "Confirm" ? 0 : 1),
                            await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }, session.Transaction));
                        if (operation is "Save" or "Delete")
                        {
                            Assert.AreEqual(firstVersion + 1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT Version FROM dbo.PurchaseOrderLine WHERE Uid=@Uid", first, session.Transaction));
                            Assert.AreEqual(thirdVersion, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT Version FROM dbo.PurchaseOrderLine WHERE Uid=@Uid", third, session.Transaction));
                        }
                        if (operation == "VoidLinks")
                        {
                            Assert.AreEqual(originalLinkVersion + 1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT Version FROM dbo.DemandExecutionLink WHERE Uid=@Uid", links[0], session.Transaction));
                            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT COUNT(*) FROM dbo.DemandExecutionLink WHERE TargetHeadId=@uid AND LinkStatus=8 AND ReleasedQty=AllocatedQty",
                                new { uid }, session.Transaction));
                        }
                        ProcurementOrderCommandSupport.EnsureWriteSucceeded(write);
                        await support.RecordSuccessAsync(commandId, operation,
                            ProcurementOrderCommandSupport.Result(head, lines), ct);
                        return head.Version;
                    }, CancellationToken.None, IsolationLevel.Serializable);
                return execution.Value;
            }, CancellationToken.None);
            Assert.IsNull(session.Transaction);
            return failure;
        });

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error?.Code);
        Assert.AreEqual("ExpectedVersion", result.Error?.Field);
        Assert.AreEqual(version, await db.ScalarAsync<int>("SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT LifecycleStatus FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT ConfirmationStatus FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        foreach (var line in created.Data.Lines)
            Assert.AreEqual(line.Version, await db.ScalarAsync<int>(
                "SELECT Version FROM dbo.PurchaseOrderLine WHERE Uid=@PurchaseOrderLineId AND IsDeleted=0", line));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BusinessOperationLog WHERE CommandId=@commandId", new { commandId }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CommandReceipt WHERE CommandId=@commandId", new { commandId }));
        if (operation == "VoidLinks")
        {
            Assert.AreEqual(3, await db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.DemandExecutionLink WHERE TargetHeadId=@uid AND LinkStatus=0 AND ReleasedQty=0 AND Version=1", new { uid }));
        }
    }

    /// <summary>为一条采购行建立三个真实贡献，以验证中间 Link 冲突后首个释放也全部回滚。</summary>
    private static Task<int> SeedLinksAsync(IndependentProcurementSqlFixture db, Guid uid, Guid lineId) => db.ExecuteAsync("""
        DECLARE @source uniqueidentifier=NEWID(),@net1 uniqueidentifier=NEWID(),@net2 uniqueidentifier=NEWID(),@net3 uniqueidentifier=NEWID();
        INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,ProductId,IsNonStandard,SkuSnapshot,DemandQty)
        VALUES(@source,0,N'Manual',NEWID(),N'CONCURRENCY-SOURCE',NEWID(),@ProductId,0,N'TEST-FG',10);
        INSERT dbo.NetDemand(Uid,GrossDemandId,DecompositionRevision,LineNumber,LifecycleStatus,RootNetDemandId,DeliveryMode,ProductId,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
        VALUES(@net1,@source,1,1,0,@net1,0,@ProductId,N'采购',N'TEST-FG',3,3),
              (@net2,@source,1,2,0,@net2,0,@ProductId,N'采购',N'TEST-FG',3,3),
              (@net3,@source,1,3,0,@net3,0,@ProductId,N'采购',N'TEST-FG',4,4);
        UPDATE dbo.PurchaseOrderLine SET UnlinkedQty=0,SourceUid=@source WHERE Uid=@lineId;
        INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
        VALUES(NEWID(),@net1,NEWID(),NEWID(),N'PO',@uid,N'TEST',@lineId,0,3,0),
              (NEWID(),@net2,NEWID(),NEWID(),N'PO',@uid,N'TEST',@lineId,0,3,0),
              (NEWID(),@net3,NEWID(),NEWID(),N'PO',@uid,N'TEST',@lineId,0,4,0);
        """, new { uid, lineId, db.ProductId });
}
