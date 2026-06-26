using CardDemo.Runtime;
using CardDemo.Domain;

namespace CardDemo.Ims;

/// <summary>
/// Encodes and decodes the two IMS Pending-Authorization segment io-areas — the root summary
/// (<c>PENDING-AUTH-SUMMARY</c>, copybook <c>CIPAUSMY</c>, 100 bytes) and the child detail
/// (<c>PENDING-AUTH-DETAILS</c>, copybook <c>CIPAUDTY</c>, 200 bytes) — to/from their fixed-width host
/// record images. This is the shared serialization used by the three IMS batch utilities
/// (<see cref="Paudbunl"/> UNLOAD, <see cref="Dbunldgs"/> GSAM-UNLOAD, <see cref="Paudblod"/> LOAD) so an
/// unload followed by a load round-trips byte-for-byte.
/// </summary>
/// <remarks>
/// <para>The field order, PIC widths and USAGE come straight from the copybooks (read in full):
/// <c>CIPAUSMY.cpy</c> and <c>CIPAUDTY.cpy</c>. COMP-3 fields go through
/// <see cref="PackedDecimalCodec"/>, COMP halfwords through <see cref="BinaryCodec"/>, USAGE-DISPLAY
/// numerics through <see cref="ZonedDecimalCodec"/>, and PIC X text through
/// <see cref="HostEncoding"/>. The COBOL programs <c>MOVE</c> the whole 01-level group into the output
/// record (a byte copy), so these methods reproduce that group image exactly, including the trailing
/// FILLER (X(34) on the summary, X(17) on the detail) emitted as spaces.</para>
/// <para>The relational <see cref="PautDetail.AuthKey"/> (PAUT9CTS CHAR(8)) is not a stored byte range of
/// the segment image; it is the child sequence key derived from the two 9s-complement components
/// (<c>PA-AUTH-DATE-9C</c> 5 digits + <c>PA-AUTH-TIME-9C</c> 9 digits). It is rebuilt on decode (load) via
/// <see cref="BuildAuthKey"/> — the same construction the authorization writer uses — so a loaded child
/// keys and orders identically to one written through the online/MQ path.</para>
/// </remarks>
internal static class PautSegmentImages
{
    /// <summary>Length in bytes of the CIPAUSMY summary group image (= OPFIL1-REC PIC X(100)).</summary>
    public const int SummaryImageLength = 100;

    /// <summary>Length in bytes of the CIPAUDTY detail group image (= CHILD-SEG-REC PIC X(200)).</summary>
    public const int DetailImageLength = 200;

    /// <summary>Byte length of the S9(11) COMP-3 root key (ROOT-SEG-KEY): floor(11/2)+1 = 6.</summary>
    public const int RootSegKeyLength = 6;

    // ---- Summary (CIPAUSMY) — 100 bytes ---------------------------------------------------------------
    // 05 PA-ACCT-ID            PIC S9(11) COMP-3.            (6 bytes)
    // 05 PA-CUST-ID            PIC  9(09).                  (9 bytes, DISPLAY unsigned)
    // 05 PA-AUTH-STATUS        PIC  X(01).                  (1)
    // 05 PA-ACCOUNT-STATUS     PIC  X(02) OCCURS 5 TIMES.   (10)
    // 05 PA-CREDIT-LIMIT       PIC S9(09)V99 COMP-3.        (6)
    // 05 PA-CASH-LIMIT         PIC S9(09)V99 COMP-3.        (6)
    // 05 PA-CREDIT-BALANCE     PIC S9(09)V99 COMP-3.        (6)
    // 05 PA-CASH-BALANCE       PIC S9(09)V99 COMP-3.        (6)
    // 05 PA-APPROVED-AUTH-CNT  PIC S9(04) COMP.             (2 bytes, signed halfword)
    // 05 PA-DECLINED-AUTH-CNT  PIC S9(04) COMP.             (2)
    // 05 PA-APPROVED-AUTH-AMT  PIC S9(09)V99 COMP-3.        (6)
    // 05 PA-DECLINED-AUTH-AMT  PIC S9(09)V99 COMP-3.        (6)
    // 05 FILLER                PIC X(34).                   (34)

    /// <summary>Serializes a <see cref="PautSummary"/> to its 100-byte CIPAUSMY group image.</summary>
    public static byte[] EncodeSummary(PautSummary s, HostKind host)
    {
        var w = new SegWriter(host);
        w.Comp3(s.AcctId, 11, 0, signed: true);            // PA-ACCT-ID
        w.Zoned(s.CustId, 9, 0, signed: false);            // PA-CUST-ID 9(09)
        w.Alpha(s.AuthStatus, 1);                          // PA-AUTH-STATUS
        w.Alpha(s.AccountStatus1, 2);                      // PA-ACCOUNT-STATUS(1)
        w.Alpha(s.AccountStatus2, 2);                      // PA-ACCOUNT-STATUS(2)
        w.Alpha(s.AccountStatus3, 2);                      // PA-ACCOUNT-STATUS(3)
        w.Alpha(s.AccountStatus4, 2);                      // PA-ACCOUNT-STATUS(4)
        w.Alpha(s.AccountStatus5, 2);                      // PA-ACCOUNT-STATUS(5)
        w.Comp3(s.CreditLimit, 11, 2, signed: true);       // PA-CREDIT-LIMIT  S9(09)V99
        w.Comp3(s.CashLimit, 11, 2, signed: true);         // PA-CASH-LIMIT
        w.Comp3(s.CreditBalance, 11, 2, signed: true);     // PA-CREDIT-BALANCE
        w.Comp3(s.CashBalance, 11, 2, signed: true);       // PA-CASH-BALANCE
        w.Comp(s.ApprovedAuthCnt, 4, 0, signed: true);     // PA-APPROVED-AUTH-CNT S9(04) COMP
        w.Comp(s.DeclinedAuthCnt, 4, 0, signed: true);     // PA-DECLINED-AUTH-CNT S9(04) COMP
        w.Comp3(s.ApprovedAuthAmt, 11, 2, signed: true);   // PA-APPROVED-AUTH-AMT
        w.Comp3(s.DeclinedAuthAmt, 11, 2, signed: true);   // PA-DECLINED-AUTH-AMT
        w.Alpha("", 34);                                   // FILLER X(34)
        return w.ToArray(SummaryImageLength);
    }

    /// <summary>Decodes a 100-byte CIPAUSMY group image into a <see cref="PautSummary"/>.</summary>
    public static PautSummary DecodeSummary(ReadOnlySpan<byte> image, HostKind host)
    {
        var r = new SegReader(image, host);
        var s = new PautSummary
        {
            AcctId = (long)r.Comp3(11, 0, signed: true),
            CustId = (long)r.Zoned(9, 0, signed: false),
            AuthStatus = r.Alpha(1),
            AccountStatus1 = r.Alpha(2),
            AccountStatus2 = r.Alpha(2),
            AccountStatus3 = r.Alpha(2),
            AccountStatus4 = r.Alpha(2),
            AccountStatus5 = r.Alpha(2),
            CreditLimit = r.Comp3(11, 2, signed: true),
            CashLimit = r.Comp3(11, 2, signed: true),
            CreditBalance = r.Comp3(11, 2, signed: true),
            CashBalance = r.Comp3(11, 2, signed: true),
            ApprovedAuthCnt = (int)r.Comp(4, 0, signed: true),
            DeclinedAuthCnt = (int)r.Comp(4, 0, signed: true),
            ApprovedAuthAmt = r.Comp3(11, 2, signed: true),
            DeclinedAuthAmt = r.Comp3(11, 2, signed: true),
        };
        r.Skip(34); // FILLER X(34)
        return s;
    }

    // ---- Detail (CIPAUDTY) — 200 bytes ----------------------------------------------------------------
    // 05 PA-AUTHORIZATION-KEY.
    //    10 PA-AUTH-DATE-9C    PIC S9(05) COMP-3.           (3 bytes)
    //    10 PA-AUTH-TIME-9C    PIC S9(09) COMP-3.           (5 bytes)
    // 05 PA-AUTH-ORIG-DATE     PIC  X(06).
    // 05 PA-AUTH-ORIG-TIME     PIC  X(06).
    // 05 PA-CARD-NUM           PIC  X(16).
    // 05 PA-AUTH-TYPE          PIC  X(04).
    // 05 PA-CARD-EXPIRY-DATE   PIC  X(04).
    // 05 PA-MESSAGE-TYPE       PIC  X(06).
    // 05 PA-MESSAGE-SOURCE     PIC  X(06).
    // 05 PA-AUTH-ID-CODE       PIC  X(06).
    // 05 PA-AUTH-RESP-CODE     PIC  X(02).
    // 05 PA-AUTH-RESP-REASON   PIC  X(04).
    // 05 PA-PROCESSING-CODE    PIC  9(06).                  (DISPLAY unsigned)
    // 05 PA-TRANSACTION-AMT    PIC S9(10)V99 COMP-3.        (7 bytes)
    // 05 PA-APPROVED-AMT       PIC S9(10)V99 COMP-3.        (7 bytes)
    // 05 PA-MERCHANT-CATAGORY-CODE PIC X(04).
    // 05 PA-ACQR-COUNTRY-CODE  PIC  X(03).
    // 05 PA-POS-ENTRY-MODE     PIC  9(02).                  (DISPLAY unsigned)
    // 05 PA-MERCHANT-ID        PIC  X(15).
    // 05 PA-MERCHANT-NAME      PIC  X(22).
    // 05 PA-MERCHANT-CITY      PIC  X(13).
    // 05 PA-MERCHANT-STATE     PIC  X(02).
    // 05 PA-MERCHANT-ZIP       PIC  X(09).
    // 05 PA-TRANSACTION-ID     PIC  X(15).
    // 05 PA-MATCH-STATUS       PIC  X(01).
    // 05 PA-AUTH-FRAUD         PIC  X(01).
    // 05 PA-FRAUD-RPT-DATE     PIC  X(08).
    // 05 FILLER                PIC  X(17).

    /// <summary>Serializes a <see cref="PautDetail"/> to its 200-byte CIPAUDTY group image.</summary>
    public static byte[] EncodeDetail(PautDetail d, HostKind host)
    {
        var w = new SegWriter(host);
        w.Comp3(d.AuthDate9c, 5, 0, signed: true);         // PA-AUTH-DATE-9C
        w.Comp3(d.AuthTime9c, 9, 0, signed: true);         // PA-AUTH-TIME-9C
        w.Alpha(d.AuthOrigDate, 6);                        // PA-AUTH-ORIG-DATE
        w.Alpha(d.AuthOrigTime, 6);                        // PA-AUTH-ORIG-TIME
        w.Alpha(d.CardNum, 16);                            // PA-CARD-NUM
        w.Alpha(d.AuthType, 4);                            // PA-AUTH-TYPE
        w.Alpha(d.CardExpiryDate, 4);                      // PA-CARD-EXPIRY-DATE
        w.Alpha(d.MessageType, 6);                         // PA-MESSAGE-TYPE
        w.Alpha(d.MessageSource, 6);                       // PA-MESSAGE-SOURCE
        w.Alpha(d.AuthIdCode, 6);                          // PA-AUTH-ID-CODE
        w.Alpha(d.AuthRespCode, 2);                        // PA-AUTH-RESP-CODE
        w.Alpha(d.AuthRespReason, 4);                      // PA-AUTH-RESP-REASON
        w.Zoned(d.ProcessingCode, 6, 0, signed: false);   // PA-PROCESSING-CODE 9(06)
        w.Comp3(d.TransactionAmt, 12, 2, signed: true);   // PA-TRANSACTION-AMT S9(10)V99
        w.Comp3(d.ApprovedAmt, 12, 2, signed: true);      // PA-APPROVED-AMT    S9(10)V99
        w.Alpha(d.MerchantCatagoryCode, 4);               // PA-MERCHANT-CATAGORY-CODE
        w.Alpha(d.AcqrCountryCode, 3);                    // PA-ACQR-COUNTRY-CODE
        w.Zoned(d.PosEntryMode, 2, 0, signed: false);     // PA-POS-ENTRY-MODE 9(02)
        w.Alpha(d.MerchantId, 15);                        // PA-MERCHANT-ID
        w.Alpha(d.MerchantName, 22);                      // PA-MERCHANT-NAME
        w.Alpha(d.MerchantCity, 13);                      // PA-MERCHANT-CITY
        w.Alpha(d.MerchantState, 2);                      // PA-MERCHANT-STATE
        w.Alpha(d.MerchantZip, 9);                        // PA-MERCHANT-ZIP
        w.Alpha(d.TransactionId, 15);                     // PA-TRANSACTION-ID
        w.Alpha(d.MatchStatus, 1);                        // PA-MATCH-STATUS
        w.Alpha(d.AuthFraud, 1);                          // PA-AUTH-FRAUD
        w.Alpha(d.FraudRptDate, 8);                       // PA-FRAUD-RPT-DATE
        w.Alpha("", 17);                                  // FILLER X(17)
        return w.ToArray(DetailImageLength);
    }

    /// <summary>
    /// Decodes a 200-byte CIPAUDTY group image into a <see cref="PautDetail"/> belonging to
    /// <paramref name="acctId"/> (the parent root key, supplied separately — IMS parentage). The composite
    /// <see cref="PautDetail.AuthKey"/> is rebuilt from the two 9s-complement components.
    /// </summary>
    public static PautDetail DecodeDetail(ReadOnlySpan<byte> image, long acctId, HostKind host)
    {
        var r = new SegReader(image, host);
        int authDate9c = (int)r.Comp3(5, 0, signed: true);
        long authTime9c = (long)r.Comp3(9, 0, signed: true);
        var d = new PautDetail
        {
            AcctId = acctId,
            AuthDate9c = authDate9c,
            AuthTime9c = authTime9c,
            AuthKey = BuildAuthKey(authDate9c, authTime9c),
            AuthOrigDate = r.Alpha(6),
            AuthOrigTime = r.Alpha(6),
            CardNum = r.Alpha(16),
            AuthType = r.Alpha(4),
            CardExpiryDate = r.Alpha(4),
            MessageType = r.Alpha(6),
            MessageSource = r.Alpha(6),
            AuthIdCode = r.Alpha(6),
            AuthRespCode = r.Alpha(2),
            AuthRespReason = r.Alpha(4),
            ProcessingCode = (int)r.Zoned(6, 0, signed: false),
            TransactionAmt = r.Comp3(12, 2, signed: true),
            ApprovedAmt = r.Comp3(12, 2, signed: true),
            MerchantCatagoryCode = r.Alpha(4),
            AcqrCountryCode = r.Alpha(3),
            PosEntryMode = (int)r.Zoned(2, 0, signed: false),
            MerchantId = r.Alpha(15),
            MerchantName = r.Alpha(22),
            MerchantCity = r.Alpha(13),
            MerchantState = r.Alpha(2),
            MerchantZip = r.Alpha(9),
            TransactionId = r.Alpha(15),
            MatchStatus = r.Alpha(1),
            AuthFraud = r.Alpha(1),
            FraudRptDate = r.Alpha(8),
        };
        r.Skip(17); // FILLER X(17)
        return d;
    }

    /// <summary>Encodes the 6-byte ROOT-SEG-KEY (PA-ACCT-ID S9(11) COMP-3) used by PAUDBUNL's OUTFIL2.</summary>
    public static byte[] EncodeRootSegKey(long acctId)
    {
        var key = new byte[RootSegKeyLength];
        PackedDecimalCodec.Encode(acctId, key, 11, 0, signed: true);
        return key;
    }

    /// <summary>Decodes the 6-byte ROOT-SEG-KEY back to the account id (PAUDBLOD's INFIL2 prefix).</summary>
    public static long DecodeRootSegKey(ReadOnlySpan<byte> key)
        => (long)PackedDecimalCodec.Decode(key, 0);

    /// <summary>
    /// COBOL <c>IS NUMERIC</c> class test for a PIC S9(11) COMP-3 field (PA-ACCT-ID / ROOT-SEG-KEY): every
    /// digit nibble is 0-9 and the sign nibble is a valid packed sign. Reproduces the gate PAUDBUNL and
    /// PAUDBLOD place on the account key before writing/loading.
    /// </summary>
    public static bool IsNumericComp3(ReadOnlySpan<byte> packed)
    {
        if (packed.Length == 0) return false;
        for (int i = 0; i < packed.Length; i++)
        {
            int high = (packed[i] >> 4) & 0x0F;
            int low = packed[i] & 0x0F;
            if (high > 9) return false;                      // every high nibble is a digit
            if (i < packed.Length - 1)
            {
                if (low > 9) return false;                   // interior low nibbles are digits
            }
            else
            {
                // Last byte's low nibble is the sign; A-F are valid signs in IBM packed decimal.
                if (low < 0x0A) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Builds the 8-byte PAUT9CTS child sequence key as the zero-padded decimal concatenation of the two
    /// 9s-complement components (date-9C = 5 digits, time-9C = 9 digits). Identical to the construction the
    /// authorization writer uses, so a loaded child orders the same as the IMS twin chain.
    /// </summary>
    public static string BuildAuthKey(int authDate9c, long authTime9c)
    {
        int d = (int)(Math.Abs((long)authDate9c) % 100000L);
        long t = Math.Abs(authTime9c) % 1000000000L;
        return d.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)
             + t.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---- Sequential field writer (group-image builder) -----------------------------------------------
    private sealed class SegWriter(HostKind host)
    {
        private readonly List<byte> _bytes = [];
        private readonly HostKind _host = host;

        public void Zoned(decimal value, int totalDigits, int scale, bool signed)
        {
            var f = new byte[totalDigits];
            ZonedDecimalCodec.Encode(value, f, totalDigits, scale, signed, _host);
            _bytes.AddRange(f);
        }

        public void Zoned(long value, int totalDigits, int scale, bool signed)
            => Zoned((decimal)value, totalDigits, scale, signed);

        public void Comp3(decimal value, int totalDigits, int scale, bool signed)
        {
            var f = new byte[PackedDecimalCodec.ByteLength(totalDigits)];
            PackedDecimalCodec.Encode(value, f, totalDigits, scale, signed);
            _bytes.AddRange(f);
        }

        public void Comp(decimal value, int totalDigits, int scale, bool signed)
        {
            var f = new byte[BinaryCodec.ByteLength(totalDigits)];
            BinaryCodec.Encode(value, f, totalDigits, scale, signed);
            _bytes.AddRange(f);
        }

        public void Alpha(string text, int width)
        {
            text ??= "";
            string padded = text.Length >= width ? text[..width] : text.PadRight(width, ' ');
            _bytes.AddRange(HostEncoding.For(_host).GetBytes(padded));
        }

        public byte[] ToArray(int expectedLength)
        {
            if (_bytes.Count != expectedLength)
                throw new InvalidOperationException(
                    $"Segment image length {_bytes.Count} != expected {expectedLength}.");
            return _bytes.ToArray();
        }
    }

    // ---- Sequential field reader (group-image decoder) -----------------------------------------------
    private ref struct SegReader(ReadOnlySpan<byte> image, HostKind host)
    {
        private readonly ReadOnlySpan<byte> _image = image;
        private readonly HostKind _host = host;
        private int _pos = 0;

        public decimal Zoned(int totalDigits, int scale, bool signed)
        {
            decimal v = ZonedDecimalCodec.Decode(_image.Slice(_pos, totalDigits), scale, signed, _host);
            _pos += totalDigits;
            return v;
        }

        public decimal Comp3(int totalDigits, int scale, bool signed)
        {
            int len = PackedDecimalCodec.ByteLength(totalDigits);
            decimal v = PackedDecimalCodec.Decode(_image.Slice(_pos, len), scale);
            _pos += len;
            return v;
        }

        public decimal Comp(int totalDigits, int scale, bool signed)
        {
            int len = BinaryCodec.ByteLength(totalDigits);
            decimal v = BinaryCodec.Decode(_image.Slice(_pos, len), scale, signed);
            _pos += len;
            return v;
        }

        public string Alpha(int width)
        {
            string s = HostEncoding.For(_host).GetString(_image.Slice(_pos, width));
            _pos += width;
            return s;
        }

        public void Skip(int width) => _pos += width;
    }
}
