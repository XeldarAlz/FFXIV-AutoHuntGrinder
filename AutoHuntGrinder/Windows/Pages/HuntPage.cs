using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using AutoHuntGrinder.Windows.Sections;
using AutoHuntGrinder.Windows.Shell;

namespace AutoHuntGrinder.Windows.Pages;

internal sealed class HuntPage
{
    private const float SwitchRevealMs = 320f;

    private static readonly HuntMode[] modes = [HuntMode.MarkBills, HuntMode.HuntingLog, HuntMode.CustomList];

    private readonly Segmented.Item[] modeItems = new Segmented.Item[modes.Length];

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
        DrawModeSwitch(configuration, controller);

        Styling.VSpace(14f);
        if (PlanCard.Draw(configuration, controller))
        {
            scrollToLibrary = true;
        }

        Styling.VSpace(26f);
        var mode = configuration.Mode;
        using (Motion.PushSwitch("##ahg_mode", (int)mode))
        {
            switch (mode)
            {
                case HuntMode.HuntingLog:
                    HuntingLogLibrary.Draw(configuration, controller, scrollToLibrary);
                    break;
                case HuntMode.CustomList:
                    CustomListEditor.Draw(configuration, controller, scrollToLibrary);
                    break;
                default:
                    BillLibrary.Draw(configuration, controller, scrollToLibrary);
                    break;
            }
        }

        scrollToLibrary = false;
        Styling.VSpace(12f);
    }

    private void DrawModeSwitch(Configuration configuration, AutoHuntController controller)
    {
        for (var index = 0; index < modes.Length; index++)
        {
            modeItems[index] = new Segmented.Item(HuntModeLabels.Icon(modes[index]), HuntModeLabels.Label(modes[index]));
        }

        var selected = Math.Max(0, Array.IndexOf(modes, configuration.Mode));
        if (!Segmented.Draw("##ahg_mode_switch", modeItems, ref selected, enabled: !controller.Running, height: Layout.SegmentHeight))
        {
            return;
        }

        configuration.Mode = modes[selected];
        configuration.SaveDebounced();
    }
}
