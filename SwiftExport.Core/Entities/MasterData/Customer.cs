using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>客户主数据实体，保存客户身份、联系方式及资料目录配置。</summary>
[Table("Customer")]
public partial class Customer : BaseEntity
{
    /// <summary>是否允许新业务引用；停用不取消已有业务承诺。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>该实体保存的客户业务编码。</summary>
    public required string CustomerCode { get; set; }
    /// <summary>该实体保存的客户名称。</summary>
    public string? CustomerName { get; set; }
    /// <summary>该实体保存的联系地址。</summary>
    public string? Address { get; set; }
    /// <summary>该实体保存的联系电话。</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>该实体保存的联系人。</summary>
    public string? ContactPerson { get; set; }
    /// <summary>该实体保存的标签资料目录。</summary>
    public string? LabelFolderPath { get; set; }
    /// <summary>该实体保存的说明书资料目录。</summary>
    public string? ManualFolderPath { get; set; }
    /// <summary>该实体保存的图纸资料目录。</summary>
    public string? DrawingFolderPath { get; set; }
    /// <summary>该实体保存的线稿资料目录。</summary>
    public string? LinedrawingFolderPath { get; set; }
    /// <summary>该实体保存的条码资料目录。</summary>
    public string? BarcodeFolderPath { get; set; }
    /// <summary>该实体保存的规格书资料目录。</summary>
    public string? SpecSheetFolderPath { get; set; }
    /// <summary>该实体保存的资料关联编号。</summary>
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
