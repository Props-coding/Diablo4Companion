using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace D4Companion.State
{
    /// <summary>
    /// Gets a picture from the clipboard as a bitmap the scanner can read.
    /// Handles screenshots (Win+Shift+S, Print Screen) and an image file copied in Explorer.
    /// </summary>
    public static class ClipboardImage
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        public static Bitmap? Read()
        {
            // The Snipping Tool also puts a PNG on the clipboard. It keeps the exact pixels, so prefer it.
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream png)
            {
                return ToBitmap(png);
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource source)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return ToBitmap(stream);
            }

            if (Clipboard.ContainsFileDropList())
            {
                var file = Clipboard.GetFileDropList().Cast<string>()
                    .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                if (file != null && File.Exists(file))
                {
                    using var stream = File.OpenRead(file);
                    return ToBitmap(stream);
                }
            }

            return null;
        }

        // Copies into a 24-bit bitmap that doesn't depend on the stream staying open.
        private static Bitmap ToBitmap(Stream stream)
        {
            stream.Position = 0;
            using var loaded = new Bitmap(stream);
            var copy = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
            }
            return copy;
        }
    }
}
