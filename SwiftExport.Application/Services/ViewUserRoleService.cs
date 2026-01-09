using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ViewUserRoleService : BaseService<ViewUserRole>, IViewUserRoleService

    {
        private readonly IViewUserRoleRepository __repo;
        private readonly IUintOfWorkFactory _uow;
        public ViewUserRoleService(IViewUserRoleRepository repo, IUintOfWorkFactory uow) : base (repo, uow)
        {
            __repo = repo;
            _uow = uow;
        }

        public async Task<ServiceResult<ViewUserRole>> GetUserRole(string userName, string password)
        {
           try 
           {
               var user = await __repo.Login(userName,  password);
               if (user == null)
               {
                    return ServiceResult<ViewUserRole>.Fail("找不到对应的账户");
               }
               if (user.Password != password)
               {
                    return ServiceResult<ViewUserRole>.Fail("密码错误");
                }

                return ServiceResult<ViewUserRole>.Ok(user);
           }
           catch (Exception ex)
           {
               throw ex;
           }

        }
    }
}
