namespace КГ_2.Rendering
{
    // <summary>
    /// Ядро метода Z-буфера
    /// Хранит буфер глубины (double[,]) и буфер цвета (int[] ARGB).
    /// Растеризация — scanline по строкам, Z-тест по каждому пикселю.
    /// </summary>
    public class ZBufferRenderer
    {
        // Буферы
        public int W, H;
        public double[,] ZBuffer;    // [x, y], init = +inf
        public int[] ColorBuffer;    // ARGB, индекс = y*W + x

        // Диапазон z для визуализации буфера в градациях серого
        public double ZNear = -1.0;
        public double ZFar = 2.0;

        // Если false — Z-тест отключается (режим "Без Z-буфера")
        public bool UseZTest = true;

        // Статистика последней строки
        public int LastLineVisible;   // сколько пикселей прошло Z-тест
        public int LastLineSkipped;   // сколько отброшено
        public int LastPixelX = -1;
        public int LastPixelY = -1;
        public double LastPixelZ;
        public double LastPixelZOld;
        public bool LastPixelVisible;

        public ZBufferRenderer(int w, int h)
        {
            W = w; H = h;
            ZBuffer = new double[w, h];
            ColorBuffer = new int[w * h];
            Reset();
        }

        public void Reset()
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    ZBuffer[x, y] = double.PositiveInfinity;

            Array.Clear(ColorBuffer, 0, ColorBuffer.Length);

            LastLineVisible = LastLineSkipped = 0;
            LastPixelX = LastPixelY = -1;
            LastPixelZ = LastPixelZOld = 0;
            LastPixelVisible = false;
        }

        // Проекция 3D -> экран (ортогональная)
        // sx = cx + X*scale, sy = cy - Y*scale (Y вверх -> вниз на экране).
        // z сохраняем как есть.
        public (double sx, double sy, double z) Project(Vec3 v, double scale, double cx, double cy)
        {
            return (cx + v.X * scale, cy - v.Y * scale, v.Z);
        }

        // Scanline-разбор одного полигона на строки 
        // Возвращает по одной строке за шаг: номер y и список интервалов [x_start..x_end]
        // с соответствующими z на концах.
        public IEnumerable<(int y, List<(double xStart, double xEnd, double zStart, double zEnd)> spans)>
            RasterizeScanlines(Polygon3D poly, double scale, double cx, double cy)
        {
            if (poly.Vertices.Length < 3) yield break;

            var proj = poly.Vertices.Select(v => Project(v, scale, cx, cy)).ToArray();
            int n = proj.Length;

            double yMin = proj.Min(p => p.sy);
            double yMax = proj.Max(p => p.sy);
            int y0 = (int)Math.Ceiling(yMin);
            int y1 = (int)Math.Floor(yMax);

            for (int y = y0; y <= y1; y++)
            {
                double yc = y + 0.5;
                var xs = new List<(double x, double z)>();

                for (int i = 0; i < n; i++)
                {
                    var a = proj[i];
                    var b = proj[(i + 1) % n];

                    // Пересечение с горизонтальной строкой yc
                    if ((a.sy <= yc && b.sy > yc) || (b.sy <= yc && a.sy > yc))
                    {
                        double t = (yc - a.sy) / (b.sy - a.sy);
                        double x = a.sx + t * (b.sx - a.sx);
                        double z = a.z + t * (b.z - a.z);
                        xs.Add((x, z));
                    }
                }

                xs.Sort((p1, p2) => p1.x.CompareTo(p2.x));

                // Пары (чётное число пересечений -> интервалы)
                var spans = new List<(double, double, double, double)>();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    spans.Add((xs[i].x, xs[i + 1].x, xs[i].z, xs[i + 1].z));

                yield return (y, spans);
            }
        }

        // Заполнение одной строки с Z-тестом
        // onPixel — необязательный колбэк на каждый проверенный пиксель:
        //   (x, y, z_new, z_old, visible)
        // Также обновляет статистику LastLine* и LastPixel*.
        public void FillScanline(
            int y,
            List<(double xStart, double xEnd, double zStart, double zEnd)> spans,
            byte r, byte g, byte b,
            Action<int, int, double, double, bool>? onPixel = null)
        {
            LastLineVisible = 0;
            LastLineSkipped = 0;

            LastPixelX = -1;
            LastPixelY = -1;
            LastPixelZ = 0;
            LastPixelZOld = 0;
            LastPixelVisible = false;
            if (y < 0 || y >= H) return;

            foreach (var (xs, xe, zs, ze) in spans)
            {
                int x0 = (int)Math.Ceiling(xs - 0.5);
                int x1 = (int)Math.Floor(xe - 0.5);
                if (x1 < x0) continue;

                double dzdx = (xe - xs) > 1e-9 ? (ze - zs) / (xe - xs) : 0;

                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || x >= W) continue;

                    double z = zs + (x + 0.5 - xs) * dzdx;

                    double zOld = ZBuffer[x, y];
                    bool visible = !UseZTest || z < zOld;

                    // статистика для правой панели
                    LastPixelX = x; LastPixelY = y;
                    LastPixelZ = z; LastPixelZOld = zOld;
                    LastPixelVisible = visible;

                    if (visible)
                    {
                        ZBuffer[x, y] = z;
                        ColorBuffer[y * W + x] = (255 << 24) | (r << 16) | (g << 8) | b;
                        LastLineVisible++;
                    }
                    else
                    {
                        LastLineSkipped++;
                    }

                    onPixel?.Invoke(x, y, z, zOld, visible);
                }
            }
        }

        // Визуализация Z-буфера в оттенках серого
        // +inf (пусто)     -> чёрный
        // ближе (маленький z) -> светлее
        public int[] BuildZBufferImage()
        {
            var img = new int[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    double z = ZBuffer[x, y];
                    byte shade;

                    if (double.IsPositiveInfinity(z))
                    {
                        shade = 0;
                    }
                    else
                    {
                        double t = (z - ZNear) / (ZFar - ZNear);
                        t = Math.Clamp(t, 0, 1);
                        shade = (byte)(255 * (1 - t));
                    }

                    img[y * W + x] = (255 << 24) | (shade << 16) | (shade << 8) | shade;
                }
            }
            return img;
        }
    }
}
