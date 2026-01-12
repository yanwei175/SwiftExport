using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class SalesOrder : BaseEntity
{
    public string? SoNo { get; set; }
    public DateTime? SoDate { get; set; }
    public DateTime? RePlyDate { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerPoNo { get; set; }
    public string? SoFolder { get; set; }
    public string? SoNote { get; set; }

}

