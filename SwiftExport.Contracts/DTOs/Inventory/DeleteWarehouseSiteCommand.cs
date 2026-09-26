using System;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>提交虚拟仓软删除意图，并携带乐观并发版本和必填业务原因。</summary>
    public class DeleteWarehouseSiteCommand
    {
        /// <summary>调用方在不确定结果重试时保持不变的命令标识。</summary>
        public Guid CommandId { get; set; }

        /// <summary>用户确认删除时看到的仓库版本。</summary>
        public int ExpectedVersion { get; set; }

        /// <summary>删除原因；服务端清理首尾空白后持久化到业务日志。</summary>
        public string Reason { get; set; }
    }
}
