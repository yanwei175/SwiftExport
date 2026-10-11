using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Contracts.Apis.Procurement;

namespace SwiftExport.Client.VSTO.Services.OrderExport
{
    /// <summary>列表导出使用的窄入口；按明确订单集合生成独立文件，不扩大到其他分页。</summary>
    public interface IPurchaseOrderExportService
    {
        /// <summary>逐张读取详情并导出；进度在 UI 线程报告，账号变化终止剩余订单。</summary>
        Task<PurchaseOrderExportResult> ExportAsync(IReadOnlyList<Guid> orderIds, string directory, Action<string> progress, CancellationToken token);
    }

    /// <summary>本次批次的成功路径和失败说明，已成功的文件不会因后续失败回滚。</summary>
    public sealed class PurchaseOrderExportResult
    {
        public List<string> Files { get; } = new List<string>();
        public List<string> Messages { get; } = new List<string>();
        public int FailedCount { get; internal set; }
    }

    /// <summary>复用采购详情及认证管线，串行编排导出；工作簿操作由 Excel 写入适配层负责。</summary>
    public sealed class PurchaseOrderExportService : IPurchaseOrderExportService
    {
        private readonly IProcurementOrderApi api;
        private readonly IPurchaseOrderWorkbookWriter writer;
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;

        /// <summary>登记详情、会话及写入依赖，不在构造时访问服务器或创建文件。</summary>
        public PurchaseOrderExportService(IProcurementOrderApi api, IPurchaseOrderWorkbookWriter writer,
            SessionService session, IUiDispatcher dispatcher, ApiCallRunner errors)
        { this.api = api; this.writer = writer; this.session = session; this.dispatcher = dispatcher; this.errors = errors; }

        /// <inheritdoc />
        public async Task<PurchaseOrderExportResult> ExportAsync(IReadOnlyList<Guid> orderIds, string directory, Action<string> progress, CancellationToken token)
        {
            if (orderIds == null || orderIds.Count == 0 || orderIds.Any(id => id == Guid.Empty))
                throw new ArgumentException("请先选择需要导出的采购订单。");
            var ids = orderIds.Distinct().ToArray();
            var path = PurchaseOrderExportFiles.NormalizeDirectory(directory);
            var version = session.Capture().Version;
            EnsureCurrent(version, token);
            // 目录检查不占用 Excel 主线程，传入目录在本次操作中被冻结。
            await Task.Run(() => { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(path); }, token);
            var result = new PurchaseOrderExportResult();
            for (var index = 0; index < ids.Length; index++)
            {
                EnsureCurrent(version, token);
                var id = ids[index]; var label = id.ToString();
                if (progress != null)
                    await dispatcher.InvokeAsync(() => { EnsureCurrent(version, token); progress("正在导出 " + (index + 1) + " / " + ids.Length + " 张采购订单……"); });
                try
                {
                    var response = await api.GetDetailAsync(id, token);
                    EnsureCurrent(version, token);
                    if (response == null || !response.Success || response.Data == null)
                        throw new InvalidOperationException(response?.Error?.Message ?? "获取采购详情失败。");
                    if (response.Data.PurchaseOrderId != id) throw new InvalidOperationException("返回的采购详情身份与请求不一致。");
                    label = response.Data.OrderNo ?? label;
                    var document = new PurchaseOrderExportDocument(response.Data);
                    var file = await writer.WriteAsync(document, path, version, token);
                    result.Files.Add(file);
                    result.Messages.AddRange(document.Warnings.Select(warning => label + "：" + warning));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || session.Capture().Version != version) { throw; }
                catch (Exception ex)
                {
                    EnsureCurrent(version, token);
                    result.FailedCount++;
                    var message = ex is InvalidOperationException || ex is ArgumentException ? ex.Message
                        : ex is UnauthorizedAccessException ? "没有输出文件夹的写入权限。"
                        : ex is IOException ? "文件保存失败，请检查目录、文件占用和磁盘空间。" : errors.Describe(ex);
                    result.Messages.Add(label + "：" + message);
                }
            }
            return result;
        }

        // 即使 HTTP 或 COM 实现忽略取消，旧账号也不得继续生成文件。
        private void EnsureCurrent(long version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var identity = session.Capture();
            if (identity.Version != version || identity.User == null) throw new OperationCanceledException("账号已变化，导出已停止。", token);
        }
    }
}
