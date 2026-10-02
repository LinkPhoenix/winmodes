using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Services;

/// <summary>The Optimize catalog and the live registry, seen by the mode planner and the mode switcher.</summary>
internal sealed class CatalogTweakProbe : ITweakProbe
{
    public TweakInfo? Find(string id)
    {
        if (ServiceTuning.Catalog.Find(id) is not { } tweak)
        {
            return null;
        }

        return new TweakInfo(tweak.Id, tweak.Title, ServiceTuning.UserTweaks.GetState(tweak) == TweakState.Applied, tweak.NeedsElevation, tweak.Restart);
    }
}
