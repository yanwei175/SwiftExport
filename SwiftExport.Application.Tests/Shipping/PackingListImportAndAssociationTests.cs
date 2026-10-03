using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Application.Services.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Data;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;
namespace SwiftExport.Application.Tests.Shipping;

/// <summary>真实服务、管道和命令执行器的行为测试；事务存储为内存替身，不作为 SQL 集成证据。</summary>
[TestClass]
public sealed class PackingListImportAndAssociationTests
{
    /// <summary>唯一、歧义、停用及非标分别处理，型号差异必须明确确认。</summary>
    [TestMethod]
    public async Task ProductCheckKeepsAmbiguityAndNonStandardExplicit()
    {
        var f = new Fixture(); var line = f.Standard(); line.OriginalLinkSku = "different";
        var result = (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single();
        Assert.AreEqual("Matched", result.MatchStatus); Assert.AreEqual("SKU", result.Product!.Sku);
        Assert.IsTrue(result.Issues.Single().RequiresConfirmation); Assert.IsFalse(result.Issues.Single().Acknowledged);
        line.AcceptLinkSkuMismatch = true;
        Assert.IsTrue((await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().Issues.Single().Acknowledged);
        line.ProductId = null; f.Products.Add(f.Products[0].Adapt<SalesOrderImportProductCandidateDto>()); f.Products[1].CustomerProductId = Guid.NewGuid();
        Assert.AreEqual("Ambiguous", (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().MatchStatus);
        line.SelectedCustomerProductId = f.Products[1].CustomerProductId;
        Assert.AreEqual(f.Products[1].CustomerProductId, (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().Product!.CustomerProductId);
        line.CustomerSkuSnapshot = "UNKNOWN";
        Assert.AreEqual("NotMatched", (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().MatchStatus);
        line.IsNonStandardSnapshot = true; line.DescriptionEnSnapshot = "manual";
        Assert.AreEqual("NonStandard", (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().MatchStatus);
        line.DescriptionEnSnapshot = "";
        Assert.IsTrue((await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().Issues.Any(x => x.Code == "DescriptionRequired"));
        line.IsNonStandardSnapshot = false; line.CustomerSkuSnapshot = "CUSTOMER"; f.Products[1].IsUsable = false;
        Assert.AreEqual("Unavailable", (await f.Checker.CheckAsync(f.Head.CustomerId, [line], default)).Single().MatchStatus);
    }
    /// <summary>前次检查不能授权保存，创建在事务中重新验证关系，并保持来源为空。</summary>
    [TestMethod]
    public async Task CreateUnassociatedRevalidatesProduct()
    {
        var f = new Fixture { Exists = false };
        var command = f.Create(); var result = await f.Service.CreateAsync(command);
        Assert.IsTrue(result.Success, result.Message); Assert.IsNull(result.Data.Detail.Lines[0].GrossDemandId);
        Assert.AreEqual(string.Empty, result.Data.Detail.Lines[0].SourceDocumentNoSnapshot); Assert.IsNull(result.Data.Detail.Lines[0].SourceLineNumberSnapshot); Assert.AreEqual("SKU", result.Data.Detail.Lines[0].SkuSnapshot);
        Assert.AreEqual("Excel text", result.Data.Detail.Lines[0].DescriptionEnSnapshot); Assert.AreEqual(1, f.Commits);
        var rejected = new Fixture { Exists = false }; rejected.Products[0].IsUsable = false;
        Assert.IsFalse((await rejected.Service.CreateAsync(rejected.Create())).Success);
        Assert.AreEqual(1, rejected.Rollbacks); Assert.IsFalse(rejected.Exists);
    }
    /// <summary>完整或部分分配数量守恒，拆行清空包装，原参考文字不变，重试不重复拆行。</summary>
    [TestMethod]
    public async Task AssociationConservesQuantityAndReplaysOnlyOnce()
    {
        foreach (var qty in new[] { 10m, 6m })
        {
            var f = new Fixture(); var parent = f.Rows[0].Uid; var command = f.Command(qty);
            var result = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
            Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(10m, f.Rows.Sum(x => x.Qty));
            Assert.IsTrue(f.Deleted.Any(x => x.Uid == parent)); Assert.AreEqual(qty == 10 ? 1 : 2, f.Rows.Count);
            Assert.AreEqual(qty, f.Rows.Where(x => x.GrossDemandId.HasValue).Sum(x => x.Qty));
            Assert.IsTrue(f.Rows.All(x => x.PcsPerPackage == null && x.NwPerPackage == null && x.PackageCount == null));
            Assert.IsTrue(f.Rows.All(x => x.CustomerPoNo == "P1/P2" && x.InvoiceNo == "INV1;INV2" && x.Note == "note" && x.DescriptionEnSnapshot == "manual"));
            Assert.AreEqual("SO1", f.Rows.First().SourceDocumentNoSnapshot);
            var ids = f.Rows.Select(x => x.Uid).ToArray();
            var replay = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
            Assert.IsTrue(replay.Success, replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied);
            CollectionAssert.AreEqual(ids, f.Rows.Select(x => x.Uid).ToArray());
            command.Allocations[0].Qty -= 1;
            Assert.AreEqual("COMMAND_CONFLICT", (await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command)).Error.Code);
            CollectionAssert.AreEqual(ids, f.Rows.Select(x => x.Uid).ToArray()); Assert.AreEqual(1, f.Logs.Count);
            StringAssert.Contains(f.Logs[0].ChangedFieldsJson!, parent.ToString());
        }
    }
    /// <summary>身份、版本、单位和整单同来源累计超量均在写入前拒绝。</summary>
    [TestMethod]
    public async Task AssociationRejectsChangedFactsAndAggregatesExistingQuantity()
    {
        foreach (var reason in new[] { "customer", "product", "unit", "nonstandard", "version", "sourceVersion", "quantity", "over", "line", "head", "posted" })
        {
            var f = new Fixture(); var source = f.Sources.Values.Single(); var command = f.Command(6);
            if (reason == "customer") source.CustomerId = Guid.NewGuid();
            if (reason == "product") source.ProductId = Guid.NewGuid();
            if (reason == "unit") source.Unit = "SET";
            if (reason == "nonstandard") source.IsNonStandard = false;
            if (reason == "version") source.DemandVersion++;
            if (reason == "sourceVersion") source.SourceVersion++;
            if (reason == "quantity") { source.RemainingQty = 8; f.Rows.Add(new() { PackingListHeadId = f.Head.Uid, GrossDemandId = source.GrossDemandId, Qty = 3, LineNumber = 2 }); }
            if (reason == "over") command.Allocations[0].Qty = 11;
            if (reason == "line") command.ExpectedLineVersion++;
            if (reason == "head") command.ExpectedVersion++;
            if (reason == "posted") f.Head.PostingStatus = SwiftExport.Contracts.SysEnums.Shipping.PackingListPostingStatusEnum.Posted;
            var before = f.Rows.Sum(x => x.Qty);
            Assert.IsFalse((await f.Service.AssociateSourcesAsync(f.Head.Uid, f.Rows[0].Uid, command)).Success, reason);
            Assert.AreEqual(before, f.Rows.Sum(x => x.Qty)); Assert.AreEqual(0, f.Deleted.Count); Assert.AreEqual(1, f.Rollbacks);
            if (reason == "quantity") Assert.AreEqual(9m, f.LastClaims.Single().Quantity);
        }
    }
    /// <summary>写入后的审计失败回滚全部头行及回执，重试可成功；余量待关联必须阻止过账。</summary>
    [TestMethod]
    public async Task AuditFailureRollsBackAndPendingBlocksPosting()
    {
        var f = new Fixture { FailLog = true }; var original = f.Rows[0].Uid; var version = f.Head.Version; var command = f.Command(4);
        Assert.IsFalse((await f.Service.AssociateSourcesAsync(f.Head.Uid, original, command)).Success);
        Assert.AreEqual(original, f.Rows.Single().Uid); Assert.AreEqual(version, f.Head.Version); Assert.AreEqual(0, f.Deleted.Count);
        f.FailLog = false; Assert.IsTrue((await f.Service.AssociateSourcesAsync(f.Head.Uid, original, command)).Success);
        var resolved = new PackingListPostingFactsResolver().Resolve(new() { Head = f.Head, Lines = f.Rows, CustomerExists = true }, [], DateTime.Today, false);
        Assert.IsTrue(resolved.Preview.Blockers.Any(x => x.Code == "SOURCE_NOT_ASSOCIATED"));
    }
    /// <summary>具备事务快照的内存端口；不复制被测服务的拆行算法。</summary>
    internal sealed class Fixture
    {
        internal PackingListHead Head = new() { CustomerId = Guid.NewGuid(), PackingListNo = "PL1", InvoiceDate = DateTime.Today };
        internal List<PackingListLine> Rows = [], Deleted = [];
        internal List<BusinessOperationLog> Logs = [];
        internal List<SalesOrderImportProductCandidateDto> Products = [new() { CustomerProductId = Guid.NewGuid(), ProductId = Guid.NewGuid(), CustomerSku = "CUSTOMER", Sku = "SKU", LinkSku = "LINK", Unit = "PCS", IsUsable = true }];
        internal Dictionary<Guid, SourceDemandDeliveryItemDto> Sources = [];
        internal IReadOnlyCollection<SourceDeliveryClaim> LastClaims = [];
        internal bool Exists = true, FailLog;
        internal int Commits, Rollbacks;
        internal PackingListDraftService Service;
        internal PackingListImportChecker Checker;
        private Dictionary<Guid, CommandReceipt> receipts = [];
        internal Fixture(DbSession? providedSession = null, IPackingListDocumentRepository? providedRepository = null,
            ICommandReceiptRepository? providedReceipts = null)
        {
            Rows.Add(new() { PackingListHeadId = Head.Uid, LineNumber = 1, Qty = 10, IsNonStandardSnapshot = true, DescriptionEnSnapshot = "manual", UnitSnapshot = "PCS", InvoiceNo = "INV1;INV2", CustomerPoNo = "P1/P2", Marks = "MARK", Note = "note", PcsPerPackage = 5, PackageCount = 2, NwPerPackage = 1 });
            var source = new SourceDemandDeliveryItemDto { GrossDemandId = Guid.NewGuid(), CustomerId = Head.CustomerId, IsActive = true, IsNonStandard = true, Unit = "PCS", DemandVersion = 3, SourceVersion = 4, RemainingQty = 100, SourceType = "SO", SourceNo = "SO1", SourceLineNumber = 1 };
            Sources.Add(source.GrossDemandId, source);
            var headSnapshot = Head; List<PackingListLine> rowSnapshot = [], deletedSnapshot = []; var existsSnapshot = true;
            Dictionary<Guid, CommandReceipt> receiptSnapshot = []; List<BusinessOperationLog> logSnapshot = [];
            IDbConnection? connection = null;
            var transaction = Proxy.Make<IDbTransaction>((m, _) =>
            {
                if (m == "get_Connection") return connection;
                if (m == "Commit") { Commits++; return null; }
                if (m == "Rollback") { Rollbacks++; Head = headSnapshot; Rows = rowSnapshot; Deleted = deletedSnapshot; Exists = existsSnapshot; receipts = receiptSnapshot; Logs = logSnapshot; return null; }
                if (m == "Dispose") return null;
                throw new NotSupportedException(m);
            });
            connection = Proxy.Make<IDbConnection>((m, _) =>
            {
                if (m != "BeginTransaction") throw new NotSupportedException(m);
                headSnapshot = Head.Adapt<PackingListHead>(); rowSnapshot = Rows.Adapt<List<PackingListLine>>(); deletedSnapshot = Deleted.Adapt<List<PackingListLine>>(); existsSnapshot = Exists;
                receiptSnapshot = new(receipts); logSnapshot = new(Logs); return transaction;
            });
            var session = providedSession ?? (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            if (providedSession == null) typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, connection);
            var context = new ServiceBaseContext(session, NullLoggerFactory.Instance);
            var repository = providedRepository ?? Proxy.Make<IPackingListDocumentRepository>((m, a) => m switch
            {
                "IsSchemaReadyAsync" => Task.FromResult(true), "GetDetailAsync" => Task.FromResult<PackingListDetailDto?>(Exists ? Detail() : null),
                "LockHeaderAsync" => Task.FromResult<PackingListHead?>(Exists ? Head : null), "LockLinesAsync" => Task.FromResult<IReadOnlyList<PackingListLine>>(Rows),
                "LockLineOwnersAsync" => Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>()),
                "SaveAsync" => Save((PackingListHead)a[0]!, (IReadOnlyList<PackingListLine>)a[1]!, (IReadOnlyList<PackingListLine>)a[2]!), _ => throw new NotSupportedException(m)
            });
            var queries = Proxy.Make<IPackingListQueryRepository>((m, _) => m == "GetSourceEnrichmentsAsync" ? Task.FromResult<IReadOnlyList<PackingListSourceEnrichment>>([]) : throw new NotSupportedException(m));
            var facts = Proxy.Make<IPackingListPostingFactsRepository>((m, _) => m == "LockSourcesAsync" ? Task.CompletedTask : throw new NotSupportedException(m));
            var delivery = Proxy.Make<ISourceDeliveryService>((m, a) =>
            {
                if (m != "VerifyForUpdateAsync") throw new NotSupportedException(m);
                LastClaims = (IReadOnlyCollection<SourceDeliveryClaim>)a[0]!; Assert.IsNotNull(session.Transaction);
                if (LastClaims.Any(x => Sources[x.GrossDemandId].RemainingQty < x.Quantity)) throw new SourceDeliveryException("EXCEEDED", "超过余额");
                return Task.FromResult<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>>(Sources);
            });
            var customers = Proxy.Make<ICustomerRepository>((m, _) => m == "GetByIdAsync" ? Task.FromResult<Customer?>(new() { Uid = Head.CustomerId, CustomerName = "customer", CustomerCode = "C1", DocRelateNo = "C1", IsActive = true }) : throw new NotSupportedException(m));
            var logs = Proxy.Make<IBusinessOperationLogRepository>((m, a) => { if (m != "InsertAsync") throw new NotSupportedException(m); if (FailLog) return Task.FromResult(0); Logs.Add((BusinessOperationLog)a[0]!); return Task.FromResult(1); });
            var numbers = Proxy.Make<IDocumentNoService>((m, _) => Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new() { DocumentNo = "PL-NEW" })));
            var userId = Guid.NewGuid();
            var user = Proxy.Make<ICurrentUser>((m, _) => m switch { "get_IsAuthenticated" => true, "get_UserId" => userId, "get_UserName" => "test", _ => throw new NotSupportedException(m) });
            var permissions = Proxy.Make<IPermissionEvaluationService>((m, _) => Task.FromResult(true));
            var receiptsRepository = providedReceipts ?? Proxy.Make<ICommandReceiptRepository>((m, a) => m switch { "LockAsync" => Task.FromResult(0), "FindAsync" => Task.FromResult(receipts.GetValueOrDefault((Guid)a[0]!)), "InsertAsync" => Insert((CommandReceipt)a[0]!), _ => throw new NotSupportedException(m) });
            var validators = new ServiceCollection(); validators.AddSingleton<IValidator<CreatePackingListDraftCommand>>(new CreatePackingListDraftCommandValidator()); validators.AddSingleton<IValidator<AssociatePackingListSourcesCommand>>(new AssociatePackingListSourcesCommandValidator());
            var correlation = Proxy.Make<ICorrelationContext>((m, _) => "test");
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators.BuildServiceProvider()), correlation, user, NullLogger<CommandPipeline>.Instance);
            Checker = new PackingListImportChecker(Proxy.Make<IForExcelSalesOrderRepository>((m, _) => m == "MatchProductsAsync" ? Task.FromResult<IReadOnlyList<SalesOrderImportProductCandidateDto>>(Products) : throw new NotSupportedException(m)));
            Service = new(context, repository, queries, facts, delivery, customers, logs, numbers, user, permissions, pipeline, new BusinessCommandExecutor(context, receiptsRepository, user), Checker);
        }
        private PackingListDetailDto Detail() => new() { Header = Head.Adapt<PackingListHeaderDto>(), Lines = Rows.Adapt<List<PackingListLineDto>>() };
        private Task Save(PackingListHead head, IReadOnlyList<PackingListLine> rows, IReadOnlyList<PackingListLine> deleted)
        { Head = head; Head.Version++; Rows = rows.ToList(); foreach (var row in Rows) row.Version++; Deleted.AddRange(deleted); Exists = true; return Task.CompletedTask; }
        private Task Insert(CommandReceipt value) { receipts.Add(value.CommandId, value); return Task.CompletedTask; }
        internal PackingListDraftLineInput Standard() => new() { Uid = Guid.NewGuid(), LineNumber = 1, Qty = 2, CustomerSkuSnapshot = "CUSTOMER", ProductId = Products[0].ProductId, UnitSnapshot = "PCS", DescriptionEnSnapshot = "Excel text" };
        internal CreatePackingListDraftCommand Create() => new() { Uid = Head.Uid, CommandId = Guid.NewGuid(), Header = new() { CustomerId = Head.CustomerId, InvoiceDate = DateTime.Today }, Lines = [Standard()] };
        internal AssociatePackingListSourcesCommand Command(decimal qty) => new() { CommandId = Guid.NewGuid(), ExpectedVersion = Head.Version, ExpectedLineVersion = Rows[0].Version, Allocations = [new() { GrossDemandId = Sources.Keys.Single(), Qty = qty, ExpectedDemandVersion = 3, ExpectedSourceVersion = 4 }] };
    }
}
