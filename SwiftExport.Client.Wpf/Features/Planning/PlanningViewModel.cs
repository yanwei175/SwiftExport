using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Planning;

public sealed class PlanningViewModel : ViewModelBaseEx
{
    public PlanningViewModel()
    {
        Title = "计划";
        Description = "计划工作区将承载需求拆解、净需求和后续计划相关页面。";
    }

    public string Description { get; }
}
