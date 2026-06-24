namespace CardDemo.Cobol.Runtime;

/// <summary>
/// A decoded view of one fixed-width record: the values of every elementary field, decoded according
/// to a <see cref="RecordLayout"/>. <see cref="Parse"/> reads a record image into typed values and
/// <see cref="ToBytes"/> writes them back, so <c>ToBytes(Parse(image)) == image</c> is the byte-exact
/// fidelity check that proves the codecs and layout are correct.
/// </summary>
public sealed class FixedRecord
{
    private readonly object?[] _values;

    /// <summary>The layout this record was decoded against.</summary>
    public RecordLayout Layout { get; }

    private FixedRecord(RecordLayout layout, object?[] values)
    {
        Layout = layout;
        _values = values;
    }

    /// <summary>Alphanumeric field value as a host-decoded string (fixed width, space padded).</summary>
    public string GetText(string name) => (string)GetValue(name)!;

    /// <summary>Numeric field value as a <see cref="decimal"/> with the field's scale applied.</summary>
    public decimal GetNumber(string name) => (decimal)GetValue(name)!;

    /// <summary>Raw decoded value of a field (string for alphanumeric, decimal for numeric).</summary>
    public object? GetValue(string name) => _values[IndexOf(name)];

    /// <summary>
    /// Creates an all-spaces / zero record, modelling a freshly-initialised COBOL WORKING-STORAGE record:
    /// alphanumeric fields are spaces, numeric fields are zero.
    /// </summary>
    public static FixedRecord CreateBlank(RecordLayout layout)
    {
        var values = new object?[layout.Fields.Count];
        for (int i = 0; i < layout.Fields.Count; i++)
        {
            FieldDef f = layout.Fields[i];
            values[i] = f.Category == CobolCategory.Alphanumeric ? new string(' ', f.Length) : 0m;
        }
        return new FixedRecord(layout, values);
    }

    /// <summary>
    /// Stores text into an alphanumeric field with COBOL MOVE semantics: left-justified, right-truncated,
    /// and right-padded with spaces to the field width.
    /// </summary>
    public FixedRecord SetText(string name, string value)
    {
        int i = IndexOf(name);
        FieldDef f = Layout.Fields[i];
        if (f.Category != CobolCategory.Alphanumeric)
            throw new InvalidOperationException($"Field '{name}' is numeric; use SetNumber.");
        _values[i] = value.Length >= f.Length ? value[..f.Length] : value.PadRight(f.Length, ' ');
        return this;
    }

    /// <summary>
    /// Stores a number into a numeric field with COBOL MOVE semantics: truncate-toward-zero to the field
    /// scale and silent high-order overflow (unsigned fields keep the magnitude).
    /// </summary>
    public FixedRecord SetNumber(string name, decimal value)
    {
        int i = IndexOf(name);
        FieldDef f = Layout.Fields[i];
        if (f.Category != CobolCategory.Numeric)
            throw new InvalidOperationException($"Field '{name}' is alphanumeric; use SetText.");
        _values[i] = Decimals.Store(value, f.IntegerDigits, f.Scale, f.Signed);
        return this;
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < Layout.Fields.Count; i++)
            if (Layout.Fields[i].Name == name) return i;
        throw new KeyNotFoundException($"Field '{name}' not found in record '{Layout.Name}'.");
    }

    /// <summary>Decodes a single record image (length must equal <see cref="RecordLayout.Length"/>).</summary>
    public static FixedRecord Parse(RecordLayout layout, ReadOnlySpan<byte> image, HostKind host)
    {
        if (image.Length != layout.Length)
            throw new ArgumentException(
                $"Record image length {image.Length} != layout '{layout.Name}' length {layout.Length}.", nameof(image));

        var values = new object?[layout.Fields.Count];
        for (int i = 0; i < layout.Fields.Count; i++)
        {
            FieldDef f = layout.Fields[i];
            ReadOnlySpan<byte> slice = image.Slice(f.Offset, f.Length);
            values[i] = f.Category == CobolCategory.Alphanumeric
                ? HostEncoding.For(host).GetString(slice)
                : DecodeNumeric(f, slice, host);
        }
        return new FixedRecord(layout, values);
    }

    /// <summary>Encodes this record back to its fixed-width image.</summary>
    public byte[] ToBytes(HostKind host)
    {
        var image = new byte[Layout.Length];
        var span = image.AsSpan();
        for (int i = 0; i < Layout.Fields.Count; i++)
        {
            FieldDef f = Layout.Fields[i];
            Span<byte> slice = span.Slice(f.Offset, f.Length);
            if (f.Category == CobolCategory.Alphanumeric)
                EncodeText((string)_values[i]!, slice, host);
            else
                EncodeNumeric(f, (decimal)_values[i]!, slice, host);
        }
        return image;
    }

    private static decimal DecodeNumeric(FieldDef f, ReadOnlySpan<byte> slice, HostKind host) => f.Usage switch
    {
        CobolUsage.Display => ZonedDecimalCodec.Decode(slice, f.Scale, f.Signed, host),
        CobolUsage.Comp3 => PackedDecimalCodec.Decode(slice, f.Scale),
        CobolUsage.Comp => BinaryCodec.Decode(slice, f.Scale, f.Signed),
        _ => throw new NotSupportedException($"Unsupported usage {f.Usage} for field {f.Name}."),
    };

    private static void EncodeNumeric(FieldDef f, decimal value, Span<byte> slice, HostKind host)
    {
        switch (f.Usage)
        {
            case CobolUsage.Display:
                ZonedDecimalCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed, host);
                break;
            case CobolUsage.Comp3:
                PackedDecimalCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed);
                break;
            case CobolUsage.Comp:
                BinaryCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed);
                break;
            default:
                throw new NotSupportedException($"Unsupported usage {f.Usage} for field {f.Name}.");
        }
    }

    private static void EncodeText(string value, Span<byte> dest, HostKind host)
    {
        byte[] bytes = HostEncoding.For(host).GetBytes(value);
        if (bytes.Length != dest.Length)
            throw new InvalidOperationException(
                $"Alphanumeric value re-encoded to {bytes.Length} bytes, expected {dest.Length}.");
        bytes.CopyTo(dest);
    }
}
