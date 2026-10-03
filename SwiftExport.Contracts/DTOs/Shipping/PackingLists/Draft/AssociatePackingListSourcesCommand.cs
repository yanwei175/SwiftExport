using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>将待关联草稿行分配到多个来源；命令整体原子执行。</summary>
    public sealed class AssociatePackingListSourcesCommand
    {
        /// <summary>稳定的业务意图，重试沿用同一身份和内容。</summary>
        public Guid CommandId { get; set; }
        /// <summary>单头预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>待关联行预期版本。</summary>
        public int ExpectedLineVersion { get; set; }
        /// <summary>用户选择的来源与数量，余量自动保留为待关联行。</summary>
        public List<PackingListSourceAllocationInput> Allocations { get; set; } = new List<PackingListSourceAllocationInput>();
    }
}
