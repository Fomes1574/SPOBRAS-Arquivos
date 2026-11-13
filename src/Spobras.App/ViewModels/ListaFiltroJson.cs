using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spobras.App.ViewModels;

public sealed class ListaFiltroJson : JsonConverter<IReadOnlyList<string>>
{
    public override bool HandleNull => true;

    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => [],
            JsonTokenType.String => reader.GetString() is { Length: > 0 } valor ? [valor] : [],
            _ => JsonSerializer.Deserialize<string[]>(ref reader, options) ?? [],
        };

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToArray(), options);
}
