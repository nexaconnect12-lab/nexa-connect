using System.Security.Cryptography;
using System.Text;
using NexaConnect.Services.Restaurant.Application.Qr;

namespace NexaConnect.Services.Restaurant.Infrastructure;

public sealed class QrSecrets : IQrSecrets
{
    public string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
