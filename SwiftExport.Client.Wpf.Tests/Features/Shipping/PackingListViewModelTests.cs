using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Services;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>覆盖草稿保存、请求竞争、只读状态和占位命令，不连接后端。</summary>
[TestClass]
public sealed class PackingListViewModelTests
{
    /// <summary>不合法每箱数量阻止请求并警告，修正为相等整数后可提交。</summary>
    [TestMethod]
    public async Task SaveRejectsInvalidPackageQuantityWithWarning()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        using var vm = Detail(api, ui); await NewDraft(vm);
        var row = vm.Lines.Items.Single();
        row.PcsPerPackage = 1.5m;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Creates.Count);
        Assert.AreEqual("第 1 行：每箱数量必须是整数。", ui.Warnings.Single());
        row.PcsPerPackage = 3;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Creates.Count);
        Assert.AreEqual("第 1 行：每箱数量不能大于出货数量。", ui.Warnings.Last());
        Assert.AreEqual(2, ui.Warnings.Count);
        Assert.IsTrue(vm.HasUnsavedChanges);
        row.PcsPerPackage = 2;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Creates.Count);
    }

    /// <summary>净重大于毛重时保存不发请求，保留编辑，修正后可继续提交。</summary>
    [TestMethod]
    public async Task SaveRejectsNetWeightAboveGrossWeight()
    {
        var api = new PackingListApiStub();
        var ui = new PackingListUiStub();
        using var vm = Detail(api, ui);
        await NewDraft(vm);
        var row = vm.Lines.Items.Single();
        row.NwPerPackage = 5; row.GwPerPackage = 4;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Creates.Count);
        Assert.AreEqual("第 1 行：单箱净重不能大于单箱毛重。", vm.ValidationSummary);
        Assert.AreEqual(1, ui.Warnings.Count);
        Assert.AreEqual(vm.ValidationSummary, ui.Warnings.Single());
        Assert.AreEqual(5m, row.NwPerPackage);
        Assert.IsTrue(vm.HasUnsavedChanges);
        row.GwPerPackage = 5;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Creates.Count);
    }

    /// <summary>失败保存不丢编辑，原内容重试复用身份，改动后创建新意图。</summary>
    [TestMethod]
    public async Task SaveFailureRetainsEditsAndStableCommandId()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        using var vm = Detail(api, ui);
        await NewDraft(vm);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasUnsavedChanges); Assert.AreEqual(1, api.Creates.Count);
        Assert.AreEqual(1, ui.Warnings.Count);
        Assert.AreEqual(vm.ValidationSummary, ui.Warnings.Single());
        var id = api.Creates[0].CommandId;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(id, api.Creates[1].CommandId);
        vm.Header.Note = "changed";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreNotEqual(id, api.Creates[2].CommandId);
        Assert.AreEqual(2m, vm.Lines.Items.Single().Qty);
        ui.Confirm = false;
        Assert.IsFalse(await vm.CanCloseAsync(default));
    }
    /// <summary>写入期间冻结编辑并阻止重入；只有完整服务器响应清除脏状态。</summary>
    [TestMethod]
    public async Task SaveBusyAndSuccessfulResponse()
    {
        var api = new PackingListApiStub(); var gate = new TaskCompletionSource<ServiceResult<PackingListDraftCommandResult>>();
        api.Create = _ => gate.Task;
        using var vm = Detail(api, new PackingListUiStub()); await NewDraft(vm);
        var save = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy); Assert.IsFalse(vm.IsEditable);
        Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Creates.Count);
        var detail = new PackingListDetailDto { Header = vm.Header.ToDto(), Lines = vm.Lines.Items.Select(x => x.ToDto()).ToList() };
        detail.Header.Version = 1; detail.Header.PackingListNo = "PL-001";
        foreach (var line in detail.Lines) line.Version = 1;
        gate.SetResult(ServiceResult<PackingListDraftCommandResult>.Ok(new() { Uid = detail.Header.Uid, Detail = detail }));
        await save;
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.IsTrue(vm.IsEditable); Assert.AreEqual("PL-001", vm.DocumentTitle);
    }
    /// <summary>更新提交完整有效行和显式删除身份，图片不作为写入字段。</summary>
    [TestMethod]
    public async Task UpdateCarriesDeletedVersionsAndActiveSnapshot()
    {
        var api = new PackingListApiStub(); var dto = Existing();
        var deleted = new PackingListLineDto { Uid = Guid.NewGuid(), PackingListHeadId = dto.Header.Uid, Version = 7, LineNumber = 2, Qty = 1 };
        dto.Lines.Add(deleted); api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(dto));
        using var vm = Detail(api, new PackingListUiStub());
        await vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = dto.Header.Uid }, default);
        Assert.IsFalse(vm.HasUnsavedChanges);
        vm.SelectedLine = vm.Lines.Items[1]; vm.DeleteLineCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        var command = api.Updates.Single();
        Assert.AreEqual(3, command.ExpectedVersion);
        Assert.AreEqual(1, command.Lines.Count); Assert.AreEqual(deleted.Uid, command.DeletedLines.Single().Uid);
        Assert.AreEqual(7, command.DeletedLines.Single().ExpectedVersion);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }
    /// <summary>已过账只读，未知读取失败也不能被误认为可编辑新单。</summary>
    [TestMethod]
    public async Task PostedAndFailedLoadAreReadOnly_PostCommandsRequirePermission()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub(); var dto = Existing();
        dto.Header.PostingStatus = PackingListPostingStatusEnum.Posted;
        api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(dto));
        using var vm = Detail(api, ui); await vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = dto.Header.Uid }, default);
        Assert.IsTrue(vm.IsReadOnly); Assert.IsFalse(vm.IsEditable);
        await vm.SaveCommand.ExecuteAsync(null); vm.PostCommand.Execute(null); vm.ReverseCommand.Execute(null);
        Assert.AreEqual(0, api.Updates.Count);
        Assert.IsFalse(vm.PostCommand.CanExecute(null)); Assert.IsFalse(vm.ReverseCommand.CanExecute(null));
        api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Fail("404"));
        using var failed = Detail(api, ui); await failed.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Guid.NewGuid() }, default);
        Assert.IsTrue(failed.IsReadOnly); Assert.IsFalse(failed.HasUnsavedChanges);
    }
    /// <summary>两次搜索倒序返回，早发请求不得覆盖最新列表。</summary>
    [TestMethod]
    public async Task ManagerLatestQueryWinsAndDisposeRejectsResults()
    {
        var first = new TaskCompletionSource<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>();
        var second = new TaskCompletionSource<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>();
        var count = 0; var api = new PackingListApiStub { Search = _ => ++count == 1 ? first.Task : second.Task };
        using var vm = new PackingListManagerViewModel(api, Runner(), new PackingListNavigationStub(), new PackingListUiStub(), new PackingListUiStub(), new ReferenceDataCacheServiceStub());
        var a = vm.SearchCommand.ExecuteAsync(null); var b = vm.SearchCommand.ExecuteAsync(null);
        second.SetResult(Page("latest")); await b;
        first.SetResult(Page("old")); await a;
        Assert.AreEqual("latest", vm.Headers.Items.Single().PackingListNo);
        Assert.AreEqual(2, count);
        vm.Dispose(); await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(2, count);
    }
    /// <summary>Picker 保留图片、阻止草稿加选超量，并按本地可选余额生成默认数量。</summary>
    [TestMethod]
    public async Task PickerChecksDraftRemainingAndRetainsImage()
    {
        var customer = Guid.NewGuid(); var source = Guid.NewGuid();
        var api = new PackingListApiStub { Sources = _ => Task.FromResult(ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>.Ok(new()
        { Items = [new() { GrossDemandId = source, CustomerId = customer, ProductId = Guid.NewGuid(), RemainingQty = 10, ImagePath = "image.png" }] })) };
        using var vm = new PackingListSourcePickerViewModel(api, Runner());
        await vm.InitializeAsync(customer, new Dictionary<Guid, decimal> { [source] = 8 }, default);
        var row = vm.Items.Single(); row.IsChecked = true;
        Assert.AreEqual(10m, row.RemainingQty);
        Assert.AreEqual(8m, row.DraftQty);
        Assert.AreEqual(2m, row.AvailableQty);
        Assert.IsTrue(vm.IsPrimaryButtonEnabled);
        Assert.AreEqual(2m, vm.GetSelectedResult().Single().Qty);
        Assert.AreEqual("image.png", vm.GetSelectedResult().Single().Source.ImagePath);
        Assert.IsTrue(vm.CanClose(DialogResult.Primary));
        await vm.SearchCommand.ExecuteAsync(null); Assert.IsTrue(row.IsChecked, "不得因查询丢弃勾选");
    }
    /// <summary>选择同来源产生不同物理行，库存位置不会由来源身份强制推断。</summary>
    [TestMethod]
    public async Task SourceSelectionCreatesIndependentRowsAndMoveRenumbers()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub(); var source = new PackingListSourceStub();
        using var vm = Detail(api, ui, source); await NewDraft(vm);
        source.Selection = [new() { Source = new() { CustomerId = vm.Header.CustomerId, GrossDemandId = vm.Lines.Items[0].GrossDemandId,
            ProductId = vm.Lines.Items[0].ProductId, ImagePath = "new.png", RemainingQty = 20, PcsPerPackage = 2 }, Qty = 3 }];
        await vm.SelectSourcesCommand.ExecuteAsync(null);
        Assert.AreEqual(2, vm.Lines.Items.Count);
        Assert.AreNotEqual(vm.Lines.Items[0].Uid, vm.Lines.Items[1].Uid);
        Assert.AreEqual("new.png", vm.Lines.Items[1].ImagePath); Assert.AreEqual(1.5m, vm.Lines.Items[1].PackageCount);
        var last = vm.Lines.Items[1]; vm.MoveLineUpCommand.Execute(last);
        Assert.AreSame(last, vm.Lines.Items[0]); Assert.AreEqual(1, last.LineNumber);
    }
    /// <summary>关闭时编辑器产生的新修改仍需确认，不能先检查旧脏状态。</summary>
    [TestMethod]
    public async Task ClosingCommitsBeforeCheckingDirtyState()
    {
        var dto = Existing(); var api = new PackingListApiStub { Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(dto)) };
        var ui = new PackingListUiStub { Confirm = false };
        using var vm = Detail(api, ui);
        await vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = dto.Header.Uid }, default);
        vm.CommitEdits = () => { vm.Header.Note = "编辑器刚提交的值"; return true; };
        Assert.IsFalse(await vm.CanCloseAsync(default)); Assert.IsTrue(vm.HasUnsavedChanges);
    }
    /// <summary>批量关键词传给后端；本地仅扣除当前草稿，不改服务端分页总数。</summary>
    [TestMethod]
    public async Task PickerSendsKeywordsAndHidesExhaustedDraftSources()
    {
        var customer = Guid.NewGuid(); var source = Guid.NewGuid();
        PackingListSourceSearchRequest? captured = null;
        var api = new PackingListApiStub { Sources = request =>
        {
            captured = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>.Ok(new()
            { TotalCount = 1, Items = [new() { GrossDemandId = source, CustomerId = customer,
                IsNonStandard = true, RemainingQty = 5, ImagePath = "custom.png" }] }));
        }};
        using var vm = new PackingListSourcePickerViewModel(api, Runner());
        vm.Search.KeywordsText = " A\r\nB\na ";
        await vm.InitializeAsync(customer, new Dictionary<Guid, decimal> { [source] = 5 }, default);
        CollectionAssert.AreEqual(new[] { "A", "B" }, captured!.Keywords);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(1, vm.TotalCount);
        Assert.IsFalse(vm.IsPrimaryButtonEnabled);
    }

    /// <summary>拆分保留总量和来源，紧邻插行并清空尾箱指标；整除不可重复拆分。</summary>
    [TestMethod]
    public async Task SplitTailPreservesQuantityAndClearsMeasures()
    {
        using var vm = Detail(new PackingListApiStub(), new PackingListUiStub());
        await NewDraft(vm);
        var row = vm.Lines.Items[0];
        row.Qty = 105; row.PcsPerPackage = 20;
        row.NwPerPackage = 10; row.GwPerPackage = 11; row.CbmPerPackage = .2m;
        Assert.AreEqual(5.25m, row.PackageCount);
        Assert.AreEqual(52.5m, vm.Lines.NetWeightSummary);
        vm.SplitTailCommand.Execute(row);
        Assert.AreEqual(2, vm.Lines.Items.Count);
        var tail = vm.Lines.Items[1];
        Assert.AreEqual(100m, row.Qty); Assert.AreEqual(5m, row.PackageCount);
        Assert.AreEqual(5m, tail.Qty); Assert.AreEqual(5m, tail.PcsPerPackage); Assert.AreEqual(1m, tail.PackageCount);
        Assert.AreEqual(row.GrossDemandId, tail.GrossDemandId); Assert.AreNotEqual(row.Uid, tail.Uid);
        Assert.IsNull(tail.NwPerPackage); Assert.IsNull(tail.GwPerPackage); Assert.IsNull(tail.CbmPerPackage);
        Assert.IsNull(vm.Lines.NetWeightSummary);
        Assert.AreEqual(105m, vm.Lines.Items.Sum(x => x.Qty));
        Assert.IsFalse(vm.SplitTailCommand.CanExecute(row)); Assert.IsFalse(vm.SplitTailCommand.CanExecute(tail));
        tail.NwPerPackage = 3; tail.GwPerPackage = 4; tail.CbmPerPackage = .1m;
        Assert.AreEqual(53m, vm.Lines.NetWeightSummary); Assert.AreEqual(59m, vm.Lines.GrossWeightSummary);
        Assert.AreEqual(1.1m, vm.Lines.CbmSummary);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(3m, vm.Lines.Items[1].NwPerPackage);
    }

    /// <summary>不足一箱时就地转为尾箱，不生成零数量行。</summary>
    [TestMethod]
    public async Task SplitSmallLineCreatesNoZeroLine()
    {
        using var vm = Detail(new PackingListApiStub(), new PackingListUiStub()); await NewDraft(vm);
        var row = vm.Lines.Items[0]; row.PcsPerPackage = 20; row.NwPerPackage = 4;
        vm.SplitTailCommand.Execute(row);
        Assert.AreEqual(1, vm.Lines.Items.Count); Assert.AreEqual(2m, row.Qty);
        Assert.AreEqual(2m, row.PcsPerPackage); Assert.AreEqual(1m, row.PackageCount); Assert.IsNull(row.NwPerPackage);
    }

    private static ApiCallRunner Runner() => new(NullLogger<ApiCallRunner>.Instance);
    /// <summary>整行转组与移出不改变数量身份，空组保存阻断，组移动保持连续。</summary>
    [TestMethod]
    public async Task MixedPackingCommandsPreserveSourcesAndOrder()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        using var vm = Detail(api, ui); await NewDraft(vm);
        var first = vm.Lines.Items.Single();
        var dto = first.ToDto(); dto.Uid = Guid.NewGuid(); dto.LineNumber = 2;
        var second = vm.Lines.AddNew(dto);
        var firstId = first.Uid; var total = vm.Lines.Items.Sum(x => x.Qty);
        vm.AddPackingGroupCommand.Execute(null);
        var group = vm.SelectedPackingGroup!;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Creates.Count); Assert.IsTrue(vm.HasUnsavedChanges);
        vm.SelectedLine = first; await vm.MoveIntoPackingGroupCommand.ExecuteAsync(null);
        vm.SelectedLine = second; await vm.MoveIntoPackingGroupCommand.ExecuteAsync(null);
        Assert.AreEqual(2, group.Members.Count);
        Assert.AreEqual(total, vm.Lines.Items.Sum(x => x.Qty)); Assert.AreEqual(firstId, first.Uid);
        Assert.AreEqual(1m, vm.Lines.CartonSummary);
        group.GrossWeight = 3; group.NetWeight = 2;
        vm.AddPackingGroupCommand.Execute(null); var other = vm.SelectedPackingGroup!;
        await vm.MoveIntoPackingGroupCommand.ExecuteAsync(null);
        Assert.AreEqual(other.Id, second.PackingGroupId); Assert.IsNull(first.GwPerPackage);
        vm.MovePackingGroupUpCommand.Execute(null);
        Assert.AreSame(second, vm.Lines.Items[0]);
        await vm.MoveOutOfPackingGroupCommand.ExecuteAsync(null);
        Assert.IsNull(second.PackingGroupId); Assert.AreEqual(second.Qty, second.PcsPerPackage);
        vm.DeletePackingGroupCommand.Execute(null); Assert.IsFalse(vm.Lines.HasEmptyGroups);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(group.Id, api.Creates.Single().Lines.Single(x => x.Uid == firstId).PackingGroupId);
        Assert.AreEqual(total, api.Creates.Single().Lines.Sum(x => x.Qty));
    }

    /// <summary>取消移入确认不改变行；只读和 Busy 不能新建拼箱。</summary>
    [TestMethod]
    public async Task MixedPackingCancellationAndReadonlyDoNotMutate()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub { Confirm = false };
        using var vm = Detail(api, ui); await NewDraft(vm);
        vm.AddPackingGroupCommand.Execute(null);
        vm.SelectedLine = vm.Lines.Items.Single();
        await vm.MoveIntoPackingGroupCommand.ExecuteAsync(null);
        Assert.IsNull(vm.SelectedLine.PackingGroupId);
        var posted = Existing(); posted.Header.PostingStatus = PackingListPostingStatusEnum.Posted;
        api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(posted));
        using var readOnly = Detail(api, ui);
        await readOnly.InitializeDocumentAsync(new PackingListOpenRequest { Uid = posted.Header.Uid }, default);
        readOnly.AddPackingGroupCommand.Execute(null);
        Assert.AreEqual(0, readOnly.Lines.PackingGroups.Count);
    }
    private static PackingListDetailViewModel Detail(PackingListApiStub api, PackingListUiStub ui, PackingListSourceStub? source = null) =>
        new(api, Runner(), new ReferenceDataCacheServiceStub(), source ?? new(), new PackingListNavigationStub(), ui, ui);
    private static async Task NewDraft(PackingListDetailViewModel vm)
    {
        await vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Guid.NewGuid(), IsNew = true }, default);
        vm.SelectedCustomer = new CustomerDto { Uid = Guid.NewGuid(), CustomerName = "Customer" };
        vm.Lines.AddNew(new() { Uid = Guid.NewGuid(), PackingListHeadId = vm.Header.Uid, LineNumber = 1,
            GrossDemandId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Qty = 2, ImagePath = "image.png" });
    }
    private static PackingListDetailDto Existing()
    {
        var uid = Guid.NewGuid();
        return new() { Header = new() { Uid = uid, CustomerId = Guid.NewGuid(), CustomerNameSnapshot = "customer", InvoiceDate = DateTime.Today, Version = 3 },
            Lines = [new() { Uid = Guid.NewGuid(), PackingListHeadId = uid, Version = 2, LineNumber = 1, GrossDemandId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Qty = 2 }] };
    }
    private static ServiceResult<PagedResponseBase<PackingListManagerListItemDto>> Page(string number) =>
        ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>.Ok(new() { Items = [new() { Uid = Guid.NewGuid(), PackingListNo = number }], TotalCount = 1 });
}

/// <summary>可控制完成顺序的契约替身，不执行网络。</summary>
internal sealed class PackingListApiStub : IPackingListApi
{
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingPreviewDto>> PostPreviewAsync(Guid uid, PackingListPostPreviewRequest request, CancellationToken cancellationToken = default) => Preview(uid, request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReversePreviewRequest request, CancellationToken cancellationToken = default) => ReversePreview(uid, request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, PostPackingListCommand request, CancellationToken cancellationToken = default) => Post(uid, request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, ReversePackingListCommand request, CancellationToken cancellationToken = default) => Reverse(uid, request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListTraceDto>> TraceAsync(Guid uid, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("本夹具未配置执行追踪查询。");
    public List<CreatePackingListDraftCommand> Creates { get; } = [];
    public Func<Guid, PackingListPostPreviewRequest, Task<ServiceResult<PackingListPostingPreviewDto>>> Preview { get; set; } = (_, _) => throw new NotSupportedException();
    public Func<Guid, PostPackingListCommand, Task<ServiceResult<PackingListPostingCommandResult>>> Post { get; set; } = (_, _) => throw new NotSupportedException();
    public Func<Guid, PackingListReversePreviewRequest, Task<ServiceResult<PackingListPostingPreviewDto>>> ReversePreview { get; set; } = (_, _) => throw new NotSupportedException();
    public Func<Guid, ReversePackingListCommand, Task<ServiceResult<PackingListPostingCommandResult>>> Reverse { get; set; } = (_, _) => throw new NotSupportedException();
    public Func<Guid, UpdatePackingListDraftCommand, Task<ServiceResult<PackingListDraftCommandResult>>> Update { get; set; } = (_, _) => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("待开发"));
    public List<UpdatePackingListDraftCommand> Updates { get; } = [];
    public Func<CreatePackingListDraftCommand, Task<ServiceResult<PackingListDraftCommandResult>>> Create { get; set; } = _ => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("待开发"));
    public Func<Guid, Task<ServiceResult<PackingListDetailDto>>> Get { get; set; } = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Fail("待开发"));
    public Func<PackingListManagerSearchRequest, Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>> Search { get; set; } = _ => throw new NotSupportedException();
    public Func<PackingListSourceSearchRequest, Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>>> Sources { get; set; } = _ => throw new NotSupportedException();
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken cancellationToken = default) => Search(request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => Get(uid);
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default) => Sources(request);
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync(CreatePackingListDraftCommand request, CancellationToken cancellationToken = default) { Creates.Add(request); return Create(request); }
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, UpdatePackingListDraftCommand request, CancellationToken cancellationToken = default) { Updates.Add(request); return Update(uid, request); }
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, DeletePackingListDraftCommand request, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("待开发"));
}
/// <summary>记录通知与关闭确认的 UI 替身。</summary>
internal sealed class PackingListUiStub : IDialogService, IMessageService
{
    public bool Confirm { get; set; } = true;
    public int Infos { get; private set; }
    /// <summary>记录警告内容，验证每次失败只通知一次。</summary>
    public List<string> Warnings { get; } = [];
    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
    /// <inheritdoc/>
    public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <inheritdoc/>
    public void ShowInfo(string title, string message) => Infos++;
    /// <inheritdoc/>
    public void ShowSuccess(string title, string message) { }
    /// <inheritdoc/>
    public void ShowWarning(string title, string message) => Warnings.Add(message);
    /// <inheritdoc/>
    public void ShowError(string title, string message) { }
}
/// <summary>不创建真实页签的导航替身。</summary>
internal sealed class PackingListNavigationStub : INavigationService
{
    public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
    public NavigationTabItem? SelectedTab { get; set; }
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    /// <inheritdoc/>
    public Task OpenAsync(string routeKey) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task OpenDocumentAsync(string routeKey, string documentKey, object? parameter = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <inheritdoc/>
    public void UpdateDocumentKey(object contentViewModel, string documentKey) { }
    /// <inheritdoc/>
    public void Activate(NavigationTabItem? tab) { }
    /// <inheritdoc/>
    public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
    /// <inheritdoc/>
    public void Reset() { }
}
/// <summary>为详情提供确定的来源选择结果。</summary>
internal sealed class PackingListSourceStub : IPackingListSourcePickerService
{
    public IReadOnlyList<PackingListSourcePickerSelection>? Selection { get; set; }
    /// <inheritdoc/>
    public Task<IReadOnlyList<PackingListSourcePickerSelection>?> PickAsync(Guid customerId, IReadOnlyDictionary<Guid, decimal> draftQuantities, CancellationToken token) => Task.FromResult(Selection);
}
/// <summary>默认取消仓库选择的替身。</summary>
internal sealed class PackingListWarehouseStub : IWarehousePickerService
{
    /// <inheritdoc/>
    public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default) => Task.FromResult<WarehouseSitePickerDto?>(null);
}
