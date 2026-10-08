using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.ViewModels;

/// <summary>管理绑定弹窗输入、编号预览和一次提交，失败保留输入且写入期间禁止关闭。</summary>
public sealed partial class BomBindingDialogViewModel : ViewModelBaseEx, IDialogContentState, IDialogCloseRequest, IDisposable
{
    private readonly IBomBindingClientService _service;
    private readonly Guid _productId;
    private readonly int _expectedVersion;
    private CancellationTokenSource? _previewCts;
    private bool _disposed;

    /// <summary>捕获打开时身份与版本，默认以成品 SKU 建立新编号。</summary>
    public BomBindingDialogViewModel(IBomBindingClientService service, BomProductItemModel product)
    {
        _service = service;
        _productId = product.Uid;
        _expectedVersion = product.Version;
        ProductText = product.Sku;
        BomNo = product.Sku;
        PropertyChanged += OnDialogPropertyChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(PreviewCommand);
    }

    /// <summary>获取目标产品显示编码。</summary>
    public string ProductText { get; }
    [ObservableProperty] public partial string BomNo { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BindExisting { get; set; }
    [ObservableProperty] public partial bool IsPreviewing { get; set; }
    [ObservableProperty] public partial string PreviewText { get; set; } = "请输入编号并查看共享信息。";
    /// <summary>仅明确提交成功后保存回执，关闭或失败不会产生默认成功值。</summary>
    public SaveItemResult? Saved { get; private set; }
    /// <summary>未知写入结果需关闭弹窗并刷新成品核实，禁止直接再次提交。</summary>
    public bool OutcomeUnknown { get; private set; }
    /// <inheritdoc />
    public bool IsPrimaryButtonEnabled => CanSave();
    /// <inheritdoc />
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc />
    public event EventHandler? StateChanged;
    /// <inheritdoc />
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    partial void OnBomNoChanged(string value)
    {
        _previewCts?.Cancel();
        PreviewText = "编号已变化，请重新预览。";
        if (!OutcomeUnknown) ValidationSummary = null;
    }
    partial void OnBindExistingChanged(bool value) { if (!OutcomeUnknown) ValidationSummary = null; }

    /// <summary>查询可被新请求替代，更新摘要前同时检查编号、请求身份和对象存活。</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        if (!CanPreview()) return;
        var number = BomNo.Trim();
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _previewCts, cts);
        old?.Cancel(); old?.Dispose();
        IsPreviewing = true;
        try
        {
            var result = await _service.PreviewAsync(number, cts.Token);
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_previewCts, cts) || BomNo.Trim() != number) return;
            PreviewText = result.Success && result.Data is { } info
                ? $"有效绑定 {info.BoundProductCount} 个产品，当前子件 {info.ComponentCount} 项，存储组成 {info.StoredComponentCount} 项。"
                    + (info.HasInvalidComponents ? "存在异常组成，不能绑定。" : string.Empty)
                : result.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch { if (!_disposed && ReferenceEquals(_previewCts, cts)) PreviewText = "编号查询失败，请稍后重试。"; }
        finally { if (ReferenceEquals(_previewCts, cts)) IsPreviewing = false; }
    }

    /// <summary>锁定当前输入后提交窄绑定；预览失败、并发或结果未知均保留弹窗。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        _previewCts?.Cancel();
        var request = new ProductBomBindingRequest { BomNo = BomNo.Trim(), ExpectedVersion = _expectedVersion, BindExisting = BindExisting };
        await RunWriteAsync(async () =>
        {
            var result = await _service.BindAsync(_productId, request);
            if (_disposed) return;
            if (!result.Success || result.Data is null)
            {
                OutcomeUnknown = result.Error?.Code == ServiceErrorCodes.OutcomeUnknown;
                ValidationSummary = result.Message + (OutcomeUnknown ? " 请关闭弹窗并刷新成品核实。" : string.Empty);
                return;
            }
            BomNo = request.BomNo;
            Saved = result.Data;
        }, "正在保存 BOM 绑定...");
        if (Saved is not null && !_disposed) CloseRequested?.Invoke(this, new(DialogResult.Primary));
    }

    private bool CanPreview() => !_disposed && IsNotBusy && !string.IsNullOrWhiteSpace(BomNo) && BomNo.Length <= 200;
    private bool CanSave() => CanPreview() && !IsPreviewing && !OutcomeUnknown;
    /// <inheritdoc />
    public bool CanClose(DialogResult requestedResult) => IsNotBusy && (requestedResult != DialogResult.Primary || Saved is not null);
    private void OnDialogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SaveCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>取消仍在进行的编号查询并解除事件，不中断已发出的写命令。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PropertyChanged -= OnDialogPropertyChanged;
        _previewCts?.Cancel(); _previewCts?.Dispose();
    }
}
