namespace GMS.Core.Utilities;

/// <summary>Generates short, shareable member referral codes (tenant-unique).</summary>
public static class ReferralCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1
    private static readonly Random Shared = new();

    /// <summary>Returns an 8-char code, e.g. R7K2M9QX (prefix R).</summary>
    public static string Create()
    {
        Span<char> chars = stackalloc char[8];
        chars[0] = 'R';
        lock (Shared)
        {
            for (var i = 1; i < chars.Length; i++)
                chars[i] = Alphabet[Shared.Next(Alphabet.Length)];
        }

        return new string(chars);
    }
}
