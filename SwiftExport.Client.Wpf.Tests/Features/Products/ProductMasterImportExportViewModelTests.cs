using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.Services.Files.Reading;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Services;
using SwiftExportClient.Features.Products.ProductMasterImportExport.ViewModels;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证导入批次、最后单元格提交、过期会话及导出筛选快照，并装载真实页面。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterImportExportViewModelTests
{
    /// <summary>记录代表性页面性能及渲染证据。</summary>
    public TestContext TestContext { get; set; } = null!;
    /// <summary>输入编辑使预览失效；提交携带精确依据，成功后锁定批次。</summary>
    [TestMethod]
    public async Task EditAndCommit_FreezeBasisAndPreventRepeat()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        var row = vm.Rows.Single();
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.ImportCommand.CanExecute(null));
        row.DefaultDescriptionZh = "最后编辑";
        Assert.IsNull(row.Preview);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        var committedBeforeBusy = false;
        vm.CommitPendingEdits = () => { committedBeforeBusy = !vm.IsBusy; return true; };
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.IsTrue(committedBeforeBusy);
        await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreEqual("最后编辑", fixture.Api.Committed!.Rows[0].Input.DefaultDescriptionZh);
        Assert.AreEqual(fixture.Api.MasterId, fixture.Api.Committed.Rows[0].ProductMasterId);
        Assert.AreEqual(7, fixture.Api.Committed.Rows[0].ExpectedVersion);
        Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.IsTrue(vm.IsGridReadOnly);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        Assert.AreEqual(1, fixture.Api.ImportCalls);
    }

    /// <summary>普通查看用户可以读文件和预览，但管理员提交始终禁用。</summary>
    [TestMethod]
    public async Task Viewer_CanPreviewButCannotImport()
    {
        using var fixture = new Fixture(false);
        using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.AreEqual("更新", vm.Rows[0].Status);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        Assert.IsTrue(vm.IsGridReadOnly);
        Assert.IsTrue(vm.DownloadTemplateCommand.CanExecute(null));
    }

    /// <summary>再次校验失败时旧预览不再授权提交，输入仍留在原批次中。</summary>
    [TestMethod]
    public async Task FailedNewPreview_InvalidatesOldSubmitBasis()
    {
        using var fixture = new Fixture(); using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.ImportCommand.CanExecute(null));
        fixture.Api.PreviewReply = _ => Task.FromResult(ServiceResult<ProductMasterImportPreviewResult>.Fail("Unavailable", "服务不可用"));
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        Assert.IsNull(vm.Rows[0].Preview);
        Assert.AreEqual("0001", vm.Rows[0].MasterSku);
    }

    /// <summary>在途预览中修改输入或切换用户，忽略旧回执并保留当前批次。</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task DelayedPreview_DoesNotOverwriteChangedInputOrIdentity(int change)
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        var reply = new TaskCompletionSource<ServiceResult<ProductMasterImportPreviewResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.PreviewReply = _ => reply.Task;
        var pending = vm.PreviewCommand.ExecuteAsync(null);
        if (change == 1) fixture.User.SetCurrentUser(Fixture.UserInfo(true));
        else if (change == 2) { Assert.IsTrue(vm.CancelOperationCommand.CanExecute(null)); vm.CancelOperationCommand.Execute(null); }
        else vm.Rows[0].DefaultDescriptionZh = "新输入";
        reply.SetResult(fixture.Api.PreviewResult([vm.Rows[0].ToInput()]));
        await pending;
        Assert.IsNull(vm.Rows[0].Preview);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        Assert.AreEqual(1, vm.Rows.Count);
    }

    /// <summary>退出或更换 API 地址时，迟到的导出响应不能覆盖现有文件。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportAfterSessionOrServerChange_DoesNotPublish(bool server)
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        await File.WriteAllTextAsync(fixture.Output, "original");
        var reply = new TaskCompletionSource<ServiceResult<List<ProductMasterExportRowDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.ExportReply = _ => reply.Task;
        var pending = vm.ExportCommand.ExecuteAsync(new ProductMasterFileTarget(fixture.Output, true));
        if (server) fixture.Options.BaseUrl = "https://changed.example/";
        else fixture.Session.EndSession(SessionEndReason.Logout);
        reply.SetResult(ServiceResult<List<ProductMasterExportRowDto>>.Ok([new() { Data = new() { MasterSku = "PRIVATE" } }]));
        await pending;
        Assert.AreEqual("original", await File.ReadAllTextAsync(fixture.Output));
    }

    /// <summary>新文件读取失败保留原文件身份和输入，网络失败保留批次且不自动重试。</summary>
    [TestMethod]
    public async Task FailedReadOrImport_PreservesBatch()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        var original = vm.Rows[0];
        var invalid = Path.Combine(fixture.Root, "invalid.xlsx");
        await File.WriteAllTextAsync(invalid, "not xlsx");
        await vm.LoadFileCommand.ExecuteAsync(invalid);
        Assert.AreEqual(fixture.Input, vm.FilePath);
        Assert.AreSame(original, vm.Rows[0]);
        await vm.PreviewCommand.ExecuteAsync(null);
        fixture.Api.FailImport = true;
        await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreSame(original, vm.Rows[0]);
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        Assert.AreEqual(1, fixture.Api.ImportCalls);
    }

    /// <summary>来源筛选在页签初始化时冻结，之后来源对象变化不会扩大导出范围。</summary>
    [TestMethod]
    public async Task Export_UsesFrozenAppliedFilter()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        var filter = new ProductMasterExportRequest { Keyword = "已应用", CategoryIds = [Guid.NewGuid()] };
        var expectedCategory = filter.CategoryIds[0];
        await vm.InitializeDocumentAsync(new ProductMasterImportOpenRequest(filter), default);
        filter.Keyword = "未应用"; filter.CategoryIds.Clear();
        await vm.ExportCommand.ExecuteAsync(new ProductMasterFileTarget(fixture.Output));
        Assert.AreEqual("已应用", fixture.Api.Exported!.Keyword);
        Assert.AreEqual(expectedCategory, fixture.Api.Exported.CategoryIds.Single());
        Assert.IsTrue(File.Exists(fixture.Output));
    }

    /// <summary>首表是说明页时仍可选择真正导入表，失败不会将旧输入伪装成新文件资料。</summary>
    [TestMethod]
    public async Task MultipleSheets_CanSelectDataAfterInvalidFirstSheet()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        var original = vm.Rows[0]; var source = vm.BatchSource;
        var path = Path.Combine(fixture.Root, "multiple.xlsx");
        using var content = ExcelReadFixture.Create(book =>
        {
            book.AddWorksheet("说明").Cell("A1").Value = "使用说明";
            var sheet = book.AddWorksheet("导入数据"); sheet.Cell("A1").Value = "主型号"; sheet.Cell("A2").Value = "NEW";
        });
        await File.WriteAllBytesAsync(path, content.ToArray());
        await vm.LoadFileCommand.ExecuteAsync(path);
        Assert.AreEqual(2, vm.Worksheets.Count);
        Assert.AreSame(original, vm.Rows[0]); Assert.AreEqual(source, vm.BatchSource);
        vm.SelectedSheet = "导入数据";
        await vm.ReadSheetCommand.ExecuteAsync(null);
        Assert.AreEqual("NEW", vm.Rows[0].MasterSku);
        StringAssert.Contains(vm.BatchSource, "导入数据");
    }

    /// <summary>实际 XAML 使用项目资源，在原生编辑最后一个单元格后校验并释放桥接委托。</summary>
    [TestMethod]
    public void ActualView_CommitsNativeCellBeforePreviewAndRenders() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture();
        using var vm = fixture.Create();
        using (var content = ExcelReadFixture.Create(book =>
        {
            var sheet = book.AddWorksheet("父级型号"); sheet.Cell("A1").Value = "主型号"; sheet.Cell("B1").Value = "中文描述";
            for (var number = 2; number <= 1001; number++) { sheet.Cell(number,1).Value = number.ToString("D6"); sheet.Cell(number,2).Value = "千行显示与编辑"; }
        })) await File.WriteAllBytesAsync(fixture.Input, content.ToArray());
        var measured = Stopwatch.StartNew();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        Assert.AreEqual(1000, vm.Rows.Count);
        var view = new ProductMasterImportExportView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 760, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            var grid = (DataGrid)view.FindName("ImportGrid");
            Assert.AreEqual(15, grid.Columns.Count);
            Assert.IsTrue(grid.EnableRowVirtualization); Assert.IsTrue(grid.EnableColumnVirtualization);
            Assert.IsNull(grid.ItemContainerGenerator.ContainerFromIndex(999), "首屏不应实现末尾行容器。");
            TestContext.WriteLine($"1000 行读取及真实页面首屏：{measured.ElapsedMilliseconds} ms。");
            Assert.IsNotNull(vm.CommitPendingEdits);
            var row = vm.Rows[0]; grid.SelectedItem = row; grid.CurrentCell = new(row, grid.Columns[3]); grid.ScrollIntoView(row, grid.Columns[3]);
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsTrue(grid.BeginEdit());
            var editor = (TextBox)grid.Columns[3].GetCellContent(row);
            editor.Text = "单元格最终值";
            await vm.PreviewCommand.ExecuteAsync(null);
            Assert.AreEqual("单元格最终值", fixture.Api.Previewed!.Rows[0].DefaultDescriptionZh);
            Assert.IsFalse(((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row)).IsEditing);
            Assert.IsTrue(vm.ImportCommand.CanExecute(null));
            await Dispatcher.Yield(DispatcherPriority.Background);
            var root = FindRoot();
            var directory = Path.Combine(root, ".artifacts", "product-master-import-export", "review");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap(1280, 760, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle((Brush)Application.Current.Resources["UserLayerOnMicaBaseAltFillColorDefaultBrush"], null, new Rect(0,0,1280,760));
                drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0,0,1280,760));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var image = File.Create(Path.Combine(directory, "父级型号导入导出页面.png")); encoder.Save(image);
        }
        finally { window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.IsNull(vm.CommitPendingEdits);
    });

    /// <summary>定位仅用于渲染验收的仓库内忽略目录。</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new AssertFailedException("找不到仓库根目录。");
    }

    /// <summary>每次测试使用独立文件、身份和 API 回执。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "SwiftExport-parent-vm-" + Guid.NewGuid().ToString("N"));
        internal string Input => Path.Combine(Root, "input.xlsx");
        internal string Output => Path.Combine(Root, "output.xlsx");
        internal readonly CurrentUserService User = new();
        internal readonly SessionStub Session = new();
        internal readonly ApiStub Api = new();
        internal readonly ApiOptions Options = new() { BaseUrl = "https://original.example/" };
        internal Fixture(bool admin = true)
        {
            Directory.CreateDirectory(Root); User.SetCurrentUser(UserInfo(admin));
            using var content = ExcelReadFixture.Create(book =>
            {
                var sheet = book.AddWorksheet("父级型号"); sheet.Cell("A1").Value = "主型号"; sheet.Cell("B1").Value = "中文描述";
                sheet.Cell("A2").Value = "0001"; sheet.Cell("B2").Value = "导入描述";
            });
            File.WriteAllBytes(Input, content.ToArray());
        }
        internal static MeDto UserInfo(bool admin) => new() { UserId = Guid.NewGuid().ToString(), UserName = "test", IsAdmin = admin,
            Permissions = admin ? [PermissionKeys.ProductsMasterView, PermissionKeys.ProductsMasterCreate, PermissionKeys.ProductsMasterEdit] : [PermissionKeys.ProductsMasterView] };
        internal ProductMasterImportExportViewModel Create() => new(() => Api, new(new ClosedXmlExcelReadService()), new Runner(), User, Session,
            Microsoft.Extensions.Options.Options.Create(Options), new Dialogs(), NullLogger<ProductMasterImportExportViewModel>.Instance);
        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Root, true);
    }

    /// <summary>保存请求快照，并支持不能取消的延迟回执以检查身份防回写。</summary>
    private sealed class ApiStub : IProductMasterImportExportApi
    {
        internal readonly Guid MasterId = Guid.NewGuid();
        internal ProductMasterImportPreviewRequest? Previewed;
        internal ProductMasterImportRequest? Committed;
        internal ProductMasterExportRequest? Exported;
        internal int ImportCalls;
        internal bool FailImport;
        internal Func<ProductMasterImportPreviewRequest, Task<ServiceResult<ProductMasterImportPreviewResult>>>? PreviewReply;
        internal Func<ProductMasterExportRequest, Task<ServiceResult<List<ProductMasterExportRowDto>>>>? ExportReply;
        internal ServiceResult<ProductMasterImportPreviewResult> PreviewResult(List<ProductMasterImportRowInput> rows) => ServiceResult<ProductMasterImportPreviewResult>.Ok(new()
        {
            Rows = rows.Select(row => new ProductMasterImportPreviewRowDto { SourceRowNumber = row.SourceRowNumber, Operation = "Update", ProductMasterId = MasterId, ExpectedVersion = 7,
                CurrentData = new() { MasterSku = row.MasterSku, DefaultDescriptionZh = "旧值" }, ProposedData = new() { MasterSku = row.MasterSku, DefaultDescriptionZh = row.DefaultDescriptionZh } }).ToList()
        });
        /// <inheritdoc />
        public Task<ServiceResult<ProductMasterImportPreviewResult>> PreviewAsync(ProductMasterImportPreviewRequest request, CancellationToken cancellationToken = default)
        { Previewed = request; return PreviewReply?.Invoke(request) ?? Task.FromResult(PreviewResult(request.Rows)); }
        /// <inheritdoc />
        public Task<ServiceResult<ProductMasterImportResult>> ImportAsync(ProductMasterImportRequest request, CancellationToken cancellationToken = default)
        {
            ImportCalls++; Committed = request;
            return Task.FromResult(FailImport ? ServiceResult<ProductMasterImportResult>.Fail("NetworkUnavailable", "结果未知") : ServiceResult<ProductMasterImportResult>.Ok(new()
            { Rows = request.Rows.Select(row => new ProductMasterImportSavedRowDto { SourceRowNumber = row.Input.SourceRowNumber, Operation = "Update", ProductMasterId = row.ProductMasterId, Version = 8 }).ToList() }));
        }
        /// <inheritdoc />
        public Task<ServiceResult<List<ProductMasterExportRowDto>>> ExportAsync(ProductMasterExportRequest request, CancellationToken cancellationToken = default)
        { Exported = request; return ExportReply?.Invoke(request) ?? Task.FromResult(ServiceResult<List<ProductMasterExportRowDto>>.Ok([])); }
    }

    /// <summary>直接传递受控 API 结果，确保测试聚焦页面接纳边界。</summary>
    private sealed class Runner : IApiCallRunner
    {
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>模拟会话结束事件，不访问实际登录令牌。</summary>
    private sealed class SessionStub : ISessionService
    {
        /// <inheritdoc />
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        /// <inheritdoc />
        public void ClearSession() => EndSession(SessionEndReason.Logout);
        /// <inheritdoc />
        public void EndSession(SessionEndReason reason) => SessionEnded?.Invoke(this, new(reason));
    }

    /// <summary>显式允许测试批次确认，禁止意外弹窗。</summary>
    private sealed class Dialogs : IDialogService
    {
        /// <inheritdoc />
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        /// <inheritdoc />
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => throw new AssertFailedException(title);
        /// <inheritdoc />
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => throw new AssertFailedException(message);
    }
}

