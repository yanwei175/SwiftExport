using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchSaveRequestBase<TDto>
    {
        public List<CreateItemRequest<TDto>> CreateItems { get; set; } = new List<CreateItemRequest<TDto>>();
        public List<UpdateItemRequest<TDto>> UpdateItems { get; set; } = new List<UpdateItemRequest<TDto>>();
        public List<DeleteItemRequest> DeleteItems { get; set; } = new List<DeleteItemRequest>();
        public int TotalCount => CreateItems.Count + UpdateItems.Count + DeleteItems.Count;
        public bool HasChanges => CreateItems.Count > 0 || UpdateItems.Count > 0 || DeleteItems.Count > 0;
    }
    public abstract class SaveItemRequestBase
    {
        public string ClientKey { get; set; }

        public int? RowIndex { get; set; }
    }
    public sealed class CreateItemRequest<TCreateDto> : SaveItemRequestBase
    {
        public TCreateDto Data { get; set; }
    }
    public sealed class UpdateItemRequest<TUpdateDto> : SaveItemRequestBase
    {

        public Guid Uid { get; set; }

        public int Version { get; set; }

        public TUpdateDto Data { get; set; }
    }
    public sealed class DeleteItemRequest : SaveItemRequestBase
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public bool IsSoftDelete { get; set; } = true;
    }




}
