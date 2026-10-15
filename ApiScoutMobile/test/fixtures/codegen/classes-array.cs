using System.Text.Json;
using System.Text.Json.Serialization;

// var data = JsonSerializer.Deserialize<List<ThingItem>>(json);
// Types are inferred from this one response: widen int to double where a value can have decimals,
// and add ? to anything the API may leave out.

public sealed class ThingItem
{
    [JsonPropertyName("a")]
    public int A { get; set; }
}