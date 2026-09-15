using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
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
        Assert.AreEqual("products/source-a-100.png", previewLine.Contributions.Single().ImagePath);
        StringAssert.Contains(previewLine.SourceSupplierGroupLabel, "SO-1001");
        StringAssert.Contains(previewLine.SourceSupplierGroupLabel, "供应商一");
        Assert.AreEqual("CUSTOMER-PO", viewModel.Documents.Single().Header.CustomerPoNo);
        Assert.AreEqual("CINV-S", viewModel.Documents.Single().Header.BoNo);
        Assert.AreEqual("创建时按现有规则生成", viewModel.Documents.Single().Header.OrderNoDisplay);
        Assert.AreEqual(120m, viewModel.Documents.Single().Header.UntaxedTotalAmount);
        Assert.AreEqual(135.6m, viewModel.Documents.Single().Header.TaxIncludedTotalAmount);

        viewModel.Items.Single().PurchaseQty = 6m;

        Assert.IsTrue(viewModel.IsStale);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "返回采购准备后重新校验");
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
        Assert.IsFalse(viewModel.Targets.Single().HasMultipleChoices);
        Assert.AreEqual("新建采购草稿", viewModel.Targets.Single().SelectedChoiceDisplayText);
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
        StringAssert.Contains(viewModel.ProblemSummaryText, "待确认 2 项");
        StringAssert.Contains(viewModel.ProblemSummaryText, "资料待补 1 项");
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));

        var target = viewModel.Targets.Single();
        Assert.IsTrue(target.HasMultipleChoices);
        target.SelectedChoice = target.Choices.Single(choice => choice.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting);
        Assert.IsTrue(viewModel.IsStale);
        Assert.HasCount(0, viewModel.Warnings);
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

    /// <summary>预览成功时 Busy 释放必须再次通知实际按钮命令，并让创建、返回观察到最终可用状态。</summary>
    [TestMethod]
    public async Task PreviewSuccess_ShouldNotifyFinalButtonCommandStatesAfterBusyRelease()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        viewModel.Items.Single().PurchaseQty = 9m;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PurchaseDraftPreviewRequest? pendingRequest = null;
        api.PreviewHandler = (request, _) =>
        {
            pendingRequest = request;
            started.SetResult();
            return completed.Task;
        };
        var returnStates = new List<bool>();
        var createStates = new List<bool>();
        viewModel.ReturnToPreparationCommand.CanExecuteChanged += (_, _) =>
            returnStates.Add(viewModel.ReturnToPreparationCommand.CanExecute(null));
        viewModel.CreateCommand.CanExecuteChanged += (_, _) =>
            createStates.Add(viewModel.CreateCommand.CanExecute(null));

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await started.Task;
        Assert.IsFalse(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        completed.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(api.CreatePreview(pendingRequest!)));
        await previewTask;

        Assert.IsTrue(returnStates.Count > 0);
        Assert.IsTrue(createStates.Count > 0);
        Assert.IsTrue(returnStates[^1]);
        Assert.IsTrue(createStates[^1]);
    }

    /// <summary>预览失败时 Busy 释放必须重新启用返回命令，同时创建命令保持最终禁用。</summary>
    [TestMethod]
    public async Task PreviewFailure_ShouldNotifyFinalButtonCommandStatesAfterBusyRelease()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        viewModel.Items.Single().PurchaseQty = 9m;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<ServiceResult<PurchaseDraftPreviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        api.PreviewHandler = (_, _) =>
        {
            started.SetResult();
            return completed.Task;
        };
        var returnStates = new List<bool>();
        var createStates = new List<bool>();
        viewModel.ReturnToPreparationCommand.CanExecuteChanged += (_, _) =>
            returnStates.Add(viewModel.ReturnToPreparationCommand.CanExecute(null));
        viewModel.CreateCommand.CanExecuteChanged += (_, _) =>
            createStates.Add(viewModel.CreateCommand.CanExecute(null));

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await started.Task;
        completed.SetResult(ServiceResult<PurchaseDraftPreviewResponse>.Fail(
            PurchaseDraftErrorCodes.PreviewFactChanged,
            "需求事实已变化。"));
        await previewTask;

        Assert.IsTrue(returnStates.Count > 0);
        Assert.IsTrue(createStates.Count > 0);
        Assert.IsTrue(returnStates[^1]);
        Assert.IsFalse(createStates[^1]);
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
        Assert.HasCount(0, viewModel.Warnings);
        Assert.HasCount(0, viewModel.PreviewLines);
        StringAssert.Contains(viewModel.ProblemSummaryText, "采购审核已失效");
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

        StringAssert.Contains(viewModel.StatusText, "返回采购准备后重新校验");
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
        Assert.IsTrue(viewModel.Results.All(result => result.SourceNo == "SO-1001"));
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
        Assert.IsTrue(viewModel.Results.All(item => item.OutcomeText == "创建成功"));
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
        var returnStates = new List<bool>();
        var createStates = new List<bool>();
        viewModel.ReturnToPreparationCommand.CanExecuteChanged += (_, _) =>
            returnStates.Add(viewModel.ReturnToPreparationCommand.CanExecute(null));
        viewModel.CreateCommand.CanExecuteChanged += (_, _) =>
            createStates.Add(viewModel.CreateCommand.CanExecute(null));

        await viewModel.CreateCommand.ExecuteAsync(null);
        var frozen = api.CreateCommands[0];
        viewModel.Items.Single().PurchaseQty = 3m;

        Assert.IsTrue(viewModel.HasUnknownCreateResult);
        Assert.IsFalse(viewModel.CanClose);
        Assert.IsTrue(viewModel.CreateCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PreviewCommand.CanExecute(null));
        Assert.IsTrue(returnStates.Count > 0);
        Assert.IsTrue(createStates.Count > 0);
        Assert.IsFalse(returnStates[^1]);
        Assert.IsTrue(createStates[^1]);

        await viewModel.CreateCommand.ExecuteAsync(null);
        var replay = api.CreateCommands[1];

        Assert.AreSame(frozen, replay);
        Assert.AreEqual(10m, replay.Sources.Single().Items.Single().PurchaseQty);
        Assert.AreEqual(frozen.ExecutionRequestId, replay.ExecutionRequestId);
        Assert.IsFalse(viewModel.HasUnknownCreateResult);
        Assert.IsTrue(viewModel.CanClose);
        Assert.IsTrue(viewModel.HasAppliedResults);
    }

    /// <summary>未识别创建异常按未知结果冻结原命令；Busy 释放后只重新通知并开放原命令重试。</summary>
    [TestMethod]
    public async Task CreateException_ShouldNotifyFrozenRetryGateAfterBusyRelease()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        api.CreateHandler = (_, _) => Task.FromException<ServiceResult<CreatePurchaseDraftsResult>>(
            new InvalidOperationException("未识别创建异常"));
        var returnStates = new List<bool>();
        var createStates = new List<bool>();
        viewModel.ReturnToPreparationCommand.CanExecuteChanged += (_, _) =>
            returnStates.Add(viewModel.ReturnToPreparationCommand.CanExecute(null));
        viewModel.CreateCommand.CanExecuteChanged += (_, _) =>
            createStates.Add(viewModel.CreateCommand.CanExecute(null));

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasUnknownCreateResult);
        Assert.IsFalse(viewModel.CanClose);
        Assert.IsTrue(returnStates.Count > 0);
        Assert.IsTrue(createStates.Count > 0);
        Assert.IsFalse(returnStates[^1]);
        Assert.IsTrue(createStates[^1]);
        Assert.AreEqual("未识别创建异常", viewModel.ValidationSummary);
    }

    /// <summary>结构化业务失败是确定失败，不得误冻结为未知结果；审核失效后允许返回准备修正。</summary>
    [TestMethod]
    public async Task DefiniteCreateFailure_ShouldUnlockReturnWithoutUnknownRetryState()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        api.CreateHandler = (_, _) => Task.FromResult(
            ServiceResult<CreatePurchaseDraftsResult>.Fail("INVALID_COMMAND", "采购输入无效。"));

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.HasUnknownCreateResult);
        Assert.IsTrue(viewModel.CanClose);
        Assert.IsTrue(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "返回采购准备修正后重新校验");
    }

    /// <summary>创建阶段的结构化事实冲突不自动查询候选，并保留返回准备显式刷新的安全路径。</summary>
    [TestMethod]
    public async Task CreateConflict_ShouldStayInvalidUntilPreparationRefresh()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(10m, api.SourceUid)],
        });
        api.CreateHandler = (_, _) => Task.FromResult(
            ServiceResult<CreatePurchaseDraftsResult>.Fail(
                PurchaseDraftErrorCodes.PreviewExpired,
                "来源事实已变化。"));
        var candidateQueriesBefore = api.CandidateQueries.Count;
        var returnStates = new List<bool>();
        var createStates = new List<bool>();
        viewModel.ReturnToPreparationCommand.CanExecuteChanged += (_, _) =>
            returnStates.Add(viewModel.ReturnToPreparationCommand.CanExecute(null));
        viewModel.CreateCommand.CanExecuteChanged += (_, _) =>
            createStates.Add(viewModel.CreateCommand.CanExecute(null));

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.AreEqual(candidateQueriesBefore, api.CandidateQueries.Count);
        Assert.IsFalse(viewModel.HasUnknownCreateResult);
        Assert.IsTrue(viewModel.IsStale);
        Assert.IsTrue(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        Assert.IsTrue(returnStates.Count > 0);
        Assert.IsTrue(createStates.Count > 0);
        Assert.IsTrue(returnStates[^1]);
        Assert.IsFalse(createStates[^1]);
        Assert.AreEqual(
            "待采购数据已变化，请返回采购准备，刷新待采购数据后重新校验。",
            viewModel.StatusText);
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

    [TestMethod]
    public async Task SupplierSelections_ShouldRemainIndependentAndRequireExplicitDeliveryWhenParentHasMultipleActualSuppliers()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var suggestedDeliverySupplierId = Guid.NewGuid();
        var firstActual = new PurchaseDraftActualSupplierFactDto
        {
            SupplierId = Guid.NewGuid(),
            SupplierCode = "ACT-1",
            SupplierName = "实际供应商一",
            IsActive = true,
            PurchaseOrderNos = "PO-001",
        };
        var secondActual = new PurchaseDraftActualSupplierFactDto
        {
            SupplierId = Guid.NewGuid(),
            SupplierCode = "ACT-2",
            SupplierName = "实际供应商二",
            IsActive = true,
            PurchaseOrderNos = "PO-002",
        };
        var row = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(),
            Version = 2,
            SourceType = "SO",
            SourceUid = api.SourceUid,
            SourceNo = "SO-1001",
            Sku = "CS-100",
            LinkSku = "CUSTOMER-SUPPLIED",
            IsCustomerSupplied = true,
            SupplyType = SupplyTypeEnums.采购,
            NetQty = 5m,
            SuggestedDeliverySupplierId = suggestedDeliverySupplierId,
            SuggestedDeliverySupplierCode = "SUG-DELIVERY",
            SuggestedDeliverySupplierName = "建议送达供应商",
            ParentActualPurchaseSuppliers = { firstActual, secondActual },
        });

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [row],
        });
        var item = viewModel.Items.Single();

        Assert.AreEqual(Guid.Empty, item.SupplierId);
        Assert.IsNull(item.DeliveryTargetSupplierId);
        Assert.IsTrue(item.RequiresExplicitDeliverySelection);
        StringAssert.Contains(item.ParentActualSupplierEvidence, "PO-001");
        StringAssert.Contains(item.ParentActualSupplierEvidence, "PO-002");
        Assert.IsNull(api.LastPreviewRequest!.Items.Single().DeliveryTargetSupplierId);

        item.PurchaseSupplierSelection = new ProcurementSupplierCandidateDto
        {
            Uid = firstActual.SupplierId,
            SupplierCode = firstActual.SupplierCode,
            SupplierName = firstActual.SupplierName,
        };
        viewModel.CommitPurchaseSupplierCommand.Execute(item);
        Assert.IsTrue(viewModel.IsStale);
        Assert.AreEqual(firstActual.SupplierId, item.SupplierId);
        Assert.IsNull(item.DeliveryTargetSupplierId);

        await viewModel.PreviewCommand.ExecuteAsync(null);
        item.DeliverySupplierSelection = new ProcurementSupplierCandidateDto
        {
            Uid = secondActual.SupplierId,
            SupplierCode = secondActual.SupplierCode,
            SupplierName = secondActual.SupplierName,
        };
        viewModel.CommitDeliverySupplierCommand.Execute(item);
        Assert.IsTrue(viewModel.IsStale);
        Assert.AreEqual(firstActual.SupplierId, item.SupplierId);
        Assert.AreEqual(secondActual.SupplierId, item.DeliveryTargetSupplierId);

        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.AreEqual(firstActual.SupplierId, api.LastPreviewRequest!.Items.Single().SupplierId);
        Assert.AreEqual(secondActual.SupplierId, api.LastPreviewRequest.Items.Single().DeliveryTargetSupplierId);

        viewModel.ClearPurchaseSupplierCommand.Execute(item);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.AreEqual(Guid.Empty, api.LastPreviewRequest!.Items.Single().SupplierId);
    }

    [TestMethod]
    public async Task RecommendedDeliverySupplier_ShouldNotBeOverriddenByParentPurchaseEvidence()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var suggestedId = Guid.NewGuid();
        var actualId = Guid.NewGuid();
        var row = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(),
            Version = 2,
            SourceType = "SO",
            SourceUid = api.SourceUid,
            SourceNo = "SO-1001",
            Sku = "CS-200",
            LinkSku = "CUSTOMER-SUPPLIED-2",
            IsCustomerSupplied = true,
            SupplyType = SupplyTypeEnums.采购,
            NetQty = 5m,
            SuggestedPurchaseSupplierId = Guid.NewGuid(),
            SuggestedDeliverySupplierId = suggestedId,
            SuggestedDeliverySupplierCode = "SUG",
            SuggestedDeliverySupplierName = "建议供应商",
            ParentActualPurchaseSuppliers =
            {
                new PurchaseDraftActualSupplierFactDto
                {
                    SupplierId = actualId,
                    SupplierCode = "ACT",
                    SupplierName = "实际供应商",
                    IsActive = true,
                    PurchaseOrderNos = "PO-ACTUAL",
                },
            },
        });

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [row],
        });

        Assert.AreEqual(suggestedId, viewModel.Items.Single().DeliveryTargetSupplierId);
        Assert.AreNotEqual(actualId, viewModel.Items.Single().DeliveryTargetSupplierId);
        StringAssert.Contains(viewModel.Items.Single().ParentActualSupplierEvidence, "PO-ACTUAL");
    }

    [TestMethod]
    public async Task RebuildWarnings_ShouldMergeAllAffectedIdentitiesBySourceAndCode()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var first = CreateRow(5m, api.SourceUid);
        var second = CreateRow(6m, api.SourceUid);
        var firstSupplier = Guid.NewGuid();
        var secondSupplier = Guid.NewGuid();
        api.PreviewHandler = (request, _) =>
        {
            var response = api.CreatePreview(request);
            response.HasBlockers = true;
            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.PurchaseSupplierInvalid,
                Message = "供应商一已失效。",
                IsBlocker = true,
                SourceType = "SO",
                SourceUid = api.SourceUid,
                SupplierId = firstSupplier,
                NetDemandIds = { first.Uid },
            });
            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.PurchaseSupplierInvalid,
                Message = "供应商二已失效。",
                IsBlocker = true,
                SourceType = "SO",
                SourceUid = api.SourceUid,
                SupplierId = secondSupplier,
                NetDemandIds = { second.Uid },
            });
            return Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(response));
        };

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [first, second],
        });

        Assert.HasCount(1, viewModel.Warnings);
        Assert.AreEqual(2, viewModel.Warnings.Single().NetDemandIds.Count);
        Assert.AreEqual(2, viewModel.Warnings.Single().SupplierIds.Count);
        Assert.AreEqual(2, viewModel.Warnings.Single().AffectedItems.Count);
        Assert.AreEqual(2, viewModel.BlockerCount);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));

        viewModel.LocateWarningCommand.Execute(viewModel.Warnings.Single());
        Assert.IsNotNull(viewModel.SelectedInputItem);
    }

    [TestMethod]
    public async Task RelatedWarning_ShouldDisplayUnselectedAffectedCustomerSuppliedDemand()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var parent = CreateRow(5m, api.SourceUid);
        var relatedChildId = Guid.NewGuid();
        api.PreviewHandler = (request, _) =>
        {
            var response = api.CreatePreview(request);
            response.RequiresConfirmation = true;
            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining,
                Message = "仍有关联客供件未转采购。",
                RequiresConfirmation = true,
                SourceType = "SO",
                SourceUid = api.SourceUid,
                NetDemandIds = { parent.Uid, relatedChildId },
                AffectedObjects =
                {
                    new PurchaseDraftWarningAffectedObjectDto
                    {
                        NetDemandId = parent.Uid,
                        SourceNo = parent.SourceNo,
                        SourceLineNumber = 1,
                        Sku = parent.Sku,
                        LinkSku = parent.LinkSku,
                        IsIncludedInCurrentPurchase = true,
                    },
                    new PurchaseDraftWarningAffectedObjectDto
                    {
                        NetDemandId = relatedChildId,
                        SourceNo = parent.SourceNo,
                        SourceLineNumber = 2,
                        Sku = "CS-CHILD",
                        LinkSku = "MODEL-CS-CHILD",
                        IsIncludedInCurrentPurchase = false,
                        RemainingAfterSelectionQty = 3m,
                    },
                },
            });
            return Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(response));
        };

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [parent],
        });

        var warning = viewModel.Warnings.Single();
        Assert.AreEqual(2, warning.AffectedCount);
        Assert.HasCount(1, warning.AffectedItems);
        StringAssert.Contains(warning.AffectedObjectText, "关联未选");
        StringAssert.Contains(warning.AffectedObjectText, "MODEL-CS-CHILD");
        StringAssert.Contains(warning.AffectedObjectText, "3.0000");
        Assert.IsTrue(warning.CanLocate);
    }

    /// <summary>准备页必须展示关联客供件真实确认内容，并在确认后直接解锁进入审核而非停留在失效提示。</summary>
    [TestMethod]
    public async Task PreparationRelatedWarning_ShouldShowAffectedFactsAndUnlockAfterConfirmation()
    {
        var api = new NetDemandApiStub();
        using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        using var preview = CreateViewModel(api);
        picker.AttachWorkflow(preview);
        preview.BeginSession();
        var parentDto = new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(), Version = 2, SourceType = "SO", SourceUid = api.SourceUid,
            SourceNo = "SO-1001", SourceLineNumber = 1, Sku = "PARENT-SKU", LinkSku = "PARENT-MODEL",
            SupplyType = SupplyTypeEnums.采购, NetQty = 5m,
            SuggestedPurchaseSupplierId = api.SupplierId,
            SuggestedPurchaseSupplierCode = "S001",
            SuggestedPurchaseSupplierName = "供应商一",
        };
        var relatedChildDto = new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(), Version = 2, SourceType = "SO", SourceUid = api.SourceUid,
            SourceNo = "SO-1001", SourceLineNumber = 2, Sku = "CS-CHILD", LinkSku = "MODEL-CS-CHILD",
            SupplyType = SupplyTypeEnums.采购, NetQty = 3m, IsCustomerSupplied = true,
            DeliveryTargetNetDemandId = parentDto.Uid,
            SuggestedPurchaseSupplierId = Guid.NewGuid(),
            SuggestedPurchaseSupplierCode = "S002",
            SuggestedPurchaseSupplierName = "客供采购方",
            SuggestedDeliverySupplierId = api.SupplierId,
            SuggestedDeliverySupplierCode = "S001",
            SuggestedDeliverySupplierName = "供应商一",
        };
        api.CandidateGroups.Add(new PurchaseDraftCandidateSourceGroupDto
        {
            SourceType = "SO", SourceUid = api.SourceUid, SourceNo = "SO-1001", CandidateCount = 2,
        });
        api.Candidates.AddRange([parentDto, relatedChildDto]);
        await picker.OpenAsync();
        await picker.SelectSourceGroupCommand.ExecuteAsync(picker.SourceGroups[1]);
        var parent = picker.Items.Single(item => item.Uid == parentDto.Uid);
        var relatedChild = picker.Items.Single(item => item.Uid == relatedChildDto.Uid);
        parent.IsChecked = true;
        Assert.IsTrue(relatedChild.IsChecked);
        relatedChild.IsChecked = false;
        api.PreviewHandler = (request, _) =>
        {
            var response = api.CreatePreview(request);
            response.RequiresConfirmation = true;
            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining,
                Message = "父件仍有关联客供件未转采购。",
                RequiresConfirmation = true,
                SourceType = "SO",
                SourceUid = api.SourceUid,
                NetDemandIds = { parent.Uid, relatedChild.Uid },
                AffectedObjects =
                {
                    new PurchaseDraftWarningAffectedObjectDto
                    {
                        NetDemandId = parent.Uid,
                        SourceNo = parent.SourceNo,
                        SourceLineNumber = 1,
                        Sku = parent.Sku,
                        LinkSku = parent.LinkSku,
                        IsIncludedInCurrentPurchase = true,
                    },
                    new PurchaseDraftWarningAffectedObjectDto
                    {
                        NetDemandId = relatedChild.Uid,
                        SourceNo = parent.SourceNo,
                        SourceLineNumber = 2,
                        Sku = "CS-CHILD",
                        LinkSku = "MODEL-CS-CHILD",
                        IsIncludedInCurrentPurchase = false,
                        RemainingAfterSelectionQty = 3m,
                    },
                },
            });
            return Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(response));
        };
        var previewRequested = 0;
        picker.PreviewRequested += (_, _) => previewRequested++;

        await picker.ValidateSelectionCommand.ExecuteAsync(null);

        Assert.IsFalse(preview.IsStale);
        Assert.IsTrue(picker.HasValidatedSelection);
        Assert.IsFalse(picker.EnterPreviewCommand.CanExecute(null));
        Assert.IsFalse(picker.SelectionValidationText.Contains("预览已失效", StringComparison.Ordinal));
        StringAssert.Contains(picker.SelectionValidationText, "已选父件仍有关联客供件未选");
        StringAssert.Contains(picker.SelectionValidationText, "MODEL-CS-CHILD");
        StringAssert.Contains(picker.SelectionValidationText, "3.0000");

        preview.Warnings.Single().IsConfirmed = true;

        Assert.IsTrue(preview.HasValidPreparation);
        Assert.IsTrue(picker.EnterPreviewCommand.CanExecute(null));
        StringAssert.Contains(picker.SelectionValidationText, "可以进入采购审核");
        picker.EnterPreviewCommand.Execute(null);
        Assert.AreEqual(1, previewRequested);

        relatedChild.IsChecked = true;
        Assert.IsTrue(preview.IsStale);
        Assert.HasCount(0, preview.Warnings);
        Assert.IsFalse(picker.EnterPreviewCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task AggregatedConfirmation_ShouldSendOneSourceScopedWarningCode()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        var first = CreateRow(5m, api.SourceUid);
        var second = CreateRow(6m, api.SourceUid);
        api.PreviewHandler = (request, _) =>
        {
            var response = api.CreatePreview(request);
            response.RequiresConfirmation = true;
            foreach (var row in new[] { first, second })
            {
                response.Warnings.Add(new PurchaseDraftWarningDto
                {
                    Code = PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining,
                    Message = $"{row.Sku} 仍有关联客供件未转采购。",
                    RequiresConfirmation = true,
                    SourceType = "SO",
                    SourceUid = api.SourceUid,
                    NetDemandIds = { row.Uid },
                });
            }
            return Task.FromResult(ServiceResult<PurchaseDraftPreviewResponse>.Ok(response));
        };

        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [first, second],
        });

        Assert.HasCount(1, viewModel.Warnings);
        Assert.AreEqual(2, viewModel.Warnings.Single().AffectedCount);
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        viewModel.Warnings.Single().IsConfirmed = true;
        Assert.IsTrue(viewModel.CreateCommand.CanExecute(null));

        await viewModel.CreateCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(
            new[] { PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining },
            api.LastCreateCommand!.Sources.Single().ConfirmedWarningCodes);
    }

    /// <summary>原生 Cell 提交失败时不发送旧值；修正后同一入口可重新校验并进入审核。</summary>
    [TestMethod]
    public async Task Preparation_ShouldRecoverAfterPendingCellCommitFailure()
    {
        var api = new NetDemandApiStub();
        using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        using var preview = CreateViewModel(api);
        using var viewModel = new PurchaseOrderFromMaterialViewModel(picker, preview);
        preview.BeginSession();
        var row = CreateRow(5m, api.SourceUid);
        row.SupplierId = api.SupplierId;
        row.PurchaseSupplierCode = "S001";
        row.PurchaseSupplierName = "供应商一";
        row.IsChecked = true;
        picker.Items.Add(row);
        picker.CommitPendingEdits = static () => false;

        await picker.ValidateSelectionCommand.ExecuteAsync(null);

        Assert.IsNull(api.LastPreviewRequest);
        Assert.AreEqual(PurchaseOrderFromMaterialStage.Selection, viewModel.Stage);
        Assert.AreEqual("重新校验所选物料", picker.ValidationActionText);
        StringAssert.Contains(picker.SelectionValidationText, "当前单元格");

        picker.CommitPendingEdits = static () => true;
        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        picker.EnterPreviewCommand.Execute(null);

        Assert.IsNotNull(api.LastPreviewRequest);
        Assert.AreEqual(row.SupplierId, api.LastPreviewRequest.Items.Single().SupplierId);
        Assert.AreEqual(PurchaseOrderFromMaterialStage.Preview, viewModel.Stage);
        Assert.IsFalse(row.IsInputEnabled);
    }

    /// <summary>准备页刷新只读取一次当前来源候选，保留采购输入并失效旧审核；重新校验后进入审核不重复请求。</summary>
    [TestMethod]
    public async Task PreparationRefresh_ShouldPreserveInputsInvalidatePreviewAndReuseRevalidation()
    {
        var api = new NetDemandApiStub();
        var candidate = new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            SourceType = "SO",
            SourceUid = api.SourceUid,
            SourceNo = "SO-1001",
            SourceLineNumber = 1,
            Sku = "SKU-REFRESH",
            LinkSku = "MODEL-REFRESH",
            SupplyType = SupplyTypeEnums.采购,
            NetQty = 10m,
            SuggestedPurchaseSupplierId = api.SupplierId,
            SuggestedPurchaseSupplierCode = "S001",
            SuggestedPurchaseSupplierName = "供应商一",
        };
        api.CandidateGroups.Add(new PurchaseDraftCandidateSourceGroupDto
        {
            SourceType = "SO",
            SourceUid = api.SourceUid,
            SourceNo = "SO-1001",
            CandidateCount = 1,
        });
        api.Candidates.Add(candidate);
        using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        using var preview = CreateViewModel(api);
        using var container = new PurchaseOrderFromMaterialViewModel(picker, preview);
        await container.OpenAsync();
        await picker.SelectSourceGroupCommand.ExecuteAsync(picker.SourceGroups[1]);
        var row = picker.Items.Single();
        row.IsChecked = true;
        row.PurchaseQty = 6m;
        row.UnitPrice = 12.5m;
        row.TaxPrice = 13.8m;
        var plannedDate = new DateTime(2026, 11, 12);
        row.PlannedDeliveryDate = plannedDate;

        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        picker.EnterPreviewCommand.Execute(null);
        preview.ReturnToPreparationCommand.Execute(null);
        var previewCallsBeforeRefresh = api.PreviewCallCount;
        var candidateCallsBeforeRefresh = api.CandidateQueries.Count;
        candidate.Version = 2;

        await picker.RefreshCandidateDataCommand.ExecuteAsync(null);

        Assert.AreEqual(candidateCallsBeforeRefresh + 1, api.CandidateQueries.Count);
        Assert.AreEqual(previewCallsBeforeRefresh, api.PreviewCallCount);
        Assert.HasCount(0, api.CreateCommands);
        row = picker.Items.Single();
        Assert.AreEqual(2, row.Version);
        Assert.IsTrue(row.IsChecked);
        Assert.AreEqual(6m, row.PurchaseQty);
        Assert.AreEqual(12.5m, row.UnitPrice);
        Assert.AreEqual(13.8m, row.TaxPrice);
        Assert.AreEqual(plannedDate, row.PlannedDeliveryDate);
        Assert.IsTrue(preview.IsStale);
        Assert.IsFalse(picker.EnterPreviewCommand.CanExecute(null));
        StringAssert.Contains(picker.SelectionValidationText, "重新校验");

        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        var previewCallsAfterValidation = api.PreviewCallCount;
        picker.EnterPreviewCommand.Execute(null);

        Assert.AreEqual(previewCallsBeforeRefresh + 1, previewCallsAfterValidation);
        Assert.AreEqual(previewCallsAfterValidation, api.PreviewCallCount);
        Assert.AreEqual(PurchaseOrderFromMaterialStage.Preview, container.Stage);
    }

    /// <summary>审核目标变化后必须返回准备重新校验，重新接纳时复用目标选择而不由审核页直接刷新。</summary>
    [TestMethod]
    public async Task StalePreview_ShouldRequirePreparationRevalidation()
    {
        var api = new NetDemandApiStub { IncludeSafeAppend = true };
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(5m, api.SourceUid)],
        });
        var initialCallCount = api.PreviewCallCount;
        var target = viewModel.Targets.Single();
        Assert.IsTrue(target.HasMultipleChoices);

        target.SelectedChoice = target.Choices.Last();

        Assert.IsTrue(viewModel.IsStale);
        Assert.IsTrue(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "返回采购准备");

        await viewModel.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(5m, api.SourceUid)],
        });

        Assert.AreEqual(initialCallCount + 1, api.PreviewCallCount);
        Assert.IsFalse(viewModel.IsStale);
        Assert.AreEqual(PurchaseDraftTargetModeEnum.AppendExisting, viewModel.Targets.Single().SelectedChoice?.TargetMode);
    }

    /// <summary>预览事实冲突只失效审核并给出准备页刷新动作，不再从审核页自动查询候选。</summary>
    [TestMethod]
    public async Task PreviewConflict_ShouldRequirePreparationRefreshWithoutCandidateQuery()
    {
        var api = new NetDemandApiStub();
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [CreateRow(5m, api.SourceUid)],
        });
        api.PreviewHandler = (_, _) => Task.FromResult(
            ServiceResult<PurchaseDraftPreviewResponse>.Fail(
                PurchaseDraftErrorCodes.PreviewFactChanged,
                "需求事实已变化。"));
        var candidateQueriesBefore = api.CandidateQueries.Count;

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsStale);
        Assert.AreEqual(candidateQueriesBefore, api.CandidateQueries.Count);
        Assert.IsTrue(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "刷新待采购数据后重新校验");
    }

    /// <summary>部分成功先累计并排除；冲突来源留在审核页并要求用户返回准备显式刷新。</summary>
    [TestMethod]
    public async Task PartialSuccessWithConflict_ShouldStayInPreviewAndPreserveCompletedIdentity()
    {
        var api = new NetDemandApiStub();
        var secondSourceUid = Guid.NewGuid();
        var first = CreateRow(5m, api.SourceUid);
        var second = CreateRow(4m, secondSourceUid);
        api.PreviewHandler = (request, _) => Task.FromResult(
            ServiceResult<PurchaseDraftPreviewResponse>.Ok(CreateMultiSourcePreview(
                api,
                request,
                new Dictionary<Guid, Guid>
                {
                    [first.Uid] = api.SourceUid,
                    [second.Uid] = secondSourceUid,
                })));
        using var viewModel = CreateViewModel(api);
        await OpenSessionAsync(viewModel, new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            Rows = [first, second],
        });
        api.CreateHandler = (command, _) => Task.FromResult(
            ServiceResult<CreatePurchaseDraftsResult>.Ok(new CreatePurchaseDraftsResult
            {
                ExecutionRequestId = command.ExecutionRequestId,
                Sources =
                {
                    new CreatePurchaseDraftSourceResult
                    {
                        SourceType = "SO",
                        SourceUid = api.SourceUid,
                        Outcome = NetDemandCommandOutcomeEnum.Succeeded,
                        Message = "已创建",
                    },
                    new CreatePurchaseDraftSourceResult
                    {
                        SourceType = "SO",
                        SourceUid = secondSourceUid,
                        Outcome = NetDemandCommandOutcomeEnum.Failed,
                        ErrorCode = PurchaseDraftErrorCodes.PreviewExpired,
                        Message = "事实已变化",
                    },
                },
            }));
        var creationCompleted = 0;
        viewModel.CreationCompleted += (_, _) => creationCompleted++;

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasAppliedResults);
        Assert.HasCount(2, viewModel.Results);
        Assert.HasCount(1, viewModel.Items);
        Assert.AreEqual(second.Uid, viewModel.Items.Single().NetDemandId);
        Assert.AreEqual(0, creationCompleted);
        Assert.IsTrue(viewModel.GetCompletedSourceKeys().Single()
            .Contains(api.SourceUid.ToString("N"), StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(viewModel.ReturnToPreparationCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CreateCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "刷新待采购数据后重新校验");
    }

    private static PurchaseDraftWorkflowViewModel CreateViewModel(NetDemandApiStub api, bool allowErrors = false) =>
        new(api, new ProcurementMasterDataApiStub(), new DirectApiCallRunner(), new MessageServiceStub { AllowErrors = allowErrors });

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
        LinkSku = "MODEL-A-100",
        SupplyType = SupplyTypeEnums.采购,
        NetQty = remainingQty,
        SuggestedPurchaseSupplierId = Guid.NewGuid(),
        SuggestedPurchaseSupplierCode = "S001",
        SuggestedPurchaseSupplierName = "供应商一",
    });

    private sealed class NetDemandApiStub : IPurchaseDraftApi
    {
        public Guid SourceUid { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public bool IncludeWarnings { get; set; }
        public bool IncludeSafeAppend { get; set; }
        public int PreviewCallCount { get; private set; }
        public PurchaseDraftPreviewRequest? LastPreviewRequest { get; private set; }
        public CreatePurchaseDraftsCommand? LastCreateCommand { get; private set; }
        public List<CreatePurchaseDraftsCommand> CreateCommands { get; } = [];
        public List<PurchaseDraftCandidateSourceGroupDto> CandidateGroups { get; } = [];
        public List<PurchaseDraftCandidateDto> Candidates { get; } = [];
        public List<PurchaseDraftCandidateQuery> CandidateQueries { get; } = [];
        public Func<PurchaseDraftPreviewRequest, CancellationToken, Task<ServiceResult<PurchaseDraftPreviewResponse>>>? PreviewHandler { get; set; }
        public Func<CreatePurchaseDraftsCommand, CancellationToken, Task<ServiceResult<CreatePurchaseDraftsResult>>>? CreateHandler { get; set; }

        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            LastPreviewRequest = request;
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
                Header = new PurchaseDraftPreviewHeaderDto
                {
                    CustomerPoNo = "CUSTOMER-PO",
                    BoNo = "CINV-S",
                    Currency = "CNY",
                    DiscountRate = 0.95m,
                    UntaxedTotalAmount = 12m * item.PurchaseQty,
                    TaxIncludedTotalAmount = 13.56m * item.PurchaseQty,
                    ExpectedDate = new DateTime(2026, 8, 10),
                    SalesPerson = "业务员甲",
                    PurchasePerson = "采购员乙"
                },
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
                        ImagePath = "products/source-a-100.png",
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

        public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Ok(CandidateGroups.ToList()));

        public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default)
        {
            CandidateQueries.Add(request);
            return Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(request.SourceUid.HasValue
                ? Candidates.Where(item => item.SourceUid == request.SourceUid
                    && string.Equals(item.SourceType, request.SourceType, StringComparison.OrdinalIgnoreCase)).ToList()
                : Candidates.ToList()));
        }
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

    private sealed class ProcurementMasterDataApiStub : IProcurementMasterDataQueryApi
    {
        public Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(
            PagedRequestBase request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>.Ok(new PagedResponseBase<ProcurementSupplierCandidateDto>()));

        public Task<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>> SearchCustomersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>> SearchProductsAsync(ProcurementProductSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>> SearchCustomerHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>> SearchSupplierHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
