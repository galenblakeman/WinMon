using System.Collections;

namespace WinMon.Monitoring;

/// <summary>A read-only prefix of a sample list, optionally ending in a replacement sample, without copying it.</summary>
/// <remarks>Implements <see cref="IList{T}"/> so LINQ takes its indexed paths instead of enumerating.</remarks>
internal sealed class SampleList : IList<CpuSample>, IReadOnlyList<CpuSample>
{
    private readonly IReadOnlyList<CpuSample> _source;
    private readonly CpuSample? _last;

    /// <param name="source">Samples that do not change while this view is in use.</param>
    /// <param name="count">Number of leading samples to show.</param>
    /// <param name="last">Shown instead of the last of those samples, or null to keep it.</param>
    internal SampleList(IReadOnlyList<CpuSample> source, int count, CpuSample? last = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, source.Count);
        if (last is not null) ArgumentOutOfRangeException.ThrowIfZero(count);
        _source = source;
        Count = count;
        _last = last;
    }

    public int Count { get; }

    public bool IsReadOnly => true;

    public CpuSample this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _last is not null && index == Count - 1 ? _last : _source[index];
        }
        set => throw new NotSupportedException();
    }

    public int IndexOf(CpuSample item)
    {
        for (var i = 0; i < Count; i++)
            if (EqualityComparer<CpuSample>.Default.Equals(this[i], item))
                return i;
        return -1;
    }

    public bool Contains(CpuSample item)
    {
        return IndexOf(item) >= 0;
    }

    public void CopyTo(CpuSample[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var i = 0; i < Count; i++) array[arrayIndex + i] = this[i];
    }

    public IEnumerator<CpuSample> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(CpuSample item)
    {
        throw new NotSupportedException();
    }

    public void Clear()
    {
        throw new NotSupportedException();
    }

    public void Insert(int index, CpuSample item)
    {
        throw new NotSupportedException();
    }

    public bool Remove(CpuSample item)
    {
        throw new NotSupportedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotSupportedException();
    }
}