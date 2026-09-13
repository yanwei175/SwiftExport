using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Suppliers.Components.Model;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Suppliers.Components.ViewModel
{
    /// <summary>编排一次性供应商只读单选会话，并从共享缓存快照执行本地搜索。</summary>
    public sealed partial class SupplierPickerViewModel
        : ViewModelBaseEx, IDialogContentState, IDialogCloseRequest, IDisposable
    {
        private readonly IReferenceDataCacheService _cache;
        private readonly List<SupplierPickerModel> _snapshot = [];
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private bool _disposed;

        /// <summary>创建一次性供应商选择会话；每次弹窗必须解析独立实例。</summary>
        public SupplierPickerViewModel(IReferenceDataCacheService cache)
        {
            _cache = cache;
            Title = "选择供应商";
            IsReadOnly = true;
            RegisterBusyAwareCommand(RefreshCommand);
            RegisterBusyAwareCommand(ConfirmCommand);
        }

        /// <inheritdoc/>
        public event EventHandler? StateChanged;

        /// <inheritdoc/>
        public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

        /// <summary>获取当前关键词筛选后的供应商候选。</summary>
        public ObservableCollection<SupplierPickerModel> Items { get; } = [];

        /// <summary>获取当前列表状态摘要。</summary>
        [ObservableProperty]
        public partial string ResultSummary { get; set; } = "尚未加载。";

        /// <summary>获取或设置供应商代码或名称搜索词。</summary>
        [ObservableProperty]
        public partial string Keyword { get; set; } = string.Empty;

        /// <summary>获取或设置当前唯一选中的供应商。</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        public partial SupplierPickerModel? SelectedItem { get; set; }

        /// <inheritdoc/>
        public bool IsPrimaryButtonEnabled => CanConfirm();

        /// <inheritdoc/>
        public bool IsSecondaryButtonEnabled => false;

        /// <inheritdoc/>
        public bool CanClose(DialogResult requestedResult) => true;

        /// <summary>确保基础资料缓存已加载，并建立与共享 DTO 隔离的本次弹窗快照。</summary>
        public async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return;
            using var cancellationRegistration = cancellationToken.Register(_lifetimeCancellation.Cancel);
            ResultSummary = "正在加载供应商...";
            var result = await _cache.EnsureLoadedAsync();
            if (_disposed || _lifetimeCancellation.IsCancellationRequested) return;

            RebuildSnapshot();
            if (!result.Success) ResultSummary = result.Message;
        }

        /// <summary>返回独立的共享 DTO 结果；无有效选择时返回空值。</summary>
        public SupplierDto? GetSelectedResult() => CanConfirm()
            ? SelectedItem!.Adapt<SupplierDto>()
            : null;

        /// <summary>强制刷新供应商缓存；失败时保留当前可用快照。</summary>
        [RelayCommand(CanExecute = nameof(CanInteract))]
        private async Task RefreshAsync()
        {
            await RunBusyAsync(async () =>
            {
                RaiseDialogStateChanged();
                var result = await _cache.RefreshSuppliersAsync();
                if (_disposed || _lifetimeCancellation.IsCancellationRequested) return;

                if (result.Success)
                {
                    RebuildSnapshot();
                    ResultSummary = "供应商缓存已刷新。";
                }
                else
                {
                    ResultSummary = result.Message;
                }
            }, "正在刷新供应商...");
            RaiseDialogStateChanged();
        }

        /// <summary>使用与 Dialog 主按钮相同的准入条件请求确认，供有效数据行双击复用。</summary>
        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() =>
            CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(DialogResult.Primary));

        private bool CanInteract() => !_disposed && !IsBusy;

        private bool CanConfirm() =>
            !_disposed && !IsBusy && SelectedItem is not null && Items.Contains(SelectedItem);

        /// <summary>从缓存 DTO 生成弹窗私有模型，防止页面选择状态污染会话级缓存对象。</summary>
        private void RebuildSnapshot()
        {
            _snapshot.Clear();
            _snapshot.AddRange(_cache.Suppliers.Select(item => item.Adapt<SupplierPickerModel>()));
            ApplyFilter();
        }

        /// <summary>按代码和名称执行本地筛选，并清除已不属于当前结果的选择。</summary>
        private void ApplyFilter()
        {
            if (_disposed) return;
            SelectedItem = null;
            var keyword = Keyword.Trim();
            var filtered = _snapshot.Where(item =>
                string.IsNullOrEmpty(keyword)
                || Contains(item.SupplierCode, keyword)
                || Contains(item.SupplierName, keyword));

            Items.Clear();
            foreach (var item in filtered) Items.Add(item);
            ResultSummary = Items.Count == 0
                ? "没有符合条件的供应商。"
                : $"共显示 {Items.Count} 个供应商。";
            RaiseDialogStateChanged();
        }

        private static bool Contains(string? source, string keyword) =>
            !string.IsNullOrEmpty(source)
            && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

        partial void OnKeywordChanged(string value) => ApplyFilter();

        partial void OnSelectedItemChanged(SupplierPickerModel? value) => RaiseDialogStateChanged();

        private void RaiseDialogStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            SelectedItem = null;
            Items.Clear();
            _snapshot.Clear();
            RaiseDialogStateChanged();
        }
    }
}
