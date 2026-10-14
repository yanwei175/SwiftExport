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
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductNeutralImport.Models;
using SwiftExportClient.Features.Products.ProductNeutralImport.Services;
using SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels;
using SwiftExportClient.Features.Products.ProductNeutralImport.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>用真实 XLSX 检查统一校验、串行队列和中断回写，不访问业务数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductNeutralBatchImportViewModelTests
{
    /// <summary>205 行只能发送 100、100、5，任何时刻最多一个请求。</summary>
    [TestMethod]
    public async Task Queue205UsesThreeSerialRequestsAndMemoryPages()
    {
        using var fixture = new Fixture(205); using var vm = fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        Assert.AreEqual(205,vm.Rows.Count);Assert.AreEqual(100,vm.VisibleRows.Count);
        vm.NextPageCommand.Execute(null);vm.NextPageCommand.Execute(null);Assert.AreEqual(5,vm.VisibleRows.Count);
        await vm.ImportCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] {100,100,5},fixture.Api.Sizes.ToArray());
        Assert.AreEqual(1,fixture.Api.MaximumActive);Assert.AreEqual(205,vm.CompletedCount);
        Assert.IsTrue(vm.Rows.All(row=>row.Status==ProductNeutralImportStatus.Success));Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));Assert.IsTrue(vm.CanUseFiles);
    }

    /// <summary>任一行本地错误阻止所有上传；下载包含全文件、原值和最后原因列。</summary>
    [TestMethod]
    public async Task LocalErrorsBlockAllUploadsAndDownloadWholeFile()
    {
        using var fixture = new Fixture(205, invalid:true); using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));await vm.ImportCommand.ExecuteAsync(null);Assert.AreEqual(0,fixture.Api.Sizes.Count);
        await vm.DownloadErrorsCommand.ExecuteAsync(new ProductNeutralImportFileTarget(fixture.Output,false));
        using (var book = new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {
            var sheet=book.Worksheet(1);Assert.AreEqual(206,sheet.LastRowUsed()!.RowNumber());Assert.AreEqual("错误原因",sheet.Cell(1,9).GetString());
            Assert.AreEqual("0",sheet.Cell(3,6).GetString());StringAssert.Contains(sheet.Cell(3,9).GetString(),"汇率");
            Assert.AreEqual("N-00000",sheet.Cell(2,2).GetString());
            sheet.Cell(3,6).Value="";book.Save();
        }
        await vm.LoadFileCommand.ExecuteAsync(fixture.Output);Assert.IsTrue(vm.ImportCommand.CanExecute(null));
        Assert.IsNull(vm.Rows[1].Input.DefaultExchangeRate);Assert.IsTrue(vm.Rows.All(row=>row.Reason.Length==0),"旧错误原因列不参与重导入");
    }

    /// <summary>保存失败保留原因，网络中断停止剩余队列；下载只含失败、未知及未发送行。</summary>
    [TestMethod]
    public async Task MixedReceiptAndNetworkFailureDownloadOnlyUnsuccessful()
    {
        using var fixture=new Fixture(205);fixture.Api.FailBatch=2;fixture.Api.FailRow=5;using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreEqual(100,vm.CompletedCount);Assert.AreEqual(99,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Success));
        Assert.AreEqual(1,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Failed));
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.OutcomeUnknown));
        Assert.AreEqual(5,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Pending));Assert.AreEqual(2,fixture.Api.Sizes.Count);
        await vm.DownloadErrorsCommand.ExecuteAsync(new ProductNeutralImportFileTarget(fixture.Output,false));
        using var book=new ClosedXML.Excel.XLWorkbook(fixture.Output);var sheet=book.Worksheet(1);
        Assert.AreEqual(107,sheet.LastRowUsed()!.RowNumber());Assert.AreEqual("N-00003",sheet.Cell(2,2).GetString());
        Assert.AreEqual("颜色不存在",sheet.Cell(2,9).GetString());Assert.AreEqual("待提交",sheet.Cell(107,9).GetString());
    }

    /// <summary>回执缺行属于结果未知，不能继续发送下一批或自动重试。</summary>
    [TestMethod]
    public async Task IncompleteReceiptStopsQueue()
    {
        using var fixture=new Fixture(205);fixture.Api.Incomplete=true;using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreEqual(1,fixture.Api.Sizes.Count);Assert.AreEqual(0,vm.CompletedCount);
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.OutcomeUnknown));
        Assert.AreEqual(105,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Pending));
        StringAssert.Contains(vm.Summary,"队列已停止");
    }

    /// <summary>即使 API 不支持取消，身份、会话、服务器和停止操作均禁止迟到回执推进队列。</summary>
    [TestMethod]
    [DataRow("user")][DataRow("session")][DataRow("server")][DataRow("stop")]
    public async Task LateReceiptCannotCrossIdentityOrCancellation(string change)
    {
        using var fixture=new Fixture(205);fixture.Api.Delay=true;using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);var importing=vm.ImportCommand.ExecuteAsync(null);
        await fixture.Api.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Submitting));
        if(change=="user")fixture.User.SetCurrentUser(Fixture.UserInfo(true));
        if(change=="session")fixture.Session.EndSession(SessionEndReason.Logout);
        if(change=="server")fixture.Options.BaseUrl="https://changed.example/";
        if(change=="stop")vm.StopCommand.Execute(null);
        fixture.Api.Release.SetResult();await importing;
        Assert.AreEqual(1,fixture.Api.Sizes.Count);Assert.AreEqual(0,vm.CompletedCount);
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.OutcomeUnknown));
        Assert.AreEqual(105,vm.Rows.Count(row=>row.Status==ProductNeutralImportStatus.Pending));
        StringAssert.Contains(vm.Summary,"队列已停止");
    }

    /// <summary>查看权限可以下载和校验，但无法启动上传；无产品权限入口关闭。</summary>
    [TestMethod]
    public async Task ViewerCanUseTemplateButCannotUpload()
    {
        using var fixture=new Fixture(1,admin:false);using var vm=fixture.Create();
        await vm.DownloadTemplateCommand.ExecuteAsync(new ProductNeutralImportFileTarget(fixture.Output,false));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {Assert.AreEqual(8,book.Worksheet(1).LastColumnUsed()!.ColumnNumber());Assert.AreEqual(ClosedXML.Excel.XLColor.Red,book.Worksheet(1).Cell(1,1).Style.Font.FontColor);}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        fixture.User.SetCurrentUser(new MeDto{UserId=Guid.NewGuid().ToString(),Permissions=[]});Assert.IsFalse(vm.CanUseFiles);
    }

    /// <summary>超过一万行仍完整读写；文本以字面值导出，原因列忽略，保留公共单元格保护。</summary>
    [TestMethod]
    public async Task RealWorkbook10001AndLiteralFormulaRoundTrip()
    {
        using var fixture=new Fixture(1);var service=new ProductNeutralWorkbookService(new ClosedXmlExcelReadService());
        var rows=Enumerable.Range(0,10001).Select(i=>new ProductNeutralImportRowModel(new(){SourceRowNumber=i+2,MasterSku="M-1",ColorCodeUnique="C-1",ProductCode=i==0?"=2+3":i.ToString("D8")}){Reason="上次失败"}).ToArray();
        await service.WriteAsync(new(fixture.Output,false),rows,true,CancellationToken.None);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))Assert.IsFalse(book.Worksheet(1).Cell(2,2).HasFormula);
        var read=await service.ReadAsync(fixture.Output,"中性产品",CancellationToken.None);
        Assert.AreEqual(10001,read.Count);Assert.AreEqual("=2+3",read[0].Input.ProductCode);Assert.IsTrue(read.All(row=>row.Status==ProductNeutralImportStatus.Pending));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output)){book.Worksheet(1).Cell(31251,1).Value="too-many-cells";book.Save();}
        await Assert.ThrowsAsync<SwiftExportClient.Services.Files.FileOperationException>(()=>service.ReadAsync(fixture.Output,"中性产品",CancellationToken.None));
    }

    /// <summary>完整表头必须存在且不重复，重新选择坏文件不能继续使用旧队列校验。</summary>
    [TestMethod]
    public async Task BadHeaderInvalidatesPreviousUpload()
    {
        using var fixture=new Fixture(2);using var vm=fixture.Create();await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input)){book.Worksheet(1).Cell(1,2).Value="主型号";book.Save();}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.IsFalse(vm.ImportCommand.CanExecute(null));Assert.AreEqual(0,fixture.Api.Sizes.Count);
    }

    /// <summary>失败和取消不得覆盖已有错误文件，默认拒绝未获许可的覆盖。</summary>
    [TestMethod]
    public async Task CancelledOrUnapprovedOutputKeepsExistingFile()
    {
        using var fixture=new Fixture(1);var service=new ProductNeutralWorkbookService(new ClosedXmlExcelReadService());
        await File.WriteAllTextAsync(fixture.Output,"原文件");using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.WriteAsync(new(fixture.Output,true),[],true,cancellation.Token));
        Assert.AreEqual("原文件",await File.ReadAllTextAsync(fixture.Output));
        await Assert.ThrowsAsync<SwiftExportClient.Services.Files.FileOperationException>(()=>service.WriteAsync(new(fixture.Output,false),[],true,CancellationToken.None));
        Assert.AreEqual("原文件",await File.ReadAllTextAsync(fixture.Output));
    }

    /// <summary>真实页面只显示一百行并只读，绑定和文件按钮在 Busy 结束后恢复。</summary>
    [TestMethod]
    public void ActualViewIsReadonlyAndPaged() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();using var fixture=new Fixture(1001);using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        var view=new ProductNeutralImportView{DataContext=vm};var window=new Window{Content=view,Width=1280,Height=760,ShowInTaskbar=false};
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.Background);var grid=(DataGrid)view.FindName("ImportGrid");
            Assert.IsTrue(grid.IsReadOnly);Assert.AreEqual(100,grid.Items.Count);Assert.AreEqual(11,grid.Columns.Count);
            Assert.IsTrue(grid.EnableRowVirtualization);Assert.IsNull(grid.ItemContainerGenerator.ContainerFromIndex(99));
            Assert.IsTrue(vm.CanUseFiles);Assert.IsTrue(vm.CanDownloadResults);
            var bitmap=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);
            var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen())
            {drawing.DrawRectangle((Brush)Application.Current.Resources["UserLayerOnMicaBaseAltFillColorDefaultBrush"],null,new Rect(0,0,1280,760));drawing.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,1280,760));}
            bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var image=File.Create(Path.Combine(FindRoot(),".artifacts","neutral-code-uniqueness","中性产品导入页面.png"));encoder.Save(image);
        }
        finally {window.Close();}
    });

    /// <summary>整文件校验聚合字段及重复错误，换序表头仍保留文本编码与原始利润率。</summary>
    [TestMethod]
    public async Task FieldValidationAndReorderedColumnsPreserveCodes()
    {
        using var fixture = new Fixture(4); var service = new ProductNeutralWorkbookService(new ClosedXmlExcelReadService());
        using (var book = new ClosedXML.Excel.XLWorkbook(fixture.Input))
        {
            var sheet = book.Worksheet(1);
            sheet.Cell(2,2).Value=" DUP "; sheet.Cell(3,2).Value="dup";
            sheet.Cell(2,4).Value="-1"; sheet.Cell(2,5).Value="0.12345"; sheet.Cell(2,6).Value="0"; sheet.Cell(2,7).Value="1";
            sheet.Cell(4,3).Value=""; sheet.Cell(5,8).Value=new string('x',1001); book.Save();
        }
        var rows = await service.ReadAsync(fixture.Input,"中性产品",CancellationToken.None);
        Assert.IsTrue(rows.All(row=>row.Status==ProductNeutralImportStatus.Failed));
        foreach(var expected in new[]{"采购价","销售价","汇率","利润率","重复"}) StringAssert.Contains(rows[0].Reason,expected);
        StringAssert.Contains(rows[2].Reason,"不能为空"); StringAssert.Contains(rows[3].Reason,"1000");
        await service.WriteAsync(new(fixture.Output,false),[],false,CancellationToken.None);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {
            var sheet=book.Worksheet(1);
            for(var i=1;i<=3;i++) Assert.AreEqual(ClosedXML.Excel.XLColor.Red,sheet.Cell(1,i).Style.Font.FontColor);
            Assert.AreEqual("@",sheet.Column(2).Style.NumberFormat.Format);
            sheet.Cell(1,1).Value="中性产品编码"; sheet.Cell(1,2).Value="主型号";
            sheet.Cell(2,1).Value="0007"; sheet.Cell(2,2).Value="M-1"; sheet.Cell(2,3).Value="C-1";
            sheet.Cell(2,7).Value=0.2; sheet.Cell(2,7).Style.NumberFormat.Format="0%";
            sheet.Cell(2,9).Value="旧原因"; book.Save();
        }
        var read=await service.ReadAsync(fixture.Output,"中性产品",CancellationToken.None);
        Assert.AreEqual("0007",read.Single().Input.ProductCode); Assert.AreEqual("M-1",read.Single().Input.MasterSku);
        Assert.AreEqual("0.2",read.Single().Input.ProfitMargin); Assert.AreEqual(ProductNeutralImportStatus.Pending,read.Single().Status);
    }

    /// <summary>不同小数分隔符文化下，Excel 数值与百分比按原始值校验，不受展示文本影响。</summary>
    [TestMethod]
    [DataRow("zh-CN")][DataRow("fr-FR")]
    public async Task NumericCellsUseInvariantValues(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            using var fixture = new Fixture(1);
            using (var book = new ClosedXML.Excel.XLWorkbook(fixture.Input))
            {
                var sheet=book.Worksheet(1); sheet.Cell(2,4).Value=12.3456;
                sheet.Cell(2,6).Value=7.123456; sheet.Cell(2,7).Value=0.2;
                sheet.Cell(2,7).Style.NumberFormat.Format="0%"; book.Save();
            }
            using var vm=fixture.Create(); await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
            Assert.IsTrue(vm.ImportCommand.CanExecute(null)); Assert.AreEqual("12.3456",vm.Rows[0].Input.DefaultPurchasePriceCny);
            Assert.AreEqual("7.123456",vm.Rows[0].Input.DefaultExchangeRate); Assert.AreEqual("0.2",vm.Rows[0].Input.ProfitMargin);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=previous; }
    }

    /// <summary>管理页命令按查看权限打开单个固定身份页签，重复进入保留当前导入对象。</summary>
    [TestMethod]
    public async Task ManagerEntryReusesExistingImportTabAndRequiresViewPermission()
    {
        using var fixture=new Fixture(1,admin:false);
        var factory=new ImportTabFactory(fixture.Create); var registry=new SwiftExportClient.Navigation.NavigationRouteRegistry();
        using var navigation=new SwiftExportClient.Navigation.NavigationService(factory,registry,
            new SwiftExportClient.Navigation.NavigationAccessService(registry,fixture.User),fixture.User,
            new StatusMessageService(),NullLogger<SwiftExportClient.Navigation.NavigationService>.Instance);
        using var manager=new SwiftExportClient.Features.Products.ProductList.ViewModels.ProductNeutralManagerViewModel(
            null!,null!,null!,null!,null!,new Dialogs(),new UnusedImageAddresses(),navigation,fixture.User);
        Assert.IsTrue(manager.OpenImportCommand.CanExecute(null));
        await manager.OpenImportCommand.ExecuteAsync(null); var first=navigation.SelectedTab;
        await manager.OpenImportCommand.ExecuteAsync(null);
        Assert.AreEqual(1,factory.CreatedCount); Assert.AreEqual(1,navigation.OpenTabs.Count); Assert.AreSame(first,navigation.SelectedTab);
        fixture.User.SetCurrentUser(new MeDto { UserId=fixture.User.CurrentUser!.UserId,Permissions=[] });
        Assert.IsFalse(manager.OpenImportCommand.CanExecute(null));
    }

    /// <summary>测试只允许导航创建导入页签，实际释放由导航拥有。</summary>
    private sealed class ImportTabFactory(Func<ProductNeutralImportViewModel> create) : SwiftExportClient.Navigation.INavigationTabFactory
    {
        internal int CreatedCount;
        /// <inheritdoc />
        public SwiftExportClient.Navigation.INavigationTabLease Create(SwiftExportClient.Navigation.NavigationRoute route)
        { Assert.AreEqual(typeof(ProductNeutralImportViewModel),route.ViewModelType); CreatedCount++; return new Lease(create()); }
        private sealed class Lease(ProductNeutralImportViewModel vm) : SwiftExportClient.Navigation.INavigationTabLease
        {
            /// <inheritdoc />
            public object ViewModel=>vm;
            /// <inheritdoc />
            public void Dispose()=>vm.Dispose();
        }
    }

    /// <summary>打开导入不读取或重建图片，任何图片调用均失败以验证入口边界。</summary>
    private sealed class UnusedImageAddresses : SwiftExportClient.Services.Images.IImageAddressResolver
    {
        /// <inheritdoc />
        public event EventHandler? Changed { add { } remove { } }
        /// <inheritdoc />
        public bool TryGetProductBaseAddress(out string baseAddress)=>throw new AssertFailedException("导入入口不读取图片配置");
        /// <inheritdoc />
        public SwiftExportClient.Services.Images.ImageLoadAddress? Resolve(string? sourcePath,SwiftExportClient.Services.Images.ImageDisplayMode mode,int decodePixelWidth)
            =>throw new AssertFailedException("导入入口不解析图片");
    }

    /// <summary>只定位测试产物目录，不访问应用配置或业务数据库。</summary>
    private static string FindRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"AGENTS.md")))return directory.FullName;
        throw new AssertFailedException("找不到仓库根目录");
    }

    /// <summary>独立临时文件和身份，防止使用实际用户令牌或数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"SwiftExport-neutral-batches-"+Guid.NewGuid().ToString("N"));
        internal string Input=>Path.Combine(Root,"input.xlsx");
        internal string Output=>Path.Combine(Root,"output.xlsx");
        internal readonly CurrentUserService User=new();
        internal readonly SessionStub Session=new();
        internal readonly ApiStub Api=new();
        internal readonly ApiOptions Options=new(){BaseUrl="https://original.example/"};
        internal Fixture(int count,bool invalid=false,bool admin=true)
        {
            Directory.CreateDirectory(Root);User.SetCurrentUser(UserInfo(admin));
            using var content=ExcelReadFixture.Create(book=>
            {
                var sheet=book.AddWorksheet("中性产品");
                for(var i=0;i<ProductNeutralWorkbookService.Columns.Count;i++)sheet.Cell(1,i+1).Value=ProductNeutralWorkbookService.Columns[i].Header;
                for(var i=0;i<count;i++){sheet.Cell(i+2,1).Value="M-1";sheet.Cell(i+2,2).Value="N-"+i.ToString("D5");sheet.Cell(i+2,3).Value="C-1";}
                if(invalid)sheet.Cell(3,6).Value="0";
            });File.WriteAllBytes(Input,content.ToArray());
        }
        internal static MeDto UserInfo(bool admin)=>new(){UserId=Guid.NewGuid().ToString(),UserName="test",IsAdmin=admin,Permissions=[PermissionKeys.ProductsNeutralView,PermissionKeys.ProductsNeutralEdit]};
        internal ProductNeutralImportViewModel Create()=>new(()=>Api,new(new ClosedXmlExcelReadService()),new Runner(),User,Session,Microsoft.Extensions.Options.Options.Create(Options),new Dialogs(),NullLogger<ProductNeutralImportViewModel>.Instance);
        /// <inheritdoc />
        public void Dispose()=>Directory.Delete(Root,true);
    }

    /// <summary>记录并发和批次，支持不响应取消的迟到结果。</summary>
    private sealed class ApiStub : IProductNeutralManagerApi
    {
        /// <inheritdoc />
        public Task<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.ProductNeutralManagerListItemDto>>> GetPagedAsync(
            SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests.ProductNeutralManagerSearchRequest request, CancellationToken cancellationToken=default) => throw new AssertFailedException("导入不读取管理列表");
        /// <inheritdoc />
        public Task<ServiceResult<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses.ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
            SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests.ProductNeutralManagerBatchSaveRequest request, CancellationToken cancellationToken=default) => throw new AssertFailedException("导入不调用原子批量保存");

        internal readonly List<int> Sizes=[];
        internal readonly List<List<ProductNeutralImportRowDto>> Requests=[];
        internal int Active,MaximumActive,FailBatch,FailRow;
        internal bool Delay,Incomplete;
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public async Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductNeutralImportRowDto> rows,CancellationToken cancellationToken=default)
        {
            Sizes.Add(rows.Count);Requests.Add(rows);Active++;MaximumActive=Math.Max(MaximumActive,Active);
            try
            {
                Started.TrySetResult();if(Delay)await Release.Task;else await Task.Yield();
                if(Sizes.Count==FailBatch)return ServiceResult<BatchSaveResponseBase>.Fail("NetworkUnavailable","连接中断");
                var items=rows.Select(row=>new SaveItemResult{RowIndex=row.SourceRowNumber,Success=row.SourceRowNumber!=FailRow,Message=row.SourceRowNumber==FailRow?"颜色不存在":null}).ToList();
                if(Incomplete)items.RemoveAt(items.Count-1);
                return ServiceResult<BatchSaveResponseBase>.Ok(new(){TotalCount=rows.Count,Items=items});
            }
            finally {Active--;}
        }
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


