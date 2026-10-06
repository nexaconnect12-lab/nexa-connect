using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

// Low-level hashing only. Owning Infrastructure chooses the evidence rows and snapshot.
public static class BoundedEvidenceHash
{
    public static async Task<string?> ReadAsync(NpgsqlCommand command, string scope, CancellationToken ct)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(scope);int count=0,total=0;
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
        {
            if(++count>10_000)return null;
            string row=reader.GetString(0);total+=Encoding.UTF8.GetByteCount(row);
            if(total>16*1024*1024)return null;
            Append(row);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        void Append(string value)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(value);
            Span<byte> length=stackalloc byte[4];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length,bytes.Length);
            hash.AppendData(length);hash.AppendData(bytes);
        }
    }
}
