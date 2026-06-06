namespace UndoModMS;

public static class UndoLogger
{
    /// <summary>
    /// false일 때 Info/Debug는 no-op. 카드 플레이마다 디스크 쓰기로 인한
    /// 프레임 끊김 방지. 회귀 디버깅 시 true로 전환.
    /// </summary>
    public static readonly bool EnableInfoLogging = false;

    public static void Info(string msg)
    {
        if (!EnableInfoLogging) return;
        ModStart.Logger.Info(msg);
    }

    public static void Warn(string msg)
    {
        ModStart.Logger.Warn(msg);
    }

    public static void Debug(string msg)
    {
        if (!EnableInfoLogging) return;
        ModStart.Logger.Info(msg);
    }

    public static void Error(string msg)
    {
        ModStart.Logger.Error(msg);
    }
}