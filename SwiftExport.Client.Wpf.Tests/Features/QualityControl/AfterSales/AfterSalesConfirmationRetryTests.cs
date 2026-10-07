using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.AfterSales;

/// <summary>验证售后页面的保存与确认分别持有未决身份，重试不重新生成请求。</summary>
[TestClass]
public sealed class AfterSalesConfirmationRetryTests
{
    /// <summary>普通保存的未知结果及后续限流均保留原请求；成功后只读取当前详情。</summary>
    [TestMethod]
    public void OrdinarySaveUnknownThenRateLimited_RetriesSameRequestAndRefreshes()
    {
        StaTest.RunAsync(async () =>
        {
            var fixture = new Fixture();
            using var vm = fixture.ViewModel;
            vm.Capabilities = new AfterSalesCapabilitiesDto { CanSave = true, CanEditFacts = true };
            Assert.IsTrue(vm.CanEditFacts);
            var editStates = new List<bool>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.CanEditFacts)) editStates.Add(vm.CanEditFacts);
            };
            var original = new SaveAfterSalesCommand
            {
                Uid = Guid.NewGuid(), Version = 4, CommandId = Guid.NewGuid(),
                Header = new AfterSalesEditHeaderDto { IssueDescription = "原始内容" }
            };
            var calls = new List<SaveAfterSalesCommand>();
            var errors = new Queue<string>([ServiceErrorCodes.OutcomeUnknown, ServiceErrorCodes.RateLimited]);
            Func<Task<ServiceResult<AfterSalesCommandResult>>> call = () =>
            {
                calls.Add(original);
                return Task.FromResult(errors.TryDequeue(out var code)
                    ? ServiceResult<AfterSalesCommandResult>.Fail(code, "请求尚未确定")
                    : ServiceResult<AfterSalesCommandResult>.Ok(new AfterSalesCommandResult { Uid = original.Uid, Version = 5 }));
            };
            var pendingType = typeof(AfterSalesDetailViewModel).GetNestedType("PendingWrite", BindingFlags.NonPublic)!;
            var pending = Activator.CreateInstance(pendingType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ["save", original.Uid, "售后服务单已保存", call], null)!;
            typeof(AfterSalesDetailViewModel).GetMethod("SetPendingWrite", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, [pending]);
            Assert.IsFalse(vm.CanEditFacts);
            Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
            await vm.RetryPendingWriteCommand.ExecuteAsync(null);
            await vm.RetryPendingWriteCommand.ExecuteAsync(null);
            Assert.AreEqual(2, calls.Count);
            Assert.IsTrue(vm.HasPendingIntent);
            await vm.RetryPendingWriteCommand.ExecuteAsync(null);
            Assert.AreEqual(3, calls.Count);
            Assert.IsTrue(calls.All(request => ReferenceEquals(request, original)));
            Assert.AreEqual(4, original.Version);
            Assert.AreEqual("原始内容", original.Header.IssueDescription);
            Assert.IsFalse(vm.HasPendingIntent);
            Assert.IsTrue(vm.CanEditFacts);
            Assert.IsTrue(vm.CanEditSupplement);
            Assert.IsTrue(editStates.Count > 0 && editStates[^1], "Busy 结束后应通知编辑绑定恢复。");
        });
    }

    /// <summary>保存未知后限流仍复用原创建标记、身份、版本和完整事实，成功后才发确认。</summary>
    [TestMethod]
    public void SaveUnknownThenRateLimited_RetriesOriginalSaveBeforeConfirm()
    {
        StaTest.RunAsync(async () =>
        {
            var fixture = new Fixture();
            fixture.SaveFailures.Enqueue(ServiceErrorCodes.OutcomeUnknown);
            fixture.SaveFailures.Enqueue(ServiceErrorCodes.RateLimited);
            using var vm = fixture.ViewModel;
            fixture.SetPending(vm);
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(1, fixture.SaveCalls.Count);
            Assert.AreEqual(0, fixture.ConfirmCalls.Count);
            var original = JsonSerializer.Serialize(fixture.SaveCalls[0]);
            Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
            Assert.IsFalse(vm.ReloadCommand.CanExecute(null));
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(2, fixture.SaveCalls.Count);
            Assert.AreSame(fixture.SaveCalls[0], fixture.SaveCalls[1]);
            Assert.AreEqual(original, JsonSerializer.Serialize(fixture.SaveCalls[1]));
            Assert.AreEqual(0, fixture.ConfirmCalls.Count);
            Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
            Assert.IsTrue(vm.ConfirmDemandCommand.CanExecute(null));
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(3, fixture.SaveCalls.Count);
            Assert.AreSame(fixture.SaveCalls[0], fixture.SaveCalls[2]);
            Assert.AreEqual(original, JsonSerializer.Serialize(fixture.SaveCalls[2]));
            CollectionAssert.AreEqual(new[] { true, true, true }, fixture.SaveCreateFlags);
            Assert.AreEqual(1, fixture.ConfirmCalls.Count);
            Assert.AreEqual(7, fixture.ConfirmCalls[0].Version);
            Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None));
        });
    }

    /// <summary>确认未知后限流只重发原确认，不重新保存或取得新版本。</summary>
    [TestMethod]
    public void ConfirmUnknownThenRateLimited_RetriesConfirmWithoutSavingAgain()
    {
        StaTest.RunAsync(async () =>
        {
            var fixture = new Fixture();
            fixture.ConfirmFailures.Enqueue(ServiceErrorCodes.OutcomeUnknown);
            fixture.ConfirmFailures.Enqueue(ServiceErrorCodes.RateLimited);
            using var vm = fixture.ViewModel;
            fixture.SetPending(vm);
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(1, fixture.SaveCalls.Count);
            Assert.AreEqual(1, fixture.ConfirmCalls.Count);
            Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(1, fixture.SaveCalls.Count);
            Assert.AreEqual(2, fixture.ConfirmCalls.Count);
            Assert.AreEqual(fixture.ConfirmCalls[0].CommandId, fixture.ConfirmCalls[1].CommandId);
            Assert.AreEqual(7, fixture.ConfirmCalls[1].Version);
            Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
            Assert.IsTrue(vm.ConfirmDemandCommand.CanExecute(null));
            await vm.ConfirmDemandCommand.ExecuteAsync(null);
            Assert.AreEqual(1, fixture.SaveCalls.Count);
            Assert.AreEqual(3, fixture.ConfirmCalls.Count);
            Assert.AreSame(fixture.ConfirmCalls[0], fixture.ConfirmCalls[2]);
            Assert.AreEqual(7, fixture.ConfirmCalls[2].Version);
            Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None));
        });
    }

    /// <summary>首次前置拒绝结束意图；保存曾未知不应传染给尚未执行的确认阶段。</summary>
    [TestMethod]
    public void InitialRejectionAndIndependentConfirmationStageReleasePending()
    {
        StaTest.RunAsync(async () =>
        {
            foreach (var rejectSave in new[] { true, false })
            {
                var fixture = new Fixture();
                using var vm = fixture.ViewModel;
                fixture.SetPending(vm);
                if (rejectSave) fixture.SaveFailures.Enqueue(ServiceErrorCodes.Forbidden);
                else
                {
                    fixture.SaveFailures.Enqueue(ServiceErrorCodes.OutcomeUnknown);
                    fixture.ConfirmFailures.Enqueue(ServiceErrorCodes.Forbidden);
                    await vm.ConfirmDemandCommand.ExecuteAsync(null);
                    Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
                }
                await vm.ConfirmDemandCommand.ExecuteAsync(null);
                Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None));
                Assert.AreEqual(rejectSave ? 0 : 1, fixture.ConfirmCalls.Count);
            }
        });
    }

    private sealed class Fixture
    {
        private readonly Guid uid = Guid.NewGuid();
        public readonly List<SaveAfterSalesCommand> SaveCalls = [];
        public readonly List<bool> SaveCreateFlags = [];
        public readonly List<ConfirmAfterSalesDemandCommand> ConfirmCalls = [];
        public Queue<string> SaveFailures { get; } = new();
        public Queue<string> ConfirmFailures { get; } = new();
        public AfterSalesDetailViewModel ViewModel { get; }

        public Fixture()
        {
            var feature = Proxy<IAfterSalesFeatureService>((method, args) => method.Name switch
            {
                "SaveAsync" => Save((SaveAfterSalesCommand)args![0]!, (bool)args[1]!),
                "ConfirmAsync" => Confirm((ConfirmAfterSalesDemandCommand)args![1]!),
                _ => throw new AssertFailedException(method.Name)
            });
            var api = Proxy<IAfterSalesApi>((method, args) => method.Name == "GetAsync"
                ? Task.FromResult(ServiceResult<AfterSalesDetailDto>.Ok(new AfterSalesDetailDto
                { Header = new AfterSalesHeaderDto { Uid = uid, Version = 8,
                    AfterSalesNo = "CR-1", CustomerId = Guid.Empty },
                    Capabilities = new AfterSalesCapabilitiesDto { CanSave = true, CanEditFacts = true } }))
                : throw new AssertFailedException(method.Name));
            var cache = DispatchProxy.Create<IReferenceDataCacheService, CachePort>();
            var user = Proxy<ICurrentUserService>((method, args) => method.Name == "HasPermission"
                ? true : throw new AssertFailedException(method.Name));
            ViewModel = new AfterSalesDetailViewModel(api, feature, new DirectRunner(),
                Proxy<INavigationService>((method, args) => method.Name == "UpdateDocumentKey"
                    ? null : throw new AssertFailedException(method.Name)),
                Proxy<IDialogService>((method, args) => method.Name == "ConfirmAsync"
                    ? Task.FromResult(true) : throw new AssertFailedException(method.Name)),
                Proxy<IAfterSalesReasonDialogService>((method, args) => throw new AssertFailedException(method.Name)),
                Proxy<IMessageService>((method, args) => null), user, cache,
                Proxy<IProductApi>((method, args) => throw new AssertFailedException(method.Name)),
                Proxy<IAfterSalesQualityCategoryApi>((method, args) => throw new AssertFailedException(method.Name)));
        }

        /// <summary>模拟已捕获的不可变确认意图，专注验证两个网络阶段的恢复。</summary>
        public void SetPending(AfterSalesDetailViewModel vm)
        {
            var pendingType = typeof(AfterSalesDetailViewModel).GetNestedType("PendingDemandConfirmation",
                BindingFlags.NonPublic)!;
            var save = new SaveAfterSalesCommand
            {
                Uid = uid, Version = 6, CommandId = Guid.NewGuid(),
                Header = new AfterSalesEditHeaderDto { IssueDescription = "原始投诉事实" },
                AddedLines = [new AfterSalesEditLineDto { Uid = Guid.NewGuid(), SkuSnapshot = "SKU-ORIGINAL", Quantity = 2 }]
            };
            var pending = Activator.CreateInstance(pendingType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [save, true, Guid.NewGuid()], null)!;
            typeof(AfterSalesDetailViewModel).GetMethod("SetPendingConfirmation",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [pending]);
        }

        private Task<ServiceResult<AfterSalesCommandResult>> Save(SaveAfterSalesCommand command, bool create)
        {
            SaveCalls.Add(command);
            SaveCreateFlags.Add(create);
            return Task.FromResult(SaveFailures.TryDequeue(out var code)
                ? ServiceResult<AfterSalesCommandResult>.Fail(code, "本次保存未取得结果")
                : ServiceResult<AfterSalesCommandResult>.Ok(new AfterSalesCommandResult
                { Uid = uid, Version = 7, AfterSalesNo = "CR-1" }));
        }
        private Task<ServiceResult<AfterSalesCommandResult>> Confirm(ConfirmAfterSalesDemandCommand command)
        {
            ConfirmCalls.Add(command);
            return Task.FromResult(ConfirmFailures.TryDequeue(out var code)
                ? ServiceResult<AfterSalesCommandResult>.Fail(code, "本次确认未取得结果")
                : ServiceResult<AfterSalesCommandResult>.Ok(new AfterSalesCommandResult
                { Uid = uid, Version = 8, AfterSalesNo = "CR-1" }));
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var port = DispatchProxy.Create<T, MethodPort>();
        ((MethodPort)(object)port).Handler = handler;
        return port;
    }
    public class MethodPort : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    public class CachePort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var type = method!.ReturnType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyObservableCollection<>))
            {
                var collection = Activator.CreateInstance(typeof(ObservableCollection<>).MakeGenericType(type.GenericTypeArguments[0]));
                return Activator.CreateInstance(type, collection);
            }
            throw new AssertFailedException(method.Name);
        }
    }
    private sealed class DirectRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken token, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action());
    }
}
