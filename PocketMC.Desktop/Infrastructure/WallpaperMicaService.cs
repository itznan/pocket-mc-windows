using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Simulates a Mica-like background for Windows 10 where the native
/// Mica backdrop is unavailable.
/// </summary>
public static class WallpaperMicaService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(
        int uAction, int uParam, System.Text.StringBuilder lpvParam, int fuWinIni);

    private const int SPI_GETDESKWALLPAPER = 0x0073;

    public static bool IsWindows11OrLater
        => Environment.OSVersion.Version.Build >= 22000;

    public static string GetWallpaperPath()
    {
        var sb = new System.Text.StringBuilder(260);
        SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
        return sb.ToString();
    }

    public static BitmapSource? CreateMicaBackground(
        int targetWidth,
        int targetHeight,
        double blurRadius = 80,
        double tintOpacity = 0.75,
        Color? tintColor = null)
    {
        var tint = tintColor ?? Color.FromRgb(32, 32, 32);

        try
        {
            var wallpaperPath = GetWallpaperPath();
            if (string.IsNullOrEmpty(wallpaperPath) || !File.Exists(wallpaperPath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(wallpaperPath);
            bitmap.DecodePixelWidth = targetWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, targetWidth, targetHeight);
                ctx.DrawImage(bitmap, rect);
                ctx.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(
                        (byte)(tintOpacity * 255),
                        tint.R, tint.G, tint.B)),
                    null, rect);
            }

            visual.Effect = new BlurEffect
            {
                Radius = blurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };

            var rtb = new RenderTargetBitmap(
                targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            return rtb;
        }
        catch
        {
            return null;
        }
    }
}
