using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>拥有一次图片上传弹窗的队列、逐项结果和停止边界；处理中禁止关闭。</summary>
public sealed partial class ProductImageUploadViewModel(
    IProductImageUploadClient client, ICurrentUserService currentUser,
    ILogger<ProductImageUploadViewModel> logger) : ObservableObject, IDialogContentState
{
    private bool _stopRequested;
    private bool _started;

    /// <summary>本次选择的只读队列，上传期间不允许更换。</summary>
    public IReadOnlyList<ProductImageUploadItem> Items { get; private set; } = [];
    /// <summary>已取得结果的项目数；不把字节发送完成当成图片发布完成。</summary>
    [ObservableProperty] public partial int Completed { get; set; }
    /// <summary>上传或等待当前服务器处理结果时为真。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelectFiles))]
    public partial bool IsUploading { get; set; }
    /// <summary>上传结束或尚未开始时允许重新选择；重新选择会重置本次结果。</summary>
    public bool CanSelectFiles => !IsUploading;
    /// <summary>向用户展示的汇总与当前处理阶段。</summary>
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;

    /// <inheritdoc />
    public event EventHandler? StateChanged;
    /// <inheritdoc />
    public bool IsPrimaryButtonEnabled => false;
    /// <inheritdoc />
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc />
    public bool CanClose(DialogResult requestedResult) => !IsUploading;

    /// <summary>初始化新的选择；不在上传过程中替换队列。</summary>
    public void Prepare(IReadOnlyList<string> paths)
    {
        if (IsUploading) throw new InvalidOperationException("上传中不能更换文件。");
        Items = paths.Select(path => new ProductImageUploadItem(path)).ToArray();
        OnPropertyChanged(nameof(Items));
        Completed = 0;
        _started = false;
        _stopRequested = false;
        Summary = Items.Count == 0
            ? "请先选择图片。上传会覆盖服务器同名图片并生成缩略图，不修改产品资料中的主图引用。"
            : $"已选择 {Items.Count} 张图片。同名图片将覆盖并自动生成缩略图；重新选择会替换当前列表。";
        StartCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsUploading && !_started && Items.Count > 0;
    private bool CanStop() => IsUploading && !_stopRequested;

    /// <summary>仅停止尚未发出的项目；当前写入等待明确结果，不暗示撤销已完成的覆盖。</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _stopRequested = true;
        Summary = "正在停止：等待当前图片处理结束，已上传图片不会撤销。";
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>逐张调用批量 API，允许单项失败；网络未知、授权或服务级错误停止后续上传。</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart()) return;
        if (!currentUser.IsAdmin || !currentUser.IsAuthenticated)
        {
            Summary = "只有已登录的管理员可以上传产品图片。";
            return;
        }
        var identity = currentUser.CurrentUser;
        _started = true;
        IsUploading = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        var succeeded = 0;
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in Items)
            {
                if (_stopRequested) break;
                if (!currentUser.IsAdmin || !ReferenceEquals(identity, currentUser.CurrentUser))
                {
                    Summary = "身份或权限已变化，已停止后续上传。";
                    break;
                }
                if (!targets.Add(item.TargetName))
                {
                    item.Status = "未上传：本次选择中存在相同目标文件名";
                    Completed++;
                    continue;
                }
                item.Status = "正在上传并生成缩略图…";
                Summary = $"正在处理 {Completed + 1} / {Items.Count}：{item.FileName}";
                var result = await client.UploadAsync(item.Path);
                Completed++;
                if (!result.Success)
                {
                    item.Status = result.Error?.Code == ServiceErrorCodes.OutcomeUnknown
                        ? "结果未知，请核对服务器图片；未自动重试"
                        : "已停止：" + result.Message;
                    break;
                }
                var uploaded = result.Data?.Items?.SingleOrDefault();
                if (uploaded is null)
                {
                    item.Status = "结果未知：服务器未返回单张图片结果，请核对后重传";
                    break;
                }
                item.Status = uploaded.Success ? "上传成功（原图和缩略图）" : "失败：" + uploaded.Message;
                if (uploaded.Success) succeeded++;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "产品图片上传队列异常终止");
            foreach (var item in Items.Where(item => item.Status.StartsWith("正在上传", StringComparison.Ordinal)))
                item.Status = "结果未知，请核对服务器图片；未自动重试";
        }
        finally
        {
            foreach (var item in Items.Where(item => item.Status == "等待上传")) item.Status = "未上传";
            Summary = $"已结束：成功 {succeeded} / {Items.Count}，已处理 {Completed} 张。请查看逐项结果；已上传图片不会撤销。";
            IsUploading = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
