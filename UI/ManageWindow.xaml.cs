using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskFrames.Frames;
using DeskFrames.Imaging;
using DeskFrames.Model;

namespace DeskFrames.UI;

/// <summary>Eintrag in der Rahmenliste.</summary>
internal sealed class FrameItem : INotifyPropertyChanged
{
    private static readonly Brush NormalBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));
    private static readonly Brush ProblemBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18)));

    public FrameItem(FrameWindow window) { Window = window; }

    public FrameWindow Window { get; }
    public string Path => Window.Config.ImagePath;
    public string Name => string.IsNullOrEmpty(Path) ? "(no image)" : System.IO.Path.GetFileName(Path);

    public string Status => Window.ImageStatus switch
    {
        ImageStatus.Missing => "Image not found",
        ImageStatus.Unreadable => "Image can't be opened",
        ImageStatus.NoPath => "No image",
        _ => FolderName(Path),
    };

    private static string FolderName(string path)
    {
        try { return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? ""; }
        catch (ArgumentException) { return ""; }
    }

    public Brush StatusBrush => Window.ImageStatus == ImageStatus.Ok ? NormalBrush : ProblemBrush;

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); }
    }

    public void Refresh()
    {
        foreach (var p in new[] { nameof(Name), nameof(Path), nameof(Status), nameof(StatusBrush) })
            PropertyChanged?.Invoke(this, new(p));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}

/// <summary>
/// Verwaltungsfenster: Liste aller Rahmen mit Vorschau, Bild wechseln, Farbe, Breite, Entfernen, Autostart.
/// Änderungen wirken sofort auf dem Desktop und werden gespeichert.
/// </summary>
internal partial class ManageWindow : Window
{
    private readonly AppController _controller;
    private readonly ObservableCollection<FrameItem> _items = new();
    private readonly Dictionary<string, ImageSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private bool _updating;   // Anzeige wird gefüllt – keine Änderungen zurückschreiben, nicht blinken
    private string? _previewKey;

    public ManageWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Icon = AppIcon.LoadWpf();
        FrameList.ItemsSource = _items;

        foreach (var (name, color) in StylePresets.Colors)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                ToolTip = name,
                CommandParameter = color,
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, name);   // für Bildschirmleser
            swatch.Click += (_, _) => SetColor(color);
            Swatches.Children.Insert(Swatches.Children.Count - 1, swatch);
        }

        _controller.Frames.FramesChanged += OnFramesChanged;
        Closed += (_, _) => _controller.Frames.FramesChanged -= OnFramesChanged;

        RebuildList();
        if (_items.Count > 0)
        {
            _updating = true;
            FrameList.SelectedIndex = 0;   // beim Öffnen nicht blinken
            _updating = false;
        }
        ShowDetails();
        RefreshAutostart();
    }

    private FrameItem? Selected => FrameList.SelectedItem as FrameItem;

    /// <summary>Rahmen in der Liste auswählen (aus Kontextmenü oder nach „Add frame“).</summary>
    public void Select(FrameWindow frame)
    {
        var item = _items.FirstOrDefault(i => i.Window == frame);
        if (item == null) { RebuildList(); item = _items.FirstOrDefault(i => i.Window == frame); }
        if (item == null) return;
        _updating = true;
        FrameList.SelectedItem = item;
        FrameList.ScrollIntoView(item);
        _updating = false;
        ShowDetails();
    }

    public void RefreshAutostart() => AutostartBox.IsChecked = _controller.Autostart.IsEnabled;

    // ---------- Liste ----------

    private void OnFramesChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnFramesChanged); return; }
        RebuildList();
        ShowDetails();
    }

    /// <summary>Liste an die Rahmen anpassen, Auswahl beibehalten.</summary>
    private void RebuildList()
    {
        var windows = _controller.Frames.Windows;
        var selected = Selected?.Window;
        _updating = true;

        for (int i = _items.Count - 1; i >= 0; i--)
            if (!windows.Contains(_items[i].Window)) _items.RemoveAt(i);
        foreach (var w in windows)
            if (_items.All(i => i.Window != w)) _items.Add(new FrameItem(w));

        foreach (var item in _items)
        {
            item.Refresh();
            LoadThumbnail(item);
        }

        if (selected != null && _items.FirstOrDefault(i => i.Window == selected) is { } keep) FrameList.SelectedItem = keep;
        else if (Selected == null && _items.Count > 0) FrameList.SelectedIndex = 0;
        _updating = false;

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = Selected != null;
    }

    private async void LoadThumbnail(FrameItem item)
    {
        string path = item.Path;
        string key = path + "|" + item.Window.ImageStatus;
        if (_thumbnails.TryGetValue(key, out var cached)) { item.Thumbnail = cached; return; }
        var img = await Task.Run(() => ImageLoader.Load(path, 96));
        _thumbnails[key] = img.Bitmap;
        if (item.Path == path) item.Thumbnail = img.Bitmap;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = Selected != null;
        ShowDetails();
        // Gewählten Rahmen auf dem Desktop kurz aufblinken lassen, damit man ihn findet
        if (!_updating && Selected is { } item) _controller.Frames.Blink(item.Window);
    }

    // ---------- Details ----------

    private async void ShowDetails()
    {
        var item = Selected;
        Details.IsEnabled = item != null;
        Details.Opacity = item != null ? 1 : 0.5;
        if (item == null)
        {
            FilePath.Text = "";
            PreviewImage.Source = null;
            PreviewMissing.Visibility = Visibility.Collapsed;
            _previewKey = null;
            return;
        }

        var cfg = item.Window.Config;
        _updating = true;
        WidthSlider.Value = cfg.BorderWidth;
        _updating = false;
        WidthText.Text = cfg.BorderWidth == 0 ? "No border" : $"{cfg.BorderWidth} px";
        FilePath.Text = string.IsNullOrEmpty(cfg.ImagePath) ? "No image" : cfg.ImagePath;
        FilePath.ToolTip = cfg.ImagePath;

        foreach (var swatch in Swatches.Children.OfType<Button>().Where(b => b.CommandParameter is string))
            swatch.Tag = string.Equals((string)swatch.CommandParameter, cfg.BorderColor, StringComparison.OrdinalIgnoreCase) ? "selected" : null;

        // Vorschau in echten Proportionen: Bild + Rahmen werden gemeinsam verkleinert
        PreviewFrame.Width = cfg.Width + 2 * cfg.BorderWidth;
        PreviewFrame.Height = cfg.Height + 2 * cfg.BorderWidth;
        PreviewFrame.BorderThickness = new Thickness(cfg.BorderWidth);
        PreviewFrame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cfg.BorderColor));

        // Gleiches Bild wie zuletzt: nicht neu laden (Schieberegler, Farbwechsel)
        string path = cfg.ImagePath;
        string key = path + "|" + item.Window.ImageStatus;
        if (key == _previewKey) return;
        _previewKey = key;
        var preview = await Task.Run(() => ImageLoader.Load(path, 640));
        if (Selected != item || item.Window.Config.ImagePath != path) return;
        PreviewImage.Source = preview.Bitmap;
        PreviewMissing.Text = preview.Status switch
        {
            ImageStatus.Unreadable => "Image can't be opened",
            ImageStatus.NoPath => "No image",
            _ => "Image not found",
        };
        PreviewMissing.Visibility = preview.Status == ImageStatus.Ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetColor(string color)
    {
        if (Selected is not { } item) return;
        _controller.Frames.SetBorder(item.Window, color, item.Window.Config.BorderWidth);
    }

    private void OnCustomColor(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        if (AppController.PickColor(item.Window.Config.BorderColor) is { } c) SetColor(c);
    }

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || Selected is not { } item || !IsLoaded) return;
        int width = (int)Math.Round(e.NewValue);
        WidthText.Text = width == 0 ? "No border" : $"{width} px";
        _controller.Frames.SetBorder(item.Window, item.Window.Config.BorderColor, width);
    }

    private void OnApplyToAll(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var cfg = item.Window.Config;
        _controller.Frames.ApplyBorderToAll(cfg.BorderColor, cfg.BorderWidth);
    }

    // ---------- Schaltflächen ----------

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        if (await _controller.AddFrameAsync() is { } frame) Select(frame);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) _controller.RemoveFrame(item.Window);
    }

    private async void OnChangeImage(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) await _controller.ChangeImageAsync(item.Window);
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e) =>
        _controller.SetAutostart(AutostartBox.IsChecked == true);
}
