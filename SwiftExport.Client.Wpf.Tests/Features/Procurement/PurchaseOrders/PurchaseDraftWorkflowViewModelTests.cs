using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

[TestClass]
public sealed class PurchaseDraftWorkflowViewModelTests
{
    [TestMethod]
    public async Task ManualPreview_ShouldDefaultRemainingQtyAndRequireRepreviewAfterEdit()
    {
        var api = new NetDemandApiStub { IncludeSafeAppend = true };
        using var viewModel = CreateViewModel(api);

        await viewModel.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(remainingQty: 10m)],
        });

        Assert.AreEqual(10m, viewModel.Items.Single().PurchaseQty);
        Assert.IsFalse(viewModel.Items.Single().IsQuantityReadOnly);
        Assert.IsFalse(viewModel.IsStale);

        viewModel.Items.Single().PurchaseQty = 6m;

        Assert.IsTrue(viewModel.IsStale);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "重新预览");

        viewModel.Items.Single().PurchaseQty = 11m;
        Assert.IsNotNull(viewModel.Items.Single().QuantityError);
        Assert.IsFalse(viewModel.PreviewCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task SourceOneClick_ShouldUseServerFullRemainingQtyAndKeepQuantityReadOnly()
    {
        var api = new NetDemandApiStub { IncludeSafeAppend = true };
        using var viewModel = CreateViewModel(api);

        await viewModel.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.SourceOneClick,
            SourceType = "SO",
            SourceUid = api.SourceUid,
            SourceNo = "SO-1001",
        });

        Assert.HasCount(1, viewModel.Items);
        Assert.AreEqual(10m, viewModel.Items[0].PurchaseQty);
        Assert.IsTrue(viewModel.Items[0].IsQuantityReadOnly);
        Assert.HasCount(1, viewModel.Targets.Single().Choices);
        Assert.AreEqual(PurchaseDraftTargetModeEnum.CreateNew, viewModel.Targets.Single().Choices[0].TargetMode);
    }

    [TestMethod]
    public async Task StrongWarningsAndTargetChanges_ShouldGateCreateUntilFreshAndConfirmed()
    {
        var api = new NetDemandApiStub { IncludeWarnings = true, IncludeSafeAppend = true };
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });

        Assert.HasCount(3, viewModel.Warnings);
        Assert.AreEqual(2, viewModel.Warnings.Count(item => item.RequiresConfirmation));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));

        var target = viewModel.Targets.Single();
        target.SelectedChoice = target.Choices.Single(choice => choice.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting);
        Assert.IsTrue(viewModel.IsStale);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));

        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.IsStale);
        foreach (var warning in viewModel.Warnings.Where(item => item.RequiresConfirmation))
        {
            warning.IsConfirmed = true;
        }

        Assert.IsTrue(viewModel.CreateCommand.CanExecute(null));
        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasAppliedResults);
        Assert.IsNotNull(api.LastCreateCommand);
        Assert.AreEqual(PurchaseDraftTargetModeEnum.AppendExisting, api.LastCreateCommand.Sources.Single().Targets.Single().TargetMode);
        CollectionAssert.AreEquivalent(
            new[] { PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates, PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining },
            api.LastCreateCommand.Sources.Single().ConfirmedWarningCodes);
    }

    [TestMethod]
    public async Task LatestPreviewRequest_ShouldWinWhenOlderRequestCompletesLast()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });

        var first = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        api.PreviewHandler = (request, _) => ++call == 1 ? first.Task : second.Task;

        var olderTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await Task.Yield();
        var newerTask = viewModel.PreviewCommand.ExecuteAsync(null);
        second.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(new PurchaseDraftPreviewRequest
        {
            ExecutionRequestId = viewModel.ExecutionRequestId,
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
            Items = viewModel.Items.Select(item => item.ToDto()).ToList(),
        }, "NEW-SKU")));
        await newerTask;
        first.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(new PurchaseDraftPreviewRequest
        {
            ExecutionRequestId = viewModel.ExecutionRequestId,
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
            Items = viewModel.Items.Select(item => item.ToDto()).ToList(),
        }, "OLD-SKU")));
        await olderTask;

        Assert.AreEqual("NEW-SKU", viewModel.PreviewLines.Single().Sku);
    }

    [TestMethod]
    public async Task PendingCreate_ShouldBlockDialogCloseUntilAppliedResultIsObserved()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<ServiceResult<CreatePurchaseDraftsResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        api.CreateHandler = (_, _) =>
        {
            started.SetResult();
            return completed.Task;
        };

        var createTask = viewModel.CreateCommand.ExecuteAsync(null);
        await started.Task;

        Assert.IsTrue(viewModel.IsCreateInProgress);
        Assert.IsFalse(viewModel.CanClose);

        completed.SetResult(api.CreateSucceededResult());
        await createTask;

        Assert.IsFalse(viewModel.IsCreateInProgress);
        Assert.IsTrue(viewModel.CanClose);
        Assert.IsTrue(viewModel.HasAppliedResults);
    }

    private static PurchaseDraftWorkflowViewModel CreateViewModel(NetDemandApiStub api) =>
        new(api, new DirectApiCallRunner(), new MessageServiceStub());

    private static PurchaseCandidateItemModel CreateRow(decimal remainingQty, Guid? sourceUid = null) => PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
    {
        Uid = Guid.NewGuid(),
        Version = 2,
        SourceType = "SO",
        SourceUid = sourceUid ?? Guid.NewGuid(),
        SourceNo = "SO-1001",
        Sku = "A-100",
        SupplyType = SupplyTypeEnums.采购,
        NetQty = remainingQty,
        SuggestedPurchaseSupplierId = Guid.NewGuid(),
    });

    private sealed class NetDemandApiStub : IPurchaseDraftApi
    {
        public Guid SourceUid { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public bool IncludeWarnings { get; set; }
        public bool IncludeSafeAppend { get; set; }
        public CreatePurchaseDraftsCommand? LastCreateCommand { get; private set; }
        public Func<PurchaseDraftPreviewRequest, CancellationToken, Task<ServiceResult<PurchaseDraftPreviewResponse>>>? PreviewHandler { get; set; }
        public Func<CreatePurchaseDraftsCommand, CancellationToken, Task<ServiceResult<CreatePurchaseDraftsResult>>>? CreateHandler { get; set; }

        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default)
        {
            return PreviewHandler?.Invoke(request, cancellationToken)
                ?? Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(CreatePreview(request)));
        }

        public PurchaseDraftPreviewResponse CreatePreview(PurchaseDraftPreviewRequest request, string sku = "A-100")
        {
            var item = request.Items.FirstOrDefault() ?? new PurchaseDraftPreviewItem
            {
                CommandItemId = Guid.NewGuid(),
                NetDemandId = Guid.NewGuid(),
                SupplierId = SupplierId,
                ExpectedVersion = 2,
                ExpectedRemainingQty = 10m,
                PurchaseQty = 10m,
            };
            var requestedTarget = request.Targets.FirstOrDefault();
            var response = new PurchaseDraftPreviewResponse
            {
                ExecutionRequestId = request.ExecutionRequestId,
                EntryMode = request.EntryMode,
                LineMode = request.LineMode,
                Currency = "CNY",
                PreviewToken = Guid.NewGuid().ToString("N"),
                RequiresConfirmation = IncludeWarnings,
            };
            var source = new PurchaseDraftPreviewSourceGroupDto
            {
                SourceType = "SO",
                SourceUid = SourceUid,
                SourceNo = "SO-1001",
                SourcePreviewToken = Guid.NewGuid().ToString("N"),
                CanCreate = true,
                DocumentCount = 1,
            };
            var document = new PurchaseDraftPreviewDocumentDto
            {
                SourceType = "SO",
                SourceUid = SourceUid,
                SourceNo = "SO-1001",
                SupplierId = SupplierId,
                SupplierCode = "S001",
                SupplierName = "供应商一",
                Currency = "CNY",
                TargetMode = requestedTarget?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
                ExistingPurchaseOrderId = requestedTarget?.ExistingPurchaseOrderId,
                ExpectedPurchaseOrderVersion = requestedTarget?.ExpectedPurchaseOrderVersion,
                SourceLineCount = 1,
                PreviewLineCount = 1,
                TotalQty = item.PurchaseQty,
            };
            if (IncludeSafeAppend)
            {
                document.SafeAppendCandidates.Add(new SafePurchaseDraftCandidateDto
                {
                    PurchaseOrderId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Version = 3,
                    OrderNo = "PO-DRAFT-1",
                    SourceType = "SO",
                    SourceUid = SourceUid,
                    SupplierId = SupplierId,
                });
            }
            document.Lines.Add(new PurchaseDraftPreviewLineDto
            {
                Sku = sku,
                PurchaseQty = item.PurchaseQty,
                Contributions =
                {
                    new PurchaseDraftSourceContributionDto
                    {
                        CommandItemId = item.CommandItemId,
                        NetDemandId = item.NetDemandId,
                        NetDemandVersion = item.ExpectedVersion,
                        SupplierId = item.SupplierId,
                        ExpectedRemainingQty = item.ExpectedRemainingQty,
                        PurchaseQty = item.PurchaseQty,
                    },
                },
            });
            source.Documents.Add(document);
            response.SourceGroups.Add(source);
            if (IncludeWarnings)
            {
                response.Warnings.Add(new PurchaseDraftWarningDto
                {
                    Code = PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates,
                    Message = "交期不同，合并后取最早交期。",
                    RequiresConfirmation = true,
                    SourceType = "SO",
                    SourceUid = SourceUid,
                });
                response.Warnings.Add(new PurchaseDraftWarningDto
                {
                    Code = PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining,
                    Message = "父件仍有关联客供件未转采购。",
                    RequiresConfirmation = true,
                    SourceType = "SO",
                    SourceUid = SourceUid,
                });
                response.Warnings.Add(new PurchaseDraftWarningDto
                {
                    Code = PurchaseDraftWarningCodes.PurchaseDataIncomplete,
                    Message = "采购价格待补充。",
                    RequiresConfirmation = false,
                    SourceType = "SO",
                    SourceUid = SourceUid,
                });
            }
            return response;
        }

        public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default)
        {
            LastCreateCommand = command;
            return CreateHandler?.Invoke(command, cancellationToken)
                ?? Task.FromResult(CreateSucceededResult());
        }

        public ServiceResult<CreatePurchaseDraftsResult> CreateSucceededResult()
        {
            return ServiceResult<CreatePurchaseDraftsResult>.Ok(new CreatePurchaseDraftsResult
            {
                ExecutionRequestId = LastCreateCommand?.ExecutionRequestId ?? Guid.NewGuid(),
                Sources =
                {
                    new CreatePurchaseDraftSourceResult
                    {
                        SourceType = "SO",
                        SourceUid = SourceUid,
                        Outcome = NetDemandCommandOutcomeEnum.Succeeded,
                        Message = "已创建",
                    },
                },
            });
        }

        public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(Guid uid, CancelPurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(Guid uid, PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(Guid uid, ReleasePurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }
}
