using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.Common
{
    public static class SaveItemResultFactory
    {
        public static SaveItemResult FromCreate<T>(CreateItemRequest<T> request, RepositoryWriteResult writeResult)
        {
            return new SaveItemResult
            {
                ClientKey = request.ClientKey,
                RowIndex = request.RowIndex,
                Operation = SaveOperation.Create,
                Uid = writeResult.Uid,
                Success = writeResult.Success,
                OldVersion = writeResult.OldVersion,
                NewVersion = writeResult.NewVersion,
                ErrorCode = writeResult.ErrorCode,
                Message = writeResult.Message
            };
        }

        public static SaveItemResult FromUpdate<T>(UpdateItemRequest<T> request, RepositoryWriteResult writeResult)
        {
            return new SaveItemResult
            {
                ClientKey = request.ClientKey,
                RowIndex = request.RowIndex,
                Operation = SaveOperation.Update,
                Uid = writeResult.Uid,
                Success = writeResult.Success,
                OldVersion = writeResult.OldVersion,
                NewVersion = writeResult.NewVersion,
                ErrorCode = writeResult.ErrorCode,
                Message = writeResult.Message
            };
        }

        public static SaveItemResult FromDelete(DeleteItemRequest request, RepositoryWriteResult writeResult)
        {
            return new SaveItemResult
            {
                ClientKey = request.ClientKey,
                RowIndex = request.RowIndex,
                Operation = SaveOperation.Delete,
                Uid = writeResult.Uid,
                Success = writeResult.Success,
                OldVersion = writeResult.OldVersion,
                NewVersion = writeResult.NewVersion,
                ErrorCode = writeResult.ErrorCode,
                Message = writeResult.Message
            };
        }

        public static SaveItemResult Failed(SaveItemRequestBase request, SaveOperation operation, string errorCode, string message, Guid? uid = null, int? oldVersion = null)
        {
            return new SaveItemResult
            {
                ClientKey = request.ClientKey,
                RowIndex = request.RowIndex,
                Operation = operation,
                Uid = uid,
                Success = false,
                OldVersion = oldVersion,
                NewVersion = null,
                ErrorCode = errorCode,
                Message = message
            };
        }
    }
}
