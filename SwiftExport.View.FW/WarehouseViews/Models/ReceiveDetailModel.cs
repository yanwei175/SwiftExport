using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.WarehouseViews.Models
{
    public class ReceiveDetailModel : ViewModelBaseEx
    {
        private ReceiveHeadModel _receiveHeadModel = new ReceiveHeadModel();
        public ReceiveHeadModel ReceiveHeadModel
        {
            get => _receiveHeadModel;
            set => SetProperty(ref _receiveHeadModel, value);
        }

        private ReceiveLineList _receiveLineList = new ReceiveLineList();
        public ReceiveLineList ReceiveLineList
        {
            get => _receiveLineList;
            set => SetProperty(ref _receiveLineList, value);
        }
    }
}
