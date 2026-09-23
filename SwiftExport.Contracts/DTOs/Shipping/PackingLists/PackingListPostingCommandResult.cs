using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>过账或反向命令结果；失败时前端保留草稿和原命令身份。</summary>
    public class PackingListPostingCommandResult
    {
        /// <summary>Success、AlreadyApplied、VersionConflict、PreviewExpired 或 Blocked。</summary>
        public string Status { get; set; }
        /// <summary>失败稳定错误码；成功为空。</summary>
        public string ErrorCode { get; set; }
        /// <summary>成功时的持久化执行记录身份。</summary>
        public Guid? ExecutionId { get; set; }
        /// <summary>成功时服务器确认的最新完整详情。</summary>
        public PackingListDetailDto Detail { get; set; }
        /// <summary>实际库存单据，不由客户端计算。</summary>
        public List<PackingListGeneratedDocumentDto> Documents { get; set; } = new List<PackingListGeneratedDocumentDto>();
        /// <summary>失败定位及缺口信息。</summary>
        public List<PackingListPostingBlockerDto> Blockers { get; set; } = new List<PackingListPostingBlockerDto>();
    }
}
