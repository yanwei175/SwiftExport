using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchSaveResponseBase
    {
        public int TotalCount { get; set; }
        public List<SaveItemResult> Items { get; set; } = new List<SaveItemResult>();

        public int SuccessCount => Items.Count(x => x.Success);

        public int FailedCount => Items.Count(x => !x.Success);

        public bool AllSucceeded => FailedCount == 0;


        public int AddSuccessCount =>
            Items.Count(x => x.Operation == SaveOperation.Create && x.Success);

        public int UpdateSuccessCount =>
            Items.Count(x => x.Operation == SaveOperation.Update && x.Success);

        public int DeleteSuccessCount =>
            Items.Count(x => x.Operation == SaveOperation.Delete && x.Success);

        public List<Guid> GetAllAffectedIdList()
        {
            return Items
                .Where(x => x.Success && x.Uid.HasValue)
                .Select(x => x.Uid.Value)
                .Distinct()
                .ToList();
        }
    }

    public class SaveItemResult
    {
        public string ClientKey { get; set; }

        public int? RowIndex { get; set; }

        public SaveOperation Operation { get; set; }

        public Guid? Uid { get; set; }

        public bool Success { get; set; }

        public int? OldVersion { get; set; }

        public int? NewVersion { get; set; }

        public string ErrorCode { get; set; }

        public string Message { get; set; }
    }




}
