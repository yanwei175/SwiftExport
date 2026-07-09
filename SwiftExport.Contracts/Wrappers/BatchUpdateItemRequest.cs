using System;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchUpdateItemRequest<TUpdateDto>
    {
        public Guid Uid { get; set; }

        public int Version { get; set; }

        public TUpdateDto Data { get; set; }
    }
}
