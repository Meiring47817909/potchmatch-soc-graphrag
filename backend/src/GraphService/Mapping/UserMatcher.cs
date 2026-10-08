namespace GraphService.Mapping;

/// <summary>Helpers for matching SOC user identifiers (emails) to Identity user ids.</summary>
public static class UserMatcher
{
    public static string EmailKey(string email) => email.Trim().ToLowerInvariant();

    public static bool LooksLikeEmail(string? userId) =>
        !string.IsNullOrWhiteSpace(userId)
        && userId.Contains('@')
        && !Guid.TryParse(userId, out _);

    public static string UnknownUserId(string email) => "email:" + EmailKey(email);
}
