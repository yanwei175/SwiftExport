using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>验证来源单候选整单加载、选择隔离和过期请求保护。</summary>
[TestClass]
public sealed class SourceDemandCandidatePickerViewModelTests
{
    /// <summary>整单查询不使用分页，即使超过旧上限 200 条也必须全部接纳。</summary>
    [TestMethod]
    public async Task OpenAsync_ShouldLoadAllCandidatesBeyondLegacyPageLimit()
    {
        var sourceUid = Guid.NewGuid();
        var (api, proxy) = CreateApi();
        proxy.Groups = [CreateGroup("SO-250", sourceUid, 250)];
        proxy.DetailHandler = (_, uid, _) => Task.FromResult(
            ServiceResult<List<SourceDemandCandidateDto>>.Ok(
                Enumerable.Range(1, 250).Select(index => CreateCandidate(uid, "SO-250", index)).ToList()));
        using var viewModel = new SourceDemandCandidatePickerViewModel(api, new DirectApiCallRunner());

        await viewModel.OpenAsync();

        Assert.AreEqual(250, viewModel.Items.Count);
        Assert.AreEqual("已完整加载 250 条待建立需求。", viewModel.StatusText);
        Assert.AreEqual(1, proxy.DetailCalls.Count);
        Assert.AreEqual(sourceUid, proxy.DetailCalls[0].SourceUid);
    }

    /// <summary>快速切换来源单时，较晚完成的旧请求不得覆盖当前来源及其选择状态。</summary>
    [TestMethod]
    public async Task SelectSourceGroupAsync_ShouldIgnoreLateResultFromPreviousSource()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var (api, proxy) = CreateApi();
        proxy.Groups = [CreateGroup("SO-A", sourceA, 1), CreateGroup("SO-B", sourceB, 1)];
        proxy.DetailHandler = (_, uid, _) => Task.FromResult(
            ServiceResult<List<SourceDemandCandidateDto>>.Ok([CreateCandidate(uid, uid == sourceA ? "SO-A" : "SO-B", 1)]));
        using var viewModel = new SourceDemandCandidatePickerViewModel(api, new DirectApiCallRunner());
        await viewModel.OpenAsync();
        viewModel.Items.Single().IsChecked = true;

        var previousStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousCompletion = new TaskCompletionSource<ServiceResult<List<SourceDemandCandidateDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.DetailHandler = (_, uid, _) =>
        {
            if (uid == sourceB)
            {
                previousStarted.TrySetResult();
                return previousCompletion.Task;
            }
            return Task.FromResult(ServiceResult<List<SourceDemandCandidateDto>>.Ok(
                [CreateCandidate(sourceA, "SO-A", 2)]));
        };

        var previous = viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
        await previousStarted.Task;
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        previousCompletion.SetResult(ServiceResult<List<SourceDemandCandidateDto>>.Ok(
            [CreateCandidate(sourceB, "SO-B", 9)]));
        await previous;

        Assert.AreEqual(sourceA, viewModel.SelectedSourceGroup!.SourceUid);
        Assert.AreEqual("SO-A", viewModel.Items.Single().Demand.SourceNo);
        Assert.AreEqual(2, viewModel.Items.Single().Demand.SourceLineNumber);
        Assert.IsFalse(viewModel.Items.Single().IsChecked);
    }

    /// <summary>建立后保留仍有候选的当前来源单，全部建立后从左侧移除。</summary>
    [TestMethod]
    public async Task EstablishSelectedAsync_ShouldRefreshGroupsAndCurrentDetails()
    {
        var sourceUid = Guid.NewGuid();
        var remaining = new List<SourceDemandCandidateDto>
        {
            CreateCandidate(sourceUid, "SO-REFRESH", 1),
            CreateCandidate(sourceUid, "SO-REFRESH", 2),
        };
        var (api, proxy) = CreateApi();
        proxy.GroupHandler = () => remaining.Count == 0
            ? []
            : [CreateGroup("SO-REFRESH", sourceUid, remaining.Count)];
        proxy.DetailHandler = (_, _, _) => Task.FromResult(
            ServiceResult<List<SourceDemandCandidateDto>>.Ok(remaining.ToList()));
        proxy.EstablishHandler = command =>
        {
            var selectedIds = command.Items.Select(item => item.GrossDemandId).ToHashSet();
            remaining.RemoveAll(item => selectedIds.Contains(item.GrossDemandId));
            return Task.FromResult(ServiceResult<EstablishMaterialDemandsResult>.Ok(new EstablishMaterialDemandsResult
            {
                CreatedSourceCount = selectedIds.Count,
                CreatedMaterialDemandCount = selectedIds.Count,
            }));
        };
        using var viewModel = new SourceDemandCandidatePickerViewModel(api, new DirectApiCallRunner());
        await viewModel.OpenAsync();

        viewModel.Items[0].IsChecked = true;
        await viewModel.EstablishSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual(1, viewModel.SourceGroups.Count);
        Assert.AreEqual(sourceUid, viewModel.SelectedSourceGroup!.SourceUid);
        Assert.AreEqual(1, viewModel.Items.Count);
        Assert.AreEqual(2, viewModel.Items[0].Demand.SourceLineNumber);

        viewModel.ToggleAllCommand.Execute(null);
        await viewModel.EstablishSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual(0, viewModel.SourceGroups.Count);
        Assert.AreEqual(0, viewModel.Items.Count);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsTrue(viewModel.HasEstablished);
    }

    private static (INetDemandWorkspaceApi Api, CandidateApiProxy Proxy) CreateApi()
    {
        var api = DispatchProxy.Create<INetDemandWorkspaceApi, CandidateApiProxy>();
        return (api, (CandidateApiProxy)(object)api);
    }

    private static SourceDemandCandidateGroupDto CreateGroup(string sourceNo, Guid sourceUid, int count) => new()
    {
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        CandidateCount = count,
    };

    private static SourceDemandCandidateDto CreateCandidate(Guid sourceUid, string sourceNo, int lineNumber) => new()
    {
        GrossDemandId = Guid.NewGuid(),
        SourceDemandVersion = 1,
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        SourceLineId = Guid.NewGuid(),
        SourceLineNumber = lineNumber,
        Sku = $"SKU-{lineNumber}",
        LinkSku = $"LINK-{lineNumber}",
        ImagePath = $"images/{lineNumber}.png",
        DemandQty = 1,
        CanEstablish = true,
    };

    /// <summary>只实现本测试使用的候选分组和整单明细接口。</summary>
    public class CandidateApiProxy : DispatchProxy
    {
        public List<SourceDemandCandidateGroupDto> Groups { get; set; } = [];
        public Func<List<SourceDemandCandidateGroupDto>>? GroupHandler { get; set; }
        public List<(string SourceType, Guid SourceUid)> DetailCalls { get; } = [];
        public Func<string, Guid, CancellationToken, Task<ServiceResult<List<SourceDemandCandidateDto>>>> DetailHandler { get; set; }
            = (_, _, _) => Task.FromResult(ServiceResult<List<SourceDemandCandidateDto>>.Ok([]));
        public Func<EstablishMaterialDemandsCommand, Task<ServiceResult<EstablishMaterialDemandsResult>>> EstablishHandler { get; set; }
            = _ => throw new NotSupportedException(nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync));

        /// <summary>按方法名返回确定结果，未授权调用会立即令测试失败。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(INetDemandWorkspaceApi.GetSourceDemandCandidateGroupsAsync))
            {
                return Task.FromResult(ServiceResult<List<SourceDemandCandidateGroupDto>>.Ok(
                    GroupHandler is null ? Groups.ToList() : GroupHandler()));
            }
            if (targetMethod.Name == nameof(INetDemandWorkspaceApi.GetSourceDemandCandidatesBySourceAsync))
            {
                var sourceType = (string)args![0]!;
                var sourceUid = (Guid)args[1]!;
                DetailCalls.Add((sourceType, sourceUid));
                return DetailHandler(sourceType, sourceUid, (CancellationToken)args[2]!);
            }
            if (targetMethod.Name == nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync))
            {
                return EstablishHandler((EstablishMaterialDemandsCommand)args![0]!);
            }
            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action(), successMessage);
    }
}
