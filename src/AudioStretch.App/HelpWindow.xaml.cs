using AudioStretch.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Xml;
using System.Xml.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace AudioStretch;
public sealed partial class HelpWindow : Window
{
    private readonly MainWindow _main;
    private bool _ready;
    private int _generation;
    internal bool IsClosed { get; private set; }
    public HelpWindow(MainWindow main)
    {
        _main = main; InitializeComponent(); App.Themes.Attach(this); App.Accent.Attach(Root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        _ready = true; Root.Loaded += async (_, _) => { Placement.Restore(this, Root, settings: true); await LoadReference(); };
        Closed += (_, _) => { IsClosed = true; _generation++; App.Themes.Detach(this); App.Accent.Detach(Root); };
    }
    private async void ViewChanged(object sender, SelectionChangedEventArgs e) { if (_ready) await LoadReference(); }
    private async Task LoadReference()
    {
        int generation = ++_generation, view = View.SelectedIndex; Reference.Text = "Loading…";
        try
        {
            string text = await Task.Run(() => {
                if (view != 0) return _main.CoreAccess.Call(new Request { Operation = view switch { 1 => "help", 2 => "full_help", _ => "tool_version" } }).Text;
                using var reader = XmlReader.Create(Path.Combine(AppContext.BaseDirectory, "docs", "rubberband-r3-help.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                return string.Join("\n\n", document.Descendants().Where(e => e.HasAttributes || e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))).Select(e => {
                    string label = e.Name.LocalName.Replace('-', ' ');
                    string attributes = string.Join(" · ", e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName + "=" + a.Value));
                    string body = string.Join(" ", e.Nodes().OfType<XText>().Select(t => t.Value));
                    return label + (attributes.Length == 0 ? "" : " · " + attributes) + "\n" + System.Text.RegularExpressions.Regex.Replace(body.Trim(), @"\s+", " ");
                }));
            });
            if (!IsClosed && generation == _generation) Reference.Text = text;
        }
        catch (Exception error) { if (!IsClosed && generation == _generation) Reference.Text = "Reference could not be loaded: " + error.Message; }
    }
    private void CopyClicked(object sender, RoutedEventArgs e) { var data = new DataPackage(); data.SetText(Reference.Text); Clipboard.SetContent(data); }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
