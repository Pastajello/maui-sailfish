using System.Text.Json.Serialization;

namespace Trailhead.Data;

/// <summary>Source-generated serialization: trim-safe, no reflection at runtime (plan D8).</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(Trip))]
[JsonSerializable(typeof(List<TripSummary>))]
public sealed partial class TrailheadJson : JsonSerializerContext
{
}
