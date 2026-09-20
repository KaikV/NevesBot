using KBot.App.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KBot.App.Views;

public sealed class DetectionDebugWindow : Window
{
    private readonly KBotLifecycle _lifecycle;
    private readonly TextBlock _details;
    private readonly Canvas _canvas;

    public DetectionDebugWindow(KBotLifecycle lifecycle)
    {
        _lifecycle = lifecycle;
        Title = "PokeAlliance · Ver detecção";
        Width = 1100;
        Height = 750;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        var root = new DockPanel();
        _details = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(_details, Dock.Top);
        root.Children.Add(_details);
        _canvas = new Canvas { Background = Brushes.Black };
        root.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        _lifecycle.Changed += Refresh;
        Closed += (_, _) => _lifecycle.Changed -= Refresh;
        Draw();
    }

    private void Refresh(KBotLifecycle _) => Dispatcher.InvokeAsync(Draw);

    private void Draw()
    {
        var result = _lifecycle.LastDetection;
        _details.Text = result is null ? "Aguardando captura da janela do jogo..." :
            $"Reader: {result.ReaderStatus} — {result.ReaderMessage}\n" +
            $"Vision: {result.VisionStatus}\n" +
            $"Character: {result.State}  ·  Detection: {result.DetectionStatus}  ·  Last InGame: {result.LastConfirmedInGame:HH:mm:ss}\n" +
            $"Confidence: {result.Confidence:P0}  ·  Source: {result.Source}";
        _canvas.Children.Clear();
        if (result?.Frame is null) return;
        var scale = Math.Min(1, 1020d / result.Frame.PixelWidth);
        var width = result.Frame.PixelWidth * scale;
        var height = result.Frame.PixelHeight * scale;
        _canvas.Width = width;
        _canvas.Height = height;
        _canvas.Children.Add(new Image { Source = result.Frame, Width = width, Height = height });
        foreach (var region in result.Regions)
        {
            var color = region.Detected ? Brushes.LimeGreen : Brushes.OrangeRed;
            var outline = new Rectangle
            {
                Width = region.Width * width, Height = region.Height * height,
                Stroke = color, StrokeThickness = 2, Fill = Brushes.Transparent
            };
            Canvas.SetLeft(outline, region.X * width);
            Canvas.SetTop(outline, region.Y * height);
            _canvas.Children.Add(outline);
            var label = new TextBlock { Text = region.Name, Foreground = Brushes.White, Background = color, FontSize = 11 };
            Canvas.SetLeft(label, region.X * width);
            Canvas.SetTop(label, region.Y * height);
            _canvas.Children.Add(label);
        }
    }
}
