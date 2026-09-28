using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

/// <summary>创建需求选择与旧采购预览视图，并管理业务状态的打开、结果接纳和关闭。</summary>
public sealed class NetDemandDialogService(SourceDemandCandidatePickerViewModel sourceDemandPicker, IDialogService dialogService) : INetDemandDialogService
{
    /// <summary>展示物料领域候选，建立成功后在对话框内刷新来源单与当前明细。</summary>
    public async Task<bool> ShowSourceDemandCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await sourceDemandPicker.OpenAsync(cancellationToken);
            await dialogService.ShowAsync(
                new SourceDemandCandidatePickerView
                {
                    DataContext = sourceDemandPicker
                },
                new DialogOptions("从来源需求建立物料需求")
                {
                    ScrollStrategy = DialogScrollStrategy.Content,
                    WidthRatio = 1,
                    HeightRatio = 0.9
                },
                new SourcePickerDialogState(sourceDemandPicker),
                cancellationToken);
            return sourceDemandPicker.HasEstablished;
        }
        finally
        {
            sourceDemandPicker.Close();
        }
    }

    /// <summary>仅提供同步关闭保护；选择器内部按钮负责建立操作，无需弹窗主次按钮。</summary>
    private sealed class SourcePickerDialogState(SourceDemandCandidatePickerViewModel picker) : IDialogContentState
    {
        /// <summary>主次按钮状态固定，不订阅业务状态；关闭时直接读取当前写入状态。</summary>
        public event EventHandler? StateChanged { add { } remove { } }
        /// <summary>本弹窗不提供主按钮。</summary>
        public bool IsPrimaryButtonEnabled => false;
        /// <summary>本弹窗不提供次按钮。</summary>
        public bool IsSecondaryButtonEnabled => false;
        /// <summary>写入结束前不允许关闭，以便接纳建立结果和刷新列表。</summary>
        public bool CanClose(DialogResult requestedResult) => !picker.IsWriting;
    }
}
