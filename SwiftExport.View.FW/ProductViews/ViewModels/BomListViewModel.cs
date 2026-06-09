using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using GalaSoft.MvvmLight.Command;
using System.Threading.Tasks;
using System.Linq;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.UiInterface;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class BomListViewModel : BaseListModel<BomItemModel>
    {
        private readonly IBomApi _bomApi;
        private readonly IDialogService _msg;

        public BomListViewModel(IBomApi bomApi, IDialogService msg)
        {
            _bomApi = bomApi;
            _msg = msg;
        }

        public async Task LoadDataAsync(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku))
            {
                Items.Clear();
                return;
            }

            var result = await _bomApi.GetBomTreeAsync(sku);
            
            if (result != null && result.Success && result.Data != null)
            {
                var dtos = FlattenBomTree(result.Data)
                    .Where(x => !string.Equals(x.SubSku, sku, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                    
                ReLoadRangeFromDtoList(dtos);
            }
            else
            {
                Clear();
            }
        }

        private System.Collections.Generic.IEnumerable<BomTreeDto> FlattenBomTree(System.Collections.Generic.IEnumerable<BomTreeDto> nodes)
        {
            if (nodes == null) yield break;

            foreach (var node in nodes)
            {
                if (node == null) continue;
                yield return node;

                if (node.Children == null || node.Children.Count == 0) continue;

                foreach (var child in FlattenBomTree(node.Children))
                {
                    yield return child;
                }
            }
        }

        public async Task BatchSaveAsync()
        {
            if (!IsDirty)
            {
                await _msg.ShowInfoAsync("无变更");
                return;
            }

            var request = new BatchRequestDto<BomDto>
            {
                AddList = Items.Where(x => x.State == ModelState.Added).Select(x => { var dto = new BomDto(); Mapster.TypeAdapter.Adapt(x, dto); return dto; }).ToList(),
                UpdateList = Items.Where(x => x.State == ModelState.Modified).Select(x => { var dto = new BomDto(); Mapster.TypeAdapter.Adapt(x, dto); return dto; }).ToList(),
                DeleteIdList = Items.Where(x => x.State == ModelState.Deleted).Select(x => x.Uid).ToList()
            };

            var rst = await _bomApi.BatchSaveAsync(request);
            if (rst.Success)
            {
                await _msg.ShowInfoAsync("保存成功");
            }
            else
            {
                await _msg.ShowErrorAsync("保存失败: " + rst.Message);
            }
        }
    }
}