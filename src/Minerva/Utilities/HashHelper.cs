using System.Security.Cryptography;
using System.Text;

namespace Minerva.Utilities;

public static class HashHelper
{
    public static string GenerateChunkId(string collectionName, string sourceId, int chunkIndex)
    {
        var input = $"{collectionName}:{sourceId}:{chunkIndex}";
        return ComputeSha256(input);
    }

    public static string ComputeContentHash(string content)
    {
        return ComputeSha256(content);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
