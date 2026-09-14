using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>保留净需求追溯页面的空壳，当前不加载数据或提供业务操作。</summary>
public sealed partial class NetDemandTraceViewModel : ViewModelBaseEx
{
    /// <summary>初始化不含远程依赖和运行时状态的页面空壳。</summary>
    public NetDemandTraceViewModel()
    {
        Title = "净需求追溯";
    }
}
