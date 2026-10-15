using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Health;
using SPTarkov.Server.Core.Utils.Json;

namespace Aishi;
[Injectable(InjectionType.Singleton)]
public sealed class AishiHealthTreatmentFormat : IJsonConverterRegistrator
{
    public IEnumerable<JsonConverter> GetJsonConverters() => [new TreatmentBodyPartConverter()];
}

internal sealed class TreatmentBodyPartConverter : JsonConverter<BodyPartValues>
{
    public override BodyPartValues Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new BodyPartValues();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "Health":
                    result.Health = property.Value.GetDouble();
                    break;
                case "Effects":
                    result.Effects = ReadEffects(property.Value);
                    break;
                default:
                    result.ExtensionData[property.Name] = property.Value.Clone();
                    break;
            }
        }

        return result;
    }

    private static List<string>? ReadEffects(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.False:
                return null;
            case JsonValueKind.Number when value.TryGetDouble(out var number) && number == 0:
                return null;
            case JsonValueKind.Array:
                return value.EnumerateArray().Select(x => x.GetString() ?? throw new JsonException("Null treatment effect name.")).ToList();
            case JsonValueKind.Object:
                return value.EnumerateObject().Select(x => x.Name).ToList();
            case JsonValueKind.String:
                var name = value.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    return null;
                if (name.TrimStart().StartsWith("[") || name.TrimStart().StartsWith("{"))
                {
                    using var nested = JsonDocument.Parse(name);
                    return ReadEffects(nested.RootElement);
                }

                return[name];
            default:
                throw new JsonException("Unsupported treatment effect value: " + value.ValueKind);
        }
    }

    public override void Write(Utf8JsonWriter writer, BodyPartValues value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Health", value.Health);
        writer.WritePropertyName("Effects");
        JsonSerializer.Serialize(writer, value.Effects, options);
        foreach (var property in value.ExtensionData)
        {
            if (property.Key is "Health" or "Effects")
                continue;
            writer.WritePropertyName(property.Key);
            JsonSerializer.Serialize(writer, property.Value, options);
        }

        writer.WriteEndObject();
    }
}
