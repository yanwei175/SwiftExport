// SwiftExport.Contracts/DTOs/Base/BaseDto.cs
namespace SwiftExport.Contracts.DTOs.Base;

public abstract class BaseDto
{
    // 所有 DTO 都有 ID，且通常都需要传回前端
    public int Id { get; set; }

    // 可选：并发控制 (如果你将来想防止多人同时修改同一条数据)
    // public byte[] RowVersion { get; set; } 
}