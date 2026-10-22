using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>用真实预览与来源事务验证 BONO 拒绝及追加边界；不把事务替身当作真实 PG 验证。</summary>
[TestClass]
public sealed class 需求采购BONO创建测试
{
    /// <summary>仅读取及创建权限仍能完整新建采购；追加因缺少编辑权限在事务和任何写入之前拒绝。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CreateAndReadWithoutEditAllowsOnlyNewPurchase(bool append)
    {
        var fixture=new Fixture();var command=await fixture.CommandAsync(append);
        fixture.Commands.AllowedPermissions=[PermissionKeys.ProcurementOrderRead,PermissionKeys.ProcurementOrderCreate];
        var result=await fixture.Service.CreateAsync(command);
        if(append)
        {
            Assert.IsFalse(result.Success);Assert.AreEqual(ServiceErrorCodes.Forbidden,result.Error.Code);
            Assert.AreEqual(0,fixture.HeadWrites);Assert.AreEqual(0,fixture.LineWrites);Assert.AreEqual(0,fixture.LinkWrites);
            Assert.AreEqual(0,fixture.VersionWrites);Assert.AreEqual(0,fixture.Numbers);Assert.AreEqual(0,fixture.Commands.ReceiptLocks);
            Assert.AreEqual(0,fixture.Connection.Commits);Assert.AreEqual(0,fixture.Connection.Rollbacks);
            CollectionAssert.AreEqual(new[]{PermissionKeys.ProcurementOrderCreate,PermissionKeys.ProcurementOrderEdit},fixture.Commands.PermissionChecks.ToArray());
        }
        else
        {
            Assert.IsTrue(result.Success,result.Message);Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,result.Data.Sources.Single().Outcome);
            Assert.AreEqual(1,fixture.HeadWrites);Assert.AreEqual(1,fixture.LineWrites);Assert.AreEqual(1,fixture.LinkWrites);
            Assert.AreEqual(1,fixture.Numbers);Assert.AreEqual(1,fixture.Connection.Commits);Assert.HasCount(1,fixture.Commands.Receipts);
            CollectionAssert.AreEqual(new[]{PermissionKeys.ProcurementOrderCreate},fixture.Commands.PermissionChecks.ToArray());
        }
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>审计写入失败在已确认回滚后返回确定失败；相同原请求修复故障后可成功，不被永久失败回执占用。</summary>
    [TestMethod]
    public async Task AuditFailureIsKnownRollbackAndSameCommandCanRecover()
    {
        var fixture=new Fixture();var command=await fixture.CommandAsync(false);fixture.Commands.RejectAudit=true;
        var failure=await fixture.Service.CreateAsync(command);var source=failure.Data.Sources.Single();
        Assert.AreEqual(ServiceErrorCodes.WriteFailed,source.ErrorCode);StringAssert.Contains(source.Message,"已回滚");Assert.IsEmpty(source.PurchaseDrafts);
        Assert.AreEqual(0,fixture.Commands.Receipts.Count);Assert.AreEqual(1,fixture.Connection.Rollbacks);Assert.AreEqual(0,fixture.Connection.Commits);
        fixture.Commands.RejectAudit=false;var success=await fixture.Service.CreateAsync(command);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,success.Data.Sources.Single().Outcome);Assert.AreEqual(1,fixture.Commands.Receipts.Count);
    }
    /// <summary>实物依据失效返回可定位的待核实错误，不能伪装为没有可创建需求。</summary>
    [TestMethod]
    public async Task InvalidPhysicalBasisExplainsMaterialBeforeWrites()
    {
        var fixture=new Fixture();var command=await fixture.CommandAsync(false);fixture.InvalidateBasis();
        var result=await fixture.Service.CreateAsync(command);var source=result.Data.Sources.Single();
        Assert.AreEqual(PurchaseDraftErrorCodes.CorruptedState,source.ErrorCode);StringAssert.Contains(source.Message,command.Sources[0].Items[0].NetDemandId.ToString());
        Assert.AreEqual(0,fixture.HeadWrites);Assert.IsEmpty(source.PurchaseDrafts);
    }
    /// <summary>直接提交无需预览令牌，数量及价格均遵循现有四位物理精度边界。</summary>
    [TestMethod,DataRow("0.1234",true),DataRow("0.12345",false)]
    public async Task DirectCreatePreservesFourDecimalPrice(string price,bool valid)
    {
        var fixture=new Fixture();var command=await fixture.CommandAsync(false);
        command.Sources[0].Items[0].UnitPrice=decimal.Parse(price,System.Globalization.CultureInfo.InvariantCulture);
        var result=await fixture.Service.CreateAsync(command);
        if(valid){Assert.IsTrue(result.Success);Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,result.Data.Sources.Single().Outcome);}
        else {Assert.IsFalse(result.Success);Assert.AreEqual(0,fixture.HeadWrites);Assert.AreEqual(0,fixture.Connection.Commits);}
    }

    /// <summary>相同来源命令用不同商务输入重试必须冲突，不重新取号或生成第二份采购。</summary>
    [TestMethod]
    public async Task SameSourceCommandRejectsDifferentSupplierOrPrice()
    {
        var fixture=new Fixture();var command=await fixture.CommandAsync(false);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,(await fixture.Service.CreateAsync(command)).Data.Sources.Single().Outcome);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied,(await fixture.Service.CreateAsync(command)).Data.Sources.Single().Outcome);
        command.Sources[0].Items[0].SupplierId=Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,(await fixture.Service.CreateAsync(command)).Data.Sources.Single().ErrorCode);
        Assert.AreEqual(1,fixture.HeadWrites);Assert.AreEqual(1,fixture.Numbers);
    }
    /// <summary>查询预览与锁定创建必须采用相同型号；产品空值回退来源快照，空字符串仍保留原值。</summary>
    [TestMethod]
    [DataRow(null, "AA030063BG")]
    [DataRow("", "")]
    [DataRow("CURRENT", "CURRENT")]
    public async Task ReadOnlyPreviewThenCreate_KeepsProductModelBasis(string? productModel, string queryModel)
    {
        var f = new Fixture(true, productModel, queryModel);
        var command = await f.CommandAsync(false, true);
        var result = await f.Service.CreateAsync(command);
        var source = result.Data.Sources.Single();
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, source.Outcome, $"{source.ErrorCode}: {source.Message}");
        Assert.AreEqual(1, f.HeadWrites);
        Assert.AreEqual(1, f.LineWrites);
        Assert.AreEqual(1, f.LinkWrites);
        Assert.AreEqual(1, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>预览完成之后正式创建仍重新查重，拒绝时不取号、不写头行或来源贡献。</summary>
    [TestMethod]
    public async Task DuplicateAfterPreview_RejectsBeforeNumberOrWrites()
    {
        var f = new Fixture(); var command = await f.CommandAsync(false); f.Duplicate = true;
        var result = await f.Service.CreateAsync(command);
        var source = result.Data.Sources.Single();
        Assert.AreEqual(ServiceErrorCodes.Validation, source.ErrorCode, source.Message);
        Assert.IsEmpty(source.PurchaseDrafts); Assert.AreEqual(1, f.BoNoChecks);
        Assert.AreEqual(0, f.Numbers); Assert.AreEqual(0, f.HeadWrites); Assert.AreEqual(0, f.LineWrites);
        Assert.AreEqual(0, f.LinkWrites); Assert.AreEqual(1, f.Connection.Rollbacks); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>并发唯一冲突的结构化写入拒绝必须回滚，不能继续写行或发布未提交单号。</summary>
    [TestMethod]
    public async Task UniqueWriteConflict_RollsBackWithoutPublishing()
    {
        var f = new Fixture { WriteConflict = true }; var result = await f.Service.CreateAsync(await f.CommandAsync(false));
        var source = result.Data.Sources.Single();
        Assert.AreEqual(ServiceErrorCodes.Validation, source.ErrorCode, source.Message); Assert.IsEmpty(source.PurchaseDrafts);
        Assert.AreEqual(1, f.BoNoChecks); Assert.AreEqual(1, f.Numbers); Assert.AreEqual(1, f.HeadWrites);
        Assert.AreEqual(0, f.LineWrites); Assert.AreEqual(0, f.LinkWrites); Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>新号成功时提交完整头行与贡献；显式追加不查新号、不取号，只增加既有草稿行和版本。</summary>
    [TestMethod]
    public async Task NewAndAppend_PreserveTheirDifferentNumberingBoundaries()
    {
        var created = new Fixture(); var createResult = await created.Service.CreateAsync(await created.CommandAsync(false));
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, createResult.Data.Sources.Single().Outcome,
            createResult.Data.Sources.Single().Message);
        Assert.AreEqual(1, created.HeadWrites); Assert.AreEqual(1, created.LineWrites); Assert.AreEqual(1, created.LinkWrites);
        Assert.AreEqual(1, created.Connection.Commits);
        var appended = new Fixture { Duplicate = true }; var appendResult = await appended.Service.CreateAsync(await appended.CommandAsync(true));
        var source = appendResult.Data.Sources.Single();
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, source.Outcome, source.Message);
        Assert.IsTrue(source.PurchaseDrafts.Single().AppendedToExisting); Assert.AreEqual(appended.Head.Uid, source.PurchaseDrafts[0].PurchaseOrderId);
        Assert.AreEqual(0, appended.BoNoChecks); Assert.AreEqual(0, appended.Numbers); Assert.AreEqual(0, appended.HeadWrites);
        Assert.AreEqual(1, appended.LineWrites); Assert.AreEqual(1, appended.LinkWrites); Assert.AreEqual(1, appended.VersionWrites);
        Assert.AreEqual(1, appended.Connection.Commits); Assert.AreEqual(0, appended.Connection.Rollbacks);
    }

    /// <summary>仅提供本测试需要的主数据及活动需求，所有非声明调用立即失败。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly PurchaseDraftCreationService Service;
        public readonly PurchaseOrderHead Head;
        public readonly PurchaseDraftCommandTestContext Commands;
        public bool Duplicate, WriteConflict;
        public int BoNoChecks, Numbers, HeadWrites, LineWrites, LinkWrites, VersionWrites;
        private readonly Customer customer = new() { Uid = Guid.NewGuid(), CustomerCode = "C", DocRelateNo = "C", IsActive = true };
        private readonly Supplier supplier = new() { Uid = Guid.NewGuid(), SupplierCode = "S", SupplierName = "供应商", DocRelateNo = "S", IsActive = true };
        private readonly SalesOrderHeader sales = new() { Uid = Guid.NewGuid(), OrderNo = "SO1", InvoiceNo = "260820" };
        private readonly GrossDemand gross;
        private readonly NetDemand net;
        private readonly PurchaseDraftPreviewService preparation;
        private readonly PurchaseDraftPreviewBuilder builder = new();
        /// <summary>仅改变该夹具来源实物依据，用于提交后锁内校验拒绝路径。</summary>
        public void InvalidateBasis()=>gross.DemandBasisFingerprint=new string('B',64);

        /// <summary>组合真实预览与创建服务，仓储替身只观察本次用例的事务和写入。</summary>
        public Fixture(bool hasProduct = false, string? productModel = null, string queryModel = "")
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            gross = new() { Uid = Guid.NewGuid(), SourceType = "SO", SourceUid = sales.Uid, SourceNo = "SO1",
                CustomerId = customer.Uid, SourceLineId = Guid.NewGuid(), SourceLineNumber = 1, IsNonStandard=!hasProduct, DemandBasisFingerprint=new string('A',64) };
            net = new() { Uid = Guid.NewGuid(), Version = 1, GrossDemandId = gross.Uid, NetQty = 1, SupplyTypeSnapshot = "采购",
                SkuSnapshot = "NS1", DescriptionSnapshot = "非标配件", UnitSnapshot = "PCS", DecompositionRevision=1,SourceDemandBasisFingerprintSnapshot=gross.DemandBasisFingerprint };
            var products = hasProduct ? new[] { new Product { Uid = Guid.NewGuid(), Sku = net.SkuSnapshot, LinkSku = productModel } } : [];
            if (hasProduct)
            {
                net.ProductId = products[0].Uid;
                gross.LinkSkuSnapshot = "AA030063BG";
            }
            Head = new() { Uid = Guid.NewGuid(), Version = 1, OrderNo = "PO1", CustomerId = customer.Uid, SupplierId = supplier.Uid,
                DemandSourceType = "SO", DemandSourceUid = sales.Uid, BoNo = "C260820S",
                LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active, ConfirmationStatus = ConfirmationStatusEnum.待确认 };
            var orders = Proxy<IPurchaseOrderDraftRepository>((method, _) => method.Name switch
            {
                "BoNoExistsAsync" => CheckBoNo(),
                "GetByExecutionScopeForUpdateAsync" => Task.FromResult<PurchaseOrderHead?>(null),
                "GetSafeAppendCandidatesAsync" => Task.FromResult<IReadOnlyList<SafePurchaseDraftCandidateDto>>([new()
                { PurchaseOrderId = Head.Uid, Version = Head.Version, OrderNo = Head.OrderNo, SourceType = "SO", SourceUid = sales.Uid, SupplierId = supplier.Uid }]),
                "GetPurchaseDraftsByIdsAsync" => Task.FromResult<IReadOnlyList<PurchaseOrderHead>>([Head]),
                "GetSafeDraftForUpdateAsync" => Task.FromResult<PurchaseOrderHead?>(Head),
                "GetMaxLineNumberForUpdateAsync" => Task.FromResult(2),
                "InsertPurchaseDraftAsync" => InsertHead(),
                "InsertPurchaseDraftLinesAsync" => CountLines(),
                "AdvanceDraftVersionAfterAppendAsync" => CountVersion(),
                _ => throw new AssertFailedException(method.Name)
            });
            var grossRepo = Proxy<IGrossDemandDocumentRepository>((method, _) => method.Name == "GetActiveBySourceForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<GrossDemand>>([gross]) : throw new AssertFailedException(method.Name));
            var netRepo = Proxy<IMaterialDemandDocumentRepository>((method, _) => method.Name == "GetActiveByGrossDemandForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<NetDemand>>([net]) : throw new AssertFailedException(method.Name));
            var links = Proxy<INetDemandExecutionLinkRepository>((method, _) => method.Name switch
            {
                "GetActiveByNetDemandIdsForUpdateAsync" => Task.FromResult<IReadOnlyList<PurchaseDemandCommitmentDto>>([]),
                "GetByCommandItemIdsForUpdateAsync" => Task.FromResult<IReadOnlyList<DemandExecutionLink>>([]),
                "InsertPurchaseLinksAsync" => CountLinks(),
                _ => throw new AssertFailedException(method.Name)
            });
            preparation = new(context, Proxy<IMaterialDemandQueryRepository>((method, _) => method.Name == "SearchManagerAsync"
                ? Task.FromResult(new PagedResponseBase<MaterialDemandManagerItemDto> { Items = [new()
                {
                    Uid = net.Uid, Version = net.Version, ProductId = net.ProductId,
                    SourceType = gross.SourceType, SourceUid = gross.SourceUid, SourceNo = gross.SourceNo,
                    SourceLineId = gross.SourceLineId, SourceLineNumber = gross.SourceLineNumber,
                    CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName,
                    Sku = net.SkuSnapshot, LinkSku = queryModel, Description = net.DescriptionSnapshot,
                    Unit = net.UnitSnapshot, SupplyType = net.SupplyTypeSnapshot, NetQty = net.NetQty,
                    AssociatedPurchaseQty = 0
                }] }) : throw new AssertFailedException(method.Name)), orders,
                Proxy<IProductRepository>((method, _) => method.Name == "GetByIdListAsync" ? Task.FromResult<IReadOnlyList<Product>>(products) : throw new AssertFailedException(method.Name)),
                Proxy<ICustomerRepository>((method, _) => method.Name == "GetByIdListAsync" ? Task.FromResult<IReadOnlyList<Customer>>([customer]) : throw new AssertFailedException(method.Name)),
                Proxy<ICustomerPoConfigRepository>((method, _) => method.Name == "GetByPartiesAsync" ? Task.FromResult<CustomerPoConfigDto?>(null) : throw new AssertFailedException(method.Name)),
                Proxy<ISupplierRepository>((method, _) => method.Name == "GetByIdListAsync" ? Task.FromResult<IReadOnlyList<Supplier>>([supplier]) : throw new AssertFailedException(method.Name)),
                Proxy<ISupplierProductRepository>((method, _) => method.Name == "GetPurchaseDataAsync" ? Task.FromResult<IReadOnlyList<SupplierProduct>>([]) : throw new AssertFailedException(method.Name)),
                Proxy<ISalesOrderHeadRepository>((method, _) => method.Name == "GetByIdListAsync" ? Task.FromResult<IReadOnlyList<SalesOrderHeader>>([sales]) : throw new AssertFailedException(method.Name)), builder,
                Proxy<IProcurementDemandAssociationRepository>((method,_)=>Task.FromResult<IReadOnlyList<SwiftExport.Core.Models.Procurement.ProcurementAssociationTarget>>([])));
            var numbers = Proxy<IDocumentNoService>((method, _) =>
            { Numbers++; return Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new() { DocumentNo = "PO-NEW" })); });
            Commands=new PurchaseDraftCommandTestContext(context);
            Service = new(context, grossRepo, netRepo, links, orders, numbers, preparation, builder, new PurchaseDraftWriteModelBuilder(),Commands.Executor,Commands.Support);
        }

        /// <summary>默认直接提交正式输入；可选内部只读预检仅用于展示映射测试，不是创建前提。</summary>
        public async Task<CreatePurchaseDraftsCommand> CommandAsync(bool append, bool readOnlyPreview = false)
        {
            var source = new CreatePurchaseDraftSourceCommand { SourceType = "SO", SourceUid = sales.Uid,
                Items = [new() { CommandItemId = Guid.NewGuid(), NetDemandId = net.Uid, SupplierId = supplier.Uid,
                    ExpectedVersion = 1, PurchaseQty = 1, UnitPrice = 0 }],
                Targets = [new() { SupplierId = supplier.Uid, TargetMode = append ? PurchaseDraftTargetModeEnum.AppendExisting : PurchaseDraftTargetModeEnum.CreateNew,
                    ExistingPurchaseOrderId = append ? Head.Uid : null, ExpectedPurchaseOrderVersion = append ? Head.Version : null }] };
            var command = new CreatePurchaseDraftsCommand { ExecutionRequestId = Guid.NewGuid(), EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, Sources = [source] };
            if(!readOnlyPreview)return command;
            var preview = builder.Build(await preparation.BuildLockedPreviewRequestAsync(command, source, [gross], [net], [], CancellationToken.None));
            if (readOnlyPreview)
            {
                var item = source.Items.Single();
                var response = await preparation.PreviewAsync(new()
                {
                    ExecutionRequestId = command.ExecutionRequestId, EntryMode = command.EntryMode, LineMode = source.LineMode,
                    Items = [new() { CommandItemId = item.CommandItemId, NetDemandId = item.NetDemandId,
                        ExpectedVersion = item.ExpectedVersion, ExpectedRemainingQty = 1,
                        SupplierId = item.SupplierId, PurchaseQty = item.PurchaseQty, UnitPrice = item.UnitPrice }]
                });
                Assert.IsTrue(response.Success, response.Message);
                preview = response.Data;
            }
            Assert.IsFalse(preview.HasBlockers, string.Join("；", preview.Warnings.Select(x => x.Message)));


            return command;
        }

        // 写入口只记录事务内调用，不能据此宣称唯一索引或物理回滚已经验收。
        private Task<bool> CheckBoNo() { Assert.IsNotNull(Session.Transaction); BoNoChecks++; return Task.FromResult(Duplicate); }
        private Task<RepositoryWriteResult> InsertHead()
        { Assert.IsNotNull(Session.Transaction); HeadWrites++; return Task.FromResult(WriteConflict
            ? RepositoryWriteResult.Fail(Guid.Empty, null, ServiceErrorCodes.Validation, "BONO 已被使用") : RepositoryWriteResult.Ok(Guid.NewGuid(), null, 1)); }
        private Task<int> CountLines() { Assert.IsNotNull(Session.Transaction); LineWrites++; return Task.FromResult(1); }
        private Task<int> CountLinks() { Assert.IsNotNull(Session.Transaction); LinkWrites++; return Task.FromResult(1); }
        private Task<int> CountVersion() { Assert.IsNotNull(Session.Transaction); VersionWrites++; return Task.FromResult(1); }
        private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
            => PurchaseDraftSourceOrchestrationTests.Proxy<T>.New(handler);
    }

    /// <summary>观察来源事务的提交与回滚，所有 SQL 访问都拒绝。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Commits, Rollbacks;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new TransactionStub(this);
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() => owner.Commits++;
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
