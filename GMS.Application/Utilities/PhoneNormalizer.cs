namespace GMS.Application.Utilities;

/// <summary>
/// Normalizes Egyptian mobile phone numbers to a single canonical form: +20XXXXXXXXXX.
/// Accepts (spaces/dashes ignored throughout, since only digits are examined):
///   - Local 11-digit: 010/011/012/015 + 8 digits
///   - International with plus: +20 + 10 digits (starting 1[0125])
///   - International with 00: 0020 + 10 digits
///   - International bare: 20 + 10 digits
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>Returns the canonical +20XXXXXXXXXX form, or null if the input isn't a
    /// recognizable Egyptian mobile number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digitsOnly = new string(input.Where(char.IsDigit).ToArray());

        string local11;

        if (digitsOnly.Length == 14 && digitsOnly.StartsWith("0020"))
            local11 = "0" + digitsOnly[4..];
        else if (digitsOnly.Length == 12 && digitsOnly.StartsWith("20"))
            local11 = "0" + digitsOnly[2..];
        else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("0"))
            local11 = digitsOnly;
        else
            return null;

        // Egyptian mobile numbers are 01[0125]XXXXXXXX — 11 digits, second digit always '1'.
        if (!local11.StartsWith("01"))
            return null;

        var networkDigit = local11[2];
        if (networkDigit is not ('0' or '1' or '2' or '5'))
            return null;

        return "+20" + local11[1..];
    }
}
