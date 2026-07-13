using System.Text.Json.Serialization;

namespace KARTCompanion.WowUtils;

/// <summary>Response of GET /v1 — the discovery root. Used to validate a group key and to show
/// the group's display name in the tray/Settings UI.</summary>
public sealed class DiscoveryResponse
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "";

    [JsonPropertyName("group")]
    public GroupRef Group { get; set; } = new();

    public sealed class GroupRef
    {
        [JsonPropertyName("groupId")]
        public string GroupId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}
