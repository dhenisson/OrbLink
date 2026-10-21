using System.Text.Json;
using OrbLink.Core;

namespace OrbLink.Storage;

public sealed class AppearanceStore(SqliteStore store)
{
    public const string SettingKey = "OrbLink.Appearance.v1";

    public async Task<AppearanceSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await store.GetSettingAsync(SettingKey, cancellationToken);
        try
        {
            return (json is null ? new AppearanceSettings() :
                JsonSerializer.Deserialize<AppearanceSettings>(json) ?? new AppearanceSettings()).Normalize();
        }
        catch (JsonException) { return new AppearanceSettings(); }
    }

    public Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default) =>
        store.SetSettingAsync(SettingKey, JsonSerializer.Serialize(settings.Normalize()), cancellationToken);
}
