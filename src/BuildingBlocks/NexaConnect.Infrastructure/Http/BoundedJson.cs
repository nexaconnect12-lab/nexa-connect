using System.Text.Json;
namespace NexaConnect.Infrastructure.Http;

public static class BoundedJson
{
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response,int maximumBytes,CancellationToken ct)
    {
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var body=new MemoryStream();
        byte[] buffer=new byte[8192];int size;
        while((size=await stream.ReadAsync(buffer,ct))!=0)
        {
            if(body.Length+size>maximumBytes)throw new InvalidOperationException("Dependency body exceeds bound.");
            body.Write(buffer,0,size);
        }
        return JsonSerializer.Deserialize<T>(body.ToArray(),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidOperationException("Dependency body missing.");
    }
}
