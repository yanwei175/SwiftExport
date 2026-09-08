using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.AfterSales;

/// <summary>验证售后客户端的核心字段语义、两阶段确认及路由契约。</summary>
[TestClass]
public sealed class AfterSalesModelsAndContractsTests
{
    /// <summary>空或零数量允许保存，正数量确认才要求责任方、金额和补件身份。</summary>
    [TestMethod]
    public void LineValidation_ShouldApplyDemandRulesOnlyToPositiveQuantity()
    {
        var line = AfterSalesLineModel.CreateNew(1);
        line.IssueDescription = "客户投诉成品松动";
        Assert.IsNull(line.Validate(false));
        Assert.IsNull(line.Validate(true));

        line.Quantity = -1;
        Assert.AreEqual("补发数量不能为负数。", line.Validate(false));
        line.Quantity = 2;
        Assert.AreEqual("正数量行必须选择责任供应商。", line.Validate(true));
        line.ResponsibleSupplierId = Guid.NewGuid();
        Assert.AreEqual("正数量行必须填写人民币行金额，免费填 0。", line.Validate(true));
        line.ServiceAmountCny = 0;
        Assert.AreEqual("标准补件必须选择产品。", line.Validate(true));
        line.ProductId = Guid.NewGuid();
        Assert.IsNull(line.Validate(true));
    }

    /// <summary>非标补件不能夹带标准产品，并在交给来源需求前执行 300 字边界。</summary>
    [TestMethod]
    public void LineValidation_ShouldKeepNonStandardIdentityExclusive()
    {
        var line = AfterSalesLineModel.CreateNew(1);
        line.IssueDescription = "包装内缺少配件";
        line.Quantity = 1;
        line.ResponsibleSupplierId = Guid.NewGuid();
        line.ServiceAmountCny = 12m;
        line.IsNonStandard = true;
        line.ProductId = Guid.NewGuid();
        line.DescriptionSnapshot = "非标支架";

        Assert.AreEqual("非标补件不能同时选择标准产品。", line.Validate(true));
        line.ProductId = null;
        line.DescriptionSnapshot = new string('中', 301);
        Assert.AreEqual("确认时补发描述不能超过 300 字。", line.Validate(true));
    }

    /// <summary>问题成品和实际补件使用两组独立字段，单位默认值进入写入契约。</summary>
    [TestMethod]
    public void LineMapping_ShouldKeepIssueAndReplacementSeparate()
    {
        var issueProductId = Guid.NewGuid();
        var replacementProductId = Guid.NewGuid();
        var line = AfterSalesLineModel.CreateNew(3);
        line.IssueDescription = "成品问题";
        line.IssueProductId = issueProductId;
        line.IssueCustomerSkuSnapshot = "CUSTOMER-FINISHED";
        line.ProductId = replacementProductId;
        line.CustomerSkuSnapshot = "CUSTOMER-PART";
        line.Quantity = 2;

        var dto = line.ToDto();

        Assert.AreEqual(issueProductId, dto.IssueProductId);
        Assert.AreEqual(replacementProductId, dto.ProductId);
        Assert.AreEqual("CUSTOMER-FINISHED", dto.IssueCustomerSkuSnapshot);
        Assert.AreEqual("CUSTOMER-PART", dto.CustomerSkuSnapshot);
        Assert.AreEqual("Pcs", dto.UnitSnapshot);
    }

    /// <summary>列表金额保留“已知金额”和“仍有待定”两种事实。</summary>
    [TestMethod]
    public void ListMapping_ShouldExposeUndeterminedAmount()
    {
        var row = new AfterSalesListItemDto
        {
            KnownAmountCny = 0,
            HasUndeterminedAmount = true,
            CustomerCodeSnapshot = "C001",
            CustomerNameSnapshot = "客户一"
        }.Adapt<AfterSalesManagerRowModel>();

        Assert.AreEqual("¥0.00 + 待定", row.AmountText);
        Assert.AreEqual("C001 · 客户一", row.CustomerDisplay);
    }

    /// <summary>保存成功后才确认，并使用保存返回的最新版本。</summary>
    [TestMethod]
    public async Task FeatureService_ShouldPreserveSaveSuccessWhenConfirmFails()
    {
        var api = new RecordingAfterSalesApi
        {
            SaveResult = ServiceResult<AfterSalesCommandResult>.Ok(new AfterSalesCommandResult
            {
                Uid = Guid.NewGuid(), Version = 7, AfterSalesNo = "CR2026090001"
            }),
            ConfirmResult = ServiceResult<AfterSalesCommandResult>.Fail("SOURCE_EXIT_BLOCKED", "来源暂不可确认")
        };
        var service = new AfterSalesFeatureService(api, new PassThroughApiCallRunner());
        var command = new SaveAfterSalesCommand { Uid = api.SaveResult.Data.Uid, Version = 6 };

        var result = await service.SaveAndConfirmAsync(command, false, Guid.NewGuid());

        Assert.IsTrue(result.Save.Success);
        Assert.IsNotNull(result.Confirm);
        Assert.IsFalse(result.Confirm.Success);
        Assert.AreEqual(7, api.ConfirmedVersion);
        Assert.AreEqual(1, api.SaveCalls);
        Assert.AreEqual(1, api.ConfirmCalls);
    }

    /// <summary>Refit 路由与后端根路径一致，所有接口保留取消参数。</summary>
    [TestMethod]
    public void ApiContract_ShouldMatchAbsoluteAfterSalesRoutes()
    {
        AssertRoute(nameof(IAfterSalesApi.SearchAsync), "/api/quality-control/after-sales", typeof(GetAttribute));
        AssertRoute(nameof(IAfterSalesApi.GetAsync), "/api/quality-control/after-sales/{uid}", typeof(GetAttribute));
        AssertRoute(nameof(IAfterSalesApi.CreateAsync), "/api/quality-control/after-sales", typeof(PostAttribute));
        AssertRoute(nameof(IAfterSalesApi.SaveAsync), "/api/quality-control/after-sales/{uid}", typeof(PutAttribute));
        AssertRoute(nameof(IAfterSalesApi.ConfirmDemandAsync), "/api/quality-control/after-sales/{uid}/confirm-demand", typeof(PostAttribute));
        AssertRoute(nameof(IAfterSalesApi.CancelDemandConfirmationAsync), "/api/quality-control/after-sales/{uid}/cancel-demand-confirmation", typeof(PostAttribute));
        foreach (var method in typeof(IAfterSalesApi).GetMethods())
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType, method.Name);
    }

    /// <summary>品质菜单只暴露 Manager，详情作为受同一模块权限保护的文档路由。</summary>
    [TestMethod]
    public void Navigation_ShouldRegisterManagerAndDetail()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(AfterSalesManagerViewModel.RouteKey, out var manager));
        Assert.AreEqual(PermissionKeys.ModuleQualityControlAccess, manager.RequiredPermission);
        Assert.IsTrue(registry.TryGetRoute(AfterSalesDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual(PermissionKeys.ModuleQualityControlAccess, detail.RequiredPermission);
        var quality = registry.MenuItems.Single(item => item.RouteKey == NavigationModuleKeys.QualityControl);
        Assert.IsTrue(quality.Children.Any(item => item.RouteKey == AfterSalesManagerViewModel.RouteKey));
        Assert.IsFalse(quality.Children.Any(item => item.RouteKey == AfterSalesDetailViewModel.RouteKey));
    }

    private static void AssertRoute(string methodName, string expectedPath, Type attributeType)
    {
        var method = typeof(IAfterSalesApi).GetMethod(methodName)!;
        var attribute = method.GetCustomAttributes(attributeType, false).Single();
        var path = (string)attributeType.GetProperty("Path")!.GetValue(attribute)!;
        Assert.AreEqual(expectedPath, path);
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class RecordingAfterSalesApi : IAfterSalesApi
    {
        public required ServiceResult<AfterSalesCommandResult> SaveResult { get; init; }
        public required ServiceResult<AfterSalesCommandResult> ConfirmResult { get; init; }
        public int SaveCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int ConfirmedVersion { get; private set; }

        public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(SaveResult);
        }

        public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default)
        {
            ConfirmCalls++;
            ConfirmedVersion = command.Version;
            return Task.FromResult(ConfirmResult);
        }

        public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CreateAsync(SaveAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
