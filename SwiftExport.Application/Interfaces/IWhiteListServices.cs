namespace SwiftExport.Application.Interfaces
{
    public interface IWhiteListServices : IBaseService<WhiteLists>
    {
        Task<IReadOnlyList<string>> GetVstoWhiteListAsync();
    }
}
