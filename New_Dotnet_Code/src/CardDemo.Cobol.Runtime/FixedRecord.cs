namespace CardDemo.Cobol.Runtime;

/// <summary>
/// A fixed-width record that can be decoded from a byte image or built up field-by-field, then
/// re-serialized. Each field holds one of: a host string (alphanumeric), a decimal (numeric), or a raw
/// byte span (used for COBOL <c>STRING</c> partial writes and un-initialised WORKING-STORAGE).
/// </summary>
/// <remarks>
/// A freshly <see cref="CreateBlank"/>-ed record is all <c>LOW-VALUES</c> (0x00), matching GnuCOBOL's
/// default WORKING-STORAGE initialisation — so a field written by <c>STRING</c> keeps 0x00 in the bytes
/// it doesn't cover, which is observable in output records (e.g. the tail of <c>TRAN-DESC</c>).
/// </remarks>
public sealed class FixedRecord
{
    private readonly object?[] _values; // per field: string | decimal | byte[]
    private readonly HostKind _host;

    public RecordLayout Layout { get; }

    private FixedRecord(RecordLayout layout, object?[] values, HostKind host)
    {
        Layout = layout;
        _values = values;
        _host = host;
    }

    /// <summary>Alphanumeric field value as a host-decoded string.</summary>
    public string GetText(string name) => ValueAsText(IndexOf(name));

    /// <summary>Numeric field value as a <see cref="decimal"/> with the field's scale applied.</summary>
    public decimal GetNumber(string name) => ValueAsNumber(IndexOf(name));

    /// <summary>Raw decoded value of a field (string for alphanumeric, decimal for numeric).</summary>
    public object? GetValue(string name)
    {
        int i = IndexOf(name);
        return Layout.Fields[i].Category == CobolCategory.Alphanumeric ? ValueAsText(i) : ValueAsNumber(i);
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
        return new FixedRecord(layout, values, host);
    }

    /// <summary>
    /// Creates an all-<c>LOW-VALUES</c> (0x00) record in the given host, modelling a freshly-initialised
    /// COBOL WORKING-STORAGE record.
    /// </summary>
    public static FixedRecord CreateBlank(RecordLayout layout, HostKind host)
    {
        var values = new object?[layout.Fields.Count];
        for (int i = 0; i < layout.Fields.Count; i++)
            values[i] = new byte[layout.Fields[i].Length]; // zero-filled
        return new FixedRecord(layout, values, host);
    }

    /// <summary>
    /// MOVE into an alphanumeric field: left-justified, right-truncated, right-padded with spaces to the
    /// full field width (overwrites the whole field).
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
    /// MOVE into a numeric field: truncate-toward-zero to the field scale, silent high-order overflow,
    /// unsigned fields keep the magnitude.
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

    /// <summary>
    /// COBOL <c>STRING ... DELIMITED BY SIZE INTO field</c>: writes the encoded text into the start of
    /// the field and leaves the remaining bytes at their current value (no space padding). Truncates if
    /// the text is longer than the field.
    /// </summary>
    public FixedRecord StringInto(string name, string text)
    {
        int i = IndexOf(name);
        FieldDef f = Layout.Fields[i];
        byte[] current = SerializeField(f, _values[i]);
        byte[] textBytes = HostEncoding.For(_host).GetBytes(text);
        int n = Math.Min(textBytes.Length, f.Length);
        Array.Copy(textBytes, 0, current, 0, n);
        _values[i] = current;
        return this;
    }

    /// <summary>Encodes this record back to its fixed-width image in its own host encoding.</summary>
    public byte[] ToBytes() => ToBytes(_host);

    /// <summary>
    /// Encodes this record to a fixed-width image in the given host. Typed fields are re-encoded;
    /// raw-byte fields (STRING / blank) are written as-is.
    /// </summary>
    public byte[] ToBytes(HostKind host)
    {
        var image = new byte[Layout.Length];
        var span = image.AsSpan();
        for (int i = 0; i < Layout.Fields.Count; i++)
        {
            FieldDef f = Layout.Fields[i];
            Span<byte> slice = span.Slice(f.Offset, f.Length);
            switch (_values[i])
            {
                case byte[] raw:
                    raw.CopyTo(slice);
                    break;
                case string s:
                    EncodeText(s, slice, host);
                    break;
                case decimal d:
                    EncodeNumeric(f, d, slice, host);
                    break;
                default:
                    throw new InvalidOperationException($"Field '{f.Name}' has no value.");
            }
        }
        return image;
    }

    private byte[] SerializeField(FieldDef f, object? value)
    {
        var buf = new byte[f.Length];
        switch (value)
        {
            case byte[] raw: raw.CopyTo(buf, 0); break;
            case string s: EncodeText(s, buf, _host); break;
            case decimal d: EncodeNumeric(f, d, buf, _host); break;
        }
        return buf;
    }

    private string ValueAsText(int i) => _values[i] switch
    {
        string s => s,
        byte[] b => HostEncoding.For(_host).GetString(b),
        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };

    private decimal ValueAsNumber(int i) => _values[i] switch
    {
        decimal d => d,
        byte[] b => DecodeNumeric(Layout.Fields[i], b, _host),
        string s => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        _ => 0m,
    };

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
            case CobolUsage.Display: ZonedDecimalCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed, host); break;
            case CobolUsage.Comp3: PackedDecimalCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed); break;
            case CobolUsage.Comp: BinaryCodec.Encode(value, slice, f.TotalDigits, f.Scale, f.Signed); break;
            default: throw new NotSupportedException($"Unsupported usage {f.Usage} for field {f.Name}.");
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

    private int IndexOf(string name)
    {
        for (int i = 0; i < Layout.Fields.Count; i++)
            if (Layout.Fields[i].Name == name) return i;
        throw new KeyNotFoundException($"Field '{name}' not found in record '{Layout.Name}'.");
    }
}
