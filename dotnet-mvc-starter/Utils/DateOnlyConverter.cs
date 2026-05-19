using Newtonsoft.Json;

namespace api.Utils;

public class DateOnlyConverter : JsonConverter<DateOnly>
{
    private readonly string _format;

    public DateOnlyConverter(string format = "yyyy-MM-dd")
    {
        _format = format;
    }

    public override void WriteJson(JsonWriter writer, DateOnly value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString(_format));
    }

    public override DateOnly ReadJson(JsonReader reader, Type objectType, DateOnly existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return default;

        return DateOnly.Parse((string)reader.Value!);
    }
}