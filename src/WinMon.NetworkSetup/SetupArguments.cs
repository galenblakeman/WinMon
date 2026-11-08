using System.Security.Cryptography;

namespace WinMon.NetworkSetup;

internal sealed record SetupArguments(string? PipeName, byte[]? Token, bool Uninstall, string? RevokeUser)
{
    internal static SetupArguments Parse(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--uninstall", StringComparison.Ordinal))
            return new SetupArguments(null, null, true, null);
        if (args.Length == 2 && string.Equals(args[0], "--revoke-user", StringComparison.Ordinal))
            return new SetupArguments(null, null, false, AuthorizationPolicy.ValidateSid(args[1]));
        if (args.Length != 4 || !string.Equals(args[0], "--pipe", StringComparison.Ordinal) ||
            !string.Equals(args[2], "--token", StringComparison.Ordinal) || !IsPipeName(args[1]))
            throw new ArgumentException("Invalid setup invocation.");

        var tokenText = args[3];
        if (tokenText.Length != 64 || tokenText.Any(static c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid setup invocation.");
        return new SetupArguments(args[1], Convert.FromHexString(tokenText), false, null);
    }

    internal bool MatchesToken(string token)
    {
        return Token is { Length: 32 } && token.Length == 64 &&
               token.All(Uri.IsHexDigit) &&
               CryptographicOperations.FixedTimeEquals(Token, Convert.FromHexString(token));
    }

    private static bool IsPipeName(string value)
    {
        if (value.Length != "WinMon.NetworkSetup.".Length + 32 ||
            !value.StartsWith("WinMon.NetworkSetup.", StringComparison.Ordinal)) return false;
        return value.AsSpan("WinMon.NetworkSetup.".Length).ToString().All(Uri.IsHexDigit);
    }
}