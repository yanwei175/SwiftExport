using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Updates;

/// <summary>在模态更新窗口阻止新交互期间检查全部业务页，避免重启丢失编辑。</summary>
public sealed class ClientRestartGuard(INavigationService navigation) : IClientRestartGuard
{
    /// <inheritdoc />
    public async Task<bool> CanRestartAsync(CancellationToken cancellationToken)
    {
        var tabs = navigation.OpenTabs.ToArray();
        if (tabs.Any(IsWorking)) return false;
        foreach (var tab in tabs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tab.ContentViewModel is IDocumentTabViewModel document)
            {
                if (!await document.CanCloseAsync(cancellationToken)) return false;
            }
            else if (tab.IsDirty || tab.ContentViewModel is ViewModelBaseEx { IsDirty: true })
            {
                // 没有离开协议的页面不默认丢弃数据，要求用户先完成编辑。
                return false;
            }
        }
        return tabs.SequenceEqual(navigation.OpenTabs) && !tabs.Any(IsWorking);
    }

    private static bool IsWorking(NavigationTabItem tab) => tab.IsInitializing
        || tab.ContentViewModel is ViewModelBaseEx { IsBusy: true };
}
