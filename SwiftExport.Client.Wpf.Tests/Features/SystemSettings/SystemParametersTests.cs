using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels;
using SwiftExportClient.Services.UI;

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
        model.Value = "https://example.test/draft/";
        fixture.Api.Read = () => Task.FromResult(SystemParameterClientFixture.ParameterApi.Result("https://example.test/new/", 2));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("https://example.test/draft/", model.Value);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("https://example.test/new/", fixture.Cache.Parameters[0].Value);
        model.RejectChangesCommand.Execute(null);
        Assert.AreEqual("https://example.test/new/", model.Value);
        Assert.IsFalse(model.HasUnsavedChanges);
    }

    /// <summary>冲突保留输入，正在保存禁止关闭，成功回执立即更新缓存并清除草稿。</summary>
    [TestMethod]
    public async Task Save_ShouldKeepConflictAndApplySuccessfulReceipt()
    {
        using var fixture = new SystemParameterClientFixture();
        using var model = Model(fixture);
        await model.InitializeAsync();
        model.Value = "https://example.test/draft/";
        var pending = new TaskCompletionSource<ServiceResult<SystemParameterDto>>();
        fixture.Api.Save = _ => pending.Task;
        var save = model.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(await model.CanLeaveAsync(CancellationToken.None));
        Assert.IsFalse(model.SaveCommand.CanExecute(null));
        pending.SetResult(ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.VersionConflict, "冲突"));
        await save;
        Assert.AreEqual(1, fixture.Api.LastRequest!.ExpectedVersion);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("https://example.test/draft/", model.Value);
        fixture.Api.Save = _ => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(SystemParameterClientFixture.Parameter("https://example.test/saved/", 2)));
        await model.SaveCommand.ExecuteAsync(null);
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
        parameterModel.Value = "https://example.test/draft/";
        Assert.IsTrue(tab.HasUnsavedChanges);
        Assert.IsFalse(await tab.CanCloseAsync(CancellationToken.None));
        await tab.NavigateAsync(SettingsPage.Personal);
        Assert.AreEqual(SettingsPage.SystemParameters, tab.CurrentPage);
        dialogs.Confirm = true;
        await tab.NavigateAsync(SettingsPage.Personal);
        Assert.IsFalse(tab.HasUnsavedChanges);
        fixture.User.SetCurrentUser(new MeDto { UserId = "admin", UserName = "normal", Permissions = ["System.Parameters.Manage"] });
        Assert.IsFalse(tab.CanRead(SettingsPage.SystemParameters));
        Assert.IsFalse(parameterModel.SaveCommand.CanExecute(null));
    }

    /// <summary>关闭后迟到保存回执不能改变当前会话缓存。</summary>
    [TestMethod]
    public async Task DisposedPage_ShouldIgnoreLateSaveReceipt()
    {
        using var fixture = new SystemParameterClientFixture();
        var model = Model(fixture);
        await model.InitializeAsync();
        model.Value = "https://example.test/draft/";
        var pending = new TaskCompletionSource<ServiceResult<SystemParameterDto>>();
        fixture.Api.Save = _ => pending.Task;
        var save = model.SaveCommand.ExecuteAsync(null);
        model.Dispose();
        pending.SetResult(ServiceResult<SystemParameterDto>.Ok(SystemParameterClientFixture.Parameter("https://example.test/late/", 2)));
        await save;
        Assert.AreEqual("https://example.test/files/ProductImages/", fixture.Cache.Parameters[0].Value);
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
