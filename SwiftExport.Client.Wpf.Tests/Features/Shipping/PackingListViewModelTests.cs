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
    /// <summary>失败保存不丢编辑，原内容重试复用身份，改动后创建新意图。</summary>
    [TestMethod]
    public async Task SaveFailureRetainsEditsAndStableCommandId()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        using var vm = Detail(api, ui);
        await NewDraft(vm);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasUnsavedChanges); Assert.AreEqual(1, api.Creates.Count);
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
    public async Task PostedAndFailedLoadAreReadOnly_PostCommandsOnlyNotify()
    {
        var api = new PackingListApiStub(); var ui = new PackingListUiStub(); var dto = Existing();
        dto.Header.PostingStatus = PackingListPostingStatusEnum.Posted;
        api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(dto));
        using var vm = Detail(api, ui); await vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = dto.Header.Uid }, default);
        Assert.IsTrue(vm.IsReadOnly); Assert.IsFalse(vm.IsEditable);
        await vm.SaveCommand.ExecuteAsync(null); vm.PostCommand.Execute(null); vm.ReverseCommand.Execute(null);
        Assert.AreEqual(0, api.Updates.Count); Assert.AreEqual(2, ui.Infos);
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
    /// <summary>Picker 保留图片、阻止草稿加选超量，并拒绝未知额度和输入格式错误。</summary>
    [TestMethod]
    public async Task PickerChecksDraftRemainingAndRetainsImage()
    {
        var customer = Guid.NewGuid(); var source = Guid.NewGuid();
        var api = new PackingListApiStub { Sources = _ => Task.FromResult(ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>.Ok(new()
        { Items = [new() { GrossDemandId = source, CustomerId = customer, ProductId = Guid.NewGuid(), RemainingQty = 10, ImagePath = "image.png" }] })) };
        using var vm = new PackingListSourcePickerViewModel(api, Runner());
        await vm.InitializeAsync(customer, new Dictionary<Guid, decimal> { [source] = 8 }, default);
        var row = vm.Items.Single(); row.IsChecked = true; row.SelectedQty = 3;
        Assert.IsFalse(vm.IsPrimaryButtonEnabled);
        row.SelectedQty = 2; Assert.IsTrue(vm.IsPrimaryButtonEnabled);
        Assert.AreEqual("image.png", vm.GetSelectedResult().Single().Source.ImagePath);
        vm.ValidateEditors = () => false; Assert.IsFalse(vm.CanClose(DialogResult.Primary));
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
        Assert.AreEqual("new.png", vm.Lines.Items[1].ImagePath); Assert.AreEqual(2m, vm.Lines.Items[1].PackageCount);
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
    private static ApiCallRunner Runner() => new(NullLogger<ApiCallRunner>.Instance);
    private static PackingListDetailViewModel Detail(PackingListApiStub api, PackingListUiStub ui, PackingListSourceStub? source = null) =>
        new(api, Runner(), new ReferenceDataCacheServiceStub(), source ?? new(), new PackingListWarehouseStub(), new PackingListNavigationStub(), ui, ui);
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
    public List<CreatePackingListDraftCommand> Creates { get; } = [];
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
    public Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, UpdatePackingListDraftCommand request, CancellationToken cancellationToken = default) { Updates.Add(request); return Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("待开发")); }
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, DeletePackingListDraftCommand request, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Fail("待开发"));
}
/// <summary>记录通知与关闭确认的 UI 替身。</summary>
internal sealed class PackingListUiStub : IDialogService, IMessageService
{
    public bool Confirm { get; set; } = true;
    public int Infos { get; private set; }
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
    public void ShowWarning(string title, string message) { }
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
