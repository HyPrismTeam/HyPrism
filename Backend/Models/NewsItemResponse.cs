using System.Text.Json.Serialization;

namespace HyPrism.Backend.Models;

public class NewsItemResponse
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = "";
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";
    
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
    
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
    
    [JsonPropertyName("source")]
    public string Source { get; set; } = "hytale"; // "hytale" or "hyprism"
}
