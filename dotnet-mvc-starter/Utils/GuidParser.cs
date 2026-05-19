using Newtonsoft.Json.Linq;

namespace api.Utils;

public static class GuidParser
{
	public static Result<Guid> TryParseJsonProp(JToken? prop, string? whomId)
	{
		if (prop is null || prop.Type == JTokenType.Undefined)
			return Result.Fail<Guid>($"{whomId} property is of undefined type or null. Can't parse GUID.");
		return TryParseGuid(prop.Value<string>());
	}

	public static Result<Guid> TryParseGuid(string? idString)
	{
		if (string.IsNullOrWhiteSpace(idString))
			return Result.Fail<Guid>("id string provided is null, empty or whitespace.");
		var isSuccess = Guid.TryParse(idString, out var tutorId);
		if (!isSuccess)
			return Result.Fail<Guid>("id string provided is not Guid formated.");
		return Result.Ok(tutorId);
	}
}