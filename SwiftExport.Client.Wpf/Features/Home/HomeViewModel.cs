using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Home;

public sealed class HomeViewModel : ViewModelBaseEx
{
    public HomeViewModel()
    {
        Title = "首页";
        Description = "SwiftExport 管理端工作区已就绪。";
    }

    public string Description { get; }
}
