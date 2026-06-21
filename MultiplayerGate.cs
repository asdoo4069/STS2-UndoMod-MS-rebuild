using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace UndoModMS;

internal static class MultiplayerGate
{
    private static bool _loggedDormantThisCombat;

    public static bool IsDormant()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null || !rm.IsInProgress) return false;
            if (rm.IsSingleplayerOrFakeMultiplayer) return false;

            if (!_loggedDormantThisCombat)
            {
                _loggedDormantThisCombat = true;
                NetGameType? t = rm.NetService?.Type;
                UndoLogger.Info($"[Undo] dormant for this run — NetService.Type={t} (multiplayer/replay; snapshots + button disabled)");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ResetForNewCombat() => _loggedDormantThisCombat = false;
}