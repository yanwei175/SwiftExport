# DataGrid 图片单元格样式

```xaml
<conv:ImgPathToImgConverter x:Key="PathToImage"/>    
<!--特殊单元格样式,0内边距,适合放图片-->
    <Style x:Key="ZeroPaddingCell" TargetType="DataGridCell">
        <Setter Property="Padding" Value="0"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="HorizontalContentAlignment" Value="Center"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Padding="{TemplateBinding Padding}" 
                            Background="Transparent" 
                            BorderBrush="Transparent" 
                            BorderThickness="0">
                        <ContentPresenter />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!--图片模板-->
    <DataTemplate x:Key="ImageCellTemplate">
        <Border CornerRadius="4" BorderThickness="0" BorderBrush="Transparent" Margin="1" ClipToBounds="True">
            <Image Stretch="Uniform"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   Source="{Binding MainImage, Converter={StaticResource PathToImage}, IsAsync=True}">

                <Image.ToolTip>
                    <ToolTip Background="{DynamicResource Brush_Bck_Card}" 
                             BorderThickness="0" 
                             HasDropShadow="True" 
                             Padding="4"
                             Placement="Mouse">
                        <Image Source="{Binding MainImage, Converter={StaticResource PathToImage}, IsAsync=True}"
                               Stretch="Uniform"
                               MaxWidth="500" MaxHeight="500" 
                               RenderOptions.BitmapScalingMode="HighQuality"/>
                    </ToolTip>
                </Image.ToolTip>
            </Image>
        </Border>
    </DataTemplate>
```



# 转换器

```c#
public class ImgPathToImgConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> _accessQueue = new LinkedList<string>();
    private static readonly object _lock = new object();
    private const int MaxCacheSize = 1000; // 缓存最大图片数量限制

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string path = value as string;

        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            lock (_lock)
            {
                if (_imageCache.TryGetValue(path, out var cachedImage))
                {
                    // 命中缓存，更新为最近使用
                    _accessQueue.Remove(path);
                    _accessQueue.AddLast(path);
                    return cachedImage;
                }
            }

            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();

            // 1. 立即加载，释放文件锁
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            // 2. 指定源
            bitmap.UriSource = new Uri(path, UriKind.Absolute);

            // 3. ★ 核心优化：只解码 100-400 像素宽度的缩略图 ★
            bitmap.DecodePixelWidth = 400;

            bitmap.EndInit();
            bitmap.Freeze(); // 冻结，高性能关键

            lock (_lock)
            {
                if (!_imageCache.ContainsKey(path))
                {
                    // 超过缓存限制时，移除最久未使用的图片 (LRU)
                    if (_imageCache.Count >= MaxCacheSize)
                    {
                        string oldest = _accessQueue.First.Value;
                        _accessQueue.RemoveFirst();
                        _imageCache.Remove(oldest);
                    }

                    _imageCache[path] = bitmap;
                    _accessQueue.AddLast(path);
                }
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
} 
```

