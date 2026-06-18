using System.Windows;
using System.Windows.Controls.Primitives;

namespace AudioStretch;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = (MainViewModel)DataContext;
        vm.LogLines.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() => LogScroll.ScrollToEnd());
    }

    private void ScrubSlider_DragStarted(object sender, DragStartedEventArgs e)
        => ((MainViewModel)DataContext).IsScrubbing = true;

    private void ScrubSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.IsScrubbing = false;
        vm.SeekTo(ScrubSlider.Value);
    }

    protected override void OnClosed(EventArgs e)
    {
        ((MainViewModel)DataContext).Dispose();
        base.OnClosed(e);
    }
}
