using AudioStretch.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;

namespace AudioStretch;
public sealed partial class MapsWindow : Window
{
    public sealed class MapRow { public string Source { get; set; } = ""; public string Target { get; set; } = ""; }
    private readonly MainWindow _main;
    private readonly AudioInfo _source;
    private readonly ObservableCollection<MapRow> _rows = [];
    private readonly Dictionary<int, Maps> _documents = [];
    private Maps _draft;
    private bool _ready, _changing;
    private int _kind, _units;
    internal bool IsClosed { get; private set; }
    private MapKind SelectedKind => (MapKind)_kind;
    public MapsWindow(MainWindow main)
    {
        _main = main; _source = main.CurrentSource; _draft = main.CurrentMaps;
        InitializeComponent(); App.Themes.Attach(this); App.Accent.Attach(Root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        SourceDetails.Text = $"{_source.SampleRate:N0} Hz · {_source.FrameCount:N0} source frames · SHA-256 {_source.Sha256}";
        Rows.ItemsSource = _rows; _kind = _draft.PitchPoints.Count > 0 ? (int)_draft.PitchKind : 0;
        Kind.SelectedIndex = _kind; _ready = true; ShowRows();
        Root.Loaded += (_, _) => Placement.Restore(this, Root, settings: true);
        Closed += (_, _) => { IsClosed = true; App.Themes.Detach(this); App.Accent.Detach(Root); };
    }
    private Maps ReadRows()
    {
        // This editor selects one map family: upstream ignores time maps when pitch maps force real-time.
        return _main.CoreAccess.Call(new Request { Operation = "map_import", SourceInfo = _source, Maps = MainWindow.BoundMaps(_source), MapKind = SelectedKind, MapSeconds = _units == 1, MapText = string.Join("\n", _rows.Select(r => r.Source.Trim() + " " + r.Target.Trim())) }).Maps;
    }
    private string Export(Maps maps, bool seconds) => _main.CoreAccess.Call(new Request { Operation = "map_export", SourceInfo = _source, Maps = maps, MapKind = SelectedKind, MapSeconds = seconds }).Text;
    private void ShowRows()
    {
        _rows.Clear();
        foreach (string line in Export(_draft, _units == 1).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        { var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); _rows.Add(new() { Source = values[0], Target = values[1] }); }
        ColumnHelp.Text = _kind switch { 0 => "Source position → destination position. Both columns use the selected units.", 1 => "Source position → semitone offset (decimal point).", _ => "Source position → positive frequency multiplier (decimal point)." };
        Preview.Text = PlanSummary.Text = "";
    }
    private bool TryValidate(out Maps maps)
    {
        maps = _draft;
        try
        {
            maps = ReadRows(); var request = _main.RequestForMaps(maps); request.Operation = "plan";
            var plan = _main.CoreAccess.Call(request).Plan;
            Preview.Text = _kind == 0 ? plan.TimeMapText : plan.PitchMapText;
            PlanSummary.Text = plan.Summary + "\n" + string.Join("\n", plan.Warnings); ErrorBar.IsOpen = false; return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }
    private void ShowError(Exception error) { ErrorBar.Title = "Map needs attention"; ErrorBar.Message = error.Message; ErrorBar.IsOpen = true; }
    private void AddClicked(object sender, RoutedEventArgs e) => _rows.Add(new());
    private void RemoveClicked(object sender, RoutedEventArgs e) { if (sender is Button { DataContext: MapRow row }) _rows.Remove(row); }
    private void ValidateClicked(object sender, RoutedEventArgs e) => TryValidate(out _);
    private void ApplyClicked(object sender, RoutedEventArgs e) { if (TryValidate(out var maps)) { _main.ApplyMaps(maps); Close(); } }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _changing) return;
        try { _documents[_kind] = ReadRows(); _kind = Kind.SelectedIndex; _draft = _documents.TryGetValue(_kind, out var saved) ? saved : MainWindow.BoundMaps(_source); ShowRows(); ErrorBar.IsOpen = false; }
        catch (Exception error) { _changing = true; Kind.SelectedIndex = _kind; _changing = false; ShowError(error); }
    }
    private void UnitsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _changing) return;
        try { _draft = ReadRows(); _units = Units.SelectedIndex; ShowRows(); ErrorBar.IsOpen = false; }
        catch (Exception error) { _changing = true; Units.SelectedIndex = _units; _changing = false; ShowError(error); }
    }
    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".txt"); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            if (new FileInfo(file.Path).Length > 2_000_000) throw new InvalidDataException("Map exceeds the 2 MB limit.");
            var maps = _main.CoreAccess.Call(new Request { Operation = "map_import", SourceInfo = _source, MapKind = SelectedKind, MapSeconds = _units == 1, MapText = await File.ReadAllTextAsync(file.Path) }).Maps;
            _draft = maps; ShowRows(); TryValidate(out _);
        }
        catch (Exception error) { ShowError(error); }
    }
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (!TryValidate(out var maps)) return;
        try {
            if (SelectedKind == MapKind.Time)
                maps = _main.CoreAccess.Call(new Request { Operation = "map_import", SourceInfo = _source, MapKind = MapKind.Time, MapText = Preview.Text }).Maps;
            await _main.SaveText("Native map text", "audio-" + SelectedKind.ToString().ToLowerInvariant() + "-map", ".txt", Export(maps, _units == 1), this);
        }
        catch (Exception error) { ShowError(error); }
    }
}
