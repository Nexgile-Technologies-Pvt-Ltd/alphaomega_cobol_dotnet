using CardDemo.Domain.Common;

namespace CardDemo.Domain.Entities;

/// <summary>
/// Card master record. Source layout: CVACT02Y.cpy (CARD-RECORD, RECLN 150).
/// Primary key is the 16-byte card number; non-unique alternate access by account.
/// </summary>
public sealed class Card : IVersioned
{
    /// <summary>CARD-NUM PIC X(16) — primary key.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>CARD-ACCT-ID PIC 9(11).</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>CARD-CVV-CD PIC 9(03).</summary>
    public string Cvv { get; set; } = string.Empty;

    /// <summary>CARD-EMBOSSED-NAME PIC X(50).</summary>
    public string EmbossedName { get; set; } = string.Empty;

    /// <summary>CARD-EXPIRAION-DATE PIC X(10) — source spelling retained.</summary>
    public string ExpirationDate { get; set; } = string.Empty;

    /// <summary>CARD-ACTIVE-STATUS PIC X(01).</summary>
    public string ActiveStatus { get; set; } = string.Empty;

    public long RowVersion { get; set; }
}
