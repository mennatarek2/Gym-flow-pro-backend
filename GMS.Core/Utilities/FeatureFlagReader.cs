namespace GMS.Core.Utilities;

using System.Text.Json;

/// <summary>
/// Reads per-tenant feature flags nested under the "feature_flags" key inside Tenant.Settings.
/// Prefer <see cref="GMS.Core.Interfaces.IFeatureAccessService"/> for gate checks — this helper
/// remains only as the Phase A JSON deny overlay used by that service.
/// Every flag defaults to true (enabled) when settings/key are absent/malformed (fail-open).
/// </summary>
public static class FeatureFlagReader
{
    public const string SettingsKey = "feature_flags";

    public static bool IsEnabled(string? settingsJson, string featureName)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(SettingsKey, out var flagsElement))
                return true;

            if (!flagsElement.TryGetProperty(featureName, out var flagValue))
                return true;

            return flagValue.ValueKind switch
            {
                JsonValueKind.False => false,
                _ => true
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
