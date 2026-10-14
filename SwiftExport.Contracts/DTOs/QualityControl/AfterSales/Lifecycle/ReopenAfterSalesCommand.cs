using System;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>带原因重开售后生命周期，不重新发布成品需求。</summary>
    public class ReopenAfterSalesCommand
    {
        /// <summary>幂等命令标识。</summary>
        public Guid CommandId { get; set; }
        /// <summary>头版本。</summary>
        public int Version { get; set; } = 1;
        /// <summary>操作原因或结案说明。</summary>
        public string Reason { get; set; } = string.Empty;

    }
}

