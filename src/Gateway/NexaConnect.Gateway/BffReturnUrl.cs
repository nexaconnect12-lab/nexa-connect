namespace NexaConnect.Gateway;

public static class BffReturnUrl
{
    public static string Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            return "/";
        }

        return candidate;
    }
}
