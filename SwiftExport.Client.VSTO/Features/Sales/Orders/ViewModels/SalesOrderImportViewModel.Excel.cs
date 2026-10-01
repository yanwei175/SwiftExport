// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Excel.Reading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Apis.SystemApis;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    public sealed partial class SalesOrderImportViewModel
    {
        private readonly IExcelRangeReader rangeReader;
        private readonly SalesOrderExcelParser excelParser;
        // 已淘汰的表头链路保留历史兼容；配置直接查询旧 API，不进入前端共享缓存。
        private readonly IExcelFieldMappingApi legacyMappingApi;
        private bool parsing;
        private bool applyingParsedPart;
        private string parseMessage = "";
        /// <summary>解析反馈保留到下一次成功解析或会话清空，人工修改不会丢失来源信息。</summary>
        public string ParseMessage { get => parseMessage; private set => SetProperty(ref parseMessage, value); }
        /// <summary>只替换单头导入字段，保留当前选择客户和全部明细。</summary>
        public IAsyncRelayCommand ParseHeaderCommand { get; }
        /// <summary>只替换明细，不改当前单头，不提交后端。</summary>
        public IAsyncRelayCommand ParseLinesCommand { get; }
        private bool CanParse() => IsEditable && rangeReader != null && excelParser != null && legacyMappingApi != null;

        // 解析是互斥操作：网络、Excel 模态消息循环和纯数组计算后分别重验会话及页面存活。
        private async Task ParseExcelAsync(bool detail)
        {
            if (!CanParse()) return;
            var version = pageVersion;
            parsing = true; CancelSearches(); ErrorMessage = ""; NotifyState();
            try
            {
                var loaded = await legacyMappingApi.GetMappingsAsync("SalesOrder");
                if (!Accept(version)) return;
                if (loaded == null || !loaded.Success)
                {
                    await dispatcher.InvokeAsync(() => { if (Accept(version)) ErrorMessage = loaded?.Message ?? "旧字段映射未返回有效结果。"; }); return;
                }
                // 本次查询结果仅用于本次解析，不保留跨操作或跨页面的配置缓存。
                IReadOnlyList<ExcelFieldMappingDto> mappings = loaded.Data;
                if (mappings == null || !Accept(version)) return;
                var snapshot = await rangeReader.SelectAsync(detail);
                if (snapshot == null || !Accept(version)) return;
                var result = await Task.Run(() => detail ? excelParser.ParseLines(snapshot, mappings) : excelParser.ParseHeader(snapshot, mappings));
                await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version)) return;
                    if (HasEdits && !prompt.ConfirmDiscard(detail
                        ? "重新解析将替换全部明细，单头保持不变。是否继续？"
                        : "重新解析将替换单头资料，保留已选客户和全部明细。是否继续？")) return;
                    if (!Accept(version)) return; // 确认框的嵌套消息循环可能发生账号切换。
                    applyingParsedPart = true;
                    try
                    {
                        if (detail)
                        {
                            CancelSearches(); selectedLines.Clear(); Lines.Clear();
                            foreach (var line in result.Lines) { ConfigureSearch(line); Lines.Add(line); }
                        }
                        else
                        {
                            result.Header.CustomerId = Header.CustomerId; result.Header.CustomerCode = Header.CustomerCode;
                            header.PropertyChanged -= HeaderChanged; header = result.Header; header.PropertyChanged += HeaderChanged;
                            OnPropertyChanged(nameof(Header));
                            foreach (var line in Lines) line.InvalidateCheck();
                        }
                    }
                    finally { applyingParsedPart = false; }
                    // 一次校正行订阅和汇总，避免逐行插入触发全单扫描，拖慢 Excel 主线程。
                    if (detail) LinesChanged(Lines, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    dirty = true; ParseMessage = result.Message; RefreshOptions(); NotifyState();
                });
            }
            catch (OperationCanceledException) { /* 插件退出停止调度，不接纳任何结果。 */ }
            catch (Exception ex)
            {
                try
                {
                    if (Accept(version)) await dispatcher.InvokeAsync(() =>
                    {
                        if (Accept(version)) ErrorMessage = ex is InvalidOperationException ? ex.Message : errors.Describe(ex);
                    });
                }
                catch (OperationCanceledException) { /* 异常反馈投递期间宿主停止，不再弹出错误。 */ }
            }
            finally
            {
                try { await dispatcher.InvokeAsync(() => { parsing = false; if (!IsDisposed) NotifyState(); }); }
                catch (OperationCanceledException) { /* 宿主已停止，无需恢复控件。 */ }
            }
        }
    }
}
