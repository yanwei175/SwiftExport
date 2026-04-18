namespace SwiftExport.Application.Interfaces
{
    public interface ICustomersPoDetailsService : IBaseService<CustomersPoDetails>
    {
        Task<ServiceResult<CustomersPoDetails>> GetByCustomerCodeWithSupplierCodeAsync(string customerCode, string supplierCode);
        //新增或者修改
        Task<ServiceResult<CustomersPoDetails>> AddOrUpdateAsync(CustomersPoDetails ety);

        //根据customerCode和supplierCode删除
        Task<ServiceResult<bool>> DeleteByCustomerCodeAndSupplierCodeAsync(string customerCode, string supplierCode);

    }
}
