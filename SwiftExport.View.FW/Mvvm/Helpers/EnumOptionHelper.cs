using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm.Helpers
{
    public static class EnumOptionHelper
    {
        public static List<OptionItem<TEnum>> ToOptionItems<TEnum>()
            where TEnum : struct, Enum
        {
            return Enum.GetValues(typeof(TEnum))
                .Cast<TEnum>()
                .Select(x => new OptionItem<TEnum>
                {
                    Value = x,
                    Label = x.ToString()
                })
                .ToList();
        }

        public static ObservableCollection<CheckableItem<TEnum>> ToCheckableItems<TEnum>()
            where TEnum : struct, Enum
        {
            return new ObservableCollection<CheckableItem<TEnum>>(
                Enum.GetValues(typeof(TEnum))
                    .Cast<TEnum>()
                    .Select(x => new CheckableItem<TEnum>
                    {
                        Value = x,
                        Label = x.ToString(),
                        IsSelected = false
                    }));
        }
    }
}
