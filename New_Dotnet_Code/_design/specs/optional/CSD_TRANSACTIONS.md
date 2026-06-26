# CICS CSD Transaction / Program Routing Spec (CardDemo)

Authoritative extraction of the CICS **CSD** (CICS System Definition) `DEFINE` statements that
drive CardDemo's online (3270 / pseudo-conversational) dispatcher. This spec is the source of
truth for the **online dispatcher's transaction routing table**: given a 4-character CICS
`TRANSID`, which COBOL program is invoked, and under what task attributes (`TASKDATALOC`,
`PROFILE`, etc.).

> This is a **design spec only** — no C# is defined here. The .NET online runtime/dispatcher
> consumes this table to map an inbound transaction id to the ported program entry point.

## Scope / sources

| Source CSD file | CSD GROUP | Module / variant | Status |
| --- | --- | --- | --- |
| `app/csd/CARDDEMO.CSD` | `CARDDEMO` | **Base** online application (VSAM core) | Required |
| `app/app-authorization-ims-db2-mq/csd/CRDDEMO2.csd` | `CARDDEMO` | Pending-authorization (IMS + DB2 + MQ) add-on | Optional |
| `app/app-transaction-type-db2/csd/CRDDEMOD.csd` | `CARDDEMO` | Transaction-type maintenance (DB2) add-on | Optional |
| `app/app-vsam-mq/csd/CRDDEMOM.csd` | `CARDDEMO` | VSAM + MQ add-on (account/date utility txns) | Optional |

All four decks `DEFINE ... GROUP(CARDDEMO)`; at install they are appended into the same CSD
group, so the union below is the full transaction-routing table presented to CICS.

### CSD resource-type inventory (what each deck defines)

| Resource type | Meaning | Base | CRDDEMO2 | CRDDEMOD | CRDDEMOM |
| --- | --- | --- | --- | --- | --- |
| `TRANSACTION` | TRANSID → PROGRAM routing entry (drives the dispatcher) | 18 | 3 | 2 | 2 |
| `PROGRAM` | Loadable program (the XCTL/LINK target) | 19 | 4 | 2 | 2 |
| `MAPSET` | BMS 3270 screen map group | 18 | 2 | 2 | 0 |
| `FILE` | VSAM file (KSDS/AIX path) — see VSAM spec, not routing | 8 | 0 | 0 | 0 |
| `LIBRARY` | DFHRPL load library (LOADLIB) | 2 | 0 | 0 | 1 |
| `TDQUEUE` | Transient-data queue (`JOBS` → internal reader) | 1 | 0 | 0 | 0 |
| `DB2ENTRY` / `DB2TRAN` | DB2 plan binding for a transaction | 0 | 1 / 1 | 1 / 2 | 0 |

`FILE`, `LIBRARY`, `TDQUEUE`, `DB2ENTRY`, and `DB2TRAN` definitions are listed at the end for
completeness but are **not** part of the TRANSID→PROGRAM dispatch table.

---

## How CSD routing drives the online dispatcher

CardDemo is **pseudo-conversational**. The dispatch chain is:

1. The terminal operator (or a prior task) presents a 4-char **TRANSID**. CICS looks it up in the
   `DEFINE TRANSACTION` table and attaches a task running the named **PROGRAM** with the task
   attributes from that definition (`TASKDATALOC`, `TASKDATAKEY`, `PROFILE`, `TRANCLASS`, …).
2. The program does `EXEC CICS RETURN TRANSID(xxxx) COMMAREA(...)` to set the **next**
   transaction for the same terminal (pseudo-conversational re-entry) — so the TRANSID in the
   CSD is also the program's own re-entry id.
3. Program-to-program transfers inside a task use `EXEC CICS XCTL PROGRAM(yyyy)` (transfer, no
   return) or `LINK` (call/return). The menu programs (`COMEN01C`, `COADM01C`) `XCTL` to the
   selected option's program; the target program then `RETURN TRANSID`s with **its own** TRANSID.

Because of step 2/3, **every dispatchable program needs both** a `DEFINE PROGRAM` (so it can be
loaded / XCTL'd by name) **and** a `DEFINE TRANSACTION` (so its own `RETURN TRANSID` re-entry id
resolves). The `TRANSID(...)` attribute that also appears on some `DEFINE PROGRAM` statements is
the program's *preferred* installation transaction; the authoritative routing is the
`DEFINE TRANSACTION` table.

---

## Master routing table — TRANSID → PROGRAM (all decks)

Every `DEFINE TRANSACTION` across the four CSDs. Sorted by TRANSID. Common attributes shared by
**all** CardDemo transactions are factored out below the table to avoid repetition.

| TRANSID | PROGRAM | Source CSD | Screen / function | MAPSET(s) | Notes |
| --- | --- | --- | --- | --- | --- |
| `CAUP` | `COACTUPC` | base | Account Update | `COACTUP` | Menu opt 2; `RETURN TRANSID(CAUP)` |
| `CAVW` | `COACTVWC` | base | Account View | `COACTVW` | Menu opt 1; program also declares `TRANSID(CAVW)` |
| `CA00` | `COADM01C` | base | Admin Menu | `COADM01` | Admin landing menu (XCTLs to admin options) |
| `CB00` | `COBIL00C` | base | Bill Payment | `COBIL00` | Menu opt 10 |
| `CCDL` | `COCRDSLC` | base | Credit Card View (detail) | `COCRDSL` | Menu opt 4; program declares `TRANSID(CCDL)` |
| `CCLI` | `COCRDLIC` | base | Credit Card List | `COCRDLI` | Menu opt 3; **program declares `TRANSID(CC00)`** (mismatch — see Discrepancies) |
| `CCUP` | `COCRDUPC` | base | Credit Card Update | `COCRDUP` | Menu opt 5 |
| `CC00` | `COSGN00C` | base | Sign-on (login) | `COSGN00` | **Entry point / initial transaction**; program declares `TRANSID(CC00)` |
| `CDV1` | `COCRDSEC` | base | Developer transaction 1 (card search) | `COCRDSL` | Dev/test only; `COCRDSEC` = card-search helper |
| `CM00` | `COMEN01C` | base | Main Menu | `COMEN01` | Main menu (XCTLs to user options) |
| `CR00` | `CORPT00C` | base | Transaction Reports | `CORPT00` | Menu opt 9; submits batch report job via TDQ `JOBS` |
| `CT00` | `COTRN00C` | base | Transaction List | `COTRN00` | Menu opt 6 |
| `CT01` | `COTRN01C` | base | Transaction View | `COTRN01` | Menu opt 7 |
| `CT02` | `COTRN02C` | base | Transaction Add | `COTRN02` | Menu opt 8 |
| `CU00` | `COUSR00C` | base | User List (security) | `COUSR00` | Admin opt 1 |
| `CU01` | `COUSR01C` | base | User Add (security) | `COUSR01` | Admin opt 2 |
| `CU02` | `COUSR02C` | base | User Update (security) | `COUSR02` | Admin opt 3 |
| `CU03` | `COUSR03C` | base | User Delete (security) | `COUSR03` | Admin opt 4 |
| `CTLI` | `COTRTLIC` | CRDDEMOD | Transaction Type List/Update (DB2) | `COTRTLI` | Admin opt 5; bound to DB2 plan `CARDDEMO` via `CTLITRAN` |
| `CTTU` | `COTRTUPC` | CRDDEMOD | Transaction Type Maintenance (DB2) | `COTRTUP` | Admin opt 6; bound to DB2 plan `CARDDEMO` via `CTTUTRAN` |
| `CP00` | `COPAUA0C` | CRDDEMO2 | Pending-Auth summary (auth driver) | `COPAU00` | Auth add-on; `COPAUA0C` declares `TRANSID(CP00)` |
| `CPVS` | `COPAUS0C` | CRDDEMO2 | Pending-Auth View — summary | `COPAU00` | Menu opt 11; `COPAUS0C` declares `TRANSID(CPVS)` |
| `CPVD` | `COPAUS1C` | CRDDEMO2 | Pending-Auth View — detail | `COPAU01` | TRANSACTION→`COPAUS1C`; bound to DB2 plan `AWS01PLN` via `CPVDTRAN` (see Discrepancies re `COPAUS2C`) |
| `CDRA` | `COACCT01` | CRDDEMOM | List cards (VSAM+MQ utility) | *(none)* | `COACCT01` declares `TRANSID(CDRA)`; no MAPSET in deck |
| `CDRD` | `CODATE01` | CRDDEMOM | Date utility (VSAM+MQ) | *(none)* | `CODATE01` declares `TRANSID(CDRD)`; no MAPSET in deck |

### Common transaction attributes (identical across all rows above)

Every `DEFINE TRANSACTION` in all four decks carries this identical attribute block, so it is
factored out of the table:

```
PROFILE(DFHCICST)   STATUS(ENABLED)   TWASIZE(0)
TASKDATALOC(ANY)    TASKDATAKEY(USER) STORAGECLEAR(NO)
RUNAWAY(SYSTEM)     SHUTDOWN(DISABLED) ISOLATE(YES) DYNAMIC(NO)
ROUTABLE(NO)        PRIORITY(1)       TRANCLASS(DFHTCL00) DTIMOUT(NO)
RESTART(NO)         SPURGE(YES)       TPURGE(YES) DUMP(YES) TRACE(YES)
CONFDATA(NO)        OTSTIMEOUT(NO)    ACTION(BACKOUT) WAIT(YES)
WAITTIME(0,0,0)     RESSEC(NO)        CMDSEC(NO)
```

Routing-relevant meanings for the .NET dispatcher:

- **`TASKDATALOC(ANY)`** — task storage may live above the 16 MB line (24/31-bit agnostic). No
  effect on managed port other than confirming no below-the-line constraint.
- **`TASKDATAKEY(USER)`** / **`EXECKEY(USER)`** (on the program) — runs in CICS user key, not
  CICS key. No managed analogue; informational.
- **`PROFILE(DFHCICST)`** — the CICS-supplied default terminal/transaction profile (screen size,
  inbound/outbound conversion). All CardDemo txns use the default profile; nothing custom.
- **`TRANCLASS(DFHTCL00)`** — the default (effectively unlimited) transaction class; no MAXACTIVE
  throttling to model.
- **`ISOLATE(YES)`** — transaction isolation (storage protection) on; informational only.
- **`ACTION(BACKOUT)` / `SPURGE(YES)` / `TPURGE(YES)`** — on abend or purge, back out the UOW.
  The .NET port maps a UOW to a transaction/`SaveChanges` scope; abend ⇒ rollback.

None of these vary per transaction, so the dispatcher only needs the **TRANSID → PROGRAM** map
plus the (constant) profile/datakey defaults.

---

## Program definitions (`DEFINE PROGRAM`)

All `DEFINE PROGRAM` resources, with the program's *self-declared* `TRANSID` attribute (the
preferred install transaction) where present. The authoritative dispatch is still the
TRANSACTION table above.

| PROGRAM | Source CSD | LANGUAGE | Self-declared TRANSID | Description (from CSD) |
| --- | --- | --- | --- | --- |
| `COACTUPC` | base | COBOL¹ | *(none)* | Credit card demo account update |
| `COACTVWC` | base | COBOL¹ | `CAVW` | View account |
| `COADM01C` | base | COBOL | *(none)* | Admin menu |
| `COBIL00C` | base | COBOL | *(none)* | Bill payment |
| `COCRDLIC` | base | COBOL¹ | `CC00` | List cards (mismatch — TRANSACTION `CCLI` routes here) |
| `COCRDSEC` | base | COBOL¹ | *(none)* | Credit card search (used by dev txn `CDV1`) |
| `COCRDSLC` | base | COBOL¹ | `CCDL` | View card detail |
| `COCRDUPC` | base | COBOL¹ | *(none)* | Credit card update screen |
| `COMEN01C` | base | COBOL | *(none)* | Main menu |
| `CORPT00C` | base | COBOL | *(none)* | Transaction reports |
| `COSGN00C` | base | COBOL¹ | `CC00` | Login / sign-on (initial transaction) |
| `COTRN00C` | base | COBOL | *(none)* | Transaction list |
| `COTRN01C` | base | COBOL | *(none)* | Transaction view |
| `COTRN02C` | base | COBOL | *(none)* | Transaction add |
| `COUSR00C` | base | COBOL | *(none)* | User list |
| `COUSR01C` | base | COBOL | *(none)* | User add |
| `COUSR02C` | base | COBOL | *(none)* | User update |
| `COUSR03C` | base | COBOL | *(none)* | User delete |
| `COTRTLIC` | CRDDEMOD | COBOL | `CTLI` | Transaction type inquiry/list (DB2) |
| `COTRTUPC` | CRDDEMOD | COBOL | `CTTU` | Transaction type maintenance (DB2) |
| `COPAUA0C` | CRDDEMO2 | COBOL | `CP00` | Credit card authorization summary (auth driver) |
| `COPAUS0C` | CRDDEMO2 | COBOL | `CPVS` | Pending-auth view — summary |
| `COPAUS1C` | CRDDEMO2 | COBOL | `CPVD` | Pending-auth view — detail (TRANSACTION CPVD routes here) |
| `COPAUS2C` | CRDDEMO2 | COBOL | `CPVD` | Pending-auth detail (2) — defined but **not** the TRANSACTION target (see Discrepancies) |
| `COACCT01` | CRDDEMOM | COBOL² | `CDRA` | List cards (VSAM+MQ utility) |
| `CODATE01` | CRDDEMOM | COBOL² | `CDRD` | Date utility (VSAM+MQ) |

¹ Base-deck program definitions for the screen programs use `RELOAD(NO)` and **omit the explicit
`LANGUAGE(COBOL)`** clause (CICS infers language at load); they are COBOL load modules. The auth
and DB2 add-on decks state `LANGUAGE(COBOL)` explicitly.
² `COACCT01` / `CODATE01` in CRDDEMOM omit `LANGUAGE` (inferred COBOL) and use `RELOAD(NO)`.

### Common program attributes (shared by all program rows)

```
RESIDENT(NO)  USAGE(NORMAL)  USELPACOPY(NO)  STATUS(ENABLED)
CEDF(YES)     DATALOCATION(ANY)  EXECKEY(USER)  CONCURRENCY(QUASIRENT)
API(CICSAPI)  DYNAMIC(NO)    EXECUTIONSET(FULLAPI)  JVM(NO)
```

(`RELOAD` is `NO` for the base/CRDDEMOM screen programs and `YES` for the CRDDEMO2 auth programs.)

Routing-relevant meanings:

- **`CONCURRENCY(QUASIRENT)`** — quasi-reentrant; runs on the CICS QR TCB. Single-threaded per
  task in the classic model. Informational for the managed port.
- **`API(CICSAPI)` / `EXECUTIONSET(FULLAPI)`** — full CICS API available (not a restricted/Open
  API program). All CardDemo online programs are full-API COBOL.
- **`DATALOCATION(ANY)`** — program working storage can be above the line; informational.
- **`DYNAMIC(NO)`** — not a dynamic-routing program; runs locally. Confirms there is **no**
  dynamic transaction routing to model — the dispatch is purely the static CSD table.
- **`JVM(NO)`** — native (COBOL) program, not Java.

---

## MAPSET (BMS screen group) definitions

`DEFINE MAPSET` resources. Each online program drives one BMS mapset (same 7-char prefix +
program letter convention). Listed for screen/UI cross-reference; not part of TRANSID routing.

| MAPSET | Source CSD | Description | Used by program(s) |
| --- | --- | --- | --- |
| `COACTUP` | base | Account update map | `COACTUPC` (`CAUP`) |
| `COACTVW` | base | View account | `COACTVWC` (`CAVW`) |
| `COADM01` | base | Admin menu | `COADM01C` (`CA00`) |
| `COBIL00` | base | Bill payment | `COBIL00C` (`CB00`) |
| `COCRDLI` | base | List cards | `COCRDLIC` (`CCLI`) |
| `COCRDSL` | base | Search / view card | `COCRDSLC` (`CCDL`), `COCRDSEC` (`CDV1`) |
| `COCRDUP` | base | Card update | `COCRDUPC` (`CCUP`) |
| `COMEN01` | base | Main menu | `COMEN01C` (`CM00`) |
| `CORPT00` | base | Reports | `CORPT00C` (`CR00`) |
| `COSGN00` | base | Sign-on | `COSGN00C` (`CC00`) |
| `COTRN00` | base | Transaction list | `COTRN00C` (`CT00`) |
| `COTRN01` | base | Transaction view | `COTRN01C` (`CT01`) |
| `COTRN02` | base | Transaction add | `COTRN02C` (`CT02`) |
| `COUSR00` | base | User list | `COUSR00C` (`CU00`) |
| `COUSR01` | base | User add | `COUSR01C` (`CU01`) |
| `COUSR02` | base | User update | `COUSR02C` (`CU02`) |
| `COUSR03` | base | User delete | `COUSR03C` (`CU03`) |
| `COPAU00` | CRDDEMO2 | Auth summary map | `COPAUA0C` (`CP00`), `COPAUS0C` (`CPVS`) |
| `COPAU01` | CRDDEMO2 | Auth details map | `COPAUS1C` (`CPVD`) |
| `COTRTLI` | CRDDEMOD | Tran-type inquiry map | `COTRTLIC` (`CTLI`) |
| `COTRTUP` | CRDDEMOD | Tran-type maintenance map | `COTRTUPC` (`CTTU`) |

> CRDDEMOM (`CDRA`/`CDRD`) defines **no** mapsets in its deck — those utility transactions either
> reuse a base mapset or run without a screen.

---

## Menu-driven program selection (dispatcher fan-out)

The two menu programs do not hard-code per-option transactions in the CSD; they `XCTL` to the
option's **program** (read from a table in their copybooks). The selected program then
`RETURN TRANSID`s with its own CSD TRANSID. These tables therefore extend the routing graph and
are reproduced here so the .NET dispatcher reproduces the same option → program → TRANSID chain.

### Main menu — `COMEN01C` / `CM00` (copybook `COMEN02Y`)

| Option | Label | Program | Resulting TRANSID |
| --- | --- | --- | --- |
| 1 | Account View | `COACTVWC` | `CAVW` |
| 2 | Account Update | `COACTUPC` | `CAUP` |
| 3 | Credit Card List | `COCRDLIC` | `CCLI` |
| 4 | Credit Card View | `COCRDSLC` | `CCDL` |
| 5 | Credit Card Update | `COCRDUPC` | `CCUP` |
| 6 | Transaction List | `COTRN00C` | `CT00` |
| 7 | Transaction View | `COTRN01C` | `CT01` |
| 8 | Transaction Add | `COTRN02C` | `CT02` |
| 9 | Transaction Reports | `CORPT00C` | `CR00` |
| 10 | Bill Payment | `COBIL00C` | `CB00` |
| 11 | Pending Authorization View | `COPAUS0C` | `CPVS` (auth add-on) |

### Admin menu — `COADM01C` / `CA00` (copybook `COADM02Y`)

| Option | Label | Program | Resulting TRANSID |
| --- | --- | --- | --- |
| 1 | User List (Security) | `COUSR00C` | `CU00` |
| 2 | User Add (Security) | `COUSR01C` | `CU01` |
| 3 | User Update (Security) | `COUSR02C` | `CU02` |
| 4 | User Delete (Security) | `COUSR03C` | `CU03` |
| 5 | Transaction Type List/Update (DB2) | `COTRTLIC` | `CTLI` (DB2 add-on) |
| 6 | Transaction Type Maintenance (DB2) | `COTRTUPC` | `CTTU` (DB2 add-on) |

All admin options carry user-type `'U'`/admin gating in `COMEN02Y` (`CDEMO-MENU-OPT-USRTYPE`);
the admin menu options are reachable only for admin users. This is **authorization** policy
layered on top of the CSD routing, not a CSD attribute.

---

## DB2 plan bindings (`DB2ENTRY` / `DB2TRAN`)

Not part of TRANSID→PROGRAM dispatch, but they bind a transaction to a DB2 plan at attach time.
Recorded so the .NET port knows which online transactions touch DB2 (vs. VSAM only).

| DB2ENTRY | PLAN | DB2TRAN | Bound TRANSID | Source CSD |
| --- | --- | --- | --- | --- |
| `CARDDEMO` | `CARDDEMO` | `CTLITRAN` | `CTLI` | CRDDEMOD |
| `CARDDEMO` | `CARDDEMO` | `CTTUTRAN` | `CTTU` | CRDDEMOD |
| `AWS01PLN` | `AWS01PLN` | `CPVDTRAN` | `CPVD` | CRDDEMO2 |

Each `DB2ENTRY` uses `ACCOUNTREC(TXID) AUTHTYPE(USERID) DROLLBACK(YES) PRIORITY(HIGH)
THREADLIMIT(1) THREADWAIT(YES) PROTECTNUM(0)`. Only the three DB2-add-on transactions
(`CTLI`, `CTTU`, `CPVD`) are DB2-bound; **all base transactions are VSAM-only**.

---

## Non-routing CSD resources (reference)

### FILE (VSAM) — base deck only

All `DEFINE FILE` entries are VSAM KSDS/AIX-path definitions for the online file control. Detail
(record format, key, AIX) belongs to the VSAM/file spec; listed here only to show the base deck
owns the file definitions.

| FILE | DSNAME | Role |
| --- | --- | --- |
| `ACCTDAT` | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | Account master |
| `CARDDAT` | `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` | Card master |
| `CARDAIX` | `AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX.PATH` | Card AIX (by account) path |
| `CCXREF` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | Card→account cross-reference |
| `CXACAIX` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.PATH` | XREF AIX (by account key) path |
| `CUSTDAT` | `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` | Customer master |
| `TRANSACT` | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | Transaction file |
| `USRSEC` | `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS` | User security file |

All files: `RECORDFORMAT(V) STRINGS(1) LSRPOOLNUM(1) DISPOSITION(SHARE) OPENTIME(FIRSTREF)`,
full `ADD/BROWSE/DELETE/READ/UPDATE(YES)`, `RECOVERY(NONE)`.

### LIBRARY (DFHRPL load libraries)

| LIBRARY | DSNAME01 | STATUS | Source CSD |
| --- | --- | --- | --- |
| `CARDDLIB` | `AWS.M2.CARDDEMO.LOADLIB` | ENABLED, RANKING(50) | base + CRDDEMOM (same def) |
| `COM2DOLL` | `AWS.M2.CARDDEMO.LOADLIB` | **DISABLED**, RANKING(50) | base |

`CARDDLIB` is the active load library; `COM2DOLL` points at the same dataset but is disabled
(legacy/alternate). CRDDEMOM re-defines `CARDDLIB` identically (harmless duplicate at install).

### TDQUEUE (transient data) — base deck

| TDQUEUE | TYPE | DDNAME | Direction | Purpose |
| --- | --- | --- | --- | --- |
| `JOBS` | `EXTRA` | `INREADER` | `TYPEFILE(OUTPUT)`, `RECORDSIZE(80)`, `RECFM(FIXED/UNBLOCKED)`, `DISPOSITION(MOD)` | Submit batch JCL from CICS (internal reader). Used by `CORPT00C`/`CR00` to submit report jobs. |

---

## Discrepancies / routing gotchas (must reproduce or consciously resolve)

1. **`CCLI` vs program-declared `TRANSID(CC00)`.** Base program `COCRDLIC` declares
   `TRANSID(CC00)` on its `DEFINE PROGRAM`, but the actual `DEFINE TRANSACTION(CCLI)` routes
   `CCLI → COCRDLIC`, and `CC00` separately routes to `COSGN00C` (sign-on). The **TRANSACTION**
   definition wins: the card list is reached via `CCLI`. The stray `TRANSID(CC00)` on the program
   is a copy/paste leftover and must be ignored for routing.

2. **`CC00` is the application entry point.** `CC00 → COSGN00C` (sign-on). `COSGN00C` also
   declares `TRANSID(CC00)`. This is the initial transaction a user types to start CardDemo.

3. **`COPAUS1C` vs `COPAUS2C` both for `CPVD`.** In CRDDEMO2, **both** `COPAUS1C` and `COPAUS2C`
   declare `TRANSID(CPVD)` on their `DEFINE PROGRAM`, but only one `DEFINE TRANSACTION(CPVD)`
   exists and it routes to **`COPAUS1C`**. `COPAUS2C` is installed (loadable) but is **not** the
   CPVD dispatch target — it is reached by `XCTL`/`LINK` from `COPAUS1C`, not by typing `CPVD`.
   Route `CPVD → COPAUS1C`.

4. **Two MAPSET-less utility transactions.** `CDRA`/`CDRD` (CRDDEMOM) define no mapset in their
   deck — treat as non-screen (or base-mapset-reusing) utility transactions.

5. **Duplicate `CARDDLIB` LIBRARY** across base and CRDDEMOM — identical definition; installing
   both decks is a no-op duplicate, not a conflict.

6. **No dynamic routing.** Every program is `DYNAMIC(NO)` and every transaction is
   `ROUTABLE(NO) DYNAMIC(NO)`; the dispatch table is fully static. The .NET dispatcher can be a
   simple static `TRANSID → handler` map with no workload routing.

---

## Consolidated dispatcher map (TRANSID → PROGRAM, deduplicated)

The single table the online dispatcher must implement (25 transactions across the union of all
four installed decks):

```
CC00 -> COSGN00C   (sign-on / entry)        CM00 -> COMEN01C   (main menu)
CA00 -> COADM01C   (admin menu)
CAVW -> COACTVWC   CAUP -> COACTUPC
CCLI -> COCRDLIC   CCDL -> COCRDSLC   CCUP -> COCRDUPC   CDV1 -> COCRDSEC (dev)
CT00 -> COTRN00C   CT01 -> COTRN01C   CT02 -> COTRN02C
CR00 -> CORPT00C   CB00 -> COBIL00C
CU00 -> COUSR00C   CU01 -> COUSR01C   CU02 -> COUSR02C   CU03 -> COUSR03C
CTLI -> COTRTLIC   CTTU -> COTRTUPC                       (DB2 add-on, plan CARDDEMO)
CP00 -> COPAUA0C   CPVS -> COPAUS0C   CPVD -> COPAUS1C    (auth add-on, CPVD plan AWS01PLN)
CDRA -> COACCT01   CDRD -> CODATE01                       (VSAM+MQ add-on)
```

All entries share `TASKDATALOC(ANY)`, `TASKDATAKEY(USER)`, `PROFILE(DFHCICST)`,
`TRANCLASS(DFHTCL00)`, `PRIORITY(1)` — apply as constant defaults.
