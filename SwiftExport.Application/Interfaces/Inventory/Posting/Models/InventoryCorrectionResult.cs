using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory.Posting;

/// <summary>返回原子纠错生成或重放的反向与替代流水结果。</summary>
public sealed class InventoryCorrectionResult
{
    public IReadOnlyList<InventoryPostingResultDto> Reversals { get; set; } = [];
    public IReadOnlyList<InventoryPostingResultDto> Replacements { get; set; } = [];
}
