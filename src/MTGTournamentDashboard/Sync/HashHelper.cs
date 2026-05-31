using System.Security.Cryptography;
using System.Text;

namespace MTGTournamentDashboard.Sync;

public static class HashHelper
{
    public static string Sha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
