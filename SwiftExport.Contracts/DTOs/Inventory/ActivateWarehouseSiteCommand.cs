using System;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>提交虚拟仓启用意图，并携带乐观并发版本和必填业务原因。</summary>
    public class ActivateWarehouseSiteCommand
    {
        /// <summary>调用方在不确定结果重试时保持不变的命令标识。</summary>
        public Guid CommandId { get; set; }

        /// <summary>用户确认启用时看到的仓库版本。</summary>
        public int ExpectedVersion { get; set; }

        /// <summary>启用原因；服务端清理首尾空白后持久化到业务日志。</summary>
        public string Reason { get; set; }
    }
}
