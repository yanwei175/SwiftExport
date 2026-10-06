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

/// <summary>在获批单PG隔离库 验证仓储中途返回冲突后，公共执行器回滚头行和引用。</summary>
[TestClass, DoNotParallelize]
public sealed class ProcurementConcurrencySqlTests
{
    /// <summary>将原测试身份附到单库例间完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    /// <summary>故意向仓储传入后续项旧版本，验证已成功写入的前序项也不会提交；不修改业务库。</summary>
    [TestMethod]
    [DataRow("Save")]
    [DataRow("Confirm")]
    [DataRow("Delete")]
    [DataRow("VoidLinks")]
    public async Task RepositoryConflictRollsBackEarlierWrites(string operation)
    {
        await using var db = new IndependentProcurementPostgreSqlFixture();
        await db.InitializeAsync(TestContext);
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
                                "SELECT version FROM public.purchase_order_head WHERE uid=@uid", new { uid }, session.Transaction));
                        if (operation is "Save" or "Delete")
                        {
                            Assert.AreEqual(firstVersion + 1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT version FROM public.purchase_order_line WHERE uid=@Uid", first, session.Transaction));
                            Assert.AreEqual(thirdVersion, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT version FROM public.purchase_order_line WHERE uid=@Uid", third, session.Transaction));
                        }
                        if (operation == "VoidLinks")
                        {
                            Assert.AreEqual(originalLinkVersion + 1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT version FROM public.demand_execution_link WHERE uid=@Uid", links[0], session.Transaction));
                            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(
                                "SELECT COUNT(*) FROM public.demand_execution_link WHERE target_head_id=@uid AND link_status=8 AND released_qty=allocated_qty",
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
        Assert.AreEqual(version, await db.ScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT lifecycle_status FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT confirmation_status FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        foreach (var line in created.Data.Lines)
            Assert.AreEqual(line.Version, await db.ScalarAsync<int>(
                "SELECT version FROM public.purchase_order_line WHERE uid=@PurchaseOrderLineId AND is_deleted=FALSE", line));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.business_operation_log WHERE command_id=@commandId", new { commandId }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt WHERE command_id=@commandId", new { commandId }));
        if (operation == "VoidLinks")
        {
            Assert.AreEqual(3, await db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM public.demand_execution_link WHERE target_head_id=@uid AND link_status=0 AND released_qty=0 AND version=1", new { uid }));
        }
    }

    /// <summary>为一条采购行建立三个真实贡献，以验证中间 Link 冲突后首个释放也全部回滚。</summary>
    private static async Task<int> SeedLinksAsync(IndependentProcurementPostgreSqlFixture db, Guid uid, Guid lineId)
    {
        // 原T-SQL局部Guid改为Dapper参数，仍为本例新建同一组来源及三个物料身份。
        var source=Guid.NewGuid();var net1=Guid.NewGuid();var net2=Guid.NewGuid();var net3=Guid.NewGuid();
        return await db.ExecuteAsync("""
        INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,product_id,is_non_standard,sku_snapshot,demand_qty)
        VALUES(@source,0,'Manual',gen_random_uuid(),'CONCURRENCY-SOURCE',gen_random_uuid(),@ProductId,FALSE,'TEST-FG',10);
        INSERT INTO public.net_demand(uid,gross_demand_id,decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,product_id,supply_type_snapshot,sku_snapshot,gross_qty,net_qty)
        VALUES(@net1,@source,1,1,0,@net1,0,@ProductId,'采购','TEST-FG',3,3),
              (@net2,@source,1,2,0,@net2,0,@ProductId,'采购','TEST-FG',3,3),
              (@net3,@source,1,3,0,@net3,0,@ProductId,'采购','TEST-FG',4,4);
        UPDATE public.purchase_order_line SET unlinked_qty=0,source_uid=@source WHERE uid=@lineId;
        INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
        VALUES(gen_random_uuid(),@net1,gen_random_uuid(),gen_random_uuid(),'PO',@uid,'TEST',@lineId,0,3,0),
              (gen_random_uuid(),@net2,gen_random_uuid(),gen_random_uuid(),'PO',@uid,'TEST',@lineId,0,3,0),
              (gen_random_uuid(),@net3,gen_random_uuid(),gen_random_uuid(),'PO',@uid,'TEST',@lineId,0,4,0);
        """, new { source, net1, net2, net3, uid, lineId, db.ProductId });
    }
}
