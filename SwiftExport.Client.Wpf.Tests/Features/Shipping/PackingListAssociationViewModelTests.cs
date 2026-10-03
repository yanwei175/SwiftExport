using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Services.Api;
namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>来源分配的范围、数量、失败重试和页面状态测试，不依赖后端或真实弹窗。</summary>
[TestClass]
public sealed class PackingListAssociationViewModelTests
{
    /// <summary>单号拆分精确去重；产品范围固定，数量必须明确填写并限制原行上限。</summary>
    [TestMethod]
    public async Task PickerFiltersInvoiceAndValidatesExplicitAllocations()
    {
        var customer = Guid.NewGuid(); var product = Guid.NewGuid(); var source = Guid.NewGuid();
        var api = new PackingListApiStub(); PackingListSourceSearchRequest? sent = null;
        api.Sources = r => { sent = r; return Task.FromResult(ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>.Ok(new() { TotalCount = 1,
            Items = [new() { GrossDemandId = source, CustomerId = customer, ProductId = product, DemandVersion = 3, SourceVersion = 4, RemainingQty = 10, DeliveredQty = 0, UnitSnapshot = "PCS", InvoiceNo = "INV1" }] })); };
        using var picker = new PackingListSourcePickerViewModel(api, Runner());
        picker.ConfigureAllocation(product, false, " INV1 / inv1；INV2\nAFS1", 5);
        await picker.InitializeAsync(customer, new Dictionary<Guid, decimal> { [source] = 2 }, default);
        CollectionAssert.AreEqual(new[] { "INV1", "INV2", "AFS1" }, sent!.InvoiceNumbers);
        Assert.AreEqual(product, sent.ProductId); Assert.AreEqual(false, sent.IsNonStandard);
        var row = picker.Items.Single(); Assert.AreEqual(8m, row.AvailableQty); Assert.AreEqual("INV1", row.InvoiceNo);
        row.IsChecked = true; Assert.IsFalse(picker.IsPrimaryButtonEnabled);
        foreach (var bad in new[] { "bad", "0", "-1", "5.00001", "6", "9" }) { row.AllocationQtyText = bad; Assert.IsFalse(picker.IsPrimaryButtonEnabled, bad); }
        row.AllocationQtyText = "3"; Assert.IsTrue(picker.IsPrimaryButtonEnabled); Assert.AreEqual(3m, picker.GetSelectedResult().Single().Qty);
        row.IsChecked = false; picker.Search.InvoiceNumbersText = ""; await picker.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(0, sent.InvoiceNumbers.Count); Assert.AreEqual(product, sent.ProductId);
    }
    /// <summary>超大、负数及非法输入不使汇总抛异常；修正后确认状态和分配结果恢复。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AllocationOverflowKeepsInputAndCanRecover(bool maximumLimit)
    {
        var customer = Guid.NewGuid(); var api = new PackingListApiStub();
        api.Sources = _ => Task.FromResult(ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>.Ok(new()
        {
            TotalCount = 2,
            Items = Enumerable.Range(0, 2).Select(_ => new PackingListSourceCandidateDto
            { GrossDemandId = Guid.NewGuid(), CustomerId = customer, IsNonStandard = true, DemandVersion = 1, SourceVersion = 1,
                RemainingQty = maximumLimit ? decimal.MaxValue : 10, UnitSnapshot = "PCS" }).ToList()
        }));
        using var picker = new PackingListSourcePickerViewModel(api, Runner());
        picker.ConfigureAllocation(null, true, null, maximumLimit ? decimal.MaxValue : 5);
        await picker.InitializeAsync(customer, new Dictionary<Guid, decimal>(), default);
        foreach (var row in picker.Items) { row.AllocationQtyText = "1"; row.IsChecked = true; }
        Assert.IsTrue(picker.IsPrimaryButtonEnabled);
        var changes = 0; picker.StateChanged += (_, _) => changes++;
        foreach (var bad in new[] { decimal.MaxValue.ToString(), decimal.MinValue.ToString(), "bad" })
        {
            picker.Items[0].AllocationQtyText = bad;
            Assert.AreEqual(bad, picker.Items[0].AllocationQtyText);
            Assert.IsFalse(picker.IsPrimaryButtonEnabled);
            Assert.HasCount(0, picker.GetSelectedResult());
        }
        Assert.IsTrue(changes > 0, "错误输入仍须刷新确认状态。");
        picker.Items[0].AllocationQtyText = "2";
        Assert.IsTrue(picker.IsPrimaryButtonEnabled);
        Assert.AreEqual(3m, picker.GetSelectedResult().Sum(x => x.Qty));
        StringAssert.Contains(picker.StatusText, "已分配");
    }

    /// <summary>待关联行禁止过账；未知写入重试保持 CommandId 及分配输入，成功接纳整个拆行详情。</summary>
    [TestMethod]
    public async Task UnknownAssociationRetriesOriginalAndAcceptsSplitDetail()
    {
        var f = new Fixture(); using var vm = f.Create(); await f.Load(vm);
        Assert.IsFalse(vm.PostCommand.CanExecute(null)); vm.SelectedLine = vm.Lines.Items.Single();
        Assert.IsTrue(vm.AssociateSourcesCommand.CanExecute(null));
        f.Api.Associate = (_, _, _) => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("OUTCOME_UNKNOWN", "结果未知"));
        await vm.AssociateSourcesCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsEditable); Assert.IsFalse(await vm.CanCloseAsync(default));
        var original = f.Api.Associations.Single();
        f.Api.Associate = (_, _, cmd) =>
        {
            Assert.AreSame(original, cmd);
            f.Detail.Header.Version++;
            f.Detail.Lines = [new() { Uid = Guid.NewGuid(), PackingListHeadId = f.Detail.Header.Uid, GrossDemandId = f.SourceId, ProductId = f.ProductId, Qty = 3, Version = 1, LineNumber = 1, UnitSnapshot = "PCS" },
                new() { Uid = Guid.NewGuid(), PackingListHeadId = f.Detail.Header.Uid, ProductId = f.ProductId, Qty = 2, Version = 1, LineNumber = 2, UnitSnapshot = "PCS" }];
            return Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Ok(new() { Uid = f.Detail.Header.Uid, Detail = f.Detail }));
        };
        await vm.AssociateSourcesCommand.ExecuteAsync(null);
        Assert.AreEqual(original.CommandId, f.Api.Associations[1].CommandId); Assert.AreEqual(5m, vm.Lines.Items.Sum(x => x.Qty));
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.IsTrue(vm.IsEditable); StringAssert.Contains(vm.AssociationSummary, "待关联 1");
        Assert.IsFalse(vm.PostCommand.CanExecute(null));
    }
    /// <summary>取消分配不写入；有修改必须先保存；明确拒绝保留原行供修正。</summary>
    [TestMethod]
    public async Task CancelAndKnownFailureKeepOriginalDraft()
    {
        var f = new Fixture(); using var vm = f.Create(); await f.Load(vm); vm.SelectedLine = vm.Lines.Items.Single();
        f.Picker.Selection = null; await vm.AssociateSourcesCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Api.Associations.Count);
        vm.SelectedLine.Note = "edited"; Assert.IsFalse(vm.AssociateSourcesCommand.CanExecute(null));
        vm.SelectedLine.RejectChanges();
        f.Picker.Selection = f.Selection;
        f.Api.Associate = (_, _, _) => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("SOURCE_CHANGED", "余额变化"));
        await vm.AssociateSourcesCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsEditable);
        Assert.IsNull(vm.Lines.Items.Single().GrossDemandId); Assert.AreEqual(5m, vm.Lines.Items.Single().Qty);
    }
    /// <summary>账号替换后迟到的成功不能回填旧账号的关联结果。</summary>
    [TestMethod]
    public async Task AccountChangeRejectsLateAssociation()
    {
        var f = new Fixture(); using var vm = f.Create(); await f.Load(vm); vm.SelectedLine = vm.Lines.Items.Single();
        var completion = new TaskCompletionSource<ServiceResult<PackingListDraftCommandResult>>();
        f.Api.Associate = (_, _, _) => completion.Task;
        var task = vm.AssociateSourcesCommand.ExecuteAsync(null); f.User.Clear();
        var response = new PackingListDetailDto { Header = f.Detail.Header, Lines = [new() { Uid = Guid.NewGuid(), PackingListHeadId = f.Detail.Header.Uid, LineNumber = 1, GrossDemandId = f.SourceId, Qty = 5 }] };
        completion.SetResult(ServiceResult<PackingListDraftCommandResult>.Ok(new() { Uid = f.Detail.Header.Uid, Detail = response })); await task;
        Assert.IsNull(vm.Lines.Items.Single().GrossDemandId);
    }
    private static ApiCallRunner Runner() => new(NullLogger<ApiCallRunner>.Instance);
    /// <summary>活动、已保存且待关联的最小草稿，权限来自真实会话服务。</summary>
    private sealed class Fixture
    {
        internal readonly PackingListApiStub Api = new();
        internal readonly PackingListSourceStub Picker = new();
        internal readonly CurrentUserService User = new();
        internal readonly Guid ProductId = Guid.NewGuid(), SourceId = Guid.NewGuid();
        internal readonly PackingListDetailDto Detail = new() { Header = new() { Uid = Guid.NewGuid(), Version = 3, CustomerId = Guid.NewGuid(), InvoiceDate = DateTime.Today } };
        internal IReadOnlyList<PackingListSourcePickerSelection> Selection;
        internal Fixture()
        {
            Detail.Lines.Add(new() { Uid = Guid.NewGuid(), PackingListHeadId = Detail.Header.Uid, Version = 2, LineNumber = 1, ProductId = ProductId, Qty = 5, UnitSnapshot = "PCS", InvoiceNo = "INV1" });
            Api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(Detail));
            Selection = [new() { Qty = 3, Source = new() { GrossDemandId = SourceId, DemandVersion = 3, SourceVersion = 4, ProductId = ProductId, CustomerId = Detail.Header.CustomerId } }]; Picker.Selection = Selection;
        }
        internal PackingListDetailViewModel Create()
        {
            User.SetCurrentUser(new() { UserId = "first", Permissions = ["Shipping.PackingList.View", "Shipping.PackingList.Edit", "Shipping.PackingList.Post"] });
            var ui = new PackingListUiStub(); return new(Api, Runner(), new ReferenceDataCacheServiceStub(), Picker, new PackingListNavigationStub(), ui, ui, postingUser: User);
        }
        internal Task Load(PackingListDetailViewModel vm) => vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Detail.Header.Uid }, default);
    }
}
