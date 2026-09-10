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

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(remainingQty: 10m)],
        });

        Assert.AreEqual(10m, viewModel.Items.Single().PurchaseQty);
        Assert.AreEqual(PurchaseDraftLineModeEnum.MergeCompatibleLines, viewModel.LineMode);
        Assert.IsFalse(viewModel.Items.Single().IsQuantityReadOnly);
        Assert.IsFalse(viewModel.IsStale);
        StringAssert.Contains(viewModel.PreviewSummaryText, "选中物料 1 项");
        StringAssert.Contains(viewModel.PreviewSummaryText, "采购行 1 → 1");
        var previewLine = viewModel.PreviewLines.Single();
        Assert.AreEqual("MODEL-A-100", previewLine.LinkSku);
        Assert.AreEqual("PCS", previewLine.Unit);
        Assert.AreEqual("products/a-100.png", previewLine.ImagePath);
        Assert.AreEqual("SO-1001", previewLine.Contributions.Single().SourceNo);

        viewModel.Items.Single().PurchaseQty = 6m;

        Assert.IsTrue(viewModel.IsStale);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "重新预览");
        StringAssert.Contains(viewModel.PreviewSummaryText, "已失效");

        viewModel.Items.Single().PurchaseQty = 11m;
        Assert.IsNotNull(viewModel.Items.Single().QuantityError);
        Assert.IsFalse(viewModel.PreviewCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task SourceOneClick_ShouldUseServerFullRemainingQtyAndKeepQuantityReadOnly()
    {
        var api = new NetDemandApiStub { IncludeSafeAppend = true };
        using var viewModel = CreateViewModel(api);

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
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
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
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
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
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
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
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
        await viewModel.CreateCommand.ExecuteAsync(null);
        Assert.HasCount(1, api.CreateCommands);

        completed.SetResult(api.CreateSucceededResult());
        await createTask;

        Assert.IsFalse(viewModel.IsCreateInProgress);
        Assert.IsTrue(viewModel.CanClose);
        Assert.IsTrue(viewModel.HasAppliedResults);
    }

    [TestMethod]
    public async Task UpdateSelection_ShouldPreserveUnchangedInputIdentityAndLineMode()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var retainedRow = CreateRow(10m, api.SourceUid);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [retainedRow],
        });
        var retainedItem = viewModel.Items.Single();
        retainedItem.PurchaseQty = 6m;
        Assert.AreEqual(PurchaseDraftLineModeEnum.MergeCompatibleLines, viewModel.LineMode);
        viewModel.LineMode = PurchaseDraftLineModeEnum.KeepSourceLines;
        var executionRequestId = viewModel.ExecutionRequestId;

        var addedRow = CreateRow(4m, api.SourceUid);
        await viewModel.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [retainedRow, addedRow],
        });

        Assert.AreSame(retainedItem, viewModel.Items.Single(item => item.NetDemandId == retainedRow.Uid));
        Assert.AreEqual(6m, retainedItem.PurchaseQty);
        Assert.AreEqual(executionRequestId, viewModel.ExecutionRequestId);
        Assert.AreEqual(PurchaseDraftLineModeEnum.KeepSourceLines, viewModel.LineMode);
        Assert.AreEqual(4m, viewModel.Items.Single(item => item.NetDemandId == addedRow.Uid).PurchaseQty);

        await viewModel.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [retainedRow],
        });

        Assert.AreSame(retainedItem, viewModel.Items.Single());
        Assert.AreEqual(6m, retainedItem.PurchaseQty);

        var changedFacts = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = retainedRow.Uid,
            Version = retainedRow.Version + 1,
            SourceType = retainedRow.SourceType,
            SourceUid = retainedRow.SourceUid,
            SourceNo = retainedRow.SourceNo,
            Sku = retainedRow.Sku,
            NetQty = 9m,
            SuggestedPurchaseSupplierId = retainedRow.SuggestedPurchaseSupplierId,
            SuggestedDeliverySupplierId = retainedRow.SuggestedDeliverySupplierId,
        });
        await viewModel.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [changedFacts],
        });

        Assert.AreNotSame(retainedItem, viewModel.Items.Single());
        Assert.AreNotEqual(retainedItem.CommandItemId, viewModel.Items.Single().CommandItemId);
        Assert.AreEqual(9m, viewModel.Items.Single().PurchaseQty);
        StringAssert.Contains(viewModel.StatusText, "版本或剩余额度");
    }

    [TestMethod]
    public async Task InputChangeDuringPreview_ShouldRejectLateSuccessAndClearConfirmation()
    {
        var api = new NetDemandApiStub { IncludeWarnings = true };
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        foreach (var warning in viewModel.Warnings.Where(item => item.RequiresConfirmation))
            warning.IsConfirmed = true;

        var completion = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PurchaseDraftPreviewRequest? captured = null;
        api.PreviewHandler = (request, _) =>
        {
            captured = request;
            return completion.Task;
        };
        var pending = viewModel.PreviewCommand.ExecuteAsync(null);
        await Task.Yield();

        viewModel.Items.Single().PurchaseQty = 7m;
        completion.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(captured!, "LATE-SKU")));
        await pending;

        Assert.IsTrue(viewModel.IsStale);
        Assert.IsFalse(viewModel.Warnings.Any(item => item.IsConfirmed));
        Assert.IsFalse(viewModel.PreviewLines.Any(item => item.Sku == "LATE-SKU"));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task InputChangeDuringPreview_ShouldRejectLateFailureStatus()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        var completion = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        api.PreviewHandler = (_, _) => completion.Task;
        var pending = viewModel.PreviewCommand.ExecuteAsync(null);
        await Task.Yield();

        viewModel.Items.Single().PurchaseQty = 7m;
        completion.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Fail("旧请求失败"));
        await pending;

        StringAssert.Contains(viewModel.StatusText, "重新预览");
        Assert.AreNotEqual("旧请求失败", viewModel.StatusText);
        Assert.IsTrue(viewModel.IsStale);
    }

    [TestMethod]
    public async Task PartialSuccess_ShouldRetryOnlyFailedSourceWithStableIdentityAndKeepCumulativeResults()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var sourceA = api.SourceUid;
        var sourceB = Guid.NewGuid();
        var rowA = CreateRow(10m, sourceA);
        var rowB = CreateRow(8m, sourceB);
        var sourceByDemand = new Dictionary<Guid, Guid>
        {
            [rowA.Uid] = sourceA,
            [rowB.Uid] = sourceB,
        };
        api.PreviewHandler = (request, _) => Task.FromResult(
            ServiceResult<PurchaseDraftPreviewResponse>.Ok(CreateMultiSourcePreview(api, request, sourceByDemand)));
        var createCall = 0;
        api.CreateHandler = (command, _) => Task.FromResult(++createCall == 1
            ? CreateResult(command,
                (sourceA, NetDemandCommandOutcomeEnum.Succeeded),
                (sourceB, NetDemandCommandOutcomeEnum.Failed))
            : CreateResult(command, (sourceB, NetDemandCommandOutcomeEnum.Succeeded)));

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [rowA, rowB],
        });
        await viewModel.CreateCommand.ExecuteAsync(null);
        var firstCommand = api.CreateCommands[0];
        var failedItem = firstCommand.Sources.Single(source => source.SourceUid == sourceB).Items.Single();

        Assert.IsTrue(viewModel.HasAppliedResults);
        Assert.HasCount(2, viewModel.Results);
        Assert.AreEqual(sourceB, viewModel.Items.Single().SourceUid);

        await viewModel.PreviewCommand.ExecuteAsync(null);
        await viewModel.CreateCommand.ExecuteAsync(null);
        var retry = api.CreateCommands[1];

        Assert.HasCount(1, retry.Sources);
        Assert.AreEqual(sourceB, retry.Sources.Single().SourceUid);
        Assert.AreEqual(firstCommand.ExecutionRequestId, retry.ExecutionRequestId);
        Assert.AreEqual(failedItem.CommandItemId, retry.Sources.Single().Items.Single().CommandItemId);
        Assert.IsTrue(viewModel.HasAppliedResults);
        Assert.IsTrue(viewModel.Results.All(item => item.Outcome == NetDemandCommandOutcomeEnum.Succeeded));
    }

    [TestMethod]
    public async Task AppliedResults_ShouldSurviveSelectionUpdateAndLaterPreviewFailure()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        await viewModel.CreateCommand.ExecuteAsync(null);
        var appliedResult = viewModel.Results.Single();
        var nextSource = Guid.NewGuid();
        api.PreviewHandler = (request, _) => Task.FromResult(
            ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(request, sourceUid: nextSource)));

        await viewModel.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(5m, nextSource)],
        });
        api.PreviewHandler = (_, _) => Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Fail("预览失败"));
        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasAppliedResults);
        Assert.AreSame(appliedResult, viewModel.Results.Single());
        Assert.AreEqual("预览失败", viewModel.StatusText);
    }

    [TestMethod]
    public async Task UnknownCreateResult_ShouldRetryFrozenCommandWithoutChangingIdentityOrPayload()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api, allowErrors: true);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        var call = 0;
        api.CreateHandler = (command, _) => Task.FromResult(++call == 1
            ? ServiceResult<CreatePurchaseDraftsResult>.Fail("网络中断")
            : CreateResult(command, (api.SourceUid, NetDemandCommandOutcomeEnum.AlreadyApplied)));

        await viewModel.CreateCommand.ExecuteAsync(null);
        var frozen = api.CreateCommands[0];
        viewModel.Items.Single().PurchaseQty = 3m;

        Assert.IsTrue(viewModel.HasUnknownCreateResult);
        Assert.IsFalse(viewModel.CanClose);
        Assert.IsTrue(viewModel.CreateCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PreviewCommand.CanExecute(null));

        await viewModel.CreateCommand.ExecuteAsync(null);
        var replay = api.CreateCommands[1];

        Assert.AreSame(frozen, replay);
        Assert.AreEqual(10m, replay.Sources.Single().Items.Single().PurchaseQty);
        Assert.AreEqual(frozen.ExecutionRequestId, replay.ExecutionRequestId);
        Assert.IsFalse(viewModel.HasUnknownCreateResult);
        Assert.IsTrue(viewModel.CanClose);
        Assert.IsTrue(viewModel.HasAppliedResults);
    }

    [TestMethod]
    public async Task ClosedSessionPreview_ShouldNotWriteIntoReopenedSession()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        var oldCompletion = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        PurchaseDraftPreviewRequest? oldRequest = null;
        api.PreviewHandler = (request, _) =>
        {
            if (++call == 1)
            {
                oldRequest = request;
                return oldCompletion.Task;
            }
            return Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(request, "NEW-SESSION")));
        };
        var oldTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await Task.Yield();

        viewModel.Close();
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(5m, api.SourceUid)],
        });
        oldCompletion.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(oldRequest!, "OLD-SESSION")));
        await oldTask;

        Assert.AreEqual("NEW-SESSION", viewModel.PreviewLines.Single().Sku);
    }

    private static PurchaseDraftWorkflowViewModel CreateViewModel(NetDemandApiStub api, bool allowErrors = false) =>
        new(api, new DirectApiCallRunner(), new MessageServiceStub { AllowErrors = allowErrors });

    private static Task OpenSessionAsync(
        PurchaseDraftWorkflowViewModel viewModel,
        PurchaseDraftPreviewOpenRequest request)
    {
        viewModel.BeginSession();
        return viewModel.UpdateSelectionAsync(request);
    }

    private static PurchaseDraftPreviewResponse CreateMultiSourcePreview(
        NetDemandApiStub api,
        PurchaseDraftPreviewRequest request,
        IReadOnlyDictionary<Guid, Guid> sourceByDemand)
    {
        var combined = new PurchaseDraftPreviewResponse
        {
            ExecutionRequestId = request.ExecutionRequestId,
            EntryMode = request.EntryMode,
            LineMode = request.LineMode,
            PreviewToken = Guid.NewGuid().ToString("N"),
            Currency = "CNY",
        };
        foreach (var item in request.Items)
        {
            var singleRequest = new PurchaseDraftPreviewRequest
            {
                ExecutionRequestId = request.ExecutionRequestId,
                EntryMode = request.EntryMode,
                LineMode = request.LineMode,
                Items = [item],
                Targets = request.Targets,
            };
            var source = api.CreatePreview(singleRequest, sourceUid: sourceByDemand[item.NetDemandId]);
            combined.SourceGroups.AddRange(source.SourceGroups);
        }
        return combined;
    }

    private static ServiceResult<CreatePurchaseDraftsResult> CreateResult(
        CreatePurchaseDraftsCommand command,
        params (Guid SourceUid, NetDemandCommandOutcomeEnum Outcome)[] sources) =>
        ServiceResult<CreatePurchaseDraftsResult>.Ok(new CreatePurchaseDraftsResult
        {
            ExecutionRequestId = command.ExecutionRequestId,
            Sources = sources.Select(source => new CreatePurchaseDraftSourceResult
            {
                SourceType = "SO",
                SourceUid = source.SourceUid,
                Outcome = source.Outcome,
                Message = source.Outcome == NetDemandCommandOutcomeEnum.Failed ? "失败" : "已执行",
            }).ToList(),
        });

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
        public List<CreatePurchaseDraftsCommand> CreateCommands { get; } = [];
        public Func<PurchaseDraftPreviewRequest, CancellationToken, Task<ServiceResult<PurchaseDraftPreviewResponse>>>? PreviewHandler { get; set; }
        public Func<CreatePurchaseDraftsCommand, CancellationToken, Task<ServiceResult<CreatePurchaseDraftsResult>>>? CreateHandler { get; set; }

        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default)
        {
            return PreviewHandler?.Invoke(request, cancellationToken)
                ?? Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(CreatePreview(request)));
        }

        public PurchaseDraftPreviewResponse CreatePreview(
            PurchaseDraftPreviewRequest request,
            string sku = "A-100",
            Guid? sourceUid = null)
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
                SourceUid = sourceUid ?? SourceUid,
                SourceNo = "SO-1001",
                SourcePreviewToken = Guid.NewGuid().ToString("N"),
                CanCreate = true,
                DocumentCount = 1,
            };
            var document = new PurchaseDraftPreviewDocumentDto
            {
                SourceType = "SO",
                SourceUid = sourceUid ?? SourceUid,
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
                    SourceUid = sourceUid ?? SourceUid,
                    SupplierId = SupplierId,
                });
            }
            document.Lines.Add(new PurchaseDraftPreviewLineDto
            {
                Sku = sku,
                LinkSku = "MODEL-A-100",
                Description = "测试采购物料",
                ImagePath = "products/a-100.png",
                MaterialType = "成品",
                Unit = "PCS",
                SupplierSku = "SUP-A-100",
                UnitPrice = 12m,
                TaxPrice = 13.56m,
                PurchaseQty = item.PurchaseQty,
                Contributions =
                {
                    new PurchaseDraftSourceContributionDto
                    {
                        CommandItemId = item.CommandItemId,
                        NetDemandId = item.NetDemandId,
                        NetDemandVersion = item.ExpectedVersion,
                        SupplierId = item.SupplierId,
                        SourceType = "SO",
                        SourceNo = "SO-1001",
                        SourceLineNumber = 1,
                        Sku = sku,
                        LinkSku = "MODEL-A-100",
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
            CreateCommands.Add(command);
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
        public bool AllowErrors { get; init; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message)
        {
            if (!AllowErrors) Assert.Fail($"{title}: {message}");
        }
    }
}
