using System.Data.SqlTypes;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Infrastructure.Repositories.Inventory.AutomaticDocuments;
using SwiftExport.Infrastructure.Repositories.Inventory.Posting;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Query;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Document;
using SwiftExport.Infrastructure.Repositories.Systems.Currencies;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.ReferenceRead;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Association;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Reversal;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用现有随机夹具，集中验证本阶段完整仓储的实际 PG 读写；全部写入由父事务回滚，不替代跨连接或宿主验收。</summary>
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>集中验证采购完整Draft八方法及反向三方法的实际PG写结果，保留版本/取消/删除/作废对象副作用。</summary>
    [TestMethod]
    public async Task RealPg_ResumedPurchaseDraftAndReversal() => await Rollback(async f =>
    {
        await f.Add(f.Product);var user=new WorkspaceUser();var context=new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user);
        var draft=new PostgreSqlPurchaseOrderDraftRepository(context);var reversal=new PostgreSqlProcurementReversalRepository(context);
        f.Purchase.DemandSourceType="SO";f.Purchase.DemandSourceUid=Guid.NewGuid();f.Purchase.DemandSourceNo=f.Prefix;f.Purchase.Version=0;f.PurchaseLine.Version=0;f.PurchaseLine.PlannedDeliveryDate=f.Today.AddDays(2);
        Assert.AreEqual(1,await draft.InsertPurchaseDraftAsync(f.Purchase));Assert.AreEqual(1,f.Purchase.Version);Assert.AreEqual(0,await draft.GetMaxLineNumberForUpdateAsync(f.Purchase.Uid));
        Assert.AreEqual(1,await draft.InsertPurchaseDraftLinesAsync([f.PurchaseLine]));Assert.AreEqual(1,f.PurchaseLine.Version);Assert.AreEqual(1,await draft.GetMaxLineNumberForUpdateAsync(f.Purchase.Uid));
        Assert.AreEqual(f.Purchase.Uid,(await draft.GetByExecutionScopeForUpdateAsync(f.Purchase.ExecutionRequestId,"SO",f.Purchase.DemandSourceUid!.Value,f.Purchase.SupplierId))?.Uid);
        Assert.IsNotNull(await draft.GetSafeDraftForUpdateAsync(f.Purchase.Uid,1));
        Assert.AreEqual(1,(await draft.GetSafeAppendCandidatesAsync("SO",f.Purchase.DemandSourceUid.Value,[f.Purchase.SupplierId])).Count);
        Assert.AreEqual(f.Purchase.Uid,(await draft.GetPurchaseDraftsByIdsAsync([f.Purchase.Uid])).Single().Uid);
        Assert.AreEqual(1,await draft.AdvanceDraftVersionAfterAppendAsync(f.Purchase.Uid,1));Assert.AreEqual(1,f.Purchase.Version);
        var associated=await reversal.LockForAssociationAsync(f.Purchase.Uid,[],default);Assert.IsNull(associated.Error);Assert.IsNotNull(associated.Facts);Assert.AreEqual(f.PurchaseLine.Quantity,associated.Facts.Lines.Single().Quantity);
        var locked=await reversal.LockAsync(f.Purchase.Uid,default);Assert.IsNull(locked.Error);Assert.IsNotNull(locked.Facts);Assert.IsFalse(locked.Facts.HasUnattributedInbound);
        Assert.IsTrue((await reversal.ApplyAsync(locked.Facts,ProcurementReversalOperation.CancelConfirmation,[],"原取消理由",default)).Success);
        Assert.IsTrue((await reversal.ApplyAsync(locked.Facts,ProcurementReversalOperation.DeleteLines,[f.PurchaseLine.Uid],"原删行理由",default)).Success);
        Assert.IsTrue(locked.Facts.Lines.Single().IsDeleted);Assert.IsNull(locked.Facts.Head!.ExpectedDate);
        Assert.IsTrue((await reversal.ApplyAsync(locked.Facts,ProcurementReversalOperation.Void,[],"原作废理由",default)).Success);
        Assert.AreEqual(SwiftExport.Contracts.SysEnums.PurchaseOrderLifecycleStatusEnum.Voided,locked.Facts.Head.LifecycleStatus);
    });

    /// <summary>集中验证入库来源五方法的物理Quantity映射、资格及归属锁；所有历史门禁仍实际执行。</summary>
    [TestMethod]
    public async Task RealPg_ResumedInboundSources() => await Rollback(async f =>
    {
        f.Purchase.ConfirmationStatus=SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认;f.PurchaseLine.MaterialType="成品";await f.Seed();
        var user=new WorkspaceUser();var repository=new PostgreSqlInboundSourceRepository(new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        Assert.IsFalse(await repository.HasUnprovenReceiptAsync(default));
        var sources=await repository.GetSourcesAsync([f.PurchaseLine.Uid],default);Assert.AreEqual(f.PurchaseLine.Quantity,sources.Single().OriginalQty);
        var page=await repository.SearchSourcesAsync(new SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources.InboundSourceSearchRequest{Keyword=f.Prefix},default);
        Assert.AreEqual(1,page.TotalCount);Assert.AreEqual(f.PurchaseLine.Uid,page.Items.Single().SourceLineId);
        await repository.LockPurchaseSourcesAsync([f.PurchaseLine.Uid],default);
        var posted=await repository.ReadPostingSourcesAsync([f.PurchaseLine.Uid],default);Assert.AreEqual(f.PurchaseLine.Quantity,posted.Single().OriginalQty);Assert.IsTrue(posted.Single().IsReceivable);
    });

    /// <summary>验证库存映射、版本、锁及全局明细/原流水唯一约束；全部回滚，序号只借当前最大值偏移，不消费全局序列。</summary>
    [TestMethod]
    public async Task RealPg_ResumedInventoryPersistence() => await Rollback(async f =>
    {
        await f.Seed();var user=new WorkspaceUser();var repository=new PostgreSqlInventoryPostingRepository(new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        var commandId=Guid.NewGuid();Assert.AreEqual(0,await repository.AcquireCommandLockAsync(commandId));Assert.AreEqual(f.Product.Sku,(await repository.GetProductForPostingAsync(f.Product.Uid))?.Sku);
        Assert.IsNull(await repository.LockBalanceAsync(f.Warehouse,f.Product.Uid));
        var balance=new InventoryBalance{WarehouseSiteId=f.Warehouse,ProductId=f.Product.Uid,OnHandQty=2m,LastLedgerSequence=0,UnitSnapshot="PCS"};
        Assert.AreEqual(1,await repository.InsertBalanceAsync(balance));var locked=await repository.LockBalanceAsync(f.Warehouse,f.Product.Uid);Assert.IsNotNull(locked);
        locked.OnHandQty=3m;Assert.AreEqual(1,await repository.UpdateBalanceAsync(locked,locked.Version));Assert.AreEqual(1,locked.Version);
        Assert.AreEqual(0,await repository.UpdateBalanceAsync(locked,locked.Version));Assert.AreEqual(2,(await repository.LockBalanceAsync(f.Warehouse,f.Product.Uid))?.Version);
        var sequence=await f.Session.Connection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(sequence),0) FROM public.inventory_ledger_entry",transaction:f.Transaction);
        var original=new InventoryLedgerEntry{Sequence=sequence+1,WarehouseSiteId=f.Warehouse,ProductId=f.Product.Uid,TransactionType=InventoryTransactionTypeEnum.PurchaseReceiptInbound,QuantityDelta=2m,BalanceAfter=2m,SourceDocumentType="InboundDocument",CommandId=commandId,CommandItemId=Guid.NewGuid(),OccurredDate=f.Today,UnitSnapshot="PCS"};
        Assert.IsNull(await repository.LockLedgerByCommandItemAsync(original.CommandItemId));Assert.AreEqual(1,await repository.InsertLedgerAsync(original));
        Assert.AreEqual(original.Uid,(await repository.FindLedgerByCommandItemAsync(original.CommandItemId))?.Uid);Assert.AreEqual(original.Uid,(await repository.LockLedgerByCommandItemAsync(original.CommandItemId))?.Uid);
        Assert.AreEqual(original.Uid,(await repository.LockLedgerByIdAsync(original.Uid))?.Uid);Assert.IsNull(await repository.LockReversalByOriginalAsync(original.Uid));Assert.IsNull(await repository.LockReplacementByOriginalAsync(original.Uid));
        var group=Guid.NewGuid();var reverse=new InventoryLedgerEntry{Sequence=sequence+2,WarehouseSiteId=f.Warehouse,ProductId=f.Product.Uid,TransactionType=InventoryTransactionTypeEnum.Reversal,QuantityDelta=-2m,BalanceAfter=0m,SourceDocumentType="InboundDocument",ReversalOfLedgerId=original.Uid,CorrectionGroupId=group,CommandId=commandId,CommandItemId=Guid.NewGuid(),OccurredDate=f.Today,UnitSnapshot="PCS"};
        Assert.AreEqual(1,await repository.InsertLedgerAsync(reverse));Assert.AreEqual(reverse.Uid,(await repository.LockReversalByOriginalAsync(original.Uid))?.Uid);Assert.AreEqual(reverse.Uid,(await repository.LockCorrectionGroupAsync(group)).Single().Uid);
        // 流水不可软删；命令项不能跨单据类型或父命令复用，同原流水只能有一个反向和一个替代。
        var duplicateItem = NewLedger(); duplicateItem.CommandItemId = original.CommandItemId;
        duplicateItem.SourceDocumentType = "OutboundDocument"; duplicateItem.IsDeleted = true;
        await Reject(duplicateItem, "ck_inventory_ledger_immutable", "23514");
        duplicateItem.IsDeleted = false;
        await Reject(duplicateItem, "ux_inventory_ledger_command_item");
        var duplicateReverse = NewLedger(); duplicateReverse.TransactionType = InventoryTransactionTypeEnum.Reversal;
        duplicateReverse.QuantityDelta = -2m; duplicateReverse.ReversalOfLedgerId = original.Uid;
        await Reject(duplicateReverse, "ux_inventory_ledger_reversal_of");
        var replacement = NewLedger(); replacement.ReplacementOfLedgerId = original.Uid; replacement.CorrectionGroupId = group;
        Assert.AreEqual(1, await repository.InsertLedgerAsync(replacement));
        Assert.AreEqual(replacement.Uid, (await repository.LockReplacementByOriginalAsync(original.Uid))?.Uid);
        var duplicateReplacement = NewLedger(); duplicateReplacement.ReplacementOfLedgerId = original.Uid;
        duplicateReplacement.CorrectionGroupId = Guid.NewGuid();
        await Reject(duplicateReplacement, "ux_inventory_ledger_replacement_of");
        Assert.AreEqual(3, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id",
            new { Id = f.Warehouse }, f.Transaction));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Stage = "库存唯一约束",
            Original = original.Uid, Reverse = reverse.Uid, Replacement = replacement.Uid,
            Constraints = new[] { "ux_inventory_ledger_command_item", "ux_inventory_ledger_reversal_of", "ux_inventory_ledger_replacement_of" },
            SqlState = "23505", TransactionRollback = true, ConsumedInventorySequence = false }));

        // 每次仅改变被测重复键；其他身份和序号独立，避免由无关唯一约束产生假通过。
        InventoryLedgerEntry NewLedger() => new() { Sequence = ++sequence + 2, WarehouseSiteId = f.Warehouse,
            ProductId = f.Product.Uid, TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound,
            QuantityDelta = 2m, BalanceAfter = 2m, SourceDocumentType = "InboundDocument", CommandId = Guid.NewGuid(),
            CommandItemId = Guid.NewGuid(), OccurredDate = f.Today, UnitSnapshot = "PCS" };

        // 唯一冲突会中止PG事务；只回滚本次探针保存点，最终仍由已有finally回滚整批。
        async Task Reject(InventoryLedgerEntry candidate, string constraint, string sqlState = "23505")
        {
            await f.Session.Connection.ExecuteAsync("SAVEPOINT inventory_guard_probe", transaction: f.Transaction);
            try
            {
                var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => repository.InsertLedgerAsync(candidate));
                Assert.AreEqual(sqlState, error.SqlState);
                Assert.AreEqual(constraint, error.ConstraintName);
            }
            finally
            {
                await f.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT inventory_guard_probe", transaction: f.Transaction);
                await f.Session.Connection.ExecuteAsync("RELEASE SAVEPOINT inventory_guard_probe", transaction: f.Transaction);
            }
        }
    });

    /// <summary>验证补关联版本写入与报告后台时间，候选空集仍由实际PG计算；不替代完整需求贡献与并发验证。</summary>
    [TestMethod]
    public async Task RealPg_ResumedProcurementAssociationAndProgress() => await Rollback(async f =>
    {
        await f.Seed();var user=new WorkspaceUser();var context=new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user);
        var association=new PostgreSqlProcurementDemandAssociationRepository(context);var reports=new PostgreSqlSupplierProgressReportRepository(context);
        var targets=await association.ReadTargetsAsync([f.PurchaseLine.Uid],default);Assert.AreEqual(f.Purchase.Uid,targets.Single().HeadId);
        var candidates=await association.SearchCandidatesAsync(f.Purchase.Uid,new ProcurementDemandCandidateSearchRequest{PurchaseOrderLineId=f.PurchaseLine.Uid},default);Assert.AreEqual(0,candidates.TotalCount);
        f.Purchase.DemandSourceType="SO";f.Purchase.DemandSourceUid=Guid.NewGuid();f.Purchase.DemandSourceNo=f.Prefix;
        var oldHeadVersion=f.Purchase.Version;var oldLineVersion=f.PurchaseLine.Version;
        var applied=await association.ApplyAsync(f.Purchase,[f.PurchaseLine],default);Assert.IsTrue(applied.Success);Assert.AreEqual(oldHeadVersion+1,f.Purchase.Version);Assert.AreEqual(oldLineVersion+1,f.PurchaseLine.Version);
        Assert.IsNull(await reports.GetLatestForUpdateAsync(f.PurchaseLine.Uid));
        // 源打戳保留显式CreatedDate；采用微秒可精确存储的时间验证覆盖报告输入，避免比较PG无法保留的100ns尾数。
        var report=new SupplierProgressReport{PurchaseOrderLineId=f.PurchaseLine.Uid,Sequence=1,CommandId=Guid.NewGuid(),ReadyQty=2m,Note="供应商原报告",CreatedDate=f.Today,SupplierReportedDate=f.Today.AddDays(-1)};
        Assert.AreEqual(1,await reports.InsertAsync(report));Assert.AreEqual(report.CreatedDate,report.SupplierReportedDate);
        var latest=await reports.GetLatestForUpdateAsync(f.PurchaseLine.Uid);Assert.IsNotNull(latest);Assert.AreEqual(report.Uid,latest.Uid);Assert.AreEqual(report.Note,latest.Note);Assert.AreEqual(report.SupplierReportedDate,latest.SupplierReportedDate);
    });

    /// <summary>验证自动单据七方法的实际元数据、稳定身份插入与状态约束，父事务回滚，不触发正式库存过账。</summary>
    [TestMethod]
    public async Task RealPg_ResumedAutomaticDocuments() => await Rollback(async f =>
    {
        await f.Seed();
        var user = new WorkspaceUser(); var repository = new PostgreSqlAutomaticDocumentRepository(new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        Assert.IsTrue(await repository.IsSchemaReadyAsync(default));
        await repository.LockProductsAsync([f.Product.Uid, f.Product.Uid], default);
        Assert.AreEqual(f.Purchase.SupplierId, await repository.GetPurchaseSupplierAsync(f.PurchaseLine.Uid, default));
        var head = f.Head("AUTO"); head.CreationMode = InboundCreationModeEnum.Automatic; head.TriggerPackingListId = Guid.NewGuid(); head.Version = 0;
        var line = f.Line(head, 1, 2m); line.Version = 0; var originalUid = head.Uid;
        await repository.InsertInboundAsync(head, [line], default);
        Assert.AreEqual(originalUid, head.Uid); Assert.AreEqual(1, head.Version); Assert.AreEqual(1, line.Version);
        head.PostedDate = f.Today; head.PostedBy = user.UserName;
        await repository.SetInboundStateAsync(head, false, default); Assert.AreEqual(2, head.Version);
        head.ReversedDate = f.Today; head.ReversedBy = user.UserName; head.ReverseReason = "原反向原因";
        await repository.SetInboundStateAsync(head, true, default); Assert.AreEqual(3, head.Version);
        var inRead = new PostgreSqlInboundDocumentRepository(new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, (await inRead.LockHeaderAsync(head.Uid, default))?.PostingStatus);
        var outHead = new OutboundDocument { DocumentNo = f.Prefix + "AUTOOUT", DocumentType = OutboundDocumentTypeEnum.FinalCustomerShipment, WarehouseSiteId = f.Warehouse, ShipmentDate = f.Today, CommandId = Guid.NewGuid(), TriggerPackingListId = head.TriggerPackingListId, Version = 0 };
        var outLine = new OutboundDocumentLine { OutboundDocumentId = outHead.Uid, LineNumber = 1, ProductId = f.Product.Uid, Quantity = 2m, UnitSnapshot = "PCS", CommandItemId = Guid.NewGuid(), Version = 0 };
        await repository.InsertOutboundAsync(outHead, [outLine], default); Assert.AreEqual(1, outHead.Version); Assert.AreEqual(1, outLine.Version);
        outHead.PostedDate = f.Today; outHead.PostedBy = user.UserName; await repository.SetOutboundStateAsync(outHead, false, default);
        outHead.ReversedDate = f.Today; outHead.ReversedBy = user.UserName; outHead.ReverseReason = "原反向原因"; await repository.SetOutboundStateAsync(outHead, true, default);
        Assert.AreEqual(3, outHead.Version);
        var outRead = new PostgreSqlOutboundDocumentRepository(new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        Assert.AreEqual(OutboundPostingStatusEnum.Reversed, (await outRead.LockHeaderAsync(outHead.Uid, default))?.PostingStatus);
    });

    /// <summary>验证入出库头行的实际写入、版本副作用及省略行软删；保持源 expectedVersion 仅决定分支的行为。</summary>
    [TestMethod]
    public async Task RealPg_ResumedInventoryDocuments() => await Rollback(async f =>
    {
        var user = new WorkspaceUser(); var context = new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var inbound = new PostgreSqlInboundDocumentRepository(context); var outbound = new PostgreSqlOutboundDocumentRepository(context);
        var head = f.Head("WRITE"); var line = f.Line(head, 1, 3m); line.Version = 0;
        await inbound.SaveAsync(head, [line], 0, default);
        Assert.AreEqual(1, head.Version); Assert.AreEqual(0, line.Version);
        var locked = await inbound.LockHeaderAsync(head.Uid, default); Assert.IsNotNull(locked);
        var readLines = await inbound.LockLinesAsync(head.Uid, default); Assert.AreEqual(1, readLines.Count);
        Assert.AreEqual(1, readLines[0].Version); locked.Remark = "入库原备注"; readLines[0].Quantity = 4m;
        await inbound.SaveAsync(locked, readLines, 99, default);
        Assert.AreEqual(2, locked.Version); Assert.AreEqual(1, readLines[0].Version);
        Assert.AreEqual(4m, (await inbound.LockLinesAsync(head.Uid, default)).Single().Quantity);
        await inbound.SaveAsync(locked, [], 99, default);
        Assert.AreEqual(0, (await inbound.LockLinesAsync(head.Uid, default)).Count);
        await inbound.DeleteAsync(locked, default); Assert.IsNull(await inbound.LockHeaderAsync(head.Uid, default));

        var outHead = new OutboundDocument { DocumentNo = f.Prefix + "OUTWRITE", DocumentType = OutboundDocumentTypeEnum.StockIssue, WarehouseSiteId = f.Warehouse, ShipmentDate = f.Today, CommandId = Guid.NewGuid(), IssueReason = "原出库用途" };
        var outLine = new OutboundDocumentLine { OutboundDocumentId = outHead.Uid, LineNumber = 1, ProductId = f.Product.Uid, Quantity = 3m, UnitSnapshot = "PCS", CommandItemId = Guid.NewGuid(), Version = 0 };
        await outbound.SaveAsync(outHead, [outLine], 0, default);
        Assert.AreEqual(1, outHead.Version); Assert.AreEqual(0, outLine.Version); Assert.IsFalse(await outbound.HasLedgerAsync(outHead.Uid, default));
        var outLocked = await outbound.LockHeaderAsync(outHead.Uid, default); Assert.IsNotNull(outLocked);
        var outLines = await outbound.LockLinesAsync(outHead.Uid, default); Assert.AreEqual(1, outLines.Count);
        outLines[0].Quantity = 5m; await outbound.SaveAsync(outLocked, outLines, 99, default);
        Assert.AreEqual(2, outLocked.Version); Assert.AreEqual(1, outLines[0].Version);
        Assert.AreEqual(5m, (await outbound.LockLinesAsync(outHead.Uid, default)).Single().Quantity);
        await outbound.SaveAsync(outLocked, [], 99, default); Assert.AreEqual(0, (await outbound.LockLinesAsync(outHead.Uid, default)).Count);
        await outbound.DeleteAsync(outLocked, default); Assert.IsNull(await outbound.LockHeaderAsync(outHead.Uid, default));
    });

    /// <summary>验证售后固定列写入、乐观冲突及读取软删历史行；仅测持久化，不改确认或来源业务规则。</summary>
    [TestMethod]
    public async Task RealPg_ResumedAfterSalesDocuments() => await Rollback(async f =>
    {
        var user = new WorkspaceUser(); var repository = new PostgreSqlAfterSalesDocumentRepository(new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        var head = new AfterSalesHeader { AfterSalesNo = f.Prefix, CustomerId = Guid.NewGuid(), CustomerCodeSnapshot = f.Prefix, AcceptedDate = f.Today };
        var line = new AfterSalesLine { HeaderId = head.Uid, LineNumber = 1, ProductId = f.Product.Uid, UnitSnapshot = "PCS", Quantity = 3m };
        Assert.IsTrue(await repository.WriteHeaderAsync(head, true)); Assert.AreEqual(1, head.Version);
        Assert.IsTrue(await repository.WriteLineAsync(line, true)); Assert.AreEqual(1, line.Version);
        var locked = await repository.GetHeaderAsync(head.Uid, true); Assert.IsNotNull(locked); Assert.AreEqual(f.Today, locked.AcceptedDate);
        locked.Remark = "售后写入参数 ' ;"; Assert.IsTrue(await repository.WriteHeaderAsync(locked, false)); Assert.AreEqual(2, locked.Version);
        Assert.IsFalse(await repository.WriteHeaderAsync(head, false)); Assert.AreEqual(1, head.Version);
        line.IsDeleted = true; Assert.IsTrue(await repository.WriteLineAsync(line, false)); Assert.AreEqual(2, line.Version);
        var historical = await repository.GetLinesAsync(head.Uid, true); Assert.AreEqual(1, historical.Count); Assert.IsTrue(historical[0].IsDeleted);
        Assert.AreEqual(2, historical[0].Version); Assert.AreEqual(locked.Remark, (await repository.GetHeaderAsync(head.Uid))?.Remark);
    });

    /// <summary>集中验证采购两个完整仓储的实际 PG 字段、共享锁 SQL 和写结果；只做功能回滚场景，跨连接并发另验。</summary>
    [TestMethod]
    public async Task RealPg_ResumedProcurementPersistenceAndReferences() => await Rollback(async f =>
    {
        var customer = new Customer { CustomerCode = f.Prefix + "C", DocRelateNo = f.Prefix, IsActive = true };
        var supplier = new Supplier { SupplierCode = f.Prefix + "S", SupplierName = "采购测试供应商", DocRelateNo = f.Prefix, IsActive = true };
        f.Product.IsActive = true; f.Product.Unit = f.Prefix;
        await f.Add(customer); await f.Add(supplier); await f.Add(f.Product);
        await f.Add(new SysDictData { DictCode = "Unit", DataNameEn = f.Prefix, IsEnabled = true });
        f.Purchase.CustomerId = customer.Uid; f.Purchase.SupplierId = supplier.Uid;
        f.Purchase.DemandSourceType = "旧输入来源"; f.Purchase.DemandSourceUid = Guid.NewGuid(); f.Purchase.DemandSourceNo = "来源号";
        f.PurchaseLine.SourceType = "旧输入来源"; f.PurchaseLine.SourceNo = "来源号";
        var user = new WorkspaceUser(); var context = new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var documents = new PostgreSqlProcurementOrderDocumentRepository(context); var references = new PostgreSqlProcurementReferenceReadRepository(context);
        Assert.AreEqual(customer.Uid, (await references.GetCustomerAsync(customer.Uid, default))?.Uid);
        Assert.AreEqual(supplier.Uid, (await references.GetSupplierAsync(supplier.Uid, default))?.Uid);
        Assert.AreEqual(f.Product.Sku, (await references.GetProductAsync(f.Product.Uid, default))?.Sku);
        var candidate = await references.GetProductCandidateAsync(f.Product.Uid, customer.Uid, supplier.Uid, default);
        Assert.AreEqual(f.Product.Uid, candidate?.Uid); Assert.AreEqual("", candidate?.CustomerSku); Assert.AreEqual("", candidate?.SupplierSku);
        Assert.AreEqual(f.Prefix, await references.GetUnitAsync(f.Prefix, default));
        await documents.InsertAsync(f.Purchase, [f.PurchaseLine], default);
        var head = await documents.GetHeadAsync(f.Purchase.Uid, default);
        Assert.IsNotNull(head); Assert.IsNull(head.DemandSourceUid); Assert.IsNull(head.DemandSourceType);
        var lines = await documents.GetLinesAsync(head.Uid, default);
        Assert.AreEqual(1, lines.Count); Assert.AreEqual(f.PurchaseLine.Quantity, lines[0].Quantity); Assert.IsNull(lines[0].SourceType);
        head.Remark = "采购商务参数 ' ;"; lines[0].SupplierSku = "供应商快照";
        var saved = await documents.SaveAsync(head, lines, default);
        Assert.IsTrue(saved.Success); Assert.AreEqual(2, saved.NewVersion); Assert.AreEqual(1, head.Version);
        Assert.IsFalse((await documents.SaveAsync(head, lines, default)).Success);
        var updated = await documents.GetHeadAsync(head.Uid, default); Assert.IsNotNull(updated);
        Assert.AreEqual(head.Remark, updated.Remark); Assert.AreEqual(2, updated.Version);
        updated.ActiveConfirmationId = Guid.NewGuid(); updated.ConfirmedDate = f.Today; updated.ConfirmedBy = user.UserName;
        var confirmed = await documents.ConfirmAsync(updated, default);
        Assert.IsTrue(confirmed.Success); Assert.AreEqual(3, confirmed.NewVersion); Assert.AreEqual(2, updated.Version);
        Assert.AreEqual(updated.ActiveConfirmationId, (await documents.GetHeadAsync(head.Uid, default))?.ActiveConfirmationId);
        Assert.AreSame(f.Transaction, f.Session.Transaction);
    });

    /// <summary>集中验证本批售后查询和候选的实际 SQL、空参数、金额空值及源日期边界，父事务最终回滚。</summary>
    [TestMethod]
    public async Task RealPg_ResumedAfterSalesQueries() => await Rollback(async f =>
    {
        f.Product.IsActive = true; await f.Seed();
        var customer = new Customer { CustomerCode = f.Prefix + "C", CustomerName = "售后候选测试客户", DocRelateNo = f.Prefix, IsActive = true };
        await f.Add(customer);
        var head = new AfterSalesHeader { AfterSalesNo = f.Prefix, CustomerId = customer.Uid, CustomerCodeSnapshot = customer.CustomerCode, AcceptedDate = f.Today, IssueSkuSnapshot = f.Prefix + "Issue", IssueCustomerSkuSnapshot = "   ", IssueDescription = "原投诉事实", ServiceAmountCny = null };
        await f.Add(head);
        var user = new WorkspaceUser(); var context = new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var queries = new PostgreSqlAfterSalesQueryRepository(context); var lookups = new PostgreSqlAfterSalesLookupRepository(context);
        var page = await queries.SearchAsync(new AfterSalesSearchRequest { Keyword = f.Prefix }, default);
        Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(head.Uid, page.Items.Single().Uid);
        Assert.AreEqual(0m, page.Items.Single().KnownAmountCny); Assert.IsTrue(page.Items.Single().HasUndeterminedAmount);
        Assert.AreEqual(head.IssueSkuSnapshot, page.Items.Single().IssueModels);
        Assert.AreEqual(0, (await queries.SearchAsync(new AfterSalesSearchRequest { Keyword = f.Prefix, DateTo = f.Today.Date }, default)).TotalCount);
        foreach (var kind in new[] { "customers", "suppliers", "owners", "colors", "quality-categories", "sales-order-lines", "products" })
        {
            var result = await lookups.LookupAsync(kind, new AfterSalesLookupRequest { Keyword = f.Prefix, CustomerId = null }, default);
            Assert.AreEqual(kind is "customers" or "products" ? 1 : 0, result.TotalCount, kind);
            if (kind == "products") Assert.AreEqual(f.Product.Sku, result.Items.Single().Code);
        }
        Assert.AreEqual(customer.Uid, (await lookups.ReferenceAsync("customers", customer.Uid, null, true))?.Uid);
        Assert.AreSame(f.Transaction, f.Session.Transaction);
    });

    /// <summary>集中调用币种九方法，验证实际数组/分页/引用 CTE 及保留的 NOT NULL 失败，不改变本位币切换规则。</summary>
    [TestMethod]
    public async Task RealPg_ResumedCurrencyQueriesAndDeferredReset() => await Rollback(async f =>
    {
        var currency = new SysDicCurrency { CurrencyCode = f.Prefix, CurrencyNameZh = "币种测试", CurrencySymbol = "测", RateToBase = 1.25m, IsEnabled = true, IsBase = false, IsSystem = false };
        f.Purchase.Currency = currency.CurrencyCode; await f.Seed(); await f.Add(currency);
        var user = new WorkspaceUser(); var repository = new PostgreSqlSysDicCurrencyRepository(new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        var codes = new[] { f.Prefix.ToLowerInvariant() + "   " };
        CollectionAssert.Contains((await repository.GetExistingCurrencyCodesAsync(codes)).ToArray(), currency.CurrencyCode!);
        Assert.AreEqual(currency.Uid, (await repository.GetByCurrencyCodesAsync(codes, true)).Single().Uid);
        Assert.AreEqual(1, (await repository.GetPagedAsync(new SysDicCurrencySearchRequest { Keyword = f.Prefix })).TotalCount);
        Assert.IsTrue((await repository.GetAllForUpdateAsync()).Any(x => x.Uid == currency.Uid));
        Assert.IsTrue((await repository.GetEnabledCurrenciesAsync()).Any(x => x.Uid == currency.Uid));
        Assert.IsFalse((await repository.GetBaseCurrenciesAsync()).Any(x => x.Uid == currency.Uid));
        Assert.IsFalse((await repository.GetBaseIsBaseCurrencyIdsAsync([currency.Uid])).Contains(currency.Uid));
        CollectionAssert.Contains((await repository.GetBusinessReferencedCurrencyCodesAsync(codes)).ToArray(), currency.CurrencyCode!);
        // 用户已选择暂留原 NULL/NOT NULL 冲突；明确验证数据库拒绝，不删除约束或把失败改成成功。
        var failure = await Assert.ThrowsExactlyAsync<PostgresException>(async () => await repository.ResetAllBaseCurrencyAsync());
        Assert.AreEqual("23502", failure.SqlState);
    });

    /// <summary>验证余额/仓库与完整反向关联；包含软删行及不直接指向单头的命令明细关联，不调用过账或序列。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task RealPg_ExecutionReadsPreserveHistoricalFacts(bool outbound) => await Rollback(async f =>
    {
        await f.Seed();
        var warehouse = new WarehouseSite { Uid = f.Warehouse, Code = f.Prefix, Name = "执行读取测试仓", IsActive = true };
        var balance = new InventoryBalance { WarehouseSiteId = f.Warehouse, ProductId = f.Product.Uid, OnHandQty = 4.125m, UnitSnapshot = "PCS" };
        await f.Add(warehouse); await f.Add(balance);
        var user = new WorkspaceUser();
        var context = new RepositoryBaseContext(f.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var inboundReads = new PostgreSqlInboundExecutionReadRepository(context);
        var outboundReads = new PostgreSqlOutboundExecutionReadRepository(context);
        var headId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var itemId = Guid.NewGuid();
        if (outbound)
        {
            await f.Add(new OutboundDocument { Uid = headId, DocumentNo = f.Prefix + "OUT", DocumentType = OutboundDocumentTypeEnum.StockIssue, WarehouseSiteId = f.Warehouse, ShipmentDate = f.Today, CommandId = Guid.NewGuid() });
            await f.Add(new OutboundDocumentLine { Uid = lineId, OutboundDocumentId = headId, CommandItemId = itemId, ProductId = f.Product.Uid, LineNumber = 1, Quantity = 3, SkuSnapshot = f.Product.Sku, IsDeleted = true });
        }
        else
        {
            var head = f.Head("IN"); head.Uid = headId; await f.Add(head);
            var line = f.Line(head, 1, 3); line.Uid = lineId; line.IsDeleted = true; await f.Add(line);
            itemId = lineId;
            Assert.IsFalse(await inboundReads.HasLedgerAsync(headId, default));
        }
        var sequence = await f.Session.Connection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(sequence),0) FROM public.inventory_ledger_entry", transaction: f.Transaction);
        var direct = new InventoryLedgerEntry { Sequence = ++sequence, WarehouseSiteId = f.Warehouse, ProductId = f.Product.Uid, TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound, QuantityDelta = 3, BalanceAfter = 3, SourceDocumentType = outbound ? "OutboundDocument" : "InboundDocument", SourceDocumentId = headId, CommandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), UnitSnapshot = "PCS", OccurredDate = f.Today };
        var indirect = new InventoryLedgerEntry { Sequence = ++sequence, WarehouseSiteId = f.Warehouse, ProductId = f.Product.Uid, TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound, QuantityDelta = 2, BalanceAfter = 5, SourceDocumentType = "HistoricalIdentity", SourceDocumentId = Guid.NewGuid(), CommandId = Guid.NewGuid(), CommandItemId = itemId, UnitSnapshot = "PCS", OccurredDate = f.Today };
        var reverse = new InventoryLedgerEntry { Sequence = ++sequence, WarehouseSiteId = f.Warehouse, ProductId = f.Product.Uid, TransactionType = InventoryTransactionTypeEnum.Reversal, QuantityDelta = -3, BalanceAfter = 2, SourceDocumentType = "RelatedIdentity", ReversalOfLedgerId = direct.Uid, CommandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), UnitSnapshot = "PCS", OccurredDate = f.Today };
        var replacement = new InventoryLedgerEntry { Sequence = ++sequence, WarehouseSiteId = f.Warehouse, ProductId = f.Product.Uid, TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound, QuantityDelta = 1, BalanceAfter = 3, SourceDocumentType = "RelatedIdentity", ReplacementOfLedgerId = indirect.Uid, CommandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), UnitSnapshot = "PCS", OccurredDate = f.Today };
        await f.Add(direct); await f.Add(indirect); await f.Add(reverse); await f.Add(replacement);
        var facts = outbound ? await outboundReads.ReadReverseLedgersAsync(headId, default) : await inboundReads.ReadReverseLedgersAsync(headId, default);
        CollectionAssert.AreEqual(new[] { direct, indirect, reverse, replacement }.Select(x => x.Uid).OrderBy(x => new SqlGuid(x)).ToArray(), facts.Select(x => x.Uid).ToArray());
        Assert.AreEqual(3m, facts.Single(x => x.Uid == direct.Uid).QuantityDelta);
        Assert.AreEqual(direct.Uid, facts.Single(x => x.Uid == reverse.Uid).ReversalOfLedgerId);
        Assert.AreEqual(indirect.Uid, facts.Single(x => x.Uid == replacement.Uid).ReplacementOfLedgerId);
        if (!outbound) Assert.IsTrue(await inboundReads.HasLedgerAsync(headId, default));
        Assert.AreEqual(4.125m, outbound ? await outboundReads.ReadBalanceAsync(f.Warehouse, f.Product.Uid, default) : await inboundReads.ReadBalanceAsync(f.Warehouse, f.Product.Uid, default));
        Assert.AreEqual(0m, outbound ? await outboundReads.ReadBalanceAsync(f.Warehouse, Guid.NewGuid(), default) : await inboundReads.ReadBalanceAsync(f.Warehouse, Guid.NewGuid(), default));
        Assert.AreEqual(warehouse.Name, outbound ? await outboundReads.ReadActiveWarehouseNameAsync(f.Warehouse, default) : await inboundReads.ReadActiveWarehouseNameAsync(f.Warehouse, default));
        var readBalance = outbound ? await outboundReads.ReadReverseBalanceAsync(f.Warehouse, f.Product.Uid, default) : await inboundReads.ReadReverseBalanceAsync(f.Warehouse, f.Product.Uid, default);
        Assert.AreEqual(balance.Uid, readBalance?.Uid);
        Assert.AreEqual("PCS", readBalance?.UnitSnapshot);
        await f.Session.Connection.ExecuteAsync("UPDATE public.warehouse_site SET is_active=FALSE WHERE uid=@Uid", new { Uid = f.Warehouse }, f.Transaction);
        Assert.IsNull(outbound ? await outboundReads.ReadActiveWarehouseNameAsync(f.Warehouse, default) : await inboundReads.ReadActiveWarehouseNameAsync(f.Warehouse, default));
        Assert.IsTrue(outbound ? await outboundReads.ReverseWarehouseExistsAsync(f.Warehouse, default) : await inboundReads.ReverseWarehouseExistsAsync(f.Warehouse, default));
        await f.Session.Connection.ExecuteAsync("UPDATE public.inventory_balance SET is_deleted=TRUE WHERE uid=@Uid", new { balance.Uid }, f.Transaction);
        Assert.AreEqual(0m, outbound ? await outboundReads.ReadBalanceAsync(f.Warehouse, f.Product.Uid, default) : await inboundReads.ReadBalanceAsync(f.Warehouse, f.Product.Uid, default));
        Assert.IsTrue((outbound ? await outboundReads.ReadReverseBalanceAsync(f.Warehouse, f.Product.Uid, default) : await inboundReads.ReadReverseBalanceAsync(f.Warehouse, f.Product.Uid, default))?.IsDeleted);
        await f.Session.Connection.ExecuteAsync("UPDATE public.warehouse_site SET is_deleted=TRUE WHERE uid=@Uid", new { Uid = f.Warehouse }, f.Transaction);
        Assert.IsFalse(outbound ? await outboundReads.ReverseWarehouseExistsAsync(f.Warehouse, default) : await inboundReads.ReverseWarehouseExistsAsync(f.Warehouse, default));
        Assert.AreSame(f.Transaction, f.Session.Transaction);
    });
}
