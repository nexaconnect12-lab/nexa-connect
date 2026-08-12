namespace NexaConnect.PlatformAdminBff;

public static class BffProxyResponseCopier
{
    public static async Task CopyAsync(HttpResponseMessage source, HttpResponse target, CancellationToken cancellationToken)
    {
        target.StatusCode = (int)source.StatusCode;
        if (StatusCodes.Status204NoContent == target.StatusCode
            || StatusCodes.Status304NotModified == target.StatusCode
            || (target.StatusCode >= 100 && target.StatusCode < 200))
            return;

        if (source.Content.Headers.ContentType is not null)
            target.ContentType = source.Content.Headers.ContentType.ToString();
        await source.Content.CopyToAsync(target.Body, cancellationToken);
    }
}
