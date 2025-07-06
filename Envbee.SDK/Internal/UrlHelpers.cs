using System.Web;

namespace Envbee.SDK.Internal;

/// <summary>
/// Simple helper to append query‑string parameters to a path.
/// </summary>
internal static class UrlHelpers
{
    public static string AddQueryString(string path, IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return path;

        var qs = HttpUtility.ParseQueryString(string.Empty);
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
                qs[key] = value.ToString();
        }

        return $"{path}?{qs}";
    }
}
