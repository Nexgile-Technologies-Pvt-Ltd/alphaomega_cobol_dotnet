using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    /// <summary>
    /// COTRN01C (CT01) transaction-detail view, happy path: a reenter ENTER turn keys a (seeded) transaction
    /// id into TRNIDIN, RECEIVEs it, READs the TRANSACT master by primary key (PROCESS-ENTER-KEY ->
    /// READ-TRANSACT-FILE, RESP NORMAL), and paints every detail field from the master record before
    /// RETURN TRANSID('CT01'). The transaction is NOT a seeded master (transactions arrive via posting), so the
    /// test inserts its own row and asserts the painted fields equal the master values — exercising the core
    /// read+paint logic, including the WS-TRAN-AMT PIC +99999999.99 edit and the zoned category code.
    /// source: COTRN01C.cbl:111-114,144-192,267-296.
    /// </summary>
    [Fact]
    public void TransactionViewProgram_enter_known_transaction_id_paints_detail_from_master()
    {
        using var db = SeededDb();

        // Seed a transaction to view (TRANSACT is not part of the EBCDIC master seed).
        const string tranId = "0000000000000099";
        var txns = new TransactionRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = tranId,
            TypeCd = "01",
            CatCd = 5,
            Source = "POS",
            Desc = "COFFEE AND PASTRY",
            Amt = 12.34m,                       // edited to +00000012.34 (fits TRNAMT L12 exactly)
            MerchantId = 42,
            MerchantName = "ACME CAFE",
            MerchantCity = "ATLANTA",
            MerchantZip = "30301",
            CardNum = "4111111111111111",
            OrigTs = "2026-06-20-09.00.00.000000",
            ProcTs = "2026-06-20-09.30.00.000000",
        }));

        // Confirm the seeded master values we will assert against (read back via the repo).
        Assert.Equal(FileStatus.Ok, txns.ReadByKey(tranId, out Transaction? master));
        Assert.NotNull(master);

        // A reenter COMMAREA (CDEMO-PGM-REENTER) so the handler takes RECEIVE -> EVALUATE EIBAID -> ENTER ->
        // PROCESS-ENTER-KEY (the read+paint path). COTRN01C RECEIVEs first, then EVALUATEs EIBAID, so the AID
        // that drives the ENTER path is the one RECEIVE returns; script ENTER on the RECEIVE and pass it to RunTurn.
        var ca = new CardDemoCommArea { FromTranId = "CT01", FromProgram = "COTRN00C" };
        ca.SetReenter();
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map => map.Field("TRNIDIN").SetValue(tranId),
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CT01", AidKey.Enter, ca);

        // Pseudo-conversational re-drive: RETURN TRANSID('CT01'); no XCTL on the happy path.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CT01", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("COTRN1A", screen.LastSentMap!.Name);

        // No error message (the read resolved NORMAL).
        Assert.Equal("", screen.LastSentMap.Field("ERRMSG").Value.TrimEnd());

        // ---- Detail fields painted straight from the TRANSACT master (PROCESS-ENTER-KEY MOVEs). ----
        Assert.Equal(tranId, screen.LastSentMap.Field("TRNID").Value.TrimEnd());           // MOVE TRAN-ID -> TRNIDI. cbl:178
        Assert.Equal(master!.CardNum, screen.LastSentMap.Field("CARDNUM").Value.TrimEnd()); // TRAN-CARD-NUM. cbl:179
        Assert.Equal("01", screen.LastSentMap.Field("TTYPCD").Value.TrimEnd());            // TRAN-TYPE-CD X(2). cbl:180
        Assert.Equal("0005", screen.LastSentMap.Field("TCATCD").Value.TrimEnd());          // TRAN-CAT-CD zoned 9(4). cbl:181
        Assert.Equal("POS", screen.LastSentMap.Field("TRNSRC").Value.TrimEnd());           // TRAN-SOURCE. cbl:182
        Assert.Equal("COFFEE AND PASTRY", screen.LastSentMap.Field("TDESC").Value.TrimEnd()); // TRAN-DESC. cbl:184

        // WS-TRAN-AMT PIC +99999999.99 edit of 12.34 -> exactly "+00000012.34". cbl:49,177,183
        Assert.Equal("+00000012.34", screen.LastSentMap.Field("TRNAMT").Value.TrimEnd());

        // Merchant fields: MID is zoned 9(9); name/city/zip are X. cbl:187-190
        Assert.Equal("000000042", screen.LastSentMap.Field("MID").Value.TrimEnd());
        Assert.Equal("ACME CAFE", screen.LastSentMap.Field("MNAME").Value.TrimEnd());
        Assert.Equal("ATLANTA", screen.LastSentMap.Field("MCITY").Value.TrimEnd());
        Assert.Equal("30301", screen.LastSentMap.Field("MZIP").Value.TrimEnd());

        // The echoed search field still carries the id the operator keyed.
        Assert.Equal(tranId, screen.LastSentMap.Field("TRNIDIN").Value.TrimEnd());
    }
}
