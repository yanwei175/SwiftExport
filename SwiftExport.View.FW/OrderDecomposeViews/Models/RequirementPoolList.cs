using Mapster;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.Mvvm;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SwiftExport.View.FW.OrderDecomposeViews.Models
{
    /// <summary>
    /// 需求池集合模型。
    /// </summary>
    public class DependentDemandList : ViewModelBaseEx
    {
        public ObservableCollection<DependentDemandModel> Items { get; } = new ObservableCollection<DependentDemandModel>();

        public DependentDemandList()
        {
            Items.CollectionChanged += Items_CollectionChanged;
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<DependentDemandModel>())
                    item.PropertyChanged += Item_PropertyChanged;
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<DependentDemandModel>())
                    item.PropertyChanged -= Item_PropertyChanged;
            }

            RaisePropertyChanged(nameof(IsAllValid));
            RaisePropertyChanged(nameof(Count));
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DependentDemandModel.State))
            {
                RaisePropertyChanged(nameof(IsAllValid));
            }
        }

        public int Count => Items.Count;

        public bool IsAllValid => Items.All(x => x.IsValid);

        public void Clear()
        {
            Items.Clear();
            RaisePropertyChanged(nameof(IsAllValid));
            RaisePropertyChanged(nameof(Count));
        }

        public void ReLoadRangeFromDtoList<TDto>(IEnumerable<TDto> dtos)
        {
            Clear();
            if (dtos == null) return;

            foreach (var dto in dtos)
            {
                var model = dto.Adapt<DependentDemandModel>();
                model.State = ModelState.Unchanged;
                model.CommitChanges();
                Items.Add(model);
            }

            RaisePropertyChanged(nameof(IsAllValid));
            RaisePropertyChanged(nameof(Count));
        }
    }
}
