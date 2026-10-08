using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.FlowManagement;

/// <summary>流量页面离线夹具；只提供稳定端口，不调用实际服务或数据库。</summary>
internal sealed class FlowTestHost
{
    /// <summary>同一夹具内稳定的物料身份。</summary>
    public static readonly Guid MaterialId = Guid.NewGuid();
    /// <summary>初始化默认功能身份。</summary>
    public static readonly Guid FunctionId = Guid.NewGuid();
    /// <summary>测试不同功能与去重的第二个身份。</summary>
    public static readonly Guid OtherFunctionId = Guid.NewGuid();
    /// <summary>初始化不限流规格身份。</summary>
    public static readonly Guid LimiterId = Guid.NewGuid();
    /// <summary>测试行级不同配置的第二个规格身份。</summary>
    public static readonly Guid OtherLimiterId = Guid.NewGuid();
    public FlowApi Api { get; } = new();
    public CurrentUserService User { get; } = new();
    public NavigationStub Navigation { get; } = new();
    public DialogStub Dialogs { get; } = new();
    public ReferenceDataCacheServiceStub Cache { get; }
    public IProductApi Products { get; } = DispatchProxy.Create<IProductApi, ProductProxy>();
    /// <summary>创建全部授权用户和两套功能、限流器字典候选。</summary>
    public FlowTestHost(IEnumerable<SysDictDataDto>? dictionary = null)
    {
        User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.QualityControlFlowView,
            PermissionKeys.QualityControlFlowCreate, PermissionKeys.QualityControlFlowEdit, PermissionKeys.QualityControlFlowDelete] });
        Cache = new(dictionaryValues: dictionary ?? Dictionaries());
        Api.Detail = Detail();
    }
    /// <summary>使用真实错误包装层验证网络及取消分类。</summary>
    public FlowDetailViewModel Editor() => new(Api, Products, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), User, Cache, Dialogs, Navigation);
    /// <summary>创建列表页，API 用离线夹具代替。</summary>
    public FlowManagerViewModel Manager() => new(Api, Products, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), Navigation, User);
    /// <summary>提供可启用、停用及改名的独立字典数据。</summary>
    public static IEnumerable<SysDictDataDto> Dictionaries() =>
    [
        new() { Uid = FunctionId, DictCode = DictCodes.FlowTestFunction, DataNameEn = "Default", DataNameZh = "原功能", IsEnabled = true },
        new() { Uid = OtherFunctionId, DictCode = DictCodes.FlowTestFunction, DataNameEn = "Spray", DataNameZh = "喷淋", IsEnabled = true },
        new() { Uid = LimiterId, DictCode = DictCodes.FlowLimiterSpecification, DataNameEn = "Unlimited", DataNameZh = "不限流", IsEnabled = true },
        new() { Uid = OtherLimiterId, DictCode = DictCodes.FlowLimiterSpecification, DataNameEn = "1.2 GPM", DataNameZh = "1.2 GPM", IsEnabled = true }
    ];
    /// <summary>创建带头行版本和历史快照的完整测试组。</summary>
    public static FlowDetailDto Detail() => new()
    {
        Uid = Guid.NewGuid(), Version = 4, MaterialId = MaterialId, SkuSnapshot = "SKU-1", TestDate = new(2026, 10, 8),
        Lines =
        [
            new() { Uid = Guid.NewGuid(), Version = 2, FunctionId = FunctionId, FunctionNameSnapshot = "历史功能",
                LimiterSpecificationId = LimiterId, LimiterSpecificationNameSnapshot = "历史不限流", Flow1BarLMin = 2, SortNo = 1 },
            new() { Uid = Guid.NewGuid(), Version = 3, FunctionId = OtherFunctionId, FunctionNameSnapshot = "历史喷淋",
                LimiterSpecificationId = OtherLimiterId, LimiterSpecificationNameSnapshot = "1.2 GPM", Flow20PsiGpm = 1, SortNo = 2 }
        ]
    };

    /// <summary>仅实现物料查找的窄代理，其他调用说明测试范围错误。</summary>
    public class ProductProxy : DispatchProxy
    {
        /// <summary>仅接受查询候选调用，防止测试意外依赖其他产品接口。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IProductApi.SearchLookupAsync)
            ? Task.FromResult(ServiceResult<List<ProductLookupDto>>.Ok([new() { Uid = MaterialId, Sku = "SKU-1" }]))
            : throw new NotSupportedException();
    }

    /// <summary>离线流量 API 支持请求捕获、延迟及失败，服务器身份用于验证回执接纳。</summary>
    public sealed class FlowApi : IFlowApi
    {
        public FlowDetailDto Detail { get; set; } = null!;
        public int SaveCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public FlowEditDto? LastEdit { get; private set; }
        public UpdateItemRequest<FlowEditDto>? LastUpdate { get; private set; }
        public FlowSearchRequest? LastSearch { get; private set; }
        public Func<CancellationToken, Task<ServiceResult<FlowDetailDto>>>? ReadOverride { get; set; }
        public Func<FlowSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<FlowListItemDto>>>>? SearchOverride { get; set; }
        public Func<Task<ServiceResult<FlowSaveResponse>>>? SaveOverride { get; set; }
        /// <summary>捕获分页条件，允许用延迟端口测试替代型查询。</summary>
        public Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync(FlowSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastSearch = request;
            return SearchOverride?.Invoke(request, cancellationToken) ?? Task.FromResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(
                new() { Items = [Detail.Adapt<FlowListItemDto>()], TotalCount = 1, PageIndex = request.PageIndex, PageSize = request.PageSize }));
        }
        /// <summary>统计读取并返回当前组或指定失败。</summary>
        public Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        { ReadCalls++; return ReadOverride?.Invoke(cancellationToken) ?? Task.FromResult(ServiceResult<FlowDetailDto>.Ok(Detail)); }
        /// <summary>捕获完整新增请求，模拟服务器生成头行身份。</summary>
        public Task<ServiceResult<FlowSaveResponse>> CreateAsync(CreateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default)
        { SaveCalls++; LastEdit = request.Data; return SaveOverride?.Invoke() ?? Saved(request.Data, Guid.NewGuid(), 1); }
        /// <summary>保留整组更新请求和预期版本，供失败保留与成功接纳断言使用。</summary>
        public Task<ServiceResult<FlowSaveResponse>> UpdateAsync(Guid id, UpdateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default)
        { SaveCalls++; LastEdit = request.Data; LastUpdate = request; return SaveOverride?.Invoke() ?? Saved(request.Data, id, request.Version + 1); }
        /// <summary>只统计离线删除请求，不执行数据库操作。</summary>
        public Task<ServiceResult<SaveItemResult>> DeleteAsync(Guid id, DeleteItemRequest request, CancellationToken cancellationToken = default)
        { DeleteCalls++; return Task.FromResult(ServiceResult<SaveItemResult>.Ok(new() { Uid = id, Success = true })); }
        /// <summary>构造服务器权威身份、版本和快照回执；不在夹具复制后端业务校验。</summary>
        private Task<ServiceResult<FlowSaveResponse>> Saved(FlowEditDto data, Guid uid, int version)
        {
            var detail = data.Adapt<FlowDetailDto>();
            detail.Uid = uid; detail.Version = version; detail.SkuSnapshot = "SKU-1";
            detail.Lines = data.Lines.Select(x =>
            {
                var line = x.Adapt<FlowLineDto>();
                line.Uid = x.Uid == Guid.Empty ? Guid.NewGuid() : x.Uid;
                line.Version = x.Version + 1;
                line.FunctionNameSnapshot = "服务端功能"; line.LimiterSpecificationNameSnapshot = "服务端规格";
                return line;
            }).ToList();
            Detail = detail;
            return Task.FromResult(ServiceResult<FlowSaveResponse>.Ok(new() { Detail = detail }));
        }
    }

    /// <summary>捕获页签打开身份，供重复打开和新增转持久化测试。</summary>
    public sealed class NavigationStub : INavigationService
    {
        /// <summary>离线导航不发布属性变化。</summary>
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
        public NavigationTabItem? SelectedTab { get; set; }
        public string? Key { get; private set; }
        public object? Parameter { get; private set; }
        /// <summary>列表激活不创建真实窗口。</summary>
        public Task OpenAsync(string routeKey) => Task.CompletedTask;
        /// <summary>捕获文档页签身份和打开参数。</summary>
        public Task OpenDocumentAsync(string routeKey, string documentKey, object? parameter = null, CancellationToken cancellationToken = default)
        { Key = documentKey; Parameter = parameter; return Task.CompletedTask; }
        /// <summary>记录新增保存后从临时身份转为持久化身份。</summary>
        public void UpdateDocumentKey(object contentViewModel, string documentKey) => Key = documentKey;
        /// <summary>离线激活不切换真实界面。</summary>
        public void Activate(NavigationTabItem? tab) { }
        /// <summary>离线关闭不释放真实页签。</summary>
        public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
        /// <summary>离线夹具无全局会话需要重置。</summary>
        public void Reset() { }
    }
    /// <summary>可控制确认结果，验证取消不会覆盖输入。</summary>
    public sealed class DialogStub : IDialogService
    {
        public bool Confirm { get; set; } = true;
        /// <summary>返回预设确认结果，不显示实际对话框。</summary>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
        /// <summary>离线内容提示无需创建窗口。</summary>
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <summary>离线错误提示由页面属性断言验证。</summary>
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

