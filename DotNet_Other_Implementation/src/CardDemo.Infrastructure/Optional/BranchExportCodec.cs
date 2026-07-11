using System.Globalization;
using System.Text;
using CardDemo.Domain.Entities;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Fixed-width 500-byte record codec for the branch export/import file
/// (CBEXPORT / CBIMPORT). Every record is exactly <see cref="RecordLength"/> bytes:
/// byte&#160;0 is the record-type character (C/A/X/T/D) and the remaining 499 bytes
/// are a fixed-width, positional text encoding of that entity's fields.
///
/// The legacy file mixed DISPLAY, COMP (big-endian binary) and COMP-3 (packed
/// decimal) encodings within each record. The safe target models this at a
/// functional level with an all-text encoding (per the phase-2 contract: no
/// byte-exact COMP-3/IMS parity is required). The only invariant that matters is
/// that the codec is <b>symmetric</b> — <c>Decode(Encode(x)) == x</c> for every
/// entity — so an export followed by an import reproduces the same logical rows.
///
/// Text fields are space-padded/truncated to a fixed width. Money is encoded as a
/// fixed 15-character signed decimal ("+/-" + 11 integer digits + '.' + 2 decimal
/// digits) so it decodes back to the exact same <see cref="decimal"/> value.
/// </summary>
public static class BranchExportCodec
{
    /// <summary>Every export/import record is exactly this many bytes.</summary>
    public const int RecordLength = 500;

    public const char CustomerType = 'C';
    public const char AccountType = 'A';
    public const char XrefType = 'X';
    public const char TransactionType = 'T';
    public const char CardType = 'D';

    // Fixed field width for a signed money value: sign + 11 int digits + '.' + 2 dp.
    private const int MoneyWidth = 15;

    // ---- Customer (type 'C') ------------------------------------------------

    public static string EncodeCustomer(Customer c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var w = new FieldWriter(CustomerType);
        w.Text(c.CustomerId, 9);
        w.Text(c.FirstName, 25);
        w.Text(c.MiddleName, 25);
        w.Text(c.LastName, 25);
        w.Text(c.AddressLine1, 50);
        w.Text(c.AddressLine2, 50);
        w.Text(c.AddressLine3, 50);
        w.Text(c.StateCode, 2);
        w.Text(c.CountryCode, 3);
        w.Text(c.Zip, 10);
        w.Text(c.PhoneNumber1, 15);
        w.Text(c.PhoneNumber2, 15);
        w.Text(c.Ssn, 9);
        w.Text(c.GovtIssuedId, 20);
        w.Text(c.DateOfBirth, 10);
        w.Text(c.EftAccountId, 10);
        w.Text(c.PrimaryCardHolderIndicator, 1);
        w.Int(c.FicoCreditScore, 5);
        return w.Finish();
    }

    public static Customer DecodeCustomer(string record)
    {
        var r = new FieldReader(record, CustomerType);
        return new Customer
        {
            CustomerId = r.Text(9),
            FirstName = r.Text(25),
            MiddleName = r.Text(25),
            LastName = r.Text(25),
            AddressLine1 = r.Text(50),
            AddressLine2 = r.Text(50),
            AddressLine3 = r.Text(50),
            StateCode = r.Text(2),
            CountryCode = r.Text(3),
            Zip = r.Text(10),
            PhoneNumber1 = r.Text(15),
            PhoneNumber2 = r.Text(15),
            Ssn = r.Text(9),
            GovtIssuedId = r.Text(20),
            DateOfBirth = r.Text(10),
            EftAccountId = r.Text(10),
            PrimaryCardHolderIndicator = r.Text(1),
            FicoCreditScore = r.Int(5),
        };
    }

    // ---- Account (type 'A') -------------------------------------------------

    public static string EncodeAccount(Account a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var w = new FieldWriter(AccountType);
        w.Text(a.AccountId, 11);
        w.Text(a.ActiveStatus, 1);
        w.Money(a.CurrentBalance);
        w.Money(a.CreditLimit);
        w.Money(a.CashCreditLimit);
        w.Text(a.OpenDate, 10);
        w.Text(a.ExpirationDate, 10);
        w.Text(a.ReissueDate, 10);
        w.Money(a.CurrentCycleCredit);
        w.Money(a.CurrentCycleDebit);
        w.Text(a.AddressZip, 10);
        w.Text(a.GroupId, 10);
        return w.Finish();
    }

    public static Account DecodeAccount(string record)
    {
        var r = new FieldReader(record, AccountType);
        return new Account
        {
            AccountId = r.Text(11),
            ActiveStatus = r.Text(1),
            CurrentBalance = r.Money(),
            CreditLimit = r.Money(),
            CashCreditLimit = r.Money(),
            OpenDate = r.Text(10),
            ExpirationDate = r.Text(10),
            ReissueDate = r.Text(10),
            CurrentCycleCredit = r.Money(),
            CurrentCycleDebit = r.Money(),
            AddressZip = r.Text(10),
            GroupId = r.Text(10),
        };
    }

    // ---- CardXref (type 'X') ------------------------------------------------

    public static string EncodeXref(CardXref x)
    {
        ArgumentNullException.ThrowIfNull(x);
        var w = new FieldWriter(XrefType);
        w.Text(x.CardNumber, 16);
        w.Text(x.CustomerId, 9);
        w.Text(x.AccountId, 11);
        return w.Finish();
    }

    public static CardXref DecodeXref(string record)
    {
        var r = new FieldReader(record, XrefType);
        return new CardXref
        {
            CardNumber = r.Text(16),
            CustomerId = r.Text(9),
            AccountId = r.Text(11),
        };
    }

    // ---- Transaction (type 'T') --------------------------------------------

    public static string EncodeTransaction(Transaction t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var w = new FieldWriter(TransactionType);
        w.Text(t.TransactionId, 16);
        w.Text(t.TypeCode, 2);
        w.Text(t.CategoryCode, 4);
        w.Text(t.Source, 10);
        w.Text(t.Description, 100);
        w.Money(t.Amount);
        w.Text(t.MerchantId, 9);
        w.Text(t.MerchantName, 50);
        w.Text(t.MerchantCity, 50);
        w.Text(t.MerchantZip, 10);
        w.Text(t.CardNumber, 16);
        w.Text(t.OriginTimestamp, 26);
        w.Text(t.ProcessTimestamp, 26);
        return w.Finish();
    }

    public static Transaction DecodeTransaction(string record)
    {
        var r = new FieldReader(record, TransactionType);
        return new Transaction
        {
            TransactionId = r.Text(16),
            TypeCode = r.Text(2),
            CategoryCode = r.Text(4),
            Source = r.Text(10),
            Description = r.Text(100),
            Amount = r.Money(),
            MerchantId = r.Text(9),
            MerchantName = r.Text(50),
            MerchantCity = r.Text(50),
            MerchantZip = r.Text(10),
            CardNumber = r.Text(16),
            OriginTimestamp = r.Text(26),
            ProcessTimestamp = r.Text(26),
        };
    }

    // ---- Card (type 'D') ----------------------------------------------------

    public static string EncodeCard(Card d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var w = new FieldWriter(CardType);
        w.Text(d.CardNumber, 16);
        w.Text(d.AccountId, 11);
        w.Text(d.Cvv, 3);
        w.Text(d.EmbossedName, 50);
        w.Text(d.ExpirationDate, 10);
        w.Text(d.ActiveStatus, 1);
        return w.Finish();
    }

    public static Card DecodeCard(string record)
    {
        var r = new FieldReader(record, CardType);
        return new Card
        {
            CardNumber = r.Text(16),
            AccountId = r.Text(11),
            Cvv = r.Text(3),
            EmbossedName = r.Text(50),
            ExpirationDate = r.Text(10),
            ActiveStatus = r.Text(1),
        };
    }

    /// <summary>The record-type character (byte 0) of a 500-byte record.</summary>
    public static char TypeOf(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Length == 0)
            throw new FormatException("Empty record.");
        return record[0];
    }

    /// <summary>True when the string is a well-formed 500-byte record of a known type.</summary>
    public static bool IsValidRecord(string? record)
    {
        if (record is null || record.Length != RecordLength)
            return false;
        return record[0] is CustomerType or AccountType or XrefType or TransactionType or CardType;
    }

    // ---- fixed-width text builder ------------------------------------------

    private sealed class FieldWriter
    {
        private readonly StringBuilder _sb = new(RecordLength);

        public FieldWriter(char type) => _sb.Append(type);

        public void Text(string? value, int width)
        {
            var v = value ?? string.Empty;
            // Sanitise the field separator space semantics: a fixed-width slot is
            // simply padded with spaces; trailing/leading spaces in the value are
            // preserved except that TrimEnd is applied on decode.
            if (v.Length > width)
                v = v[..width];
            _sb.Append(v.PadRight(width));
        }

        public void Int(int value, int width)
            => Text(value.ToString(CultureInfo.InvariantCulture), width);

        public void Money(decimal value)
        {
            var sign = value < 0 ? '-' : '+';
            var magnitude = Math.Abs(decimal.Round(value, 2, MidpointRounding.AwayFromZero));
            // 11 integer digits + '.' + 2 dp, zero-padded; prefixed with the sign.
            var body = magnitude.ToString("00000000000.00", CultureInfo.InvariantCulture);
            var field = sign + body;
            _sb.Append(field.PadRight(MoneyWidth)[..MoneyWidth]);
        }

        public string Finish()
        {
            if (_sb.Length > RecordLength)
                throw new InvalidOperationException(
                    $"Encoded record exceeds {RecordLength} bytes ({_sb.Length}).");
            if (_sb.Length < RecordLength)
                _sb.Append(' ', RecordLength - _sb.Length);
            return _sb.ToString();
        }
    }

    private sealed class FieldReader
    {
        private readonly string _record;
        private int _pos;

        public FieldReader(string record, char expectedType)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.Length != RecordLength)
                throw new FormatException(
                    $"Record length {record.Length} != {RecordLength}.");
            if (record[0] != expectedType)
                throw new FormatException(
                    $"Record type '{record[0]}' != expected '{expectedType}'.");
            _record = record;
            _pos = 1; // skip the type byte
        }

        public string Text(int width)
        {
            var slice = _record.Substring(_pos, width);
            _pos += width;
            return slice.TrimEnd();
        }

        public int Int(int width)
        {
            var slice = Text(width);
            return slice.Length == 0
                ? 0
                : int.Parse(slice, CultureInfo.InvariantCulture);
        }

        public decimal Money()
        {
            var slice = _record.Substring(_pos, MoneyWidth);
            _pos += MoneyWidth;
            var trimmed = slice.Trim();
            return decimal.Parse(trimmed, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
        }
    }
}
