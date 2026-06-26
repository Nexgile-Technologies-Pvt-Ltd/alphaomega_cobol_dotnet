using System.Globalization;
using CardDemo.Batch;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Ims;
using CardDemo.Runtime;

namespace CardDemo.Tests;

/// <summary>
/// Coverage + regression-lock suite for the five just-ported programs that previously had ZERO tests
/// (<see cref="Cbstm03a"/>/<see cref="Cbstm03b"/> statement generation; <see cref="Paudbunl"/>,
/// <see cref="Paudblod"/> and <see cref="Dbunldgs"/> IMS pending-auth unload/load utilities) plus locks
/// for the freshly-applied fidelity fixes (EditedNumeric lowercase-picture, CBTRN03C NEXT-SENTENCE
/// loop termination, COPAUS2C/AUTHFRDS targeted fraud-flag update). Each test asserts real, specific
/// values produced by the production code (no vacuous asserts).
/// </summary>
public sealed class RemediationTests
{
    /// <summary>A clock pinned so date-driven DISPLAY output (TODAYS DATE banners) is deterministic.</summary>
    private static readonly IClock FixedClk = new FixedClock(new DateTime(2026, 6, 26, 9, 0, 0));

    /// <summary>An empty (schema-only) in-memory relational DB.</summary>
    private static RelationalDb EmptyDb() => new();

    private static string TempFile(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "carddemo-remediation-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}.dat");
    }

    private static void TryDelete(params string[] paths)
    {
        foreach (string p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort cleanup */ }
    }

    // =================================================================================================
    // 1. CBSTM03A + CBSTM03B — statement generation (plain-text + HTML).
    // =================================================================================================

    /// <summary>
    /// Seeds one account + its customer + an xref card + two transactions on that card, runs CBSTM03A
    /// (which does all I/O through CBSTM03B), and asserts it produces a NON-EMPTY plain-text statement
    /// file and a NON-EMPTY HTML file whose plain-text contents carry the account/customer identifying
    /// data and BOTH transaction amounts.
    /// </summary>
    [Fact]
    public void Cbstm03a_GeneratesPlainTextAndHtmlStatement_WithAccountCustomerAndTxnAmounts()
    {
        using var db = EmptyDb();

        const long acctId = 12345678901L;
        const long custId = 222333444L;
        const string cardNum = "4444333322221111";

        // ACCOUNT (CurrBal printed as ST-CURR-BAL 9(9).99-).
        Assert.Equal(FileStatus.Ok, new AccountRepository(db.Connection).Insert(new Account
        {
            AcctId = acctId, ActiveStatus = "Y", CurrBal = 4275.18m, CreditLimit = 9000.00m,
            CashCreditLimit = 1000.00m, OpenDate = "2020-01-01", ExpirationDate = "2028-01-01",
            ReissueDate = "2024-01-01", CurrCycCredit = 0.00m, CurrCycDebit = 0.00m,
            AddrZip = "90001", GroupId = "GRP0000001",
        }));

        // CUSTOMER (CUST-FIRST/MIDDLE/LAST printed into ST-NAME; FICO into ST-FICO-SCORE).
        Assert.Equal(FileStatus.Ok, new CustomerRepository(db.Connection).Insert(new Customer
        {
            CustId = custId, FirstName = "ALICE", MiddleName = "Q", LastName = "STMTHOLDER",
            AddrLine1 = "100 STATEMENT WAY", AddrLine2 = "FLOOR 3", AddrLine3 = "METROCITY",
            AddrStateCd = "CA", AddrCountryCd = "USA", AddrZip = "90001",
            PhoneNum1 = "5551112222", PhoneNum2 = "5553334444", Ssn = 123456789,
            GovtIssuedId = "DL99887766", DobYyyyMmDd = "1980-01-01", EftAccountId = "EFT0000001",
            PriCardHolderInd = "Y", FicoCreditScore = 765,
        }));

        // CARD_XREF chains the card -> (cust, acct).
        Assert.Equal(FileStatus.Ok, new CardXrefRepository(db.Connection).Insert(new CardXref
        {
            XrefCardNum = cardNum, CustId = custId, AcctId = acctId,
        }));

        // Two TRANSACTIONs on that card (distinct ids, distinct amounts/descriptions).
        var txns = new TransactionRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = "TXNSTMT000000001", TypeCd = "01", CatCd = 1, Source = "POS",
            Desc = "GROCERY STORE PURCHASE", Amt = 123.45m, MerchantId = 1, MerchantName = "GROCERS",
            MerchantCity = "METROCITY", MerchantZip = "90001", CardNum = cardNum,
            OrigTs = "2026-06-20-10.00.00.000000", ProcTs = "2026-06-20-10.00.00.000000",
        }));
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = "TXNSTMT000000002", TypeCd = "01", CatCd = 1, Source = "POS",
            Desc = "ONLINE BOOKSTORE ORDER", Amt = 67.89m, MerchantId = 2, MerchantName = "BOOKS",
            MerchantCity = "METROCITY", MerchantZip = "90001", CardNum = cardNum,
            OrigTs = "2026-06-21-11.30.00.000000", ProcTs = "2026-06-21-11.30.00.000000",
        }));

        string stmtPath = TempFile("cbstm03a.stmt");
        string htmlPath = TempFile("cbstm03a.html");
        try
        {
            IReadOnlyList<string> sysout = Cbstm03a.Run(db, stmtPath, htmlPath);

            // The TIOT-walk DISPLAY banner is always emitted.
            Assert.Contains(sysout, l => l.StartsWith("Running JCL"));

            Assert.True(File.Exists(stmtPath), "plain-text statement file was not created");
            Assert.True(File.Exists(htmlPath), "HTML statement file was not created");

            string stmt = File.ReadAllText(stmtPath);
            string html = File.ReadAllText(htmlPath);

            Assert.True(stmt.Length > 0, "plain-text statement is empty");
            Assert.True(html.Length > 0, "HTML statement is empty");

            // Statement framing banners (ST-LINE0 / ST-LINE15).
            Assert.Contains("START OF STATEMENT", stmt);
            Assert.Contains("END OF STATEMENT", stmt);

            // Identifying data: account id (ZonedDigits 9(11)) and the customer's name parts.
            Assert.Contains("12345678901", stmt);          // ACCT-ID
            Assert.Contains("ALICE", stmt);                 // CUST-FIRST-NAME
            Assert.Contains("STMTHOLDER", stmt);            // CUST-LAST-NAME
            Assert.Contains("765", stmt);                   // CUST-FICO-CREDIT-SCORE

            // Both transactions: id, description (truncated to 49) and Z(9).99- amount.
            Assert.Contains("TXNSTMT000000001", stmt);
            Assert.Contains("TXNSTMT000000002", stmt);
            Assert.Contains("GROCERY STORE PURCHASE", stmt);
            Assert.Contains("ONLINE BOOKSTORE ORDER", stmt);
            Assert.Contains("123.45", stmt);                // first txn amount
            Assert.Contains("67.89", stmt);                 // second txn amount
            // Total EXP line: 123.45 + 67.89 = 191.34.
            Assert.Contains("Total EXP:", stmt);
            Assert.Contains("191.34", stmt);

            // HTML carries the same identifying data and the statement title.
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("Statement for Account Number: ", html);
            Assert.Contains("12345678901", html);
            Assert.Contains("ALICE", html);
            Assert.Contains("123.45", html);
            Assert.Contains("67.89", html);
            Assert.Contains("</html>", html);
        }
        finally
        {
            TryDelete(stmtPath, htmlPath);
        }
    }

    // =================================================================================================
    // 2. PAUDBUNL -> PAUDBLOD round-trip (EBCDIC and ASCII).
    // =================================================================================================

    [Theory]
    [InlineData(HostKind.Ebcdic)]
    [InlineData(HostKind.Ascii)]
    public void Paudbunl_Then_Paudblod_RoundTripsEverySummaryAndDetail(HostKind host)
    {
        // ---- Seed the source DB: 2 summaries, each owning 2 details. ----
        using var src = EmptyDb();
        var srcSummaries = new PautSummaryRepository(src.Connection);
        var srcDetails = new PautDetailRepository(src.Connection);

        long[] accts = { 11111111111L, 22222222222L };
        foreach (long acct in accts)
        {
            InsertSummary(srcSummaries, acct, approvedCnt: 2);
            // Two children per root with distinct 9s-complement date/time components so each AUTH_KEY is
            // unique and equals the canonical (date-9c|time-9c) construction the loader rebuilds.
            InsertCanonicalDetail(srcDetails, acct, authDate9c: 73822, authTime9c: 909999999L, approved: true,
                tranId: "T" + acct.ToString(CultureInfo.InvariantCulture).Substring(0, 6) + "001");
            InsertCanonicalDetail(srcDetails, acct, authDate9c: 73823, authTime9c: 809999999L, approved: false,
                tranId: "T" + acct.ToString(CultureInfo.InvariantCulture).Substring(0, 6) + "002");
        }

        int expectedSummaries = accts.Length;
        int expectedDetails = accts.Length * 2;

        string outfil1 = TempFile("paut.outfil1");
        string outfil2 = TempFile("paut.outfil2");
        try
        {
            // ---- UNLOAD: PAUDBUNL writes OUTFIL1 (summary images) + OUTFIL2 (key+detail records). ----
            PaudbunlResult unl = Paudbunl.Run(srcSummaries, srcDetails, outfil1, outfil2, FixedClk, host);
            Assert.Equal(0, unl.ReturnCode);
            Assert.Contains("STARTING PROGRAM PAUDBUNL::", unl.Sysout);

            // Record-count sanity against the fixed image widths (100 / 206 bytes).
            Assert.Equal(expectedSummaries, new FileInfo(outfil1).Length / 100);
            Assert.Equal(expectedDetails, new FileInfo(outfil2).Length / 206);

            // ---- LOAD: PAUDBLOD reads those same files into a FRESH empty DB. ----
            using var dst = EmptyDb();
            var dstSummaries = new PautSummaryRepository(dst.Connection);
            var dstDetails = new PautDetailRepository(dst.Connection);

            PaudblodResult lod = Paudblod.Run(dstSummaries, dstDetails, outfil1, outfil2, FixedClk, host);
            Assert.Equal(0, lod.ReturnCode);
            Assert.Contains("ROOT INSERT SUCCESS    ", lod.Sysout);
            Assert.Contains("CHILD SEGMENT INSERTED SUCCESS", lod.Sysout);

            // ---- Assert every summary round-trips (count + keys + fields). ----
            List<PautSummary> loadedSummaries = dstSummaries.ReadAll().ToList();
            Assert.Equal(expectedSummaries, loadedSummaries.Count);
            foreach (long acct in accts)
            {
                Assert.Equal(FileStatus.Ok, srcSummaries.ReadByKey(acct, out PautSummary? sOrig));
                Assert.Equal(FileStatus.Ok, dstSummaries.ReadByKey(acct, out PautSummary? sLoad));
                AssertSummaryEquals(sOrig!, sLoad!);
            }

            // ---- Assert every detail round-trips (count + keys + fields), per parent. ----
            int totalLoadedDetails = 0;
            foreach (long acct in accts)
            {
                List<PautDetail> orig = srcDetails.ReadAllByParent(acct).ToList();
                List<PautDetail> load = dstDetails.ReadAllByParent(acct).ToList();
                Assert.Equal(orig.Count, load.Count);
                totalLoadedDetails += load.Count;
                for (int i = 0; i < orig.Count; i++)
                {
                    // Same AUTH_KEY ordering preserved (newest-first twin chain).
                    Assert.Equal(orig[i].AuthKey, load[i].AuthKey);
                    AssertDetailEquals(orig[i], load[i]);
                }
            }
            Assert.Equal(expectedDetails, totalLoadedDetails);
        }
        finally
        {
            TryDelete(outfil1, outfil2);
        }
    }

    // =================================================================================================
    // 3. DBUNLDGS — GSAM unload; summary image byte-identical to PAUDBUNL's OUTFIL1.
    // =================================================================================================

    [Fact]
    public void Dbunldgs_SummaryGsam_IsByteIdenticalToPaudbunlOutfil1_AndDetailsMatch()
    {
        using var src = EmptyDb();
        var summaries = new PautSummaryRepository(src.Connection);
        var details = new PautDetailRepository(src.Connection);

        long[] accts = { 30000000001L, 40000000002L };
        foreach (long acct in accts)
        {
            InsertSummary(summaries, acct, approvedCnt: 3);
            InsertCanonicalDetail(details, acct, authDate9c: 50000, authTime9c: 700000000L, approved: true,
                tranId: "G" + acct.ToString(CultureInfo.InvariantCulture).Substring(0, 6) + "01");
            InsertCanonicalDetail(details, acct, authDate9c: 50001, authTime9c: 600000000L, approved: false,
                tranId: "G" + acct.ToString(CultureInfo.InvariantCulture).Substring(0, 6) + "02");
        }

        string bunlOut1 = TempFile("dbg.bunl.outfil1");
        string bunlOut2 = TempFile("dbg.bunl.outfil2");
        string gsamSum = TempFile("dbg.gsam.summary");
        string gsamDtl = TempFile("dbg.gsam.detail");
        try
        {
            // PAUDBUNL is the reference: OUTFIL1 = 100-byte summary images; OUTFIL2 = 206-byte (key+detail).
            PaudbunlResult unl = Paudbunl.Run(summaries, details, bunlOut1, bunlOut2, FixedClk, HostKind.Ebcdic);
            Assert.Equal(0, unl.ReturnCode);

            // DBUNLDGS writes two GSAM files: summary (100-byte) + detail (200-byte, NO 6-byte key prefix).
            DbunldgsResult gsam = Dbunldgs.Run(summaries, details, gsamSum, gsamDtl, FixedClk, HostKind.Ebcdic);
            Assert.Equal(0, gsam.ReturnCode);
            Assert.Contains("STARTING PROGRAM DBUNLDGS::", gsam.Sysout);

            byte[] outfil1 = File.ReadAllBytes(bunlOut1);
            byte[] outfil2 = File.ReadAllBytes(bunlOut2);
            byte[] gsamSummaryBytes = File.ReadAllBytes(gsamSum);
            byte[] gsamDetailBytes = File.ReadAllBytes(gsamDtl);

            // The summary GSAM is byte-for-byte identical to PAUDBUNL's OUTFIL1 (same 100-byte images).
            Assert.Equal(outfil1.Length, gsamSummaryBytes.Length);
            Assert.True(outfil1.AsSpan().SequenceEqual(gsamSummaryBytes),
                "DBUNLDGS summary GSAM is not byte-identical to PAUDBUNL OUTFIL1");

            // Record-count sanity.
            int expectedSummaries = accts.Length;
            int expectedDetails = accts.Length * 2;
            Assert.Equal(expectedSummaries, gsamSummaryBytes.Length / 100);
            Assert.Equal(expectedDetails, gsamDetailBytes.Length / 200);
            Assert.Equal(expectedDetails, outfil2.Length / 206);

            // The DBUNLDGS detail records (200 bytes each) equal the 200-byte detail portion of each
            // PAUDBUNL OUTFIL2 record (which is a 6-byte ROOT-SEG-KEY prefix + the same 200-byte image).
            for (int i = 0; i < expectedDetails; i++)
            {
                ReadOnlySpan<byte> gsamRec = gsamDetailBytes.AsSpan(i * 200, 200);
                ReadOnlySpan<byte> outfil2Detail = outfil2.AsSpan(i * 206 + 6, 200);
                Assert.True(gsamRec.SequenceEqual(outfil2Detail),
                    $"DBUNLDGS detail record {i} differs from PAUDBUNL OUTFIL2 detail portion");
            }
        }
        finally
        {
            TryDelete(bunlOut1, bunlOut2, gsamSum, gsamDtl);
        }
    }

    // =================================================================================================
    // 4. EditedNumeric — lowercase edit picture behaves exactly like the upper-case one (COPAUS1C fix).
    // =================================================================================================

    [Fact]
    public void EditedNumeric_LowercasePicture_MatchesUppercase_AndSuppressesZs()
    {
        string lower = EditedNumeric.Format(1234.5m, "-zzzzzzz9.99");
        string upper = EditedNumeric.Format(1234.5m, "-ZZZZZZZ9.99");

        // Case-insensitive PICTURE: the lowercase 'z' edit picture zero-suppresses identically.
        Assert.Equal(upper, lower);

        // A non-zero value must leave NO literal z/Z in the formatted output (the suppression chars are
        // either blanked or replaced by digits — never echoed verbatim).
        Assert.DoesNotContain('z', lower);
        Assert.DoesNotContain('Z', lower);

        // Specific value: 1234.5 in -ZZZZZZZ9.99 = ' '(sign) + 4 suppressed spaces + '1234' + '.' + '50'.
        Assert.Equal("     1234.50", lower);
    }

    // =================================================================================================
    // 5. CBTRN03C — NEXT-SENTENCE terminates the loop at an out-of-range record (faithful bug #1).
    // =================================================================================================

    /// <summary>
    /// Three transactions on one card are read in card-sorted, then tran-id, order. The MIDDLE record's
    /// TRAN-PROC-TS falls OUTSIDE the DATEPARM window, so CBTRN03C's inverted date filter executes
    /// <c>NEXT SENTENCE</c> — which (because the whole read loop is one COBOL sentence) branches past the
    /// loop terminator to 9000-TRANFILE-CLOSE. The first record is reported, but the record AFTER the
    /// out-of-range one is NOT, and no page/grand totals are written (the EOF totalling branch is never
    /// reached). This locks the just-applied NEXT-SENTENCE termination semantics.
    /// </summary>
    [Fact]
    public void Cbtrn03c_NextSentence_TerminatesLoop_AtOutOfRangeRecord()
    {
        using var db = EmptyDb();

        const string cardNum = "5500000000000001";
        const long acctId = 55500000001L;

        // CBTRN03C does keyed lookups on CARD_XREF / TRAN_TYPE / TRAN_CATEGORY (INVALID KEY -> abend), so
        // every referenced key must exist.
        Assert.Equal(FileStatus.Ok, new CardXrefRepository(db.Connection).Insert(new CardXref
        {
            XrefCardNum = cardNum, CustId = 9001, AcctId = acctId,
        }));
        Assert.Equal(FileStatus.Ok, new TranTypeRepository(db.Connection).Insert(new TranType
        {
            TranTypeCode = "01", TranTypeDesc = "PURCHASE",
        }));
        Assert.Equal(FileStatus.Ok, new TranCategoryRepository(db.Connection).Insert(new TranCategory
        {
            TranTypeCd = "01", TranCatCd = 1, TranCatTypeDesc = "GENERAL MERCHANDISE",
        }));

        // Read order = OrderBy(CardNum) then (stable) tran_id ASC. All three share the card, so tran-id
        // ordering decides: ...001 (in-range), ...002 (OUT-of-range), ...003 (in-range, after the break).
        var txns = new TransactionRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, txns.Insert(NewReportTxn("RPT0000000000001", cardNum, 100.00m,
            procTs: "2026-06-10-00.00.00.000000")));   // in range
        Assert.Equal(FileStatus.Ok, txns.Insert(NewReportTxn("RPT0000000000002", cardNum, 200.00m,
            procTs: "2026-01-01-00.00.00.000000")));   // BEFORE start -> out of range -> NEXT SENTENCE
        Assert.Equal(FileStatus.Ok, txns.Insert(NewReportTxn("RPT0000000000003", cardNum, 300.00m,
            procTs: "2026-06-20-00.00.00.000000")));   // in range, but loop already terminated

        string reportPath = TempFile("cbtrn03c.report");
        try
        {
            IReadOnlyList<string> sysout = Cbtrn03c.Run(
                db, reportPath, startDate: "2026-06-01", endDate: "2026-06-30");

            Assert.Equal("START OF EXECUTION OF PROGRAM CBTRN03C", sysout[0]);
            Assert.Equal("END OF EXECUTION OF PROGRAM CBTRN03C", sysout[^1]);

            // The DISPLAY trail shows the loop saw the first record and then the out-of-range one and
            // stopped: there is NO DISPLAY for the third transaction (it was never read).
            Assert.Contains(sysout, l => l.Contains("RPT0000000000001"));
            Assert.DoesNotContain(sysout, l => l.Contains("RPT0000000000003"));

            // Read the 133-byte fixed-width report back (CBTRN03C defaults to ASCII host encoding).
            string report = File.ReadAllText(reportPath);

            // The first (in-range) record IS reported.
            Assert.Contains("RPT0000000000001", report);
            // The record AFTER the out-of-range one is NOT reported (loop terminated at record #2).
            Assert.DoesNotContain("RPT0000000000003", report);
            // The out-of-range record itself never reaches the detail writer either.
            Assert.DoesNotContain("RPT0000000000002", report);
            // NEXT SENTENCE branches straight to the close paragraphs, so the EOF totalling branch (which
            // is the ONLY writer of page + grand totals) never runs.
            Assert.DoesNotContain("Grand Total", report);
            Assert.DoesNotContain("Page Total", report);
        }
        finally
        {
            TryDelete(reportPath);
        }
    }

    // =================================================================================================
    // 6. COPAUS2C / AUTHFRDS — targeted fraud update touches ONLY AUTH_FRAUD + FRAUD_RPT_DATE.
    // =================================================================================================

    /// <summary>
    /// Inserts an AUTHFRDS row with distinctive non-key column values, performs the COPAUS2C duplicate-key
    /// fraud-update path via <see cref="AuthFraudRepository.UpdateFraudFlag"/>, and asserts ONLY
    /// <c>AUTH_FRAUD</c> and <c>FRAUD_RPT_DATE</c> changed while every other column kept its original value.
    /// </summary>
    [Fact]
    public void AuthFraud_UpdateFraudFlag_ChangesOnlyFraudColumns()
    {
        using var db = EmptyDb();
        var repo = new AuthFraudRepository(db.Connection);

        const string cardNum = "6011222233334444";
        const string authTs = "2026-06-26-09.00.00.000000";

        var original = new AuthFraud
        {
            CardNum = cardNum, AuthTs = authTs,
            AuthType = "0100", CardExpiryDate = "1228", MessageType = "0100", MessageSource = "POS",
            AuthIdCode = "ABC123", AuthRespCode = "00", AuthRespReason = "0000", ProcessingCode = "000000",
            TransactionAmt = 250.75m, ApprovedAmt = 250.75m, MerchantCatagoryCode = "5411",
            AcqrCountryCode = "840", PosEntryMode = 7, MerchantId = "MERCH0000000001",
            MerchantName = "DISTINCT MERCHANT", MerchantCity = "FRAUDTON", MerchantState = "NY",
            MerchantZip = "100010000", TransactionId = "AUTHTXN00000001", MatchStatus = "M",
            AuthFraudInd = "N", FraudRptDate = "0001-01-01", AcctId = 77700000001L, CustId = 333444555L,
        };
        Assert.Equal(FileStatus.Ok, repo.Insert(original));

        const string newFraudFlag = "Y";
        const string newFraudDate = "2026-06-26";

        // The COPAUS2C FRAUD-UPDATE path: set ONLY AUTH_FRAUD + FRAUD_RPT_DATE by composite key.
        Assert.Equal(FileStatus.Ok, repo.UpdateFraudFlag(cardNum, authTs, newFraudFlag, newFraudDate));

        Assert.Equal(FileStatus.Ok, repo.ReadByKey(cardNum, authTs, out AuthFraud? updated));
        Assert.NotNull(updated);

        // The two fraud columns changed to the new values.
        Assert.Equal(newFraudFlag, updated!.AuthFraudInd.TrimEnd());
        Assert.Equal(newFraudDate, updated.FraudRptDate.TrimEnd());

        // Every OTHER column retains its original value.
        Assert.Equal(original.AuthType, updated.AuthType.TrimEnd());
        Assert.Equal(original.CardExpiryDate, updated.CardExpiryDate.TrimEnd());
        Assert.Equal(original.MessageType, updated.MessageType.TrimEnd());
        Assert.Equal(original.MessageSource, updated.MessageSource.TrimEnd());
        Assert.Equal(original.AuthIdCode, updated.AuthIdCode.TrimEnd());
        Assert.Equal(original.AuthRespCode, updated.AuthRespCode.TrimEnd());
        Assert.Equal(original.AuthRespReason, updated.AuthRespReason.TrimEnd());
        Assert.Equal(original.ProcessingCode, updated.ProcessingCode.TrimEnd());
        Assert.Equal(original.TransactionAmt, updated.TransactionAmt);
        Assert.Equal(original.ApprovedAmt, updated.ApprovedAmt);
        Assert.Equal(original.MerchantCatagoryCode, updated.MerchantCatagoryCode.TrimEnd());
        Assert.Equal(original.AcqrCountryCode, updated.AcqrCountryCode.TrimEnd());
        Assert.Equal(original.PosEntryMode, updated.PosEntryMode);
        Assert.Equal(original.MerchantId, updated.MerchantId.TrimEnd());
        Assert.Equal(original.MerchantName, updated.MerchantName.TrimEnd());
        Assert.Equal(original.MerchantCity, updated.MerchantCity.TrimEnd());
        Assert.Equal(original.MerchantState, updated.MerchantState.TrimEnd());
        Assert.Equal(original.MerchantZip, updated.MerchantZip.TrimEnd());
        Assert.Equal(original.TransactionId, updated.TransactionId.TrimEnd());
        Assert.Equal(original.MatchStatus, updated.MatchStatus.TrimEnd());
        Assert.Equal(original.AcctId, updated.AcctId);
        Assert.Equal(original.CustId, updated.CustId);
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    private static Transaction NewReportTxn(string tranId, string cardNum, decimal amt, string procTs) => new()
    {
        TranId = tranId, TypeCd = "01", CatCd = 1, Source = "POS", Desc = "REPORTED TXN " + tranId,
        Amt = amt, MerchantId = 1, MerchantName = "RPT MERCHANT", MerchantCity = "RPTCITY",
        MerchantZip = "00000", CardNum = cardNum, OrigTs = procTs, ProcTs = procTs,
    };

    private static void InsertSummary(PautSummaryRepository repo, long acctId, int approvedCnt)
    {
        Assert.Equal(FileStatus.Ok, repo.Insert(new PautSummary
        {
            AcctId = acctId,
            CustId = acctId % 1000000000,
            AuthStatus = "A",
            AccountStatus1 = "Y",
            AccountStatus2 = "Y",
            AccountStatus3 = "N",
            AccountStatus4 = "N",
            AccountStatus5 = "N",
            CreditLimit = 9000.00m,
            CashLimit = 1000.00m,
            CreditBalance = 100.00m,
            CashBalance = 0.00m,
            ApprovedAuthCnt = approvedCnt,
            DeclinedAuthCnt = 1,
            ApprovedAuthAmt = approvedCnt * 25.00m,
            DeclinedAuthAmt = 10.00m,
        }));
    }

    /// <summary>
    /// Inserts a PAUT_DETAIL whose AUTH_KEY equals the canonical (date-9c|time-9c) string the IMS loader
    /// rebuilds on decode — i.e. the 5-digit AUTH_DATE_9C followed by the 9-digit AUTH_TIME_9C — so the
    /// composite key round-trips through an unload+load byte-for-byte. (Mirrors PautSegmentImages.BuildAuthKey,
    /// which is internal to CardDemo.Ims and therefore reconstructed here.)
    /// </summary>
    private static void InsertCanonicalDetail(
        PautDetailRepository repo, long acctId, int authDate9c, long authTime9c, bool approved, string tranId)
    {
        string authKey =
            (Math.Abs((long)authDate9c) % 100000L).ToString("D5", CultureInfo.InvariantCulture) +
            (Math.Abs(authTime9c) % 1000000000L).ToString("D9", CultureInfo.InvariantCulture);

        Assert.Equal(FileStatus.Ok, repo.Insert(new PautDetail
        {
            AcctId = acctId,
            AuthKey = authKey,
            AuthDate9c = authDate9c,
            AuthTime9c = authTime9c,
            AuthOrigDate = "260626",
            AuthOrigTime = "090000",
            CardNum = acctId.ToString("D16", CultureInfo.InvariantCulture),
            AuthType = "0100",
            CardExpiryDate = "1228",
            MessageType = "0100",
            MessageSource = "POS",
            AuthIdCode = "AUT001",
            AuthRespCode = approved ? "00" : "05",
            AuthRespReason = approved ? "0000" : "4100",
            ProcessingCode = 123456,
            TransactionAmt = 25.00m,
            ApprovedAmt = approved ? 25.00m : 0.00m,
            MerchantCatagoryCode = "5411",
            AcqrCountryCode = "840",
            PosEntryMode = 1,
            MerchantId = "MERCH0000000001",
            MerchantName = "ROUND TRIP MERCHANT",
            MerchantCity = "TRIPTOWN",
            MerchantState = "CA",
            MerchantZip = "900010000",
            TransactionId = tranId,
            MatchStatus = "P",
            AuthFraud = " ",
            FraudRptDate = " ",
        }));
    }

    private static void AssertSummaryEquals(PautSummary a, PautSummary b)
    {
        Assert.Equal(a.AcctId, b.AcctId);
        Assert.Equal(a.CustId, b.CustId);
        Assert.Equal(a.AuthStatus.TrimEnd(), b.AuthStatus.TrimEnd());
        Assert.Equal(a.AccountStatus1.TrimEnd(), b.AccountStatus1.TrimEnd());
        Assert.Equal(a.AccountStatus2.TrimEnd(), b.AccountStatus2.TrimEnd());
        Assert.Equal(a.AccountStatus3.TrimEnd(), b.AccountStatus3.TrimEnd());
        Assert.Equal(a.AccountStatus4.TrimEnd(), b.AccountStatus4.TrimEnd());
        Assert.Equal(a.AccountStatus5.TrimEnd(), b.AccountStatus5.TrimEnd());
        Assert.Equal(a.CreditLimit, b.CreditLimit);
        Assert.Equal(a.CashLimit, b.CashLimit);
        Assert.Equal(a.CreditBalance, b.CreditBalance);
        Assert.Equal(a.CashBalance, b.CashBalance);
        Assert.Equal(a.ApprovedAuthCnt, b.ApprovedAuthCnt);
        Assert.Equal(a.DeclinedAuthCnt, b.DeclinedAuthCnt);
        Assert.Equal(a.ApprovedAuthAmt, b.ApprovedAuthAmt);
        Assert.Equal(a.DeclinedAuthAmt, b.DeclinedAuthAmt);
    }

    private static void AssertDetailEquals(PautDetail a, PautDetail b)
    {
        Assert.Equal(a.AcctId, b.AcctId);
        Assert.Equal(a.AuthKey, b.AuthKey);
        Assert.Equal(a.AuthDate9c, b.AuthDate9c);
        Assert.Equal(a.AuthTime9c, b.AuthTime9c);
        Assert.Equal(a.AuthOrigDate.TrimEnd(), b.AuthOrigDate.TrimEnd());
        Assert.Equal(a.AuthOrigTime.TrimEnd(), b.AuthOrigTime.TrimEnd());
        Assert.Equal(a.CardNum.TrimEnd(), b.CardNum.TrimEnd());
        Assert.Equal(a.AuthType.TrimEnd(), b.AuthType.TrimEnd());
        Assert.Equal(a.CardExpiryDate.TrimEnd(), b.CardExpiryDate.TrimEnd());
        Assert.Equal(a.MessageType.TrimEnd(), b.MessageType.TrimEnd());
        Assert.Equal(a.MessageSource.TrimEnd(), b.MessageSource.TrimEnd());
        Assert.Equal(a.AuthIdCode.TrimEnd(), b.AuthIdCode.TrimEnd());
        Assert.Equal(a.AuthRespCode.TrimEnd(), b.AuthRespCode.TrimEnd());
        Assert.Equal(a.AuthRespReason.TrimEnd(), b.AuthRespReason.TrimEnd());
        Assert.Equal(a.ProcessingCode, b.ProcessingCode);
        Assert.Equal(a.TransactionAmt, b.TransactionAmt);
        Assert.Equal(a.ApprovedAmt, b.ApprovedAmt);
        Assert.Equal(a.MerchantCatagoryCode.TrimEnd(), b.MerchantCatagoryCode.TrimEnd());
        Assert.Equal(a.AcqrCountryCode.TrimEnd(), b.AcqrCountryCode.TrimEnd());
        Assert.Equal(a.PosEntryMode, b.PosEntryMode);
        Assert.Equal(a.MerchantId.TrimEnd(), b.MerchantId.TrimEnd());
        Assert.Equal(a.MerchantName.TrimEnd(), b.MerchantName.TrimEnd());
        Assert.Equal(a.MerchantCity.TrimEnd(), b.MerchantCity.TrimEnd());
        Assert.Equal(a.MerchantState.TrimEnd(), b.MerchantState.TrimEnd());
        Assert.Equal(a.MerchantZip.TrimEnd(), b.MerchantZip.TrimEnd());
        Assert.Equal(a.TransactionId.TrimEnd(), b.TransactionId.TrimEnd());
        Assert.Equal(a.MatchStatus.TrimEnd(), b.MatchStatus.TrimEnd());
    }
}
