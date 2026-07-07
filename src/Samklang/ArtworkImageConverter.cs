using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Samklang;

/// <summary>
/// Converts <see cref="ViewModels.NowPlayingViewModel.ArtworkBytes"/> (encoded image bytes) into a
/// frozen <see cref="BitmapImage"/> for the dashboard's artwork binding. Lives in the view layer so
/// the view model can stay framework-free; returns null for missing or undecodable bytes, which
/// simply leaves the artwork placeholder visible.
/// </summary>
public sealed class ArtworkImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            // OnLoad decodes fully inside BeginInit/EndInit so the MemoryStream can be disposed
            // right away, and Freeze makes the bitmap safely shareable/cacheable by WPF.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            // Undecodable artwork bytes are treated as "no artwork" — decoration, not data.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
