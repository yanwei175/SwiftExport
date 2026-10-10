using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Services.SalesOrders.Draft;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Customers.CustomerProducts;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>通过完整销售保存命令验证重复拒绝、更新排除自身、回执重放和并发失败回滚；不访问数据库。</summary>
[TestClass]
public sealed class 销售草稿发票号唯一测试
{
    /// <summary>创建和更新发现重复时，在编号及头行写入前拒绝，并清理失败事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task DuplicateInvoiceBlocksBeforeAnyWrites(bool update)
    {
        var f = new Fixture { Duplicate = true };
        var request = f.Request(update); request.Header.InvoiceNo = "INV-1";
        var result = await f.Service.SaveAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual("Header.InvoiceNo", result.Error.Field);
        StringAssert.Contains(result.Message, "删除原来的草稿或修改发票号");
        Assert.AreEqual(request.Header.Uid, f.ExcludedUid); Assert.AreEqual("INV-1", f.CheckedInvoice);
        Assert.AreEqual(0, f.NumberCalls); Assert.AreEqual(0, f.HeaderWrites); Assert.AreEqual(0, f.LineWrites);
        Assert.AreEqual(0, f.ReceiptWrites); Assert.AreEqual(1, f.Session.Rollbacks); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>无其他占用时新建及更新可保存，同命令重放不重复查重或写入。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task AvailableInvoiceSavesAndReplayDoesNotCheckAgain(bool update)
    {
        var f = new Fixture(); var request = f.Request(update);
        Assert.IsTrue((await f.Service.SaveAsync(request)).Success);
        Assert.AreEqual(request.Header.Uid, f.ExcludedUid); Assert.AreEqual(1, f.CheckCalls);
        Assert.AreEqual(1, f.HeaderWrites); Assert.AreEqual(1, f.LineWrites); Assert.AreEqual(1, f.ReceiptWrites);
        f.Duplicate = true;
        Assert.IsTrue((await f.Service.SaveAsync(request)).Success);
        Assert.AreEqual(1, f.CheckCalls); Assert.AreEqual(1, f.HeaderWrites); Assert.AreEqual(1, f.ReceiptWrites);
        request.Header.InvoiceNo = "OTHER";
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.Service.SaveAsync(request)).Error.Code);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>查重后并发索引失败仍返回同一字段提示，整单不写行或回执且事务回滚。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ConcurrentUniqueConflictRollsBack(bool update)
    {
        var f = new Fixture { WriteConflict = true };
        var result = await f.Service.SaveAsync(f.Request(update));
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual("Header.InvoiceNo", result.Error.Field);
        Assert.AreEqual(0, f.LineWrites); Assert.AreEqual(0, f.ReceiptWrites);
        Assert.AreEqual(1, f.Session.Rollbacks); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>无维护权限的请求不进入发票查询或业务事务。</summary>
    [TestMethod]
    public async Task PermissionDeniedDoesNotInspectInvoices()
    {
        var f = new Fixture { Allowed = false };
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.SaveAsync(f.Request(false))).Error.Code);
        Assert.AreEqual(0, f.CheckCalls); Assert.AreEqual(0, f.Session.Begins);
    }

    /// <summary>仅替代保存实际使用的方法，其余调用失败，避免宽松替身掩盖编排变更。</summary>
    private sealed class Fixture
    {
        internal readonly MemorySession Session = new();
        internal readonly SalesOrderDraftService Service;
        internal bool Duplicate, WriteConflict, Allowed = true;
        internal int CheckCalls, NumberCalls, HeaderWrites, LineWrites, ReceiptWrites;
        internal Guid ExcludedUid;
        internal string? CheckedInvoice;
        private readonly Guid customerId = Guid.NewGuid(), headId = Guid.NewGuid();
        private readonly Dictionary<Guid, CommandReceipt> receipts = [];

        internal Fixture()
        {
            var user = Proxy<ICurrentUser>((m, a) => m.Name switch {
                "get_IsAuthenticated" => true, "get_UserId" => (Guid?)customerId, "get_UserName" => "测试用户", _ => Unexpected(m) });
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var heads = Proxy<ISalesOrderHeadRepository>((m, a) => m.Name switch {
                "InvoiceNoExistsAsync" => Check((string)a![0]!, (Guid)a[1]!),
                "GetByIdAsync" => Task.FromResult<SalesOrderHeader?>(new() { Uid = headId, Version = 1, OrderNo = "SO-1" }),
                "AddAsync" or "UpdateAsync" => WriteHead((SalesOrderHeader)a![0]!), _ => Unexpected(m) });
            var lines = Proxy<ISalesOrderLineRepository>((m, a) => m.Name switch {
                "GetLinesByOrderIdAsync" => Task.FromResult<IReadOnlyList<SalesOrderLine>>([]),
                "GetExistingIdListAsync" => Task.FromResult<IReadOnlyList<Guid>>([]),
                "StageLineNumbersAsync" => Task.FromResult(0),
                "DeleteLinesAsync" or "UpdateLinesAsync" => Task.FromResult<IReadOnlyList<RepositoryWriteResult>>([]),
                "AddLinesAsync" => WriteLines(), _ => Unexpected(m) });
            var query = Proxy<ISalesOrderQueryRepository>((m, a) => m.Name == "GetDetailAsync"
                ? Task.FromResult<SalesOrderDetailDto?>(new() { Header = new() { Uid = (Guid)a![0]!, OrderNo = "SO-1" } }) : Unexpected(m));
            var customer = Proxy<ICustomerRepository>((m, a) => m.Name == "GetByIdAsync"
                ? Task.FromResult<Customer?>(new() { Uid = customerId, CustomerCode = "C-1", DocRelateNo = "C-1" }) : Unexpected(m));
            var products = Proxy<IProductRepository>((m, a) => m.Name == "GetByIdListAsync"
                ? Task.FromResult<IReadOnlyList<Product>>([]) : Unexpected(m));
            var customerProducts = Proxy<ICustomerProductRepository>((m, a) => m.Name == "GetByIdListAsync"
                ? Task.FromResult<IReadOnlyList<CustomerProduct>>([]) : Unexpected(m));
            var numbers = Proxy<IDocumentNoService>((m, a) => { NumberCalls++; return Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new() { DocumentNo = "SO-1" })); });
            var permission = Proxy<IPermissionEvaluationService>((m, a) => {
                Assert.AreEqual(PermissionKeys.SalesOrderMaintain, a![1]); return Task.FromResult(Allowed); });
            var receipt = Proxy<ICommandReceiptRepository>((m, a) => m.Name switch {
                "LockAsync" => Task.FromResult(0), "FindAsync" => Task.FromResult(receipts.GetValueOrDefault((Guid)a![0]!)),
                "InsertAsync" => AddReceipt((CommandReceipt)a![0]!), _ => Unexpected(m) });
            var correlation = Proxy<ICorrelationContext>((m, a) => "invoice-test");
            var validators = new ServiceCollection().AddSingleton<IValidator<SaveSalesOrderRequest>, SaveSalesOrderRequestValidator>().BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, user, NullLogger<CommandPipeline>.Instance);
            Service = new(context, heads, lines, query, products, customer, customerProducts, null!, numbers,
                user, permission, pipeline, new BusinessCommandExecutor(context, receipt, user));
        }

        internal SaveSalesOrderRequest Request(bool update) => new() {
            CommandId = Guid.NewGuid(), Header = new() { Uid = update ? headId : Guid.Empty, Version = update ? 1 : 0,
                CustomerId = customerId, InvoiceNo = "INV-1", OrderDate = DateTime.Today, Currency = "USD" },
            Lines = [new() { Uid = Guid.NewGuid(), IsNonStandard = true, DescriptionEn = "非标件", UnitSnapshot = "PCS", Qty = 1 }] };
        // 确认查重处于业务事务内，并记录排除身份，重复场景在任何业务写入前短路。
        private Task<bool> Check(string invoice, Guid excluded) {
            Assert.IsNotNull(Session.Transaction); CheckCalls++; CheckedInvoice = invoice; ExcludedUid = excluded; return Task.FromResult(Duplicate); }
        // 用仓储结构化拒绝模拟数据库竞争；不假称该替身可以验证真实唯一索引。
        private Task<RepositoryWriteResult> WriteHead(SalesOrderHeader head) {
            HeaderWrites++; if (WriteConflict) return Task.FromResult(RepositoryWriteResult.Fail(head.Uid, head.Version,
                ServiceErrorCodes.Validation, "发票号已被未删除的销售订单使用，请删除原来的草稿或修改发票号后重新保存。"));
            if (head.Uid == Guid.Empty) head.Uid = headId;
            return Task.FromResult(RepositoryWriteResult.Ok(head.Uid, head.Version, head.Version + 1)); }
        private Task<IReadOnlyList<RepositoryWriteResult>> WriteLines() { LineWrites++; return Task.FromResult<IReadOnlyList<RepositoryWriteResult>>([]); }
        private Task AddReceipt(CommandReceipt receipt) { ReceiptWrites++; receipts.Add(receipt.CommandId, receipt); return Task.CompletedTask; }
    }

    // 代理只观察接口边界，不模拟数据库 SQL、唯一性或锁的真实行为。
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class {
        var proxy = DispatchProxy.Create<T, MethodProxy>(); ((MethodProxy)(object)proxy).Handler = handler; return proxy; }
    private static object Unexpected(MethodInfo method) => throw new AssertFailedException("未预期调用：" + method.Name);
    /// <summary>测试接口调用代理，不参与生产代码。</summary>
    public class MethodProxy : DispatchProxy
    {
        /// <summary>由场景指定的调用处理。</summary>
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        /// <summary>转交实际接口调用与参数。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    /// <summary>观察默认事务入口、回滚和清理，不建立外部连接。</summary>
    private sealed class MemorySession : IDbSession, IDbConnection
    {
        internal int Begins, Rollbacks;
        public IDbConnection Connection => this;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>记录执行器默认事务入口，返回只观察提交／回滚的事务。</summary>
        public IDbTransaction BeginTransaction() { Begins++; return new Tx(this); }
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public void Open() { }
        public void Dispose() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        private sealed class Tx(MemorySession owner) : IDbTransaction {
            public IDbConnection Connection => owner; public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() { } public void Rollback() => owner.Rollbacks++; public void Dispose() { } }
    }
}
