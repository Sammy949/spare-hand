using System.Text.Json.Serialization;
using SpareHand.Hold;

namespace SpareHand;

/// <summary>
/// Compile-time JSON serializers. The trimmed release disables reflection-based System.Text.Json, so every type Spare
/// Hand saves goes through this context (the first trimmed build died at startup reading settings.json without it).
/// Reflection is also disabled in normal builds (JsonSerializerIsReflectionEnabledByDefault) so a missing entry fails
/// during development, not only in releases.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(List<HoldItem>))]
[JsonSerializable(typeof(List<List<HoldItem>>))]
internal sealed partial class AppJson : JsonSerializerContext
{
}
