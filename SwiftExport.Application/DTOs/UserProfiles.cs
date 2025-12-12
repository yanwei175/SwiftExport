using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class UserProfiles : Profile
    {
            public  UserProfiles ()
            {
                CreateMap<ViewUserRole, ViewUserRoleDTo>();
                CreateMap<ViewUserRoleDTo, ViewUserRole>();
             }
    }
}
