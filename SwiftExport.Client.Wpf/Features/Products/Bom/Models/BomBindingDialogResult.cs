using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.Models;

/// <summary>明确区分确认保存与关闭，调用方仅接纳主按钮的成功回执。</summary>
public sealed record BomBindingDialogResult(DialogResult Reason, SaveItemResult? Saved, string BomNo, bool OutcomeUnknown = false);
