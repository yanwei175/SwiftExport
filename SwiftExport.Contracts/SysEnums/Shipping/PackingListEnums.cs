namespace SwiftExport.Contracts.SysEnums.Shipping
{
    public enum PackingListLifecycleStatusEnum
    {
        Active = 0,
        Closed = 8,
        Voided = 9
    }

    public enum PackingListPostingStatusEnum
    {
        Unposted = 0,
        Posted = 1,
        Reversed = 8
    }

    public enum PackingListCommandStatusEnum
    {
        Success = 0,
        AlreadyApplied = 1,
        VersionConflict = 2,
        Blocked = 3
    }
}
