using System.Text.Json;
using System.Text.Json.Serialization;

// var data = JsonSerializer.Deserialize<OrderResponse>(json);
// Types are inferred from this one response: widen int to double where a value can have decimals,
// and add ? to anything the API may leave out.

public sealed class OrderResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("big")]
    public long Big { get; set; }
    [JsonPropertyName("price")]
    public double Price { get; set; }
    [JsonPropertyName("mixed")]
    public List<double> Mixed { get; set; } = [];
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("when")]
    public DateTimeOffset When { get; set; }
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
    [JsonPropertyName("nothing")]
    public object? Nothing { get; set; }
    [JsonPropertyName("owner")]
    public Owner Owner { get; set; } = new();
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = [];
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";
    [JsonPropertyName("2fa")]
    public bool _2fa { get; set; }
    [JsonPropertyName("entries")]
    public List<Entry> Entries { get; set; } = [];
    [JsonPropertyName("boxes")]
    public List<Box> Boxes { get; set; } = [];
    [JsonPropertyName("addresses")]
    public List<Address> Addresses { get; set; } = [];
    [JsonPropertyName("glass")]
    public List<GlassItem> Glass { get; set; } = [];
    [JsonPropertyName("owner2")]
    public Owner2 Owner2 { get; set; } = new();
}

public sealed class Owner
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    [JsonPropertyName("site-url")]
    public object? SiteUrl { get; set; }
}

public sealed class Item
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = "";
    [JsonPropertyName("qty")]
    public int Qty { get; set; }
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class Entry
{
    [JsonPropertyName("x")]
    public int X { get; set; }
}

public sealed class Box
{
    [JsonPropertyName("w")]
    public double W { get; set; }
}

public sealed class Address
{
    [JsonPropertyName("zip")]
    public string Zip { get; set; } = "";
}

public sealed class GlassItem
{
    [JsonPropertyName("g")]
    public bool G { get; set; }
}

public sealed class Owner2
{
    [JsonPropertyName("login")]
    public int Login { get; set; }
}