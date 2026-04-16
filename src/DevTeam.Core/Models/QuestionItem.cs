using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class QuestionItem
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool IsBlocking { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public QuestionStatus Status { get; set; } = QuestionStatus.Open;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Answer { get; set; } = "";
}
