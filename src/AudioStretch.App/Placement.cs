using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AudioStretch;
internal static class Placement
{
    public static void Restore(Window window, FrameworkElement root, bool settings = false)
    {
        var p = App.Preferences;
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = root.XamlRoot.RasterizationScale;
        int width = Math.Clamp((int)((settings ? 720 : p.Width) * scale), Math.Min((int)(640 * scale), area.Width), area.Width);
        int height = Math.Clamp((int)((settings ? 720 : p.Height) * scale), Math.Min((int)(480 * scale), area.Height), area.Height);
        int x = settings || p.X == int.MinValue ? area.X + (area.Width - width) / 2 : Math.Clamp(p.X, area.X, area.X + area.Width - width);
        int y = settings || p.Y == int.MinValue ? area.Y + (area.Height - height) / 2 : Math.Clamp(p.Y, area.Y, area.Y + area.Height - height);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)((settings ? 560 : 640) * scale), area.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)(480 * scale), area.Height);
            if (!settings && p.Maximized) presenter.Maximize();
        }
    }
    public static void Save(Window window, FrameworkElement root)
    {
        var p = App.Preferences;
        p.Maximized = window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (!p.Maximized && root.XamlRoot is not null)
        {
            p.X = window.AppWindow.Position.X; p.Y = window.AppWindow.Position.Y;
            p.Width = (int)(window.AppWindow.Size.Width / root.XamlRoot.RasterizationScale);
            p.Height = (int)(window.AppWindow.Size.Height / root.XamlRoot.RasterizationScale);
        }
        p.Save();
    }
}
