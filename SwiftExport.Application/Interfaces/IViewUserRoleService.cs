using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IViewUserRoleService 
    {
        Task<ViewUserRole> GetUserRole(String userName, String password);

    }
}
