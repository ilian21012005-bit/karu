namespace Karu.Core;

/// <summary>Statuts unifiés lisibles (barre Library / tray).</summary>
public static class CaptureStatusText
{
    public const string WaitingForValorant = "En attente de Valorant";
    public const string BufferActive = "Buffer actif";
    public const string BufferDetectionOn = "Buffer actif · détection ON";
    public const string BufferNeedFocus = "Buffer actif · focus Valo pour détection";
    public const string BufferMissingPlayer = "Buffer actif · pseudo manquant";
    public const string BufferPausedBattery = "Buffer en pause (batterie)";

    public static string Build(
        bool pauseOnBattery,
        bool gameRunning,
        bool gameFocused,
        bool highlightsEnabled,
        bool hasPlayerName)
    {
        if (pauseOnBattery)
        {
            return BufferPausedBattery;
        }

        if (!gameRunning)
        {
            return WaitingForValorant;
        }

        if (!highlightsEnabled)
        {
            return BufferActive;
        }

        if (!hasPlayerName)
        {
            return BufferMissingPlayer;
        }

        return gameFocused ? BufferDetectionOn : BufferNeedFocus;
    }
}
