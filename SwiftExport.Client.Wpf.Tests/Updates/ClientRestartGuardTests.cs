using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Updates;

namespace SwiftExport.Client.Wpf.Tests.Updates;

/// <summary>验证全部页签的重启保护、在途工作及确认期间的导航变化。</summary>
[TestClass]
public sealed class ClientRestartGuardTests
{
    /// <summary>逐页调用离开协议，取消不会移除页签或清空编辑。</summary>
    [TestMethod]
    public async Task DirtyDocument_UsesCloseProtocolWithoutRemovingTabs()
    {
        var document = new DocumentStub { AllowClose = false };
        var navigation = new NavigationStub();
        navigation.OpenTabs.Add(Tab(document));
        var guard = new ClientRestartGuard(navigation);
        Assert.IsFalse(await guard.CanRestartAsync(default));
        Assert.AreEqual(1, navigation.OpenTabs.Count);
        document.AllowClose = true;
        Assert.IsTrue(await guard.CanRestartAsync(default));
        Assert.AreEqual(1, navigation.OpenTabs.Count);
    }

    /// <summary>没有离开协议的草稿要求先完成操作。</summary>
    [TestMethod]
    public async Task DirtyPageWithoutProtocol_BlocksRestart()
    {
        var navigation = new NavigationStub();
        navigation.OpenTabs.Add(Tab(new object()));
        navigation.OpenTabs[0].IsDirty = true;
        Assert.IsFalse(await new ClientRestartGuard(navigation).CanRestartAsync(default));
    }

    /// <summary>确认期间新页签使检查快照失效。</summary>
    [TestMethod]
    public async Task NavigationChangedDuringConfirmation_BlocksRestart()
    {
        var navigation = new NavigationStub();
        navigation.OpenTabs.Add(Tab(new DocumentStub { OnClose = () => navigation.OpenTabs.Add(Tab(new object())) }));
        Assert.IsFalse(await new ClientRestartGuard(navigation).CanRestartAsync(default));
    }

    /// <summary>保存中即使无草稿也不允许退出；操作结束恢复重启。</summary>
    [TestMethod]
    public async Task BusyPage_BlocksRestartUntilWorkCompletes()
    {
        var navigation = new NavigationStub();
        var document = new BusyPage();
        var completion = new TaskCompletionSource();
        var work = document.WorkAsync(completion.Task);
        navigation.OpenTabs.Add(Tab(document));
        Assert.IsFalse(await new ClientRestartGuard(navigation).CanRestartAsync(default));
        completion.SetResult();
        await work;
        Assert.IsTrue(await new ClientRestartGuard(navigation).CanRestartAsync(default));
    }

    private static NavigationTabItem Tab(object content) => new(
        new NavigationRoute("update-test", "测试", "Workspace", typeof(object), "test", null), content);

    /// <summary>模拟通过项目统一 Busy 边界执行的业务操作。</summary>
    private sealed class BusyPage : ViewModelBaseEx
    {
        public Task WorkAsync(Task work) => RunBusyAsync(() => work);
    }
    /// <summary>模拟草稿确认及确认期间发生的导航变化。</summary>
    private sealed class DocumentStub : IDocumentTabViewModel
    {
        public string DocumentTitle => "草稿";
        public bool HasUnsavedChanges => true;
        public bool AllowClose { get; set; } = true;
        public Action? OnClose { get; set; }
        /// <inheritdoc />
        public Task<bool> CanCloseAsync(CancellationToken token) { OnClose?.Invoke(); return Task.FromResult(AllowClose); }
    }
    /// <summary>提供真实页签集合，其他导航操作不会连接业务服务。</summary>
    private sealed class NavigationStub : INavigationService
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
        public NavigationTabItem? SelectedTab { get; set; }
        public Task OpenAsync(string key) => Task.CompletedTask;
        public Task OpenDocumentAsync(string key, string id, object? parameter = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateDocumentKey(object model, string key) { }
        public void Activate(NavigationTabItem? tab) { }
        public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
        public void Reset() { }
    }
}
