using System.Runtime.InteropServices;

namespace MonCore.Windows.Monitoring;

internal interface IEventMessageMetadata : IDisposable
{
    bool IsInvalid { get; }
    bool TryFormat(SafeHandle item, char[] buffer, out uint used, out int error);
}
