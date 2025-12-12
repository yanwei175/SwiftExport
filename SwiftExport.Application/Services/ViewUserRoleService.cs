using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.EntitieView;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ViewUserRoleService : IViewUserRoleService

    {
        private readonly IViewUserRoleRepository _repo;
        private readonly IMapper _mapper;
        public ViewUserRoleService(IViewUserRoleRepository repo,IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<ViewUserRole> GetUserRole(string userName, string password)
        {
           try 
           {
               var user = await _repo.Login(userName,  password);
               if (user == null)
               {
                   return null;
               }
               if (user.Password != password)
               {
                   return null;
               }

                return user;
           }
           catch (Exception ex)
           {
               throw ex;
           }

        }
    }
}
