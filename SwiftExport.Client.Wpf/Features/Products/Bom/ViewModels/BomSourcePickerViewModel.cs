using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.ViewModels;

/// <summary>以专用服务端分页选择已保存 BOM 的成品，不读取或编辑目标组成。</summary>
public sealed partial class BomSourcePickerViewModel : ObservableObject, IDialogContentState, IDisposable
{
    private readonly IBomApi _api;
    private readonly IApiCallRunner _runner;
    private readonly Guid _targetId;
    private readonly CancellationToken _lifetime;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    /// <summary>建立独立来源查询生命周期，目标成品不可作为复制来源。</summary>
    public BomSourcePickerViewModel(IBomApi api, IApiCallRunner runner, Guid targetId, CancellationToken cancellationToken = default)
    {
        _api = api; _runner = runner; _targetId = targetId; _lifetime = cancellationToken;
        PropertyChanged += OnStateChanged;
    }
    /// <summary>当前来源页；无组成的绑定可见但不能确认。</summary>
    public ObservableCollection<BomProductItemModel> Products { get; } = [];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial BomProductItemModel? SelectedProduct { get; set; }
    [ObservableProperty] public partial bool IsQuerying { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    /// <summary>来源页复用分页器的有界页大小，选择变化与请求保持一致。</summary>
    [ObservableProperty] public partial int PageSize { get; set; } = 20;
    /// <inheritdoc />
    public bool IsPrimaryButtonEnabled => !_disposed && !IsQuerying && SelectedProduct is { ComponentCount: > 0 } item && item.Uid != _targetId;
    /// <inheritdoc />
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc />
    public event EventHandler? StateChanged;
    /// <inheritdoc />
    public bool CanClose(DialogResult requestedResult) => requestedResult != DialogResult.Primary || IsPrimaryButtonEnabled;

    /// <summary>执行关键词搜索，取消前一来源页查询。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() { PageIndex = 1; return LoadAsync(); }
    /// <summary>接纳分页控件的页码与页大小请求，保留当前来源关键词。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        PageIndex = Math.Max(1, request.NewIndex); PageSize = request.NewSize;
        return LoadAsync();
    }
    /// <summary>只有最新且弹窗仍存活的结果可以更新来源集合。</summary>
    public async Task LoadAsync()
    {
        if (_disposed) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        var old = Interlocked.Exchange(ref _cts, cts); old?.Cancel(); old?.Dispose();
        IsQuerying = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchProductsAsync(new BomProductSearchRequest
            { Keyword = Keyword.Trim(), IsBound = true, PageIndex = PageIndex, PageSize = PageSize }, cts.Token), cts.Token, "查询复制来源失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;
            if (!result.Success || result.Data is null) { StatusText = result.Message; return; }
            Products.Clear();
            foreach (var dto in result.Data.Items) Products.Add(dto.Adapt<BomProductItemModel>());
            TotalCount = result.Data.TotalCount;
            SelectedProduct = Products.FirstOrDefault(item => item.ComponentCount > 0 && item.Uid != _targetId);
            StatusText = "选择具有已保存组成的成品；待完善 BOM 不可复制。";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch { if (!_disposed && ReferenceEquals(_cts, cts)) StatusText = "查询复制来源失败，请重试。"; }
        finally { if (ReferenceEquals(_cts, cts)) IsQuerying = false; }
    }
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);
    /// <summary>弹窗关闭后取消来源查询，防止过期结果继续回写。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PropertyChanged -= OnStateChanged;
        _cts?.Cancel(); _cts?.Dispose();
    }
}
