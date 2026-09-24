using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

/// <summary>验证详情页在未知结果、前置限流及页面重读期间保留原确认并冻结编辑。</summary>
[TestClass]
public sealed class SalesOrderDetailConfirmationRetryTests
{
    [TestMethod]
    public void UnknownThenRateLimited_RetriesOriginalRequestAndFreezesEditors()
    {
        StaTest.RunAsync(async () =>
        {
            var contract = DispatchProxy.Create<ISalesOrderDetailApi, DetailApiProxy>();
            var api = (DetailApiProxy)(object)contract;
            var cache = DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>();
            var viewModel = new SalesOrderDetailViewModel(contract, new DirectRunner(), null!,
                new DialogStub(), null!, new MessagesStub(), cache);
            var uid = Guid.NewGuid();
            var document = new SalesOrderDetailDto { Header = new SalesOrderEditHeaderDto
            { Uid = uid, Version = 3, OrderNo = "SO-DETAIL", DocumentStatus = SalesOrderDocumentStatusEnum.Open,
                ConfirmationStatus = ConfirmationStatusEnum.待确认 } };
            typeof(SalesOrderDetailViewModel).GetMethod("LoadDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewModel, [document]);
            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
            Assert.IsTrue(viewModel.IsEditable);
            Assert.IsFalse(viewModel.IsReadOnly);
            await viewModel.ConfirmCommand.ExecuteAsync(null);
            Assert.AreEqual(1, api.Commands.Count);
            Assert.IsFalse(await viewModel.CanCloseAsync(CancellationToken.None));
            Assert.IsFalse(viewModel.IsEditable);
            Assert.IsTrue(viewModel.IsReadOnly);
            Assert.IsTrue(viewModel.ConfirmCommand.CanExecute(null), "只读编辑状态不能阻断原确认重试。");
            CollectionAssert.IsSubsetOf(new[] { nameof(viewModel.IsEditable), nameof(viewModel.IsReadOnly),
                nameof(viewModel.HasUnsavedChanges) }, changed);
            viewModel.Header.Version = 9;
            await viewModel.ConfirmCommand.ExecuteAsync(null);
            Assert.AreEqual(2, api.Commands.Count);
            Assert.AreEqual(api.Commands[0].CommandId, api.Commands[1].CommandId);
            Assert.AreEqual(3, api.Commands[1].Version);
            Assert.AreEqual(uid, api.Commands[1].Uid);
            Assert.IsFalse(await viewModel.CanCloseAsync(CancellationToken.None));
            Assert.IsFalse(viewModel.IsEditable);
            Assert.IsTrue(viewModel.IsReadOnly);
            Assert.IsTrue(viewModel.ConfirmCommand.CanExecute(null));
            await viewModel.ConfirmCommand.ExecuteAsync(null);
            Assert.AreEqual(3, api.Commands.Count);
            Assert.AreSame(api.Commands[0], api.Commands[2], "重试必须发送未修改的原请求对象。");
            Assert.AreEqual(3, api.Commands[2].Version);
            Assert.IsFalse(viewModel.IsEditable, "确认后当前服务器状态仍锁定编辑。");
            Assert.IsTrue(viewModel.IsReadOnly);
            Assert.IsTrue(await viewModel.CanCloseAsync(CancellationToken.None));
        });
    }

    /// <summary>无权响应只拒绝本次重试；明确的版本冲突才结束旧意图并允许新命令身份。</summary>
    [TestMethod]
    public void PreReceiptRejectionKeepsIntentButDefinitiveFailureClearsIt()
    {
        StaTest.RunAsync(async () =>
        {
            var contract = DispatchProxy.Create<ISalesOrderDetailApi, DetailApiProxy>();
            var api = (DetailApiProxy)(object)contract;
            api.FailureCodes.Clear();
            api.FailureCodes.Enqueue(ServiceErrorCodes.OutcomeUnknown);
            api.FailureCodes.Enqueue(ServiceErrorCodes.Forbidden);
            api.FailureCodes.Enqueue(ServiceErrorCodes.VersionConflict);
            var vm = new SalesOrderDetailViewModel(contract, new DirectRunner(), null!,
                new DialogStub(), null!, new MessagesStub(),
                DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>());
            var changed = new List<string?>();
            vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
            var uid = Guid.NewGuid();
            typeof(SalesOrderDetailViewModel).GetMethod("LoadDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, [new SalesOrderDetailDto { Header = new SalesOrderEditHeaderDto
                { Uid = uid, Version = 3, OrderNo = "SO-DETAIL", DocumentStatus = SalesOrderDocumentStatusEnum.Open,
                    ConfirmationStatus = ConfirmationStatusEnum.待确认 } }]);

            await vm.ConfirmCommand.ExecuteAsync(null);
            await vm.ConfirmCommand.ExecuteAsync(null);
            Assert.AreSame(api.Commands[0], api.Commands[1]);
            Assert.IsFalse(vm.IsEditable);
            changed.Clear();
            await vm.ConfirmCommand.ExecuteAsync(null);
            Assert.AreSame(api.Commands[0], api.Commands[2]);
            Assert.IsTrue(vm.IsEditable);
            Assert.IsFalse(vm.IsReadOnly);
            CollectionAssert.IsSubsetOf(new[] { nameof(vm.IsEditable), nameof(vm.IsReadOnly),
                nameof(vm.HasUnsavedChanges) }, changed);
            Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None));
            await vm.ConfirmCommand.ExecuteAsync(null);
            Assert.AreNotEqual(api.Commands[0].CommandId, api.Commands[3].CommandId);
        });
    }

    /// <summary>表格 XAML 必须消费叠加未决状态的只读属性，防止只锁命令而漏锁单元格。</summary>
    [TestMethod]
    public void DetailGridUsesPendingAwareReadOnlyBinding()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var xaml = File.ReadAllText(Path.Combine(directory.FullName, "SwiftExport.Client.Wpf", "Features",
            "Sales", "Orders", "Views", "SalesOrderDetailView.xaml"));
        StringAssert.Contains(xaml, "IsReadOnly=\"{Binding IsReadOnly}\"");
        StringAssert.Contains(xaml, "IsEnabled=\"{Binding IsEditable}\"");
    }

    /// <summary>首次认证、权限或限流拒绝应解除冻结，不要求用户取得从未执行的回执。</summary>
    [TestMethod]
    public void InitialPreReceiptRejectionUnlocksEditors()
    {
        StaTest.RunAsync(async () =>
        {
            foreach (var code in new[] { ServiceErrorCodes.Forbidden, ServiceErrorCodes.Unauthenticated, ServiceErrorCodes.RateLimited })
            {
                var contract = DispatchProxy.Create<ISalesOrderDetailApi, DetailApiProxy>();
                var api = (DetailApiProxy)(object)contract;
                api.FailureCodes.Clear();
                api.FailureCodes.Enqueue(code);
                var vm = new SalesOrderDetailViewModel(contract, new DirectRunner(), null!,
                    new DialogStub(), null!, new MessagesStub(), DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>());
                typeof(SalesOrderDetailViewModel).GetMethod("LoadDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, [new SalesOrderDetailDto { Header = new SalesOrderEditHeaderDto
                    { Uid = Guid.NewGuid(), Version = 3, DocumentStatus = SalesOrderDocumentStatusEnum.Open,
                        ConfirmationStatus = ConfirmationStatusEnum.待确认 } }]);
                await vm.ConfirmCommand.ExecuteAsync(null);
                Assert.IsTrue(vm.IsEditable, code);
                Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None), code);
                await vm.ConfirmCommand.ExecuteAsync(null);
                Assert.AreNotEqual(api.Commands[0].CommandId, api.Commands[1].CommandId, code);
            }
        });
    }

    public class DetailApiProxy : DispatchProxy
    {
        public List<ConfirmSalesOrderCommand> Commands { get; } = [];
        public Queue<string> FailureCodes { get; } = new([ServiceErrorCodes.OutcomeUnknown, ServiceErrorCodes.RateLimited]);
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == "ConfirmAsync")
            {
                var command = (ConfirmSalesOrderCommand)arguments![1]!;
                Commands.Add(command);
                return Task.FromResult(FailureCodes.TryDequeue(out var code)
                    ? ServiceResult<SalesOrderConfirmationResultDto>.Fail(code, "本次请求未取得确认结果")
                    : ServiceResult<SalesOrderConfirmationResultDto>.Ok(new SalesOrderConfirmationResultDto
                    { Uid = command.Uid, Version = 4, ConfirmationStatus = ConfirmationStatusEnum.已确认 }));
            }
            if (method?.Name == "GetAsync")
                return Task.FromResult(ServiceResult<SalesOrderDetailDto>.Ok(new SalesOrderDetailDto
                { Header = new SalesOrderEditHeaderDto { Uid = (Guid)arguments![0]!, Version = 4,
                    OrderNo = "SO-DETAIL", DocumentStatus = SalesOrderDocumentStatusEnum.Open,
                    ConfirmationStatus = ConfirmationStatusEnum.已确认 } }));
            throw new AssertFailedException($"意外销售 API 调用：{method?.Name}");
        }
    }

    public class CacheProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == "GetDictionaryValues") return Array.Empty<SysDictDataDto>();
            var type = method!.ReturnType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyObservableCollection<>))
            {
                var element = type.GenericTypeArguments[0];
                var collection = Activator.CreateInstance(typeof(ObservableCollection<>).MakeGenericType(element));
                return Activator.CreateInstance(type, collection);
            }
            throw new AssertFailedException($"意外缓存调用：{method.Name}");
        }
    }

    private sealed class DirectRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken token, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action());
    }
    private sealed class DialogStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MessagesStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
