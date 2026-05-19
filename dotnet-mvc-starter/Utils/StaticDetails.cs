namespace api.Utils;

public static class StaticDetails
{
    public const string UserProfileImagePath = "uploads";

    public static string GetUserProfileImagePath(Guid userId)
    {
        return $"{UserProfileImagePath}/{userId}";
    }
}