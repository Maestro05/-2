using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using КГ_2.Rendering;


namespace КГ_2
{
    public partial class MainWindow : Window
    {
        // ===== Модель =====
        private ZBufferRenderer _renderer;
        private List<Polygon3D> _scene;

        // ===== Состояние прохода =====
        private int _polyIndex = 0;
        private IEnumerator<(int y, List<(double, double, double, double)> spans)>? _scanIter;
        private DispatcherTimer _timer;

        // ===== Режим =====
        private bool _compareMode = false;   // false = с Z-буфером, true = без
        private long _overwrites = 0;

        // ===== Геометрия =====
        private const int BufferSize = 500;
        private const double Scale = 60.0;
        private double _cx, _cy;

        public MainWindow()
        {
            InitializeComponent();

            _renderer = new ZBufferRenderer(BufferSize, BufferSize);
            _scene = Scene.Create();
            _cx = BufferSize / 2.0;
            _cy = BufferSize / 2.0;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _timer.Tick += (_, __) => DoStep();

            StartNextPolygon();
            Redraw();
            AppendLog("Готово. «Шаг» — одна строка, «Авто» — по таймеру, «Сброс» — с нуля.");
        }

        // Логика шага 

        private void StartNextPolygon()
        {
            if (_polyIndex >= _scene.Count)
            {
                _scanIter = null;
                return;
            }
            _scanIter = _renderer.RasterizeScanlines(_scene[_polyIndex], Scale, _cx, _cy).GetEnumerator();
        }

        private void DoStep()
        {
            if (_scanIter == null)
            {
                FinishAll();
                return;
            }

            if (_scanIter.MoveNext())
            {
                var (y, spans) = _scanIter.Current;
                var poly = _scene[_polyIndex];

                // Отрисовка одной строки с Z-тестом
                _renderer.FillScanline(y, spans, poly.R, poly.G, poly.B);

                // Статистика
                _overwrites += _renderer.LastLineVisible;

                // Правая панель
                UpdateStepPanel(y, spans, poly);
                AppendLog($"Полигон {_polyIndex + 1}/{_scene.Count}, y={y}, " +
                          $"spans={spans.Count}, видимо={_renderer.LastLineVisible}, " +
                          $"скрыто={_renderer.LastLineSkipped}");

                // Подсветка на сцене
                DrawOverlay(y, spans, poly);
            }
            else
            {
                _polyIndex++;
                StartNextPolygon();
                if (_scanIter == null) FinishAll();
            }

            Redraw();
        }

        private void FinishAll()
        {
            _timer.Stop();
            AutoButton.Content = "Авто ▶";

            PolyInfoText.Text = $"Все {_scene.Count} полигонов отрисованы.";
            VerticesText.Text = "—";
            RowText.Text = "—";
            SpansText.Text = "—";
            PixelsText.Text = "—";
            ZTestText.Text = "—";


            OverlayCanvas.Children.Clear();
            AppendLog("=== Все полигоны отрисованы ===");
        }

        // Правая панель 

        private void UpdateStepPanel(int y,
            List<(double xStart, double xEnd, double zStart, double zEnd)> spans,
            Polygon3D poly)
        {
            // Полигон
            PolyInfoText.Text = $"#{_polyIndex + 1} из {_scene.Count}   (рёбер: {poly.EdgeCount})";

            // Вершины 3D -> 2D
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < poly.Vertices.Length; i++)
            {
                var v = poly.Vertices[i];
                var (sx, sy, _) = _renderer.Project(v, Scale, _cx, _cy);
                sb.AppendLine($"V{i}: {v}  ->  ({sx:F1}, {sy:F1})");
            }
            VerticesText.Text = sb.ToString().TrimEnd();

            // Строка
            RowText.Text = $"y = {y}";

            // Интервалы
            if (spans.Count == 0)
            {
                SpansText.Text = "нет (граница)";
            }
            else
            {
                var sb2 = new System.Text.StringBuilder();
                for (int i = 0; i < spans.Count; i++)
                {
                    var (xs, xe, zs, ze) = spans[i];
                    sb2.AppendLine($"#{i + 1}: x ∈ [{xs:F1} .. {xe:F1}], z: {zs:F2} .. {ze:F2}");
                }
                SpansText.Text = sb2.ToString().TrimEnd();
            }

            // Пиксели строки
            int total = _renderer.LastLineVisible + _renderer.LastLineSkipped;
            PixelsText.Text = $"обработано: {total}\n" +
                              $"видимо:    {_renderer.LastLineVisible}\n" +
                              $"скрыто:    {_renderer.LastLineSkipped}";

            // Последний Z-тест
            if (_renderer.LastPixelX >= 0)
            {
                string verdict = _renderer.LastPixelVisible ? "ВИДИМ" : "скрыт (дальше)";
                string zOldStr = double.IsPositiveInfinity(_renderer.LastPixelZOld)
                    ? "+∞"
                    : _renderer.LastPixelZOld.ToString("F2");

                ZTestText.Text =
                    $"пиксель ({_renderer.LastPixelX}, {_renderer.LastPixelY})\n" +
                    $"z_new = {_renderer.LastPixelZ:F2}\n" +
                    $"z_old = {zOldStr}\n" +
                    $"вердикт: {verdict}";
            }
            else
            {
                ZTestText.Text = "—";
            }

            // Глобальный счётчик
            OverwriteText.Text = _overwrites.ToString();
        }

        // Подсветка на сцене

        private void DrawOverlay(int y,
            List<(double xStart, double xEnd, double zStart, double zEnd)> spans,
            Polygon3D poly)
        {
            OverlayCanvas.Children.Clear();

            // 1. Вершины текущего полигона (зелёные кружки + подписи)
            for (int i = 0; i < poly.Vertices.Length; i++)
            {
                var v = poly.Vertices[i];
                var (sx, sy, _) = _renderer.Project(v, Scale, _cx, _cy);

                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Stroke = Brushes.Lime,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(60, 0, 255, 0))
                };
                Canvas.SetLeft(dot, sx - 4);
                Canvas.SetTop(dot, sy - 4);
                OverlayCanvas.Children.Add(dot);

                var label = new TextBlock
                {
                    Text = $"V{i}",
                    Foreground = Brushes.Lime,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12
                };
                Canvas.SetLeft(label, sx + 6);
                Canvas.SetTop(label, sy - 8);
                OverlayCanvas.Children.Add(label);
            }

            // 2. Жёлтая строка y
            var line = new Line
            {
                X1 = 0,
                X2 = BufferSize,
                Y1 = y + 0.5,
                Y2 = y + 0.5,
                Stroke = Brushes.Yellow,
                StrokeThickness = 1.2
            };
            OverlayCanvas.Children.Add(line);

            // 3. Фиолетовые интервалы на строке
            foreach (var (xs, xe, _, _) in spans)
            {
                var seg = new Line
                {
                    X1 = xs,
                    X2 = xe,
                    Y1 = y + 0.5,
                    Y2 = y + 0.5,
                    Stroke = Brushes.Magenta,
                    StrokeThickness = 2.2
                };
                OverlayCanvas.Children.Add(seg);
            }

            // 4. Красный квадрат — последний проверенный пиксель
            if (_renderer.LastPixelX >= 0)
            {
                int px = _renderer.LastPixelX;
                int py = _renderer.LastPixelY;

                var marker = new Rectangle
                {
                    Width = 6,
                    Height = 6,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(marker, px - 3);
                Canvas.SetTop(marker, py - 3);
                OverlayCanvas.Children.Add(marker);

                var lbl = new TextBlock
                {
                    Text = $"({px}, {py})",
                    Foreground = Brushes.Red,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                };
                Canvas.SetLeft(lbl, px + 5);
                Canvas.SetTop(lbl, py + 5);
                OverlayCanvas.Children.Add(lbl);
            }
        }

        // Отрисовка буферов 

        private void Redraw()
        {
            SceneImage.Source = BitmapHelper.FromArgb(_renderer.ColorBuffer, _renderer.W, _renderer.H);
            ZBufferImage.Source = BitmapHelper.FromArgb(_renderer.BuildZBufferImage(), _renderer.W, _renderer.H);
        }

        // Кнопки

        private void StepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_timer.IsEnabled) return;   // пока авто — не реагируем
            DoStep();
        }

        private void AutoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
                AutoButton.Content = "Авто ▶";
            }
            else
            {
                _timer.Start();
                AutoButton.Content = "Пауза ⏸";
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            AutoButton.Content = "Авто ▶";

            _renderer.Reset();
            _renderer.UseZTest = !_compareMode;

            _polyIndex = 0;
            _overwrites = 0;
            OverwriteText.Text = "0";
            PolyInfoText.Text = "—";
            VerticesText.Text = "—";
            RowText.Text = "—";
            SpansText.Text = "—";
            PixelsText.Text = "—";
            ZTestText.Text = "—";

            OverlayCanvas.Children.Clear();
            StartNextPolygon();
            Redraw();
            AppendLog("--- Сброс ---");
        }

        private void ToggleCompareButton_Click(object sender, RoutedEventArgs e)
        {
            _compareMode = !_compareMode;
            _renderer.UseZTest = !_compareMode;

            if (_compareMode)
            {
                ToggleCompareButton.Content = "С Z-буфером";
                ModeText.Text = "Z-буфер ОТКЛЮЧЁН (сравнение)";
                ModeText.Foreground = Brushes.DarkRed;
                AppendLog("--- РЕЖИМ: БЕЗ Z-буфера (дальние полигоны перекрывают ближние) ---");
            }
            else
            {
                ToggleCompareButton.Content = "Без Z-буфера";
                ModeText.Text = "Z-буфер включён";
                ModeText.Foreground = Brushes.DarkGreen;
                AppendLog("--- РЕЖИМ: С Z-буфером ---");
            }

            // Сразу перезапускаем, чтобы разница была видна
            ResetButton_Click(sender, e);
        }

        // ================= Лог =================

        private void AppendLog(string s)
        {
            LogText.AppendText(s + Environment.NewLine);
            LogText.ScrollToEnd();
        }
    }
}