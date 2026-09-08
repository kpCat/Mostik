namespace Mostik.Core;

public static class VoicePolicy
{
    public static bool IsOfflineReady(bool requiresNetwork, IEnumerable<string>? features) =>
        !requiresNetwork && !(features?.Contains("notInstalled", StringComparer.Ordinal) ?? false);
}
