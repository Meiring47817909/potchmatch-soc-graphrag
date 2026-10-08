using System.Globalization;

namespace GraphService.Mapping;

/// <summary>
/// Builds the Endpoint key "{METHOD} {pathTemplate}" from a raw HTTP method and path.
/// Query strings are dropped, GUID and integer segments become {id}, and trailing slashes are removed.
/// </summary>
public static class EndpointNormaliser
{
    public static string? ToKey(string? method, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var path = rawPath.Trim();
        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => IsId(s) ? "{id}" : s);

        var normalisedPath = "/" + string.Join('/', segments);
        return $"{method.Trim().ToUpperInvariant()} {normalisedPath}";
    }

    private static bool IsId(string segment) =>
        Guid.TryParse(segment, out _)
        || long.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
