using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features
{
    /// <summary>验证入库供应商候选独立供数和候选失败后的主体查询。</summary>
    public sealed partial class InboundSourcePickerTests
    {
        /// <summary>候选超过 200 条仍完整读取，保持身份编码名称及现有 SupplierDto 选中绑定。</summary>
        [TestMethod]
        public async Task SupplierOptions_ReadAllPagesWithoutMasterDataCache()
        {
            var api = new SourceApiStub();
            var rows = Enumerable.Range(1, 201).Select(i => new InventorySupplierLookupDto { Uid = Guid.NewGuid(), SupplierCode = $"S-{i}", SupplierName = $"供应商{i}" }).ToList();
            api.SuppliersHandler = request => Task.FromResult(ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Ok(new()
            { Items = rows.Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToList(), TotalCount = rows.Count }));
            using var vm = new InboundSourcePickerViewModel(api, new RunnerStub());
            await vm.InitializeAsync(new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt }, CancellationToken.None);
            Assert.AreEqual(201, vm.Suppliers.Count); CollectionAssert.AreEqual(new[] { 1, 2 }, api.SupplierRequests.Select(x => x.PageIndex).ToArray());
            Assert.IsTrue(api.SupplierRequests.All(x => x.PageSize == 200)); Assert.AreEqual("S-201", vm.Suppliers.Last().SupplierCode);
            vm.SelectedSupplier = vm.Suppliers.Last(); await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(rows.Last().Uid, api.Requests.Last().SupplierId);
        }

        /// <summary>中途失败不显示半份候选，首次来源查询仍不带供应商条件且明确提示候选失败。</summary>
        [TestMethod]
        public async Task SupplierOptions_PartialFailureDoesNotBlockUnfilteredSources()
        {
            var api = new SourceApiStub();
            api.SuppliersHandler = request => Task.FromResult(request.PageIndex == 1
                ? ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Ok(new() { Items = Enumerable.Range(1, 200).Select(_ => new InventorySupplierLookupDto { Uid = Guid.NewGuid() }).ToList(), TotalCount = 201 })
                : ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Fail(ServiceErrorCodes.Forbidden, "候选拒绝"));
            using var vm = new InboundSourcePickerViewModel(api, new RunnerStub());
            await vm.InitializeAsync(new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt }, CancellationToken.None);
            Assert.AreEqual(0, vm.Suppliers.Count); Assert.AreEqual(1, api.Requests.Count); Assert.IsNull(api.Requests.Single().SupplierId);
            Assert.AreEqual(1, vm.Items.Count); Assert.IsTrue(vm.CanSelect); StringAssert.Contains(vm.StatusText, "供应商候选未加载"); StringAssert.Contains(vm.StatusText, "候选拒绝");
        }

        /// <summary>关闭候选读取会话时，忽略取消的响应也不能回填供应商或再发首次主体查询。</summary>
        [TestMethod]
        public async Task SupplierOptions_DisposeRejectsLateResponse()
        {
            var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>>();
            var api = new SourceApiStub { SuppliersHandler = _ => pending.Task }; using var vm = new InboundSourcePickerViewModel(api, new RunnerStub());
            var loading = vm.InitializeAsync(new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt }, CancellationToken.None);
            vm.Dispose(); pending.SetResult(ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Ok(new() { Items = [new() { Uid = Guid.NewGuid() }], TotalCount = 1 })); await loading;
            Assert.AreEqual(0, vm.Suppliers.Count); Assert.AreEqual(0, api.Requests.Count);
        }
    }
}

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory
{
    /// <summary>验证余额和流水使用业务供应商接口，不依赖完整基础资料缓存。</summary>
    public sealed partial class InventoryReadOnlyViewModelTests
    {
        /// <summary>两个库存页面均完整加载 201 条候选，选中身份仍传入原查询字段。</summary>
        [TestMethod]
        public async Task SupplierOptions_BalancesAndLedgerReadAllPages()
        {
            var api = new InventoryApiStub();
            var rows = Enumerable.Range(1, 201).Select(i => new InventorySupplierLookupDto { Uid = Guid.NewGuid(), SupplierCode = $"S-{i}", SupplierName = $"供应商{i}" }).ToList();
            api.SuppliersHandler = request => Task.FromResult(ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Ok(new()
            { Items = rows.Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToList(), TotalCount = 201 }));
            using var balances = new InventoryBalancesViewModel(api, new WarehouseApiStub(), new Runner(), new MessageStub());
            using var ledger = new InventoryLedgerViewModel(api, new WarehouseApiStub(), new Runner());
            await balances.InitializeAsync(); await ledger.InitializeAsync();
            Assert.AreEqual(201, balances.SupplierOptions.Count); Assert.AreEqual(201, ledger.SupplierOptions.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, api.SupplierRequests.Select(x => x.PageIndex).ToArray());
            balances.SelectedSupplier = balances.SupplierOptions.Last(); ledger.SelectedSupplier = ledger.SupplierOptions.Last();
            await balances.SearchCommand.ExecuteAsync(null); await ledger.SearchCommand.ExecuteAsync(null);
            Assert.AreEqual(rows.Last().Uid, api.LastBalanceRequest!.SupplierId); Assert.AreEqual(rows.Last().Uid, api.LastLedgerRequest!.SupplierId);
        }

        /// <summary>供应商接口失败不阻断余额或流水主体查询，提示独立保留。</summary>
        [TestMethod]
        public async Task SupplierOptions_FailurePreservesMainQueryAndFeedback()
        {
            var api = new InventoryApiStub { SuppliersHandler = _ => Task.FromResult(ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Fail(ServiceErrorCodes.Forbidden, "候选失败")) };
            var messages = new MessageStub();
            using var balances = new InventoryBalancesViewModel(api, new WarehouseApiStub(), new Runner(), messages);
            using var ledger = new InventoryLedgerViewModel(api, new WarehouseApiStub(), new Runner());
            await balances.InitializeAsync(); await ledger.InitializeAsync();
            Assert.IsNotNull(api.LastBalanceRequest); Assert.IsNotNull(api.LastLedgerRequest); Assert.IsNull(api.LastBalanceRequest.SupplierId); Assert.IsNull(api.LastLedgerRequest.SupplierId);
            Assert.IsTrue(messages.Errors.Any(x => x.Contains("供应商候选未加载", StringComparison.Ordinal))); StringAssert.Contains(ledger.ResultSummary, "候选失败");
        }
    }
}
