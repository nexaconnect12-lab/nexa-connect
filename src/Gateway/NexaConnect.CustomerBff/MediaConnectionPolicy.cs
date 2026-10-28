namespace NexaConnect.CustomerBff;

public static class MediaConnectionPolicy
{
    public static string? ValidateOrigin(string? value, bool localDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 256 || value.Contains('*') || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/" || uri.GetLeftPart(UriPartial.Authority) != value
            || !(uri.Scheme == "https" || localDevelopment && uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidOperationException("Bff:MediaUploadOrigin must be one exact HTTPS origin, or HTTP loopback in Development/Test.");
        return value;
    }

    public static string ContentSecurityPolicy(bool guest, string? validatedMediaOrigin)
    {
        const string common="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; ";
        return guest ? common+"connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'; form-action 'self'"
            : common+"connect-src 'self'"+(validatedMediaOrigin is null?"":" "+validatedMediaOrigin)+"; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }
}
