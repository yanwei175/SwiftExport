using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using System.Windows;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证追溯文档按稳定 Uid 读取最新详情及原状态保全。</summary>
public sealed partial class InboundManagerQueryTests
{
    /// <summary>旧 Posted 状态和错误单号不参与定位，刷新继续按 Uid 读取最新 Reversed 详情。</summary>
    [TestMethod]
    public async Task UidDocument_IgnoresHistoricalStatusAndNeverSearchesList()
    {
        var uid = Guid.NewGuid(); var api = new ApiStub { DetailHandler = id => Task.FromResult(UidDetail(id)) };
        using var vm = Create(api);
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = uid, DocumentNo = new string('旧', 250), PostingStatus = InboundPostingStatusEnum.Posted }, CancellationToken.None);
        Assert.AreEqual(0, api.Requests.Count); Assert.AreEqual(1, api.DetailCalls); Assert.AreEqual(uid, vm.SelectedItem!.Uid);
        Assert.AreEqual("IR-最新", vm.SelectedItem.DocumentNo); Assert.AreEqual(InboundPostingStatusEnum.Reversed, vm.SelectedItem.PostingStatus);
        Assert.IsTrue(vm.IsReadOnly); Assert.IsTrue(vm.HeaderReadOnly); Assert.IsTrue(vm.LinesReadOnly);
        await vm.InitializeAsync(); await vm.LoadSelectedDetailCommand.ExecuteAsync(null); Assert.AreEqual(1, api.DetailCalls);
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(2, api.DetailCalls); Assert.AreEqual(0, api.Requests.Count);
    }

    /// <summary>不存在、无权和错身份失败均保留此前完整详情，不把旧行当作新文档成功显示。</summary>
    [TestMethod]
    [DataRow("notfound")]
    [DataRow("forbidden")]
    [DataRow("wronguid")]
    [DataRow("wrongline")]
    public async Task UidDocument_FailureKeepsPreviousAcceptedRows(string failure)
    {
        var uid = Guid.NewGuid(); var api = new ApiStub { DetailHandler = id => Task.FromResult(UidDetail(id)) }; using var vm = Create(api);
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = uid }, CancellationToken.None); var row = vm.SelectedItem;
        api.DetailHandler = id => Task.FromResult(failure switch
        {
            "notfound" => ServiceResult<InboundManagerDetailDto>.Fail(ServiceErrorCodes.NotFound, "不存在或已删除"),
            "forbidden" => ServiceResult<InboundManagerDetailDto>.Fail(ServiceErrorCodes.Forbidden, "没有权限"),
            "wronguid" => UidDetail(Guid.NewGuid()),
            _ => UidDetail(id, wrongLine: true)
        });
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = Guid.NewGuid() }, CancellationToken.None);
        Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual(1, vm.Headers.Items.Count); Assert.AreEqual(0, api.Requests.Count);
        StringAssert.Contains(vm.ResultSummary, failure is "wronguid" or "wrongline" ? "不匹配" : failure == "notfound" ? "不存在" : "没有权限");
    }

    /// <summary>新 Uid 请求优先；调用方取消和释放后，即使底层忽略 Token 也不得回填。</summary>
    [TestMethod]
    public async Task UidDocument_LatestCancellationAndLifetimeProtectResults()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var pending = new TaskCompletionSource<ServiceResult<InboundManagerDetailDto>>();
        var api = new ApiStub { DetailHandler = uid => uid == first ? pending.Task : Task.FromResult(UidDetail(uid)) }; using var vm = Create(api);
        var old = vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = first }, CancellationToken.None);
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = second }, CancellationToken.None);
        pending.SetResult(UidDetail(first)); await old; Assert.AreEqual(second, vm.SelectedItem!.Uid);
        using var cancellation = new CancellationTokenSource(); pending = new(); api.DetailHandler = _ => pending.Task;
        var canceled = vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = first }, cancellation.Token);
        cancellation.Cancel(); pending.SetResult(UidDetail(first)); await canceled; Assert.AreEqual(second, vm.SelectedItem.Uid);
        pending = new(); var closing = vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = first }, CancellationToken.None);
        vm.Dispose(); pending.SetResult(UidDetail(first)); await closing; Assert.AreEqual(second, vm.SelectedItem.Uid);
    }

    /// <summary>本地编辑不被只读导航静默丢弃或改为只读，原内容和模式都保留。</summary>
    [TestMethod]
    public async Task UidDocument_PreservesUnsavedDraft()
    {
        var api = new ApiStub(); using var vm = Create(api); vm.AddNewHeaderRowCommand.Execute(null); var row = vm.SelectedItem!;
        row.Remark = "未保存内容";
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = Guid.NewGuid() }, CancellationToken.None);
        Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual("未保存内容", row.Remark); Assert.IsFalse(vm.IsReadOnly);
        Assert.AreEqual(0, api.DetailCalls); Assert.AreEqual(0, api.Requests.Count); StringAssert.Contains(vm.ResultSummary, "已保留");
    }

    /// <summary>缺少 View 时给出权限反馈且不请求，恢复权限后刷新仍按原 Uid 定位。</summary>
    [TestMethod]
    public async Task UidDocument_MissingViewPermissionCanRecoverByUid()
    {
        var uid = Guid.NewGuid(); var api = new ApiStub { DetailHandler = id => Task.FromResult(UidDetail(id)) };
        var user = InboundTestUsers.Create(); user.Clear();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), new SourceStub(), new DialogStub(), api, new RunnerStub(), postingUser: user);
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = uid }, CancellationToken.None);
        Assert.AreEqual(0, api.DetailCalls); Assert.IsTrue(vm.IsReadOnly); StringAssert.Contains(vm.ResultSummary, "没有查看");
        user.SetCurrentUser(InboundTestUsers.Create().CurrentUser!); await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(uid, vm.SelectedItem!.Uid); Assert.AreEqual(1, api.DetailCalls); Assert.AreEqual(0, api.Requests.Count);
    }

    /// <summary>真实 View 的首次显示和选择事件复用已完成详情，不能再发模糊查询或重复 Get。</summary>
    [TestMethod]
    public void View_UidDocumentLoadsExactlyOnce()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); var uid = Guid.NewGuid(); var api = new ApiStub { DetailHandler = id => Task.FromResult(UidDetail(id)) };
            using var vm = Create(api); await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = uid, PostingStatus = InboundPostingStatusEnum.Posted }, CancellationToken.None);
            var view = new InboundManagerView { DataContext = vm }; var window = new Window { Content = view, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            try { window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.AreEqual(1, api.DetailCalls); Assert.AreEqual(0, api.Requests.Count); Assert.AreEqual(uid, vm.SelectedItem!.Uid); }
            finally { window.Close(); }
        });
    }

    /// <summary>构造具有真实归属和新状态的头行快照。</summary>
    private static ServiceResult<InboundManagerDetailDto> UidDetail(Guid uid, bool wrongLine = false) => ServiceResult<InboundManagerDetailDto>.Ok(new()
    {
        Header = new() { Uid = uid, Version = 8, DocumentNo = "IR-最新", PostingStatus = InboundPostingStatusEnum.Reversed },
        Lines = [new() { Uid = Guid.NewGuid(), Version = 3, InboundDocumentId = wrongLine ? Guid.NewGuid() : uid }]
    });
}

/// <summary>验证只读导航不破坏已冻结的原入库命令。</summary>
public sealed partial class InboundManagerPersistenceTests
{
    /// <summary>创建结果未知后仍保留原输入与命令，不因另一个 Uid 打开而丢失恢复入口。</summary>
    [TestMethod]
    public async Task UidDocument_PreservesUnknownWrite()
    {
        using var f = new Fixture(); var row = f.Add(); f.Service.LoseNextResponse = true; await f.Vm.SaveCommand.ExecuteAsync(null);
        var command = f.Service.Creates.Single(); await f.Vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = Guid.NewGuid() }, CancellationToken.None);
        Assert.AreSame(row, f.Vm.SelectedItem); Assert.IsTrue(row.IsPersistencePending); Assert.IsFalse(f.Vm.IsReadOnly);
        await f.Vm.RefreshCommand.ExecuteAsync(null); Assert.AreSame(command, f.Service.Creates.Last()); Assert.AreEqual(1, f.Service.Writes);
    }
}
