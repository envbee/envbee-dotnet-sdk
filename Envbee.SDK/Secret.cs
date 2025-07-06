/// <summary>
/// Wrapper that enables implicit type conversion
/// </summary>
public readonly struct Secret
{
    /// <summary>
    /// Secret store
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Get the Length of the internal Data
    /// </summary>
    public int Length => Data?.Length ?? 0;

    /// <summary>
    /// Is the Secret empty?
    /// </summary>
    public bool IsEmpty => Length == 0;

    /// <summary>
    /// Was the Secret created from a String?
    /// </summary>
    public bool IsCreatedFromString { get; }

    private Secret(byte[] data, bool fromString = false) { Data = data; IsCreatedFromString = fromString; }

    /// <summary>
    /// From byte[]
    /// </summary>
    public static implicit operator Secret(byte[] bytes) => new(bytes);

    /// <summary>
    /// From ReadOnlySpan byte
    /// </summary>
    public static implicit operator Secret(ReadOnlySpan<byte> span) => new(span.ToArray());

    /// <summary>
    /// From string (Base64 expected in this example)
    /// </summary>
    public static implicit operator Secret(string? text) =>
        text is null ? default : new(System.Text.Encoding.UTF8.GetBytes(text), true);
}
