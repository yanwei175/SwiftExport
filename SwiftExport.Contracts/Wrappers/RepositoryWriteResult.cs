using System;

namespace SwiftExport.Contracts.Wrappers
{
    public class RepositoryWriteResult
    {
        public bool Success { get; set; }
        public Guid Uid { get; set; }

        public int? OldVersion { get; set; }

        public int? NewVersion { get; set; }

        public int AffectedRows { get; set; }

        public string ErrorCode { get; set; }

        public string Message { get; set; }
        public static RepositoryWriteResult Ok(
            Guid uid,
            int? oldVersion,
            int? newVersion,
            string message = null)
        {
            return new RepositoryWriteResult
            {
                Success = true,
                Uid = uid,
                OldVersion = oldVersion,
                NewVersion = newVersion,
                AffectedRows = 1,
                Message = message
            };
        }

        public static RepositoryWriteResult Fail(
            Guid uid,
            int? oldVersion,
            string errorCode,
            string message)
        {
            return new RepositoryWriteResult
            {
                Success = false,
                Uid = uid,
                OldVersion = oldVersion,
                NewVersion = null,
                AffectedRows = 0,
                ErrorCode = errorCode,
                Message = message
            };
        }
    }
}
