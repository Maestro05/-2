using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace КГ_2.Rendering
{
    /// <summary>
    /// Превращает массив ARGB (int[]) в BitmapSource для отображения в Image.
    /// </summary>
    public static class BitmapHelper
    {
        public static BitmapSource FromArgb(int[] argb, int w, int h)
        {
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

            // Наш буфер хранит int вида (A<<24)|(R<<16)|(G<<8)|B.
            // WPF ждёт Bgra32 — в памяти это байты B,G,R,A, что для little-endian int
            // как раз даёт тот же порядок A,R,G,B в старших разрядах.
            // То есть перекладывать ничего не нужно, но копируем в отдельный массив,
            // чтобы WritePixels получил continuous buffer.
            var bgra = new int[argb.Length];
            System.Array.Copy(argb, bgra, argb.Length);

            bmp.WritePixels(new Int32Rect(0, 0, w, h), bgra, w * 4, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
