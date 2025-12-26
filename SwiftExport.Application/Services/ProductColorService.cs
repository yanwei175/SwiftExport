using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ProductColorService : BaseService<ProductColor>, IProductColorService
    {
        //private readonly ICustomersRepository _thisrepo;
        public ProductColorService(IRepository<ProductColor> repo,
            IUintOfWorkFactory _uowFac) : base(repo, _uowFac)
        {
            //_thisrepo = repo;
        }
    }

}
