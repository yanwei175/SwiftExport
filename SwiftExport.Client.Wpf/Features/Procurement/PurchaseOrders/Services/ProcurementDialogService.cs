using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>通过公共 Dialog 服务承载采购从物料选择到创建结果的单一弹窗。</summary>
public sealed class ProcurementDialogService(PurchaseOrderFromMaterialViewModel workflow, IDialogService dialogs) : IProcurementDialogService
{
    /// <inheritdoc/>
    public async Task<PurchaseOrderFromMaterialResult> ShowFromMaterialAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await workflow.OpenAsync(cancellationToken);
            await dialogs.ShowAsync(
                new PurchaseOrderFromMaterialView
                {
                    DataContext = workflow
                },
                new DialogOptions("从物料需求建立采购草稿")
                {
                    CloseButtonText = "关闭（本次运行保留待核实请求）",
                    DefaultButton = DialogButton.Close,
                    ScrollStrategy = DialogScrollStrategy.Content,
                    WidthRatio = 1,
                    HeightRatio = 1
                },
                new WorkflowDialogState(workflow),
                cancellationToken);
            return new PurchaseOrderFromMaterialResult
            {
                HasAppliedResults = workflow.HasAppliedResults
            };
        }
        finally
        {
            workflow.Close();
        }
    }

    /// <summary>执行中禁止关闭；未知结果由应用内会话保留，关闭不表示服务端取消。</summary>
    private sealed class WorkflowDialogState(PurchaseOrderFromMaterialViewModel workflow) : IDialogContentState
    {
        /// <summary>弹窗没有主次按钮，无需发布按钮状态变化。</summary>
        public event EventHandler? StateChanged { add { } remove { } }
        /// <summary>本弹窗不显示主按钮。</summary>
        public bool IsPrimaryButtonEnabled => false;
        /// <summary>本弹窗不显示次按钮。</summary>
        public bool IsSecondaryButtonEnabled => false;
        /// <summary>仅在不执行写入且原请求可以恢复时允许关闭。</summary>
        public bool CanClose(DialogResult requestedResult) => workflow.CanClose;
    }
}
