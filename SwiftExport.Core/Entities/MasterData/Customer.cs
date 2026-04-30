using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

[Table("Customer")]
public class Customer : BaseEntity
{
    public required string CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ContactPerson { get; set; }
    public string? LabelFolderPath { get; set; }
    public string? ManualFolderPath { get; set; }
    public string? DrawingFolderPath { get; set; }
    public string? LinedrawingFolderPath { get; set; }
    public string? BarcodeFolderPath { get; set; }
    public string? SpecSheetFolderPath { get; set; }
    public required string DocRelateNo { get; set; }

    // ==================== 贸易条款默认值 ====================
    /// <summary>
    /// 贸易条款备注（用于定义完整的交易条件）
    /// 例如：FOB Jiangmen, 30% Deposit, Balance before shipment
    /// </summary>
    public string? IncotermSortRemark { get; set; }

    /// <summary>
    /// 国际贸易术语（如 EXW / FOB / CIF / DAP / DDP）
    /// </summary>
    public string? Incoterm { get; set; }

    /// <summary>
    /// 贸易条款地点（如 FOB Shenzhen 中的 Shenzhen）
    /// </summary>
    public string? IncotermLocation { get; set; }

    /// <summary>
    /// 定金比例（如 30% 存储为 0.3）
    /// </summary>
    public decimal DepositRate { get; set; }

    /// <summary>
    /// 交货周期（天数）
    /// </summary>
    public int LeadTimeDays { get; set; }

    /// <summary>
    /// 付款方式（如 TT / LC / OA）
    /// </summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// 币种（USD / RMB / EUR 等）
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// ETD 备注（用于告知客户预计离港时间，如 "Mid August"）
    /// </summary>
    public string? EtdSortRemark { get; set; }

    /// <summary>
    /// 承运方式（如 DHL / UPS / 海运 / 空运）
    /// </summary>
    public string? ShippedBy { get; set; }
}

