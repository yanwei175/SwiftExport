using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Client.VSTO.Services.OrderExport
{
    /// <summary>将冻结的采购快照写入自有工作簿并发布文件；纯托管调用方不持有 Office 对象。</summary>
    public interface IPurchaseOrderWorkbookWriter
    {
        /// <summary>保存到明确目录，返回最终路径；发布前复核账号版本及取消状态，不覆盖已有文件。</summary>
        Task<string> WriteAsync(PurchaseOrderExportDocument document, string outputDirectory, long sessionVersion, CancellationToken token);
    }
}
