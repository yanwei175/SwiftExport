using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IViewUserRoleRepository : IRepository<ViewUserRole>
    {
        Task<ViewUserRole> Login(string userName, string psswd);

    }
}
