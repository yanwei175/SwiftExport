using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExportClient.Features.SystemSettings.SystemParameters.Views;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>验证参数页草稿隔离、刷新保留、保存冲突、关闭保护与权限撤销。</summary>
[TestClass]
public sealed class SystemParametersTests
{
    /// <summary>刷新改变缓存但不覆盖输入及基线版本，撤销后显式采用最新值。</summary>
    [TestMethod]
    public async Task Refresh_ShouldKeepDraftUntilExplicitReject()
    {
        using var fixture = new SystemParameterClientFixture();
        using var model = Model(fixture);
        await model.InitializeAsync();
        model.Rows[0].Value = "https://example.test/draft/";
        fixture.Api.Read = () => Task.FromResult(SystemParameterClientFixture.ParameterApi.Result("https://example.test/new/", 2));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("https://example.test/draft/", model.Rows[0].Value);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("https://example.test/new/", fixture.Cache.Parameters[0].Value);
        model.RejectChangesCommand.Execute(model.Rows[0]);
        Assert.AreEqual("https://example.test/new/", model.Rows[0].Value);
        Assert.IsFalse(model.HasUnsavedChanges);
    }

    /// <summary>冲突保留输入，正在保存禁止关闭，成功回执立即更新缓存并清除草稿。</summary>
    [TestMethod]
    public async Task Save_ShouldKeepConflictAndApplySuccessfulReceipt()
    {
        using var fixture = new SystemParameterClientFixture();
        using var model = Model(fixture);
        await model.InitializeAsync();
        model.Rows[0].Value = "https://example.test/draft/";
        var pending = new TaskCompletionSource<ServiceResult<SystemParameterDto>>();
        fixture.Api.Save = _ => pending.Task;
        var save = model.SaveCommand.ExecuteAsync(model.Rows[0]);
        Assert.IsFalse(await model.CanLeaveAsync(CancellationToken.None));
        Assert.IsFalse(model.SaveCommand.CanExecute(model.Rows[0]));
        pending.SetResult(ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.VersionConflict, "冲突"));
        await save;
        Assert.AreEqual(1, fixture.Api.LastRequest!.ExpectedVersion);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("https://example.test/draft/", model.Rows[0].Value);
        fixture.Api.Save = _ => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(SystemParameterClientFixture.Parameter("https://example.test/saved/", 2)));
        await model.SaveCommand.ExecuteAsync(model.Rows[0]);
        Assert.AreEqual("https://example.test/saved/", fixture.Cache.Parameters[0].Value);
        Assert.IsFalse(model.HasUnsavedChanges);
    }

    /// <summary>离开需确认丢弃，保存后页签只读关闭保护；权限撤销销毁参数模型。</summary>
    [TestMethod]
    public async Task SettingsTab_ShouldProtectDraftAndRevokeAdminEntry()
    {
        using var fixture = new SystemParameterClientFixture();
        var dialogs = new Dialogs { Confirm = false };
        var parameterModel = Model(fixture, dialogs);
        using var tab = new SettingsPageViewModel(() => throw new NotSupportedException(), () => throw new NotSupportedException(),
            () => throw new NotSupportedException(), () => throw new NotSupportedException(), fixture.User, new SettingsDialogStub(),
            parametersFactory: () => parameterModel, parameterCache: fixture.Cache);
        await tab.NavigateAsync(SettingsPage.SystemParameters);
        parameterModel.Rows[0].Value = "https://example.test/draft/";
        Assert.IsTrue(tab.HasUnsavedChanges);
        Assert.IsFalse(await tab.CanCloseAsync(CancellationToken.None));
        await tab.NavigateAsync(SettingsPage.Personal);
        Assert.AreEqual(SettingsPage.SystemParameters, tab.CurrentPage);
        dialogs.Confirm = true;
        await tab.NavigateAsync(SettingsPage.Personal);
        Assert.IsFalse(tab.HasUnsavedChanges);
        fixture.User.SetCurrentUser(new MeDto { UserId = "admin", UserName = "normal", Permissions = ["System.Parameters.Manage"] });
        Assert.IsFalse(tab.CanRead(SettingsPage.SystemParameters));
        Assert.IsFalse(parameterModel.SaveCommand.CanExecute(parameterModel.Rows[0]));
    }

    /// <summary>关闭后迟到保存回执不能改变当前会话缓存。</summary>
    [TestMethod]
    public async Task DisposedPage_ShouldIgnoreLateSaveReceipt()
    {
        using var fixture = new SystemParameterClientFixture();
        var model = Model(fixture);
        await model.InitializeAsync();
        model.Rows[0].Value = "https://example.test/draft/";
        var pending = new TaskCompletionSource<ServiceResult<SystemParameterDto>>();
        fixture.Api.Save = _ => pending.Task;
        var save = model.SaveCommand.ExecuteAsync(model.Rows[0]);
        model.Dispose();
        pending.SetResult(ServiceResult<SystemParameterDto>.Ok(SystemParameterClientFixture.Parameter("https://example.test/late/", 2)));
        await save;
        Assert.AreEqual("https://example.test/files/ProductImages/", fixture.Cache.Parameters[0].Value);
    }

    /// <summary>保存一行只提交该键和版本，其他行的输入、缓存基线及关闭保护保持独立。</summary>
    [TestMethod]
    public async Task RowSave_ShouldPreserveOtherDrafts()
    {
        using var fixture = new SystemParameterClientFixture();
        fixture.Cache.ApplySaved(UploadParameter("https://example.test/files/ProductImages/"));
        using var model = Model(fixture);
        await model.InitializeAsync();
        var rebuild = model.Rows.Single(row => row.Key == SystemParameterKeys.ProductImagesBaseUrl);
        var upload = model.Rows.Single(row => row.Key == SystemParameterKeys.ProductImagesUploadBaseUrl);
        rebuild.Value = @"D:\LocalPictures";
        upload.Value = "https://public.test/new/";
        fixture.Api.Save = _ => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(UploadParameter("https://public.test/new/", 2)));
        await model.SaveCommand.ExecuteAsync(upload);
        Assert.AreEqual(SystemParameterKeys.ProductImagesUploadBaseUrl, fixture.Api.LastKey);
        Assert.AreEqual(1, fixture.Api.LastRequest!.ExpectedVersion);
        Assert.AreEqual(@"D:\LocalPictures", rebuild.Value);
        Assert.IsTrue(rebuild.HasUnsavedChanges);
        Assert.IsFalse(upload.HasUnsavedChanges);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("https://example.test/files/ProductImages/", fixture.Cache.Parameters.Single(item => item.Key == rebuild.Key).Value);
        Assert.AreEqual("https://public.test/new/", fixture.Cache.Parameters.Single(item => item.Key == upload.Key).Value);
    }

    /// <summary>上传地址的升级建议可直接显式保存为版本 1，无需为启用保存按钮制造输入差异。</summary>
    [TestMethod]
    public async Task SuggestedUnconfiguredRow_ShouldSaveWithVersionZero()
    {
        using var fixture = new SystemParameterClientFixture();
        var suggestion = UploadParameter("https://example.test/files/ProductImages/", 0);
        suggestion.IsConfigured = false;
        fixture.Cache.ApplySaved(suggestion);
        using var model = Model(fixture);
        await model.InitializeAsync();
        var row = model.Rows.Single(item => item.Key == SystemParameterKeys.ProductImagesUploadBaseUrl);
        Assert.IsFalse(model.HasUnsavedChanges);
        Assert.IsTrue(model.SaveCommand.CanExecute(row));
        fixture.Api.Save = _ => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(UploadParameter(row.Value)));
        await model.SaveCommand.ExecuteAsync(row);
        Assert.AreEqual(0, fixture.Api.LastRequest!.ExpectedVersion);
        Assert.IsTrue(row.IsConfigured);
        Assert.AreEqual(1, row.Version);
        Assert.IsFalse(model.SaveCommand.CanExecute(row));
        Assert.IsTrue(fixture.Cache.TryGetValue(row.Key, out _));
    }

    /// <summary>服务端新增注册项直接进入表格，刷新只更新没有草稿的行。</summary>
    [TestMethod]
    public async Task Refresh_ShouldAddDefinitionsAndMergeCleanRowsOnly()
    {
        using var fixture = new SystemParameterClientFixture();
        fixture.Cache.ApplySaved(UploadParameter("https://example.test/files/ProductImages/"));
        using var model = Model(fixture);
        await model.InitializeAsync();
        var rebuild = model.Rows.Single(row => row.Key == SystemParameterKeys.ProductImagesBaseUrl);
        var upload = model.Rows.Single(row => row.Key == SystemParameterKeys.ProductImagesUploadBaseUrl);
        rebuild.Value = @"D:\Draft";
        fixture.Api.Read = () => Task.FromResult(ServiceResult<IReadOnlyList<SystemParameterDto>>.Ok([
            SystemParameterClientFixture.Parameter("https://example.test/new-rebuild/", 2),
            UploadParameter("https://public.test/new/", 2),
            new() { Key = "Future.Parameter", DisplayName = "未来参数", Value = "42", Version = 1, IsConfigured = true }
        ]));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.HasCount(3, model.Rows);
        Assert.AreEqual(@"D:\Draft", rebuild.Value);
        Assert.AreEqual(1, rebuild.Version);
        Assert.AreEqual("https://public.test/new/", upload.Value);
        Assert.AreEqual(2, upload.Version);
        model.RejectChangesCommand.Execute(rebuild);
        Assert.AreEqual("https://example.test/new-rebuild/", rebuild.Value);
        Assert.AreEqual(2, rebuild.Version);
        Assert.IsFalse(model.HasUnsavedChanges);
    }

    /// <summary>错误键的保存回执不能污染缓存或清理当前行草稿。</summary>
    [TestMethod]
    public async Task MismatchedReceipt_ShouldKeepRowAndCache()
    {
        using var fixture = new SystemParameterClientFixture();
        using var model = Model(fixture);
        await model.InitializeAsync();
        var row = model.Rows[0];
        row.Value = @"D:\Draft";
        fixture.Api.Save = _ => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(UploadParameter("https://public.test/", 2)));
        await model.SaveCommand.ExecuteAsync(row);
        Assert.IsTrue(row.HasUnsavedChanges);
        Assert.HasCount(1, fixture.Cache.Parameters);
        StringAssert.Contains(row.Status, "参数键不匹配");
    }

    /// <summary>真实参数表格绑定各行的保存、撤销按钮；写入期间冻结全部输入并阻止关闭。</summary>
    [TestMethod]
    public void ParameterGrid_ShouldBindRowActionsAndFreezeInputs() => StaTest.RunAsync(async () =>
    {
        using var fixture = new SystemParameterClientFixture();
        fixture.Cache.ApplySaved(UploadParameter("https://example.test/files/ProductImages/"));
        using var model = Model(fixture);
        await model.InitializeAsync();
        var page = new SystemParametersPage { DataContext = model };
        var window = new Window { Content = page, Width = 1200, Height = 500, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);
            window.UpdateLayout();
            var grid = (DataGrid)page.FindName("ParameterGrid");
            Assert.AreSame(model.Rows, grid.ItemsSource);
            Assert.AreEqual(2, grid.Items.Count);
            var saves = Descendants<Button>(grid).Where(button => Equals(button.Content, "保存")).ToArray();
            Assert.HasCount(2, saves);
            foreach (var button in saves)
            {
                Assert.AreSame(model.SaveCommand, button.Command);
                Assert.AreSame(button.DataContext, button.CommandParameter);
            }
            var row = model.Rows.Single(item => item.Key == SystemParameterKeys.ProductImagesUploadBaseUrl);
            row.Value = "https://public.test/new/";
            var pending = new TaskCompletionSource<ServiceResult<SystemParameterDto>>();
            fixture.Api.Save = _ => pending.Task;
            var running = model.SaveCommand.ExecuteAsync(row);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsFalse(await model.CanLeaveAsync(CancellationToken.None));
            var inputs = Descendants<TextBox>(grid).ToArray();
            Assert.HasCount(2, inputs);
            Assert.IsTrue(inputs.All(input => !input.IsEnabled));
            pending.SetResult(ServiceResult<SystemParameterDto>.Ok(UploadParameter("https://public.test/new/", 2)));
            await running;
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsTrue(inputs.All(input => input.IsEnabled));
        }
        finally { window.Close(); }
    });

    /// <summary>构造独立上传参数快照，不借用重建参数的身份。</summary>
    private static SystemParameterDto UploadParameter(string value, int version = 1) => new()
    {
        Key = SystemParameterKeys.ProductImagesUploadBaseUrl, Value = value, Version = version, IsConfigured = true,
        DisplayName = "产品图片上传地址", Description = "公开原图目录"
    };
    /// <summary>遍历真实表格视觉树，确认模板命令和输入状态。</summary>
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static SystemParametersViewModel Model(SystemParameterClientFixture fixture, Dialogs? dialogs = null)
        => new(fixture.Api, fixture.Runner, fixture.Cache, fixture.User, dialogs ?? new Dialogs());
    /// <summary>控制丢弃确认，无实际弹窗。</summary>
    private sealed class Dialogs : IDialogService
    {
        public bool Confirm { get; set; } = true;
        /// <inheritdoc />
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
        /// <inheritdoc />
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <inheritdoc />
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
