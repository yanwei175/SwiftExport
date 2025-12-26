using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class SuppliersService : BaseService<Suppliers>, ISuppliersService
    {
        //private readonly IRepository _repo;
        public SuppliersService(IRepository<Suppliers> repo, IUintOfWorkFactory uowFac) : base(repo, uowFac)
        {
        }
    }
}
