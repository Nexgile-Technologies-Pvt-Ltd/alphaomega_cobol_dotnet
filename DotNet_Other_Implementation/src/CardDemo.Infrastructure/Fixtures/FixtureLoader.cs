using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;

namespace CardDemo.Infrastructure.Fixtures;

/// <summary>
/// Parses the supplied fixed-width ASCII fixtures into entity lists. Positions come
/// from the CV*.cpy copybooks and Appendix-File-and-Record-Layouts.md (1-based
/// inclusive). Each line is split on <c>\n</c> with a trailing <c>\r</c> trimmed;
/// blank trailing lines are ignored; every record's length is validated before any
/// field is sliced. Signed numeric fields are decoded via <see cref="ZonedDecimal"/>.
/// </summary>
public sealed class FixtureLoader
{
    private const int AcctReclen = 300;
    private const int CardReclen = 150;
    private const int XrefReclen = 36;
    private const int CustReclen = 500;
    private const int TranReclen = 350;
    private const int DiscReclen = 50;
    private const int TcatReclen = 50;
    private const int TrancatgReclen = 60;
    private const int TrantypeReclen = 60;

    public IReadOnlyList<Account> LoadAccounts(string path) =>
        Parse(path, AcctReclen, (line, ln) => new Account
        {
            AccountId = Text(line, 1, 11, path, ln, "ACCT-ID"),
            ActiveStatus = Text(line, 12, 1, path, ln, "ACCT-ACTIVE-STATUS"),
            CurrentBalance = Signed(line, 13, 12, 2, path, ln, "ACCT-CURR-BAL"),
            CreditLimit = Signed(line, 25, 12, 2, path, ln, "ACCT-CREDIT-LIMIT"),
            CashCreditLimit = Signed(line, 37, 12, 2, path, ln, "ACCT-CASH-CREDIT-LIMIT"),
            OpenDate = Text(line, 49, 10, path, ln, "ACCT-OPEN-DATE"),
            ExpirationDate = Text(line, 59, 10, path, ln, "ACCT-EXPIRAION-DATE"),
            ReissueDate = Text(line, 69, 10, path, ln, "ACCT-REISSUE-DATE"),
            CurrentCycleCredit = Signed(line, 79, 12, 2, path, ln, "ACCT-CURR-CYC-CREDIT"),
            CurrentCycleDebit = Signed(line, 91, 12, 2, path, ln, "ACCT-CURR-CYC-DEBIT"),
            AddressZip = Text(line, 103, 10, path, ln, "ACCT-ADDR-ZIP"),
            GroupId = Text(line, 113, 10, path, ln, "ACCT-GROUP-ID"),
        });

    public IReadOnlyList<Card> LoadCards(string path) =>
        Parse(path, CardReclen, (line, ln) => new Card
        {
            CardNumber = Text(line, 1, 16, path, ln, "CARD-NUM"),
            AccountId = Text(line, 17, 11, path, ln, "CARD-ACCT-ID"),
            Cvv = Text(line, 28, 3, path, ln, "CARD-CVV-CD"),
            EmbossedName = Text(line, 31, 50, path, ln, "CARD-EMBOSSED-NAME"),
            ExpirationDate = Text(line, 81, 10, path, ln, "CARD-EXPIRAION-DATE"),
            ActiveStatus = Text(line, 91, 1, path, ln, "CARD-ACTIVE-STATUS"),
        });

    public IReadOnlyList<Customer> LoadCustomers(string path) =>
        Parse(path, CustReclen, (line, ln) => new Customer
        {
            CustomerId = Text(line, 1, 9, path, ln, "CUST-ID"),
            FirstName = Text(line, 10, 25, path, ln, "CUST-FIRST-NAME"),
            MiddleName = Text(line, 35, 25, path, ln, "CUST-MIDDLE-NAME"),
            LastName = Text(line, 60, 25, path, ln, "CUST-LAST-NAME"),
            AddressLine1 = Text(line, 85, 50, path, ln, "CUST-ADDR-LINE-1"),
            AddressLine2 = Text(line, 135, 50, path, ln, "CUST-ADDR-LINE-2"),
            AddressLine3 = Text(line, 185, 50, path, ln, "CUST-ADDR-LINE-3"),
            StateCode = Text(line, 235, 2, path, ln, "CUST-ADDR-STATE-CD"),
            CountryCode = Text(line, 237, 3, path, ln, "CUST-ADDR-COUNTRY-CD"),
            Zip = Text(line, 240, 10, path, ln, "CUST-ADDR-ZIP"),
            PhoneNumber1 = Text(line, 250, 15, path, ln, "CUST-PHONE-NUM-1"),
            PhoneNumber2 = Text(line, 265, 15, path, ln, "CUST-PHONE-NUM-2"),
            Ssn = Text(line, 280, 9, path, ln, "CUST-SSN"),
            GovtIssuedId = Text(line, 289, 20, path, ln, "CUST-GOVT-ISSUED-ID"),
            DateOfBirth = Text(line, 309, 10, path, ln, "CUST-DOB-YYYY-MM-DD"),
            EftAccountId = Text(line, 319, 10, path, ln, "CUST-EFT-ACCOUNT-ID"),
            PrimaryCardHolderIndicator = Text(line, 329, 1, path, ln, "CUST-PRI-CARD-HOLDER-IND"),
            FicoCreditScore = Int(line, 330, 3, path, ln, "CUST-FICO-CREDIT-SCORE"),
        });

    /// <summary>Card cross-reference in the 36-byte ASCII compat form: card16 + cust9 + acct11.</summary>
    public IReadOnlyList<CardXref> LoadXrefs(string path) =>
        Parse(path, XrefReclen, (line, ln) => new CardXref
        {
            CardNumber = Text(line, 1, 16, path, ln, "XREF-CARD-NUM"),
            CustomerId = Text(line, 17, 9, path, ln, "XREF-CUST-ID"),
            AccountId = Text(line, 26, 11, path, ln, "XREF-ACCT-ID"),
        });

    public IReadOnlyList<TransactionCategoryBalance> LoadCategoryBalances(string path) =>
        Parse(path, TcatReclen, (line, ln) => new TransactionCategoryBalance
        {
            AccountId = Text(line, 1, 11, path, ln, "TRANCAT-ACCT-ID"),
            TypeCode = Text(line, 12, 2, path, ln, "TRANCAT-TYPE-CD"),
            CategoryCode = Text(line, 14, 4, path, ln, "TRANCAT-CD"),
            Balance = Signed(line, 18, 11, 2, path, ln, "TRAN-CAT-BAL"),
        });

    public IReadOnlyList<DisclosureGroup> LoadDisclosureGroups(string path) =>
        Parse(path, DiscReclen, (line, ln) => new DisclosureGroup
        {
            GroupId = Text(line, 1, 10, path, ln, "DIS-ACCT-GROUP-ID"),
            TypeCode = Text(line, 11, 2, path, ln, "DIS-TRAN-TYPE-CD"),
            CategoryCode = Text(line, 13, 4, path, ln, "DIS-TRAN-CAT-CD"),
            InterestRate = Signed(line, 17, 6, 2, path, ln, "DIS-INT-RATE"),
        });

    public IReadOnlyList<TransactionType> LoadTypes(string path) =>
        Parse(path, TrantypeReclen, (line, ln) => new TransactionType
        {
            TypeCode = Text(line, 1, 2, path, ln, "TRAN-TYPE"),
            Description = Text(line, 3, 50, path, ln, "TRAN-TYPE-DESC"),
        });

    public IReadOnlyList<TransactionCategory> LoadCategories(string path) =>
        Parse(path, TrancatgReclen, (line, ln) => new TransactionCategory
        {
            TypeCode = Text(line, 1, 2, path, ln, "TRAN-TYPE-CD"),
            CategoryCode = Text(line, 3, 4, path, ln, "TRAN-CAT-CD"),
            Description = Text(line, 7, 50, path, ln, "TRAN-CAT-TYPE-DESC"),
        });

    /// <summary>Daily transaction input records (CVTRA05Y layout) — posting INPUT, not seeded.</summary>
    public IReadOnlyList<Transaction> LoadDailyTransactions(string path) =>
        Parse(path, TranReclen, (line, ln) => new Transaction
        {
            TransactionId = Text(line, 1, 16, path, ln, "TRAN-ID"),
            TypeCode = Text(line, 17, 2, path, ln, "TRAN-TYPE-CD"),
            CategoryCode = Text(line, 19, 4, path, ln, "TRAN-CAT-CD"),
            Source = Text(line, 23, 10, path, ln, "TRAN-SOURCE"),
            Description = Text(line, 33, 100, path, ln, "TRAN-DESC"),
            Amount = Signed(line, 133, 11, 2, path, ln, "TRAN-AMT"),
            MerchantId = Text(line, 144, 9, path, ln, "TRAN-MERCHANT-ID"),
            MerchantName = Text(line, 153, 50, path, ln, "TRAN-MERCHANT-NAME"),
            MerchantCity = Text(line, 203, 50, path, ln, "TRAN-MERCHANT-CITY"),
            MerchantZip = Text(line, 253, 10, path, ln, "TRAN-MERCHANT-ZIP"),
            CardNumber = Text(line, 263, 16, path, ln, "TRAN-CARD-NUM"),
            OriginTimestamp = TextRaw(line, 279, 26, path, ln, "TRAN-ORIG-TS"),
            ProcessTimestamp = TextRaw(line, 305, 26, path, ln, "TRAN-PROC-TS"),
        });

    /// <summary>
    /// The 10 bootstrap users (no ASCII file). Each is migrated by hashing the known
    /// bootstrap password "PASSWORD"; cleartext is never stored (CSUSR01Y layout).
    /// </summary>
    public IReadOnlyList<UserSecurity> SeedUsers(IPasswordHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        var hash = hasher.Hash("PASSWORD");

        (string Id, string First, string Last, string Type)[] users =
        [
            ("ADMIN001", "MARGARET", "GOLD", "A"),
            ("ADMIN002", "RUSSELL", "RUSSELL", "A"),
            ("ADMIN003", "RAYMOND", "WHITMORE", "A"),
            ("ADMIN004", "EMMANUEL", "CASGRAIN", "A"),
            ("ADMIN005", "GRANVILLE", "LACHAPELLE", "A"),
            ("USER0001", "LAWRENCE", "THOMAS", "U"),
            ("USER0002", "AJITH", "KUMAR", "U"),
            ("USER0003", "LAURITZ", "ALME", "U"),
            ("USER0004", "AVERARDO", "MAZZI", "U"),
            ("USER0005", "LEE", "TING", "U"),
        ];

        return users
            .Select(u => new UserSecurity
            {
                UserId = u.Id,
                FirstName = u.First,
                LastName = u.Last,
                PasswordHash = hash,
                UserType = u.Type,
            })
            .ToList();
    }

    // --- parsing helpers -------------------------------------------------

    private static List<T> Parse<T>(string path, int reclen, Func<string, int, T> map)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture file not found: {path}", path);

        var raw = File.ReadAllText(path);
        var lines = raw.Split('\n');
        var result = new List<T>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];

            // Ignore blank trailing lines (final newline produces an empty entry).
            if (line.Length == 0)
                continue;

            if (line.Length != reclen)
                throw new FormatException(
                    $"{Path.GetFileName(path)} line {i + 1}: expected record length {reclen} but was {line.Length}.");

            result.Add(map(line, i + 1));
        }

        return result;
    }

    /// <summary>Slice a 1-based inclusive field and trim trailing spaces (display text/identifiers).</summary>
    private static string Text(string line, int start, int length, string path, int lineNo, string field)
    {
        return Slice(line, start, length, path, lineNo, field).TrimEnd();
    }

    /// <summary>Slice a field preserving all spaces (timestamps must remain 26 bytes).</summary>
    private static string TextRaw(string line, int start, int length, string path, int lineNo, string field)
    {
        return Slice(line, start, length, path, lineNo, field);
    }

    private static decimal Signed(string line, int start, int length, int decimals, string path, int lineNo, string field)
    {
        var raw = Slice(line, start, length, path, lineNo, field);
        try
        {
            return ZonedDecimal.Decode(raw, decimals);
        }
        catch (FormatException ex)
        {
            throw new FormatException(
                $"{Path.GetFileName(path)} line {lineNo} field {field}: {ex.Message}", ex);
        }
    }

    private static int Int(string line, int start, int length, string path, int lineNo, string field)
    {
        var raw = Slice(line, start, length, path, lineNo, field).Trim();
        if (raw.Length == 0)
            return 0;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException(
                $"{Path.GetFileName(path)} line {lineNo} field {field}: \"{raw}\" is not an integer.");
        return value;
    }

    private static string Slice(string line, int start, int length, string path, int lineNo, string field)
    {
        var from = start - 1;
        if (from < 0 || from + length > line.Length)
            throw new FormatException(
                $"{Path.GetFileName(path)} line {lineNo} field {field}: slice [{start}..{start + length - 1}] " +
                $"exceeds record length {line.Length}.");
        return line.Substring(from, length);
    }
}
