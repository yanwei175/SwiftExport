using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>过账或反向命令成功结果；失败由 ServiceResult.Error 提供，Data 为空。</summary>
    public class PackingListPostingCommandResult
    {
        /// <summary>Success 或 AlreadyApplied。</summary>
        public string Status { get; set; }
        /// <summary>成功时的持久化执行记录身份。</summary>
        public Guid? ExecutionId { get; set; }
        /// <summary>成功时服务器确认的最新完整详情。</summary>
        public PackingListDetailDto Detail { get; set; }
        /// <summary>实际库存单据，不由客户端计算。</summary>
        public List<PackingListGeneratedDocumentDto> Documents { get; set; } = new List<PackingListGeneratedDocumentDto>();
    }
}
