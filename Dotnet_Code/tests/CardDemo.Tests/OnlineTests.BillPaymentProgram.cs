using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

/// <summary>
/// Behavioural screen-flow test for the online CICS handler <c>COBIL00C</c> (TRANSID <c>CB00</c>, BMS map
/// <c>COBIL0A</c>) — the "Bill Payment" program. See <see cref="OnlineTests"/> for the shared harness.
/// </summary>
public sealed partial class OnlineTests
{
    [Fact]
    public void BillPaymentProgram_confirm_y_pays_balance_in_full_writes_transaction_and_zeroes_account()
    {
        // COBIL00C bill-pay happy path (PROCESS-ENTER-KEY with CONFIRMI='Y' -> CONF-PAY-YES):
        //   READ-CXACAIX-FILE (card num) -> STARTBR/READPREV/ENDBR TRANSACT (highest tran id) ->
        //   build + WRITE-TRANSACT-FILE one bill-pay TRANSACTION for the FULL current balance ->
        //   COMPUTE ACCT-CURR-BAL = ACCT-CURR-BAL - TRAN-AMT -> UPDATE-ACCTDAT-FILE (REWRITE).
        // source: COBIL00C.cbl:208-244. We assert all three observable effects: the painted success message,
        // the brand-new TRANSACTION row, and the account balance debited to zero in the master.
        using var db = SeededDb();

        // Pick an account that resolves through CARD_XREF (so READ-CXACAIX-FILE finds a card) AND has an
        // ACCOUNT row (so READ-ACCTDAT-FILE + the REWRITE succeed).
        long acctId = FirstResolvableAccount(db);
        var acctRepo = new AccountRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, acctRepo.ReadByKey(acctId, out Account? acct));
        Assert.NotNull(acct);

        // Seed a known positive balance on THIS in-memory account so the pay path is taken (not the
        // "You have nothing to pay..." floor at cbl:198) and the post-pay balance is deterministic.
        // This mutation is confined to this test's fresh SeededDb() instance.
        const decimal startBal = 123.45m;
        acct!.CurrBal = startBal;
        Assert.Equal(FileStatus.Ok, acctRepo.Update(acct));

        // No transactions are seeded, so READPREV hits ENDFILE -> TRAN-ID := ZEROS -> +1 -> "0000000000000001".
        var txns = new TransactionRepository(db.Connection);
        const string expectedTranId = "0000000000000001";
        Assert.Equal(FileStatus.RecordNotFound, txns.ReadByKey(expectedTranId, out _));

        // A reenter COMMAREA so MAIN-PARA takes the RECEIVE + EVALUATE-EIBAID path (cbl:123-127), and ENTER
        // with the account id keyed into ACTIDIN and CONFIRM='Y' to drive CONF-PAY-YES.
        var ca = new CardDemoCommArea { FromTranId = "CB00", FromProgram = "COMEN01C" };
        ca.SetReenter();
        string acctIdField = acctId.ToString("D11");
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("ACTIDIN").SetValue(acctIdField);
                map.Field("CONFIRM").SetValue("Y");
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CB00", AidKey.Enter, ca);

        // The turn ends with the single RETURN TRANSID('CB00'). source: cbl:146-149.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CB00", outcome.TransId);

        // (1) The painted success message — exact literal (note the TWO spaces from the trailing+leading space
        // in the STRING, and the 16-digit tran id). source: cbl:527-531.
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal(
            "Payment successful.  Your Transaction ID is " + expectedTranId + ".",
            screen.LastSentMap!.Field("ERRMSG").Value.TrimEnd());

        // (2) A brand-new TRANSACTION row was WRITTEN for the FULL pre-pay balance with the bill-pay
        // attributes. source: cbl:218-233.
        Assert.Equal(FileStatus.Ok, txns.ReadByKey(expectedTranId, out Transaction? paid));
        Assert.NotNull(paid);
        Assert.Equal(startBal, paid!.Amt);                       // TRAN-AMT = old ACCT-CURR-BAL (full balance)
        Assert.Equal("02", paid.TypeCd.TrimEnd());               // MOVE '02' TO TRAN-TYPE-CD
        Assert.Equal(2, paid.CatCd);                             // MOVE 2 TO TRAN-CAT-CD
        Assert.Equal("POS TERM", paid.Source.TrimEnd());         // MOVE 'POS TERM' TO TRAN-SOURCE
        Assert.Equal("BILL PAYMENT - ONLINE", paid.Desc.TrimEnd()); // MOVE 'BILL PAYMENT - ONLINE' TO TRAN-DESC
        Assert.Equal(999999999, paid.MerchantId);                // MOVE 999999999 TO TRAN-MERCHANT-ID
        Assert.Equal("BILL PAYMENT", paid.MerchantName.TrimEnd()); // MOVE 'BILL PAYMENT' TO TRAN-MERCHANT-NAME

        // (3) The account master balance was debited by the amount paid -> zero. source: cbl:234-235.
        Assert.Equal(FileStatus.Ok, acctRepo.ReadByKey(acctId, out Account? after));
        Assert.NotNull(after);
        Assert.Equal(0m, after!.CurrBal);
    }
}
