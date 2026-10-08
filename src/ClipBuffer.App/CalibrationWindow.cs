using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClipBuffer.Core;

namespace ClipBuffer.App;

public partial class CalibrationWindow : Window
{
    private Point _start;
    private bool _dragging;

    public double NormX { get; private set; }
    public double NormY { get; private set; }
    public double NormW { get; private set; }
    public double NormH { get; private set; }
    public bool Confirmed { get; private set; }

    public CalibrationWindow()
    {
        Title = "Calibrer le killfeed";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        Cursor = Cursors.Cross;

        var root = new Grid();
        var hint = new TextBlock
        {
            Text = "Glisse un rectangle autour du killfeed (haut droite), puis Entrée. Échap pour annuler.",
            Foreground = Brushes.White,
            FontSize = 18,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        root.Children.Add(hint);

        SelectionRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(110, 168, 254)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 110, 168, 254)),
            Visibility = Visibility.Collapsed
        };
        Canvas = new Canvas();
        Canvas.Children.Add(SelectionRect);
        root.Children.Add(Canvas);
        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
    }

    private Canvas Canvas { get; }
    private Rectangle SelectionRect { get; }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(Canvas);
        System.Windows.Controls.Canvas.SetLeft(SelectionRect, _start.X);
        System.Windows.Controls.Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetPosition(Canvas);
        var x = Math.Min(_start.X, pos.X);
        var y = Math.Min(_start.Y, pos.Y);
        var w = Math.Abs(pos.X - _start.X);
        var h = Math.Abs(pos.Y - _start.Y);
        System.Windows.Controls.Canvas.SetLeft(SelectionRect, x);
        System.Windows.Controls.Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Confirmed = false;
            Close();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, (int)ActualWidth, (int)ActualHeight);
        var left = System.Windows.Controls.Canvas.GetLeft(SelectionRect);
        var top = System.Windows.Controls.Canvas.GetTop(SelectionRect);
        if (double.IsNaN(left) || SelectionRect.Width < 20 || SelectionRect.Height < 20)
        {
            return;
        }

        NormX = left / screen.Width;
        NormY = top / screen.Height;
        NormW = SelectionRect.Width / screen.Width;
        NormH = SelectionRect.Height / screen.Height;
        Confirmed = true;
        Close();
    }
}
