using System.Collections.Immutable;

namespace api.Identity;

public static class IdentityData
{
    public const UserRole ClaimAdmin = UserRole.Admin;

    public const string PolicyAdmin = "adminPolicy";

    public const UserRole ClaimModerator = UserRole.Moderator;

    public const string PolicyModerator = "moderatorPolicy";

    public const UserRole ClaimUser = UserRole.User;

    public const string PolicyUser = "userPolicy";


    public static readonly ImmutableDictionary<UserRole, int> AccessLevelDict = ImmutableDictionary.CreateRange(
        new KeyValuePair<UserRole, int>[]
        {
            new(ClaimAdmin, 2),
            new(ClaimModerator, 1),
            new(ClaimUser, 0)
        }
    );
}