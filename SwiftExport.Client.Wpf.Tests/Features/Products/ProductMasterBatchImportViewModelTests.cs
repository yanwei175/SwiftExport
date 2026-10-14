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
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductMasterImport.Models;
using SwiftExportClient.Features.Products.ProductMasterImport.Services;
using SwiftExportClient.Features.Products.ProductMasterImport.ViewModels;
using SwiftExportClient.Features.Products.ProductMasterImport.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>用真实 XLSX 检查统一校验、串行队列和中断回写，不访问业务数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterBatchImportViewModelTests
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
        Assert.IsTrue(vm.Rows.All(row=>row.Status==ProductMasterImportStatus.Success));Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));Assert.IsTrue(vm.CanUseFiles);
    }

    /// <summary>任一行本地错误阻止所有上传；下载包含全文件、原值和最后原因列。</summary>
    [TestMethod]
    public async Task LocalErrorsBlockAllUploadsAndDownloadWholeFile()
    {
        using var fixture = new Fixture(205, invalid:true); using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        Assert.IsFalse(vm.ImportCommand.CanExecute(null));await vm.ImportCommand.ExecuteAsync(null);Assert.AreEqual(0,fixture.Api.Sizes.Count);
        await vm.DownloadErrorsCommand.ExecuteAsync(new ProductMasterImportFileTarget(fixture.Output,false));
        using (var book = new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {
            var sheet=book.Worksheet(1);Assert.AreEqual(206,sheet.LastRowUsed()!.RowNumber());Assert.AreEqual("错误原因",sheet.Cell(1,12).GetString());
            Assert.AreEqual("0",sheet.Cell(3,6).GetString());StringAssert.Contains(sheet.Cell(3,12).GetString(),"装率");
            Assert.AreEqual("M-00000",sheet.Cell(2,1).GetString());
            sheet.Cell(3,6).Value="";book.Save();
        }
        await vm.LoadFileCommand.ExecuteAsync(fixture.Output);Assert.IsTrue(vm.ImportCommand.CanExecute(null));
        Assert.IsNull(vm.Rows[1].Input.DefaultPcsPerPackage);Assert.IsTrue(vm.Rows.All(row=>row.Reason.Length==0),"旧错误原因列不参与重导入");
    }

    /// <summary>保存失败保留原因，网络中断停止剩余队列；下载只含失败、未知及未发送行。</summary>
    [TestMethod]
    public async Task MixedReceiptAndNetworkFailureDownloadOnlyUnsuccessful()
    {
        using var fixture=new Fixture(205);fixture.Api.FailBatch=2;fixture.Api.FailRow=5;using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreEqual(100,vm.CompletedCount);Assert.AreEqual(99,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Success));
        Assert.AreEqual(1,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Failed));
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.OutcomeUnknown));
        Assert.AreEqual(5,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Pending));Assert.AreEqual(2,fixture.Api.Sizes.Count);
        await vm.DownloadErrorsCommand.ExecuteAsync(new ProductMasterImportFileTarget(fixture.Output,false));
        using var book=new ClosedXML.Excel.XLWorkbook(fixture.Output);var sheet=book.Worksheet(1);
        Assert.AreEqual(107,sheet.LastRowUsed()!.RowNumber());Assert.AreEqual("M-00003",sheet.Cell(2,1).GetString());
        Assert.AreEqual("供应商不存在",sheet.Cell(2,12).GetString());Assert.AreEqual("待提交",sheet.Cell(107,12).GetString());
    }

    /// <summary>回执缺行属于结果未知，不能继续发送下一批或自动重试。</summary>
    [TestMethod]
    public async Task IncompleteReceiptStopsQueue()
    {
        using var fixture=new Fixture(205);fixture.Api.Incomplete=true;using var vm=fixture.Create();
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);await vm.ImportCommand.ExecuteAsync(null);
        Assert.AreEqual(1,fixture.Api.Sizes.Count);Assert.AreEqual(0,vm.CompletedCount);
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.OutcomeUnknown));
        Assert.AreEqual(105,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Pending));
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
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Submitting));
        if(change=="user")fixture.User.SetCurrentUser(Fixture.UserInfo(true));
        if(change=="session")fixture.Session.EndSession(SessionEndReason.Logout);
        if(change=="server")fixture.Options.BaseUrl="https://changed.example/";
        if(change=="stop")vm.StopCommand.Execute(null);
        fixture.Api.Release.SetResult();await importing;
        Assert.AreEqual(1,fixture.Api.Sizes.Count);Assert.AreEqual(0,vm.CompletedCount);
        Assert.AreEqual(100,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.OutcomeUnknown));
        Assert.AreEqual(105,vm.Rows.Count(row=>row.Status==ProductMasterImportStatus.Pending));
        StringAssert.Contains(vm.Summary,"队列已停止");
    }

    /// <summary>查看权限可以下载和校验，但无法启动上传；无产品权限入口关闭。</summary>
    [TestMethod]
    public async Task ViewerCanUseTemplateButCannotUpload()
    {
        using var fixture=new Fixture(1,admin:false);using var vm=fixture.Create();
        await vm.DownloadTemplateCommand.ExecuteAsync(new ProductMasterImportFileTarget(fixture.Output,false));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {Assert.AreEqual(11,book.Worksheet(1).LastColumnUsed()!.ColumnNumber());Assert.AreEqual(ClosedXML.Excel.XLColor.Red,book.Worksheet(1).Cell(1,1).Style.Font.FontColor);}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.IsFalse(vm.ImportCommand.CanExecute(null));
        fixture.User.SetCurrentUser(new MeDto{UserId=Guid.NewGuid().ToString(),Permissions=[]});Assert.IsFalse(vm.CanUseFiles);
    }

    /// <summary>超过一万行仍完整读写；文本以字面值导出，原因列忽略，保留公共单元格保护。</summary>
    [TestMethod]
    public async Task RealWorkbook10001AndLiteralFormulaRoundTrip()
    {
        using var fixture=new Fixture(1);var service=new ProductMasterWorkbookService(new ClosedXmlExcelReadService());
        var rows=Enumerable.Range(0,10001).Select(i=>new ProductMasterImportRowModel(new(){SourceRowNumber=i+2,MasterSku=i==0?"=2+3":i.ToString("D8")}){Reason="上次失败"}).ToArray();
        await service.WriteAsync(new(fixture.Output,false),rows,true,CancellationToken.None);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))Assert.IsFalse(book.Worksheet(1).Cell(2,1).HasFormula);
        var read=await service.ReadAsync(fixture.Output,"父级型号",CancellationToken.None);
        Assert.AreEqual(10001,read.Count);Assert.AreEqual("=2+3",read[0].Input.MasterSku);Assert.IsTrue(read.All(row=>row.Status==ProductMasterImportStatus.Pending));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output)){book.Worksheet(1).Cell(22728,1).Value="too-many-cells";book.Save();}
        await Assert.ThrowsAsync<SwiftExportClient.Services.Files.FileOperationException>(()=>service.ReadAsync(fixture.Output,"父级型号",CancellationToken.None));
    }

    /// <summary>完整表头必须存在且不重复，重新选择坏文件不能继续使用旧队列校验。</summary>
    [TestMethod]
    public async Task BadHeaderInvalidatesPreviousUpload()
    {
        using var fixture=new Fixture(2);using var vm=fixture.Create();await vm.LoadFileCommand.ExecuteAsync(fixture.Input);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input)){book.Worksheet(1).Cell(1,2).Value="主型号";book.Save();}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.IsFalse(vm.ImportCommand.CanExecute(null));Assert.AreEqual(0,fixture.Api.Sizes.Count);
    }

    /// <summary>模板和错误回写只含系列英文名，不含分类；提交原名称供服务器匹配身份。</summary>
    [TestMethod]
    public async Task TemplateAndErrorWorkbookUseSeriesEnglishName()
    {
        using var fixture=new Fixture(1);fixture.Api.FailRow=2;using var vm=fixture.Create();
        await vm.DownloadTemplateCommand.ExecuteAsync(new ProductMasterImportFileTarget(fixture.Output,false));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {
            var sheet=book.Worksheet(1);Assert.AreEqual("产品系列英文名称",sheet.Cell(1,10).GetString());
            Assert.IsFalse(sheet.Range(1,1,1,11).Cells().Any(cell=>cell.GetString().Contains("分类",StringComparison.Ordinal)));
            sheet.Cell(2,1).Value="M-1";sheet.Cell(2,10).Value=" Standard ";book.Save();
        }
        await vm.LoadFileCommand.ExecuteAsync(fixture.Output);Assert.AreEqual("Standard",vm.Rows.Single().Input.ProductSeriesNameEn);
        await vm.ImportCommand.ExecuteAsync(null);Assert.AreEqual("Standard",fixture.Api.Requests.Single().Single().ProductSeriesNameEn);
        await vm.DownloadErrorsCommand.ExecuteAsync(new ProductMasterImportFileTarget(fixture.Input,true));
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input))
        {Assert.AreEqual("产品系列英文名称",book.Worksheet(1).Cell(1,10).GetString());Assert.AreEqual("Standard",book.Worksheet(1).Cell(2,10).GetString());}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.AreEqual("Standard",vm.Rows.Single().Input.ProductSeriesNameEn);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input)){book.Worksheet(1).Cell(2,10).Value=new string('x',501);book.Save();}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.IsFalse(vm.ImportCommand.CanExecute(null));StringAssert.Contains(vm.Rows.Single().Reason,"500");
    }

    /// <summary>旧分类模板必须重新下载，不能把原分类内容误解释为系列名称。</summary>
    [TestMethod]
    public async Task OldCategoryTemplateIsRejected()
    {
        using var fixture=new Fixture(1);using var vm=fixture.Create();
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input)){book.Worksheet(1).Cell(1,10).Value="分类路径";book.Save();}
        await vm.LoadFileCommand.ExecuteAsync(fixture.Input);Assert.AreEqual(0,vm.Rows.Count);Assert.IsFalse(vm.ImportCommand.CanExecute(null));Assert.AreEqual(0,fixture.Api.Sizes.Count);
    }

    /// <summary>整文件聚合所有长度、数值和重复错误，并允许前十一列表头换序。</summary>
    [TestMethod]
    public async Task FieldErrorsAndReorderedHeadersAreHandled()
    {
        using var fixture=new Fixture(4);var service=new ProductMasterWorkbookService(new ClosedXmlExcelReadService());
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Input))
        {
            var sheet=book.Worksheet(1);sheet.Cell(2,1).Value=" DUP ";sheet.Cell(3,1).Value="dup";sheet.Cell(4,1).Value="";
            sheet.Cell(2,2).Value=new string('中',2001);sheet.Cell(2,6).Value="bad";sheet.Cell(2,7).Value="0.00001";
            sheet.Cell(2,8).Value="-1";sheet.Cell(2,9).Value="1000000000000";sheet.Cell(5,4).Value=new string('x',501);book.Save();
        }
        var rows=await service.ReadAsync(fixture.Input,"父级型号",CancellationToken.None);
        Assert.AreEqual(4,rows.Count);Assert.IsTrue(rows.All(row=>row.Status==ProductMasterImportStatus.Failed));
        foreach(var expected in new[]{"2000","装率","净重","毛重","体积","重复"})StringAssert.Contains(rows[0].Reason,expected);
        StringAssert.Contains(rows[2].Reason,"不能为空");StringAssert.Contains(rows[3].Reason,"500");
        await service.WriteAsync(new(fixture.Output,false),[],false,CancellationToken.None);
        using(var book=new ClosedXML.Excel.XLWorkbook(fixture.Output))
        {var sheet=book.Worksheet(1);sheet.Cell(1,1).Value="中文描述";sheet.Cell(1,2).Value="主型号";sheet.Cell(2,1).Value="描述";sheet.Cell(2,2).Value="0007";sheet.Cell(2,12).Value="旧原因";book.Save();}
        var reordered=await service.ReadAsync(fixture.Output,"父级型号",CancellationToken.None);
        Assert.AreEqual("0007",reordered.Single().Input.MasterSku);Assert.AreEqual("描述",reordered.Single().Input.DefaultDescriptionZh);
        Assert.AreEqual(ProductMasterImportStatus.Pending,reordered.Single().Status);
    }

    /// <summary>失败和取消不得覆盖已有错误文件，默认拒绝未获许可的覆盖。</summary>
    [TestMethod]
    public async Task CancelledOrUnapprovedOutputKeepsExistingFile()
    {
        using var fixture=new Fixture(1);var service=new ProductMasterWorkbookService(new ClosedXmlExcelReadService());
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
        var view=new ProductMasterImportView{DataContext=vm};var window=new Window{Content=view,Width=1280,Height=760,ShowInTaskbar=false};
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.Background);var grid=(DataGrid)view.FindName("ImportGrid");
            Assert.IsTrue(grid.IsReadOnly);Assert.AreEqual(100,grid.Items.Count);Assert.AreEqual(14,grid.Columns.Count);
            Assert.IsTrue(grid.EnableRowVirtualization);Assert.IsNull(grid.ItemContainerGenerator.ContainerFromIndex(99));
            Assert.IsTrue(vm.CanUseFiles);Assert.IsTrue(vm.CanDownloadResults);
            var bitmap=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);
            var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen())
            {drawing.DrawRectangle((Brush)Application.Current.Resources["UserLayerOnMicaBaseAltFillColorDefaultBrush"],null,new Rect(0,0,1280,760));drawing.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,1280,760));}
            bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var image=File.Create(Path.Combine(FindRoot(),".artifacts","product-master-import-series","父级导入页面.png"));encoder.Save(image);
        }
        finally {window.Close();}
    });

    private static string FindRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"AGENTS.md")))return directory.FullName;
        throw new AssertFailedException("找不到仓库根目录");
    }

    /// <summary>独立临时文件和身份，防止使用实际用户令牌或数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"SwiftExport-parent-batches-"+Guid.NewGuid().ToString("N"));
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
                var sheet=book.AddWorksheet("父级型号");
                for(var i=0;i<ProductMasterWorkbookService.Columns.Count;i++)sheet.Cell(1,i+1).Value=ProductMasterWorkbookService.Columns[i].Header;
                for(var i=0;i<count;i++){sheet.Cell(i+2,1).Value="M-"+i.ToString("D5");sheet.Cell(i+2,2).Value="说明";}
                if(invalid)sheet.Cell(3,6).Value="0";
            });File.WriteAllBytes(Input,content.ToArray());
        }
        internal static MeDto UserInfo(bool admin)=>new(){UserId=Guid.NewGuid().ToString(),UserName="test",IsAdmin=admin,Permissions=[PermissionKeys.ProductsMasterView,PermissionKeys.ProductsMasterEdit]};
        internal ProductMasterImportViewModel Create()=>new(()=>Api,new(new ClosedXmlExcelReadService()),new Runner(),User,Session,Microsoft.Extensions.Options.Options.Create(Options),new Dialogs(),NullLogger<ProductMasterImportViewModel>.Instance);
        /// <inheritdoc />
        public void Dispose()=>Directory.Delete(Root,true);
    }

    /// <summary>记录并发和批次，支持不响应取消的迟到结果。</summary>
    private sealed class ApiStub : IProductMasterImportApi
    {
        internal readonly List<int> Sizes=[];
        internal readonly List<List<ProductMasterImportRowDto>> Requests=[];
        internal int Active,MaximumActive,FailBatch,FailRow;
        internal bool Delay,Incomplete;
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public async Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductMasterImportRowDto> rows,CancellationToken cancellationToken=default)
        {
            Sizes.Add(rows.Count);Requests.Add(rows);Active++;MaximumActive=Math.Max(MaximumActive,Active);
            try
            {
                Started.TrySetResult();if(Delay)await Release.Task;else await Task.Yield();
                if(Sizes.Count==FailBatch)return ServiceResult<BatchSaveResponseBase>.Fail("NetworkUnavailable","连接中断");
                var items=rows.Select(row=>new SaveItemResult{RowIndex=row.SourceRowNumber,Success=row.SourceRowNumber!=FailRow,Message=row.SourceRowNumber==FailRow?"供应商不存在":null}).ToList();
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


