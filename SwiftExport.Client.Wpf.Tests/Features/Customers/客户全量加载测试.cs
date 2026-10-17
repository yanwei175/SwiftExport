using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证全量读取、刷新/关闭确认以及请求取消后的模型保护，不访问真实 API。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CustomerAllLoadingTests
{
    /// <summary>刷新读取超过旧分页上限的完整快照，刷新被拒绝时不替换草稿。</summary>
    [TestMethod]
    public void Refresh_ShouldPreserveDraftWhenDiscardRejected() => StaTest.RunAsync(async () =>
    {
        var calls = 0;
        using var vm = CreateVm(_ =>
        {
            calls++;
            return Task.FromResult(ServiceResult<IReadOnlyList<CustomerDto>>.Ok(
                Enumerable.Range(0, 251).Select(i => new CustomerDto { Uid = Guid.NewGuid(), Version = 8,
                    CustomerCode = $"C{i}", CustomerName = $"客户{i}" }).ToArray()));
        }, false);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(251, vm.Customers.Items.Count);
        Assert.AreEqual(8, vm.Customers.Items[0].Version);
        var row = vm.Customers.Items[0];
        row.CustomerName = "本地草稿";
        Assert.AreEqual(1, calls);
        Assert.AreSame(row, vm.Customers.Items[0]);
        Assert.IsTrue(vm.HasUnsavedChanges);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        Assert.AreSame(row, vm.Customers.Items[0]);
        Assert.AreEqual("本地草稿", row.CustomerName);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
    });

    /// <summary>确认刷新后若读取失败，原有行及脏状态仍保留。</summary>
    [TestMethod]
    public void RefreshFailure_ShouldNotDiscardOldDraft() => StaTest.RunAsync(async () =>
    {
        var errors = 0;
        using var vm = CreateVm(_ => Task.FromResult(ServiceResult<IReadOnlyList<CustomerDto>>.Fail("模拟读取失败")),
            true, () => errors++);
        vm.Customers.LoadFromDtos([new CustomerDto { Uid = Guid.NewGuid(), Version = 3, CustomerCode = "C1", CustomerName = "原值" }]);
        var row = vm.Customers.Items[0];
        row.CustomerName = "保留草稿";
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, errors);
        Assert.AreSame(row, vm.Customers.Items[0]);
        Assert.AreEqual("保留草稿", row.CustomerName);
        Assert.IsTrue(vm.Customers.CreateSaveRequest().HasChanges);
    });

    /// <summary>释放时取消在途请求；模拟服务忽略取消仍返回时，迟到结果不能写回页面。</summary>
    [TestMethod]
    public void Dispose_ShouldCancelAndRejectLateSnapshot() => StaTest.RunAsync(async () =>
    {
        var response = new TaskCompletionSource<ServiceResult<IReadOnlyList<CustomerDto>>>();
        var token = CancellationToken.None;
        using var vm = CreateVm(value => { token = value; return response.Task; }, true);
        vm.Customers.LoadFromDtos([new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "OLD", CustomerName = "旧值" }]);
        var row = vm.Customers.Items[0];
        var loading = vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        vm.Dispose();
        Assert.IsTrue(token.IsCancellationRequested);
        response.SetResult(ServiceResult<IReadOnlyList<CustomerDto>>.Ok([new CustomerDto { CustomerCode = "LATE" }]));
        await loading;
        Assert.AreSame(row, vm.Customers.Items[0]);
        Assert.IsFalse(vm.IsBusy);
    });

    /// <summary>使用现有字典桩和接口代理，任何非预期 API 调用立即失败。</summary>
    private static CustomerManagerViewModel CreateVm(Func<CancellationToken, Task<ServiceResult<IReadOnlyList<CustomerDto>>>> load,
        bool confirm, Action? error = null)
    {
        var api = Probe.Create<ICustomerApi>((method, args) => method.Name == "GetAllAsync"
            ? load((CancellationToken)args![0]!) : throw new NotSupportedException(method.Name));
        var runner = Probe.Create<IApiCallRunner>((_, args) => ((Delegate)args![0]!).DynamicInvoke()!);
        var dialogs = Probe.Create<IDialogService>((method, _) => method.Name == "ConfirmAsync"
            ? Task.FromResult(confirm) : throw new NotSupportedException(method.Name));
        var messages = Probe.Create<IMessageService>((_, _) => { error?.Invoke(); return null; });
        return new(api, runner, messages, dialogs, new CustomerDictionaryColumnsTests.ReferenceDataCacheStub([]));
    }

    /// <summary>仅转发本测试声明的调用，不提供静默默认结果。</summary>
    public class Probe : DispatchProxy
    {
        private Func<MethodInfo, object?[]?, object?> _handler = null!;
        /// <summary>创建接口测试代理。</summary>
        public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = Create<T, Probe>();
            ((Probe)(object)proxy)._handler = handler;
            return proxy;
        }
        /// <summary>转发调用参数并返回测试指定的任务或结果。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!, args);
    }
}
