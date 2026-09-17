using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>过账命令实际生成或反向处理的库存单据引用。</summary>
    public class PackingListGeneratedDocumentDto
    {
        /// <summary>实际库存单据身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>实际库存单据编号。</summary>
        public string DocumentNo { get; set; }
        /// <summary>Inbound 或 Outbound。</summary>
        public string Direction { get; set; }
        /// <summary>执行后的单据版本。</summary>
        public int Version { get; set; }
        /// <summary>执行后的库存单据过账状态值。</summary>
        public int PostingStatus { get; set; }
    }
}
