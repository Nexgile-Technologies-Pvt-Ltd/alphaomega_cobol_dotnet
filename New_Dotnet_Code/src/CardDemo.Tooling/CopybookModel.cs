using CardDemo.Cobol.Runtime;

namespace CardDemo.Tooling;

/// <summary>
/// A node in the parsed copybook tree: one data description entry, with its children for group items.
/// Sizes are in bytes; <see cref="UnitSize"/> is one occurrence and <see cref="TotalSize"/> accounts
/// for OCCURS.
/// </summary>
internal sealed class CopybookNode
{
    public required int Level { get; init; }
    public required string Name { get; init; }
    public bool IsFiller { get; init; }
    public PicInfo? Pic { get; init; }
    public CobolUsage Usage { get; init; } = CobolUsage.Display;
    public int Occurs { get; init; } = 1;
    public string? Redefines { get; init; }
    public List<CopybookNode> Children { get; } = [];

    public int UnitSize { get; set; }
    public int TotalSize => UnitSize * Occurs;
    public bool IsGroup => Pic is null;
}

/// <summary>
/// The parsed structure of a single copybook record (the 01 level), retaining REDEFINES alternates so
/// that a caller can flatten the layout for a specific active variant (e.g. the EXPORT file, whose
/// 460-byte data area is redefined five ways by record type).
/// </summary>
public sealed class CopybookModel
{
    private readonly CopybookNode _root;

    /// <summary>The 01-level record name.</summary>
    public string Name => _root.Name;

    internal CopybookModel(CopybookNode root)
    {
        _root = root;
        ComputeSize(_root);
    }

    /// <summary>
    /// Flattens the record into an elementary-field <see cref="RecordLayout"/>. By default each storage
    /// region uses its original (non-redefining) definition; naming a redefine in
    /// <paramref name="selectedRedefines"/> activates that alternate instead (and suppresses the
    /// original plus the other alternates of the same region).
    /// </summary>
    public RecordLayout Flatten(params string[] selectedRedefines)
    {
        var selected = new HashSet<string>(selectedRedefines, StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldDef>();
        Emit(_root, baseOffset: 0, suffix: "", selected, fields);
        return new RecordLayout(_root.Name, fields, _root.TotalSize);
    }

    private static void ComputeSize(CopybookNode node)
    {
        if (!node.IsGroup)
        {
            node.UnitSize = ElementaryLength(node);
            return;
        }

        int sum = 0;
        foreach (CopybookNode child in node.Children)
        {
            ComputeSize(child);
            if (child.Redefines is null) sum += child.TotalSize; // redefines overlay; they add no length
        }
        node.UnitSize = sum;
    }

    private static int ElementaryLength(CopybookNode node)
    {
        PicInfo pic = node.Pic!;
        if (pic.Category == CobolCategory.Alphanumeric) return pic.ByteCount;
        return node.Usage switch
        {
            CobolUsage.Display => pic.TotalDigits,
            CobolUsage.Comp3 => PackedDecimalCodec.ByteLength(pic.TotalDigits),
            CobolUsage.Comp => BinaryCodec.ByteLength(pic.TotalDigits),
            _ => throw new NotSupportedException($"Unsupported usage {node.Usage} for field {node.Name}."),
        };
    }

    private static void Emit(CopybookNode node, int baseOffset, string suffix, HashSet<string> selected, List<FieldDef> output)
    {
        for (int k = 0; k < node.Occurs; k++)
        {
            string occSuffix = node.Occurs > 1 ? $"{suffix}_{k + 1}" : suffix;
            int occBase = baseOffset + k * node.UnitSize;

            if (!node.IsGroup)
            {
                PicInfo pic = node.Pic!;
                output.Add(new FieldDef(
                    Name: node.Name + occSuffix,
                    Offset: occBase,
                    Length: node.UnitSize,
                    Category: pic.Category,
                    Usage: node.Usage,
                    Signed: pic.Signed,
                    IntegerDigits: pic.IntegerDigits,
                    Scale: pic.Scale,
                    IsFiller: node.IsFiller));
                continue;
            }

            int cursor = occBase;
            var siblingOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (CopybookNode child in node.Children)
            {
                int childBase = child.Redefines is not null && siblingOffsets.TryGetValue(child.Redefines, out int ro)
                    ? ro
                    : cursor;

                if (IsActive(child, node.Children, selected))
                    Emit(child, childBase, occSuffix, selected, output);

                siblingOffsets[child.Name] = childBase;
                if (child.Redefines is null) cursor += child.TotalSize;
            }
        }
    }

    private static bool IsActive(CopybookNode child, List<CopybookNode> siblings, HashSet<string> selected)
    {
        if (child.Redefines is not null)
            return selected.Contains(child.Name);

        // An original definition is suppressed when one of its redefines has been selected.
        foreach (CopybookNode s in siblings)
            if (string.Equals(s.Redefines, child.Name, StringComparison.OrdinalIgnoreCase) && selected.Contains(s.Name))
                return false;
        return true;
    }
}
