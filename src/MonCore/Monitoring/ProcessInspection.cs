namespace MonCore.Monitoring;

/// <summary>Optional, on-demand details for one confirmed process lifetime.</summary>
public sealed record ProcessInspection(bool Exited, string? CommandLine, ProcessOwner? Owner, ProcessSignature Signature);

/// <summary>Identity and execution context read from a process token.</summary>
public sealed record ProcessOwner(string? AccountName, int? SessionId, bool? Elevated);

/// <summary>Trust state for the image file that exists on disk when it was read.</summary>
public sealed record ProcessSignature(ProcessSignatureStatus Status, string? Signer);

public enum ProcessSignatureStatus
{
    Unavailable,
    NotSigned,
    Invalid,
    Verified
}
