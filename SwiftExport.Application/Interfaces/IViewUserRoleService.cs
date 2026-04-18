namespace SwiftExport.Application.Interfaces
{
    public interface IViewUserRoleService
    {
        Task<ServiceResult<ViewUserRole>> GetUserRole(String userName, String password);

    }
}
