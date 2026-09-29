using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    public sealed partial class SalesOrderImportViewModel
    {
        private readonly Dictionary<SalesOrderImportLine, CancellationTokenSource> searches = new Dictionary<SalesOrderImportLine, CancellationTokenSource>();

        // 命令闭包绑定具体行，不读取 DataGrid 当前行，避免编辑焦点切换导致错行回填。
        private void ConfigureSearch(SalesOrderImportLine line)
        {
            line.SearchCommand = new AsyncRelayCommand<string>(text => SearchAsync(line, text), _ => CanSearch(line), AsyncRelayCommandOptions.AllowConcurrentExecutions);
            line.CommitProductCommand = new RelayCommand<SalesOrderProductLookupDto>(candidate =>
            {
                candidate = candidate ?? line.SelectedProduct;
                if (!CanSearch(line) || candidate == null || !line.ProductCandidates.Contains(candidate)) return;
                line.SelectedProduct = candidate;
                line.ApplyProduct(candidate);
            }, _ => CanSearch(line));
            line.ClearProductCommand = new RelayCommand(() =>
            {
                if (!CanSearch(line)) return;
                CancelSearch(line); line.ClearProduct();
            }, () => CanSearch(line));
        }
        private bool CanSearch(SalesOrderImportLine line) => IsEditable && !line.IsNonStandard && Lines.Contains(line) && Header.CustomerId != Guid.Empty;
        private async Task SearchAsync(SalesOrderImportLine line, string keyword)
        {
            if (!CanSearch(line)) return;
            CancelSearch(line);
            if (string.IsNullOrWhiteSpace(keyword)) return;
            var request = new CancellationTokenSource(); searches[line] = request;
            var version = pageVersion; var customer = Header.CustomerId;
            line.IsSearchBusy = true;
            ErrorMessage = ""; OnPropertyChanged(nameof(BusyMessage));
            Func<bool> current = () => Accept(version) && Lines.Contains(line) && Header.CustomerId == customer
                && searches.TryGetValue(line, out var active) && ReferenceEquals(active, request) && !request.IsCancellationRequested;
            try
            {
                var result = await api.SearchProductsAsync(new SalesOrderProductSearchRequest
                {
                    Keyword = keyword.Trim(), CustomerId = customer, PageIndex = 1, PageSize = 30
                }, request.Token);
                if (!Accept(version)) return;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!current()) return;
                    if (result != null && result.Success && result.Data != null)
                        foreach (var product in result.Data.Items) line.ProductCandidates.Add(product);
                    else ErrorMessage = result?.Message ?? "查询产品未返回有效结果。";
                    OnPropertyChanged(nameof(BusyMessage));
                });
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { /* 新输入、删行或页面结束主动取消。 */ }
            catch (Exception ex)
            {
                if (Accept(version)) await dispatcher.InvokeAsync(() => { if (current()) { ErrorMessage = errors.Describe(ex); OnPropertyChanged(nameof(BusyMessage)); } });
            }
            finally
            {
                try
                {
                    if (!IsDisposed) await dispatcher.InvokeAsync(() =>
                    {
                        if (!IsDisposed && searches.TryGetValue(line, out var active) && ReferenceEquals(active, request))
                        {
                            searches.Remove(line); line.IsSearchBusy = false;
                        }
                    });
                }
                catch (OperationCanceledException) { /* 宿主已经停止调度，资源仍由 finally 回收。 */ }
                finally { request.Dispose(); }
            }
        }
        // CTS 在拥有它的异步 finally 中释放，取消后忽略不能及时停止的网络结果。
        private void CancelSearch(SalesOrderImportLine line)
        {
            if (searches.TryGetValue(line, out var request)) { searches.Remove(line); request.Cancel(); }
            line.IsSearchBusy = false; line.ProductCandidates.Clear(); line.SelectedProduct = null;
        }
        private void CancelSearches()
        {
            foreach (var line in Lines.Concat(searches.Keys).Distinct().ToArray()) CancelSearch(line);
        }
    }
}
