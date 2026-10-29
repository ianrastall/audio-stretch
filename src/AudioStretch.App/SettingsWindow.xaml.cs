using Microsoft.UI.Xaml;

namespace AudioStretch;
public sealed partial class SettingsWindow : Window
{
    private bool _ready;
    internal bool IsClosed { get; private set; }
    public SettingsWindow()
    {
        InitializeComponent();
        App.Themes.Attach(this); App.Accent.Attach(Root);
        ThemeControl.Service = App.Themes; AccentControl.Service = App.Accent;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        CentreFocus.IsChecked = App.Preferences.CentreFocus;
        _ready = true;
        Root.Loaded += (_, _) => Placement.Restore(this, Root, settings: true);
        Closed += (_, _) => { IsClosed = true; App.Themes.Detach(this); App.Accent.Detach(Root); };
    }
    private void CentreFocusChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        App.Preferences.CentreFocus = CentreFocus.IsChecked == true;
        try { App.Preferences.Save(); }
        catch (Exception error) { ErrorBar.Title = "Settings could not be saved"; ErrorBar.Message = error.Message; ErrorBar.IsOpen = true; }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
