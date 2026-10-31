using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed record LocationSearchResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("formatted_address")] string FormattedAddress,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("street_number")] string? StreetNumber,
    [property: JsonPropertyName("colonia")] string? Colonia,
    [property: JsonPropertyName("municipality")] string? Municipality);
