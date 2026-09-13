using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Sections;
using AutoHuntGrinder.Windows.Shell;

namespace AutoHuntGrinder.Windows.Pages;

internal sealed class HuntPage
{
    private const float SwitchRevealMs = 320f;

    private bool scrollToLibrary;

    public void Draw(Plugin plugin, AppWindow window)
    {
        MarkBillReader.Refresh();
        var configuration = plugin.Configuration;
        var controller = plugin.Controller;
        var running = controller.Running;

        using var reveal = Motion.PushSwitch("##ahg_hunt_state", running, SwitchRevealMs);
        if (running)
        {
            RunningPanel.Draw(controller);
            return;
        }

        DrawIdle(plugin, window, configuration, controller);
    }

    private void DrawIdle(Plugin plugin, AppWindow window, Configuration configuration, AutoHuntController controller)
    {
        if (Headline.Draw(configuration, controller, plugin.History))
        {
            window.Show(AppWindow.Page.Plugins);
        }

        Styling.VSpace(20f);
        if (PlanCard.Draw(configuration, controller))
        {
            scrollToLibrary = true;
        }

        Styling.VSpace(26f);
        BillLibrary.Draw(configuration, controller, scrollToLibrary);
        scrollToLibrary = false;
        Styling.VSpace(12f);
    }
}
