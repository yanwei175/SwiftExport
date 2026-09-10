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
                new PurchaseOrderFromMaterialView { DataContext = workflow },
                new DialogOptions("从物料需求建立采购草稿")
                {
                    CloseButtonText = "关闭",
                    DefaultButton = DialogButton.Close,
                    ScrollStrategy = DialogScrollStrategy.Content,
                    Width = 1400,
                    Height = 860,
                    MaxWidth = 1400,
                    MaxHeight = 860
                },
                new WorkflowDialogState(workflow),
                cancellationToken);
            return new PurchaseOrderFromMaterialResult { HasAppliedResults = workflow.HasAppliedResults };
        }
        finally
        {
            workflow.Close();
        }
    }

    /// <summary>仅同步保护采购写命令执行期间的关闭；正文内部负责全部业务动作。</summary>
    private sealed class WorkflowDialogState(PurchaseOrderFromMaterialViewModel workflow) : IDialogContentState
    {
        /// <summary>弹窗没有主次按钮，无需发布按钮状态变化。</summary>
        public event EventHandler? StateChanged { add { } remove { } }
        /// <summary>本弹窗不显示主按钮。</summary>
        public bool IsPrimaryButtonEnabled => false;
        /// <summary>本弹窗不显示次按钮。</summary>
        public bool IsSecondaryButtonEnabled => false;
        /// <summary>采购写命令执行中拒绝关闭，避免服务端成功但调用方遗漏刷新。</summary>
        public bool CanClose(DialogResult requestedResult) => !workflow.IsWriting;
    }
}
