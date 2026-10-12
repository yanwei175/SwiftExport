using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>验证独立参数缓存的去重、失败保留、会话隔离与保存后旧读取防回写。</summary>
[TestClass]
public sealed class SystemParameterCacheServiceTests
{
    /// <summary>多个首次读取共用一个请求，外部修改 DTO 不会污染缓存。</summary>
    [TestMethod]
    public async Task EnsureLoaded_ShouldDeduplicateAndIsolateSnapshots()
    {
        using var fixture = new SystemParameterClientFixture(null);
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<SystemParameterDto>>>();
        var started = new TaskCompletionSource();
        fixture.Api.Read = () => { started.SetResult(); return pending.Task; };
        var first = fixture.Cache.EnsureLoadedAsync();
        var second = fixture.Cache.EnsureLoadedAsync();
        Assert.AreSame(first, second);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, fixture.Api.Reads);
        pending.SetResult(SystemParameterClientFixture.ParameterApi.Result("https://example.test/new/"));
        Assert.IsTrue((await first).Success);
        fixture.Cache.Parameters[0].Value = "pollution";
        Assert.IsTrue(fixture.Cache.TryGetValue(SystemParameterKeys.ProductImagesBaseUrl, out var value));
        Assert.AreEqual("https://example.test/new/", value);
    }

    /// <summary>网络失败保留有效值和加载状态。</summary>
    [TestMethod]
    public async Task RefreshFailure_ShouldKeepLastSuccessfulSnapshot()
    {
        using var fixture = new SystemParameterClientFixture();
        fixture.Api.Read = () => Task.FromResult(ServiceResult<IReadOnlyList<SystemParameterDto>>.Fail("离线"));
        Assert.IsFalse((await fixture.Cache.RefreshAsync()).Success);
        Assert.IsTrue(fixture.Cache.IsLoaded);
        Assert.AreEqual("https://example.test/files/ProductImages/", fixture.Cache.Parameters[0].Value);
    }

    /// <summary>保存回执使旧刷新失效，不能把新参数换回旧值。</summary>
    [TestMethod]
    public async Task ApplySaved_ShouldRejectOlderRefresh()
    {
        using var fixture = new SystemParameterClientFixture();
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<SystemParameterDto>>>();
        fixture.Api.Read = () => pending.Task;
        var refresh = fixture.Cache.RefreshAsync();
        fixture.Cache.ApplySaved(SystemParameterClientFixture.Parameter("https://example.test/new/", 2));
        pending.SetResult(SystemParameterClientFixture.ParameterApi.Result("https://example.test/old/"));
        Assert.IsFalse((await refresh).Success);
        Assert.AreEqual("https://example.test/new/", fixture.Cache.Parameters[0].Value);
    }

    /// <summary>退出或更换账号清空参数，旧响应不能进入新会话。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionChange_ShouldClearAndRejectLateResponse(bool changeAccount)
    {
        using var fixture = new SystemParameterClientFixture();
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<SystemParameterDto>>>();
        fixture.Api.Read = () => pending.Task;
        var refresh = fixture.Cache.RefreshAsync();
        if (changeAccount) fixture.User.SetCurrentUser(new MeDto { UserId = "new-user", UserName = "new" });
        else fixture.Session.ClearSession();
        pending.SetResult(SystemParameterClientFixture.ParameterApi.Result("https://example.test/old/"));
        Assert.IsFalse((await refresh).Success);
        Assert.IsFalse(fixture.Cache.IsLoaded);
        Assert.AreEqual(0, fixture.Cache.Parameters.Count);
    }
}
