using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    // ===============================================================================================
    //  COACTUPC (TRANSID CAUP) — account update: load a known account, ENTER to display its details
    // ===============================================================================================

    /// <summary>
    /// Happy-path "fetch for update" turn through COACTUPC (CAUP). A reenter COMMAREA carrying
    /// ACUP-DETAILS-NOT-FETCHED reaches 0000-MAIN's WHEN OTHER branch, which performs 1000-PROCESS-INPUTS
    /// (edits the account key) → 2000-DECIDE-ACTION (NotFetched + valid key → 9000-READ-ACCT, which reads
    /// CARD_XREF → ACCOUNT → CUSTOMER and, on FOUND-CUST-IN-MASTER, flips to ACUP-SHOW-DETAILS) →
    /// 3000-SEND-MAP, whose 3202-SHOW-ORIGINAL-VALUES paints the master ACCOUNT + CUSTOMER fields and sets
    /// PROMPT-FOR-CHANGES. This locks the core read path: the screen must echo the keyed account id and
    /// paint the master's active status, customer id, first/last name, and the exact INFOMSG literal — all
    /// of which would diverge if 9000-READ-ACCT / 3202 were broken. source: COACTUPC.cbl:996-1003,
    /// 2562-2591, 3608-3644, 2787-2864; INFOMSG literal :470-471.
    /// </summary>
    [Fact]
    public void AccountUpdateProgram_enter_on_known_account_paints_master_details_for_update()
    {
        using var db = SeededDb();

        // Pick an account that fully resolves CARD_XREF (by acct) → ACCOUNT → CUSTOMER, so 9000-READ-ACCT
        // reaches FOUND-CUST-IN-MASTER and 2000-DECIDE-ACTION flips to ACUP-SHOW-DETAILS (not the
        // search-key prompt). COACTUPC additionally reads CUSTDAT, so we resolve the customer up front.
        var xref = new CardXrefRepository(db.Connection);
        var acctRepo = new AccountRepository(db.Connection);
        var custRepo = new CustomerRepository(db.Connection);

        long acctId = 0;
        Account? expectedAcct = null;
        Customer? expectedCust = null;
        xref.StartBrowse();
        while (xref.ReadNext(out CardXref? x) == FileStatus.Ok && x is not null)
        {
            if (x.AcctId <= 0) continue;
            if (xref.ReadByAltKey(x.AcctId, out CardXref? byAcct) != FileStatus.Ok || byAcct is null) continue;
            if (acctRepo.ReadByKey(x.AcctId, out Account? a) != FileStatus.Ok || a is null) continue;
            if (custRepo.ReadByKey(byAcct.CustId, out Customer? c) != FileStatus.Ok || c is null) continue;
            acctId = x.AcctId; expectedAcct = a; expectedCust = c; break;
        }
        xref.EndBrowse();
        Assert.True(acctId > 0, "No account fully resolvable through CARD_XREF + ACCOUNT + CUSTOMER in the seed.");
        Assert.NotNull(expectedAcct);
        Assert.NotNull(expectedCust);

        // A reenter COMMAREA (CDEMO-PGM-REENTER) with ACUP-DETAILS-NOT-FETCHED (the default for a freshly
        // loaded ProgState) drives the WHEN-OTHER process path on this single ENTER turn.
        var ca = new CardDemoCommArea { FromTranId = "CAUP", FromProgram = "COMEN01C" };
        ca.SetReenter();
        string acctIdField = acctId.ToString("D11");
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map => map.Field("ACCTSID").SetValue(acctIdField),
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CAUP", AidKey.Enter, ca);

        // Pseudo-conversational RETURN TRANSID(CAUP) — stays on the account-update screen. :1015-1019
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CAUP", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("CACTUPA", screen.LastSentMap!.Name);

        // The keyed account id is echoed back into ACCTSID (3200-SETUP-SCREEN-VARS). :2707
        Assert.Equal(acctIdField, screen.LastSentMap.Field("ACCTSID").Value.TrimEnd());

        // 3202-SHOW-ORIGINAL-VALUES painted the ACCOUNT master's status + the CUSTOMER master's id/name.
        // These are read straight from the seeded masters via 9000-READ-ACCT → 9500-STORE-FETCHED-DATA,
        // so they FAIL if the read/paint chain were broken. :2795, :2828, :2836, :2838
        Assert.Equal(expectedAcct!.ActiveStatus.TrimEnd(), screen.LastSentMap.Field("ACSTTUS").Value.TrimEnd());
        Assert.Equal(expectedCust!.CustId.ToString("D9"), screen.LastSentMap.Field("ACSTNUM").Value.TrimEnd());
        Assert.Equal(expectedCust.FirstName.TrimEnd(), screen.LastSentMap.Field("ACSFNAM").Value.TrimEnd());
        Assert.Equal(expectedCust.LastName.TrimEnd(), screen.LastSentMap.Field("ACSLNAM").Value.TrimEnd());

        // 3250-SETUP-INFOMSG prints the exact PROMPT-FOR-CHANGES literal on the show-details path. :470-471, 2963
        Assert.Equal("Update account details presented above.", screen.LastSentMap.Field("INFOMSG").Value.TrimEnd());
        // No error message on the happy path (WS-RETURN-MSG-OFF). :2981
        Assert.Equal("", screen.LastSentMap.Field("ERRMSG").Value.TrimEnd());
    }
}
