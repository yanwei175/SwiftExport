using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.WarehouseViews.Models;
using System;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.WarehouseViews.ViewModels
{
    public class ReceiptManagerViewModel : ViewModelBaseEx
    {
        private readonly IReceiveApi _receiveApi;
        public ReceiptManagerViewModel(IReceiveApi receiveApi)
        {
            _receiveApi = receiveApi;
            HeadTable = new ReceiveHeadList();
            LineTable = new ReceiveLineList();

            _ = InitEnum();
            _ = LoadDataAsync(new ReceiveOrderSearchDto { PageIndex = 1, PageSize = 20 });
        }

        private object InitEnum()
        {
            throw new NotImplementedException();
        }

        public ReceiveHeadList HeadTable { get; set; }
        public ReceiveLineList LineTable { get; set; }



        private async Task LoadDataAsync(ReceiveOrderSearchDto req)
        {
            var rst = await _receiveApi.GetPagedListAsync(req);
            if (rst.Success)
            {
                HeadTable.ReLoadRangeFromDtoList(rst.Data.Items);
            }

        }

        public RelayCommand CreatReceiptDetailCommand { get; set; }
        public RelayCommand EditReceiptDetailCommand { get; set; }
        public RelayCommand DeleteReceiptDetailCommand { get; set; }
        public RelayCommand RefreshReceiptDetailCommand { get; set; }
        public RelayCommand PostReceiptDetailCommand { get; set; }






    }
}
