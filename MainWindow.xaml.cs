using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lines
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly Window _mainWindow;

        private bool _isDrawing;
        private bool _isSelecting;
        private Point _selectionStart;
        private Point _selectionEnd;
        private Point _drawingStart;
        private Point _drawingEnd;
        private Color _defaultStrokeColor;
        private Color _highlightedStrokeColor;
        private Rectangle? _selection;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsDrawing
        {
            get
            {
                return _isDrawing;
            }
            set
            {
                _isDrawing = value;
                if (IsSelecting != !value)
                    IsSelecting = !value;
                NotifyPropertyChanged("IsDrawing");
            }
        }
        public bool IsSelecting
        {
            get
            {
                return _isSelecting;
            }
            set
            {
                _isSelecting = value;
                if (IsDrawing != !value)
                    IsDrawing = !value;
                NotifyPropertyChanged("IsSelecting");
            }
        }
        public int Latency { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            _mainWindow = Application.Current.MainWindow;
            _mainWindow.DataContext = this;
            _mainWindow.Height = SystemParameters.PrimaryScreenHeight / 1.5;
            _mainWindow.Width = SystemParameters.PrimaryScreenWidth / 1.5;
            _defaultStrokeColor = Brushes.LightGray.Color;
            _highlightedStrokeColor = Brushes.DarkRed.Color;
            IsDrawing = true;
            IsSelecting = false;
        }

        #region Private methods
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Окрашивает переданные штрихи в заданный цвет, а остальные - в заданный цвет по умолчанию.
        /// </summary>
        /// <param name="strokesToPaint">Штрихи для окрашивания</param>
        /// <param name="highLightColor">Цвет для окрашивания</param>
        /// <param name="strokesToDefault">Штрихи для окрашивания в цвет по умолчанию</param>
        /// <param name="defaultColor">Цвет по умолчанию</param>
        /// <returns></returns>
        private Task PaintStrokes(IEnumerable<Stroke> strokesToPaint, Color highLightColor, IEnumerable<Stroke> strokesToDefault, Color defaultColor)
        {
            var strokesToPaintDefault = strokesToDefault.Where(s => s.DrawingAttributes.Color != defaultColor).ToList();
            foreach (Stroke stroke in strokesToPaintDefault)
            {
                stroke.DrawingAttributes = new DrawingAttributes() { Color = defaultColor };
            }

            var strokesToPaintHighlighted = strokesToPaint.Where(s => s.DrawingAttributes.Color != highLightColor).ToList();
            foreach (Stroke stroke in strokesToPaintHighlighted)
            {
                stroke.DrawingAttributes = new DrawingAttributes() { Color = highLightColor };
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Очищает полотно и устанавливает _selection в null
        /// </summary>
        /// <param name="canvas">Полотно для очистки</param>
        /// <returns></returns>
        private Task ClearCanvas(InkCanvas canvas)
        {
            if (MessageBox.Show("Очистить полотно?", "Вы уверены?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                canvas.Children.Clear();
                canvas.Strokes.Clear();
                _selection = null;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Возвращает список штрихов на полотне, которые попали в область прямоугольника selection
        /// </summary>
        /// <param name="selection">Прямоугольник, область выбора. Должен находиться в коллекции Children передаваемого полотна.</param>
        /// <param name="canvas">Полотно, на котором производится поиск.</param>
        /// <returns>Список штрихов, которые попали в область прямоугольника selection.</returns>
        private Task<List<Stroke>> CalculateStrokes(Rectangle selection, InkCanvas canvas)
        {
            if (selection is null)
                throw new ArgumentNullException(nameof(selection));

            if (!canvas.Children.Contains(selection))
                throw new ArgumentException("Параметр selection не находится в коллекции Children переданного InkCanvas!");

            var topLeft = new Point(InkCanvas.GetLeft(selection), InkCanvas.GetTop(selection));
            var bottomRight = new Point(topLeft.X + selection.Width, topLeft.Y + selection.Height);

            // Выбираем штрихи, в которых любая из точек находится в границах _selection и которым требуется покраска
            var strokes = canvas.Strokes.Where(s =>
                                                        s.StylusPoints.Any(p => (p.X >= topLeft.X && p.Y >= topLeft.Y) && (p.X <= bottomRight.X && p.Y <= bottomRight.Y))).ToList();

            return Task.FromResult(strokes);
        }

        #endregion

        #region Events
        private void ToolbarPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                _mainWindow.WindowState = WindowState.Normal;
                return;
            }

            if (WindowState == WindowState.Normal)
            {
                _mainWindow.WindowState = WindowState.Maximized;
                return;
            }
        }

        private void btnMinimize_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.WindowState = WindowState.Minimized;
        }

        private void btDraw_Checked(object sender, RoutedEventArgs e)
        {
            IsDrawing = true;
            icCanvas.EditingMode = InkCanvasEditingMode.None;
            icCanvas.MouseDown -= Canvas_MouseDown_Selection;
            icCanvas.MouseDown += Canvas_MouseDown_Drawing;
        }

        private void btSelect_Checked(object sender, RoutedEventArgs e)
        {
            IsSelecting = true;
            icCanvas.EditingMode = InkCanvasEditingMode.None;
            icCanvas.MouseDown -= Canvas_MouseDown_Drawing;
            icCanvas.MouseDown += Canvas_MouseDown_Selection;
        }

        private void Canvas_MouseDown_Selection(object sender, MouseButtonEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            icCanvas.Children.Remove(_selection);

            _selection = new Rectangle();
            _selection.Stroke = Brushes.Red;
            _selection.StrokeThickness = 1;
            _selection.Height = 1;
            _selection.Width = 1;

            _selectionStart = e.GetPosition(canvas);

            InkCanvas.SetLeft(_selection, _selectionStart.X);
            InkCanvas.SetRight(_selection, _selectionStart.X);
            InkCanvas.SetTop(_selection, _selectionStart.Y);
            InkCanvas.SetBottom(_selection, _selectionStart.Y);

            canvas.Children.Add(_selection);

            canvas.MouseMove += Canvas_MouseMove_Selection;
            canvas.MouseUp += Canvas_MouseUp_Selection;
        }

        private void Canvas_MouseMove_Selection(object sender, MouseEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (_selection is null)
                return;

            _selectionEnd = e.GetPosition(canvas);

            if ((_selectionEnd.X - _selectionStart.X) < 0)
                InkCanvas.SetLeft(_selection, _selectionEnd.X);

            if ((_selectionEnd.Y - _selectionStart.Y) < 0)
                InkCanvas.SetTop(_selection, _selectionEnd.Y);

            _selection.Width = Math.Abs(_selectionEnd.X - _selectionStart.X);
            _selection.Height = Math.Abs(_selectionEnd.Y - _selectionStart.Y);
        }

        private void Canvas_MouseUp_Selection(object sender, MouseButtonEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            canvas.MouseMove -= Canvas_MouseMove_Selection;
            canvas.MouseUp -= Canvas_MouseUp_Selection;
        }

        private void Canvas_MouseDown_Drawing(object sender, MouseButtonEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            _drawingStart = e.GetPosition(canvas);

            canvas.MouseMove += Canvas_MouseMove_Drawing;
            canvas.MouseUp += Canvas_MouseUp_Drawing;
        }

        private void Canvas_MouseMove_Drawing(object sender, MouseEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (Latency > 0)
                Thread.Sleep(Latency); // Замедляем рисование, чтобы было проще различить отдельные штрихи

            _drawingEnd = e.GetPosition(canvas);

            var stroke = new Stroke(new StylusPointCollection(new List<Point>() { _drawingStart, _drawingEnd }));
            stroke.DrawingAttributes = new DrawingAttributes() { Color = _defaultStrokeColor };

            canvas.Strokes.Add(stroke);

            _drawingStart = _drawingEnd;
        }

        private void Canvas_MouseUp_Drawing(object sender, MouseButtonEventArgs e)
        {
            if (sender is not InkCanvas)
                return;

            var canvas = (InkCanvas)sender;

            canvas.MouseMove -= Canvas_MouseMove_Drawing;
            canvas.MouseUp -= Canvas_MouseUp_Drawing;
        }

        private async void btClear_Click(object sender, RoutedEventArgs e)
        {
            await ClearCanvas(icCanvas);
        }

        private async void btCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selection is null)
                {
                    MessageBox.Show($"Необходимо выбрать область для расчёта!");
                    return;
                }

                var stopwatch = new Stopwatch();

                stopwatch.Start();
                var strokes = await CalculateStrokes(_selection, icCanvas);
                stopwatch.Stop();
                string calculateElapsed = stopwatch.Elapsed.Milliseconds.ToString();
                stopwatch.Reset();

                if (strokes.Count == 0) 
                {
                    MessageBox.Show("Ни одного штриха не найдено в области расчёта!");
                    return;
                }

                stopwatch.Start();
                await PaintStrokes(strokes, _highlightedStrokeColor, icCanvas.Strokes, _defaultStrokeColor);
                stopwatch.Stop();
                string paintingElapsed = stopwatch.Elapsed.Milliseconds.ToString();

                MessageBox.Show($"Кол-во штрихов в области расчёта: {strokes.Count}. Всего: {icCanvas.Strokes.Count}. " +
                                $"\r\nНа расчёты затрачено: {calculateElapsed}мс. На покраску затрачено: {paintingElapsed}мс.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка! {ex.Message}");
            }
        }

        #endregion
    }
}
