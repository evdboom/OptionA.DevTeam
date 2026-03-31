using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class RoadmapItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemStatus Status { get; set; } = ItemStatus.Open;
    public int Priority { get; set; } = 50;
}