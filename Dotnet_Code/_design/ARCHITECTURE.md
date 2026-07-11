# CardDemo .NET 10 — Relational Re-Architecture (v2)

## Pivot (2026-06-26, user-directed)
1. **Drop the VSAM/BLOB-image model.** Every program is redesigned around **plain relational SQL tables**
   (one table per logical file, one column per COBOL elementary field). No byte-exact record images as the
   primary store.
2. **Zero COBOL anywhere in `New_Dotnet_Code/`.** Remove GnuCOBOL harness, embedded COBOL oracle fixtures,
   and all golden-master tests that compile COBOL. Verification is pure-.NET (see below).
3. Pure C# / .NET 10 only. SQLite as the relational engine (Microsoft.Data.Sqlite + EF Core 10).

## Target solution layout (`New_Dotnet_Code/`)
```
CardDemo.sln
├── src/CardDemo.Domain        // POCO entity per table (typed: long/decimal/string). No persistence concerns.
├── src/CardDemo.Runtime       // PURE C# COBOL-semantics helpers still needed:
│                              //   CobolDecimal (truncate-toward-zero, silent overflow),
│                              //   CobolEditedNumeric (PIC -ZZZ,ZZZ.ZZ etc.), fixed-width field formatting,
│                              //   EBCDIC/ASCII codec (import + report file boundary ONLY), IClock, Abend.
├── src/CardDemo.Data          // EF Core 10 DbContext + per-table repositories. SQLite schema (DDL via migrations
│                              //   or ExecuteSql). VSAM ops mapped to SQL: keyed read, ordered browse/range,
│                              //   insert/update/delete -> FileStatus result codes. Alt-index = secondary query.
├── src/CardDemo.Import        // One-time seeder: reads app/data/EBCDIC/*.PS via Runtime codec -> rows.
├── src/CardDemo.Batch         // One class per CB* program over repositories.
├── src/CardDemo.Online        // CICS shim (COMMAREA store, XCTL/LINK/RETURN, AID/PFKey) + BMS screen model.
├── src/CardDemo.ConsoleApp    // 24x80 console renderer + dispatcher loop; one handler per CO* transaction.
├── src/CardDemo.Db2           // optional DB2 modules -> same EF Core context, extra tables.
├── src/CardDemo.Ims           // optional IMS hierarchical data -> relational tables.
├── src/CardDemo.Mq            // optional MQ request/response shim (in-proc queue).
└── tests/CardDemo.Tests       // pure-.NET: schema round-trip, numeric edge, batch characterization vs
                               //   captured golden fixtures, online screen-parity, coverage matrix.
```

## COBOL -> C#/SQL type mapping (canonical)
| COBOL PIC | C# type | SQLite col | Notes |
|---|---|---|---|
| `9(n)` unsigned, n<=9 | `int` | INTEGER | counts, small codes |
| `9(n)` unsigned, 10<=n<=18 | `long` | INTEGER | IDs (ACCT-ID 9(11), CUST-ID 9(9)) |
| `S9(p)V(s)` | `decimal` | NUMERIC (TEXT-exact) | money; truncate-toward-zero, silent overflow; never float |
| `X(n)` | `string` | TEXT | store EXACT n chars incl. trailing spaces (faithful re-serialize) |
| dates `X(8)`/`X(10)` | `string` | TEXT | keep CCYY-MM-DD / CCYYMMDD literal form |
| `FILLER` | (dropped) | — | reconstructed as spaces on fixed-width serialize |
| COMP-3 / COMP | (no table column) | — | a *file format* (EXPORT) concern, handled by Runtime serializer only |

String fields keep their full fixed width (trailing spaces) so that re-serializing a row to the canonical
fixed-width record image is byte-identical to the mainframe dataset — this is what the verification harness
diffs against captured golden fixtures.

## Base-app relational schema (11 tables) — authoritative
- **ACCOUNT** (CVACT01Y/300) PK acct_id 9(11):
  acct_id, active_status X1, curr_bal S9(10)V99, credit_limit, cash_credit_limit, open_date X10,
  expiration_date X10 (COBOL field name EXPIRAION), reissue_date X10, curr_cyc_credit, curr_cyc_debit,
  addr_zip X10, group_id X10.
- **CARD** (CVACT02Y/150) PK card_num X16; idx acct_id 9(11):
  card_num, acct_id, cvv_cd 9(3), embossed_name X50, expiration_date X10, active_status X1.
- **CARD_XREF** (CVACT03Y/50) PK xref_card_num X16; idx acct_id 9(11):
  xref_card_num, cust_id 9(9), acct_id 9(11).
- **CUSTOMER** (CVCUS01Y/500) PK cust_id 9(9):
  cust_id, first_name X25, middle_name X25, last_name X25, addr_line_1/2/3 X50, addr_state_cd X2,
  addr_country_cd X3, addr_zip X10, phone_num_1 X15, phone_num_2 X15, ssn 9(9), govt_issued_id X20,
  dob_yyyy_mm_dd X10, eft_account_id X10, pri_card_holder_ind X1, fico_credit_score 9(3).
- **TRANSACTION** (CVTRA05Y/350) PK tran_id X16:
  tran_id, type_cd X2, cat_cd 9(4), source X10, "desc" X100, amt S9(9)V99, merchant_id 9(9),
  merchant_name X50, merchant_city X50, merchant_zip X10, card_num X16, orig_ts X26, proc_ts X26.
- **DAILY_TRANSACTION** (CVTRA06Y/350) seq input: same columns as TRANSACTION (DALYTRAN-*); PK tran_id.
- **TRAN_CAT_BAL** (CVTRA01Y/50) composite PK (acct_id 9(11), type_cd X2, cat_cd 9(4)):
  + tran_cat_bal S9(9)V99.   [TRAN-CAT-KEY = 17 bytes]
- **DISCLOSURE_GROUP** (CVTRA02Y/50) composite PK (acct_group_id X10, tran_type_cd X2, tran_cat_cd 9(4)):
  + int_rate S9(4)V99.   [DIS-GROUP-KEY = 16 bytes]
- **TRAN_TYPE** (CVTRA03Y/60) PK tran_type X2: + tran_type_desc X50.
- **TRAN_CATEGORY** (CVTRA04Y/60) composite PK (tran_type_cd X2, tran_cat_cd 9(4)): + tran_cat_type_desc X50.
- **USER_SECURITY** (CSUSR01Y/80) PK usr_id X8: + first_name X20, last_name X20, pwd X8, usr_type X1.

## Optional-module tables (from DDL — already relational)
- **TRANSACTION_TYPE** (TRNTYPE.ddl): TR_TYPE CHAR(2) PK, TR_DESCRIPTION VARCHAR(50).
- **TRANSACTION_TYPE_CATEGORY** (TRNTYCAT.ddl): (TRC_TYPE_CODE CHAR2, TRC_TYPE_CATEGORY CHAR4) PK, TRC_CAT_DATA VARCHAR(50).
- **AUTHFRDS** (AUTHFRDS.ddl): PK (CARD_NUM CHAR16, AUTH_TS TIMESTAMP) + ~25 cols incl. DECIMAL(12,2) amounts.
  (+ XAUTHFRD.ddl alt index, XTRNTYPE/XTRNTYCAT indexes.)
- IMS PSB/DBD segments -> tables (see _design/specs for the IMS module).

## VSAM-semantics -> SQL mapping (repository contract)
- READ key            -> SELECT by PK; FileStatus '00' / '23' (not found).
- READ alt key        -> SELECT by indexed col (first / browse).
- STARTBR + READNEXT  -> ORDER BY key, forward cursor; READPREV -> reverse. EBCDIC collation only matters for
  punctuated keys (TRANSACT alt = timestamp) -> use ordinal string compare (digits/space/punct coincide for
  the ASCII subset CardDemo uses; guard test pins it).
- WRITE  -> INSERT; duplicate -> FileStatus '22'.
- REWRITE-> UPDATE; missing -> '23'.
- DELETE -> DELETE; missing -> '23'.
- "create on '23'" idioms (TCATBAL) preserved at the program layer.

## Verification (no COBOL)
1. **Schema round-trip:** import EBCDIC masters -> rows -> serialize each row back to its canonical
   fixed-width record image -> assert byte-identical to the source dataset (per file). This is the
   anti-hallucination net, replacing the codec round-trip, and needs no oracle.
2. **Batch characterization:** the byte-for-byte outputs previously proven against GnuCOBOL are captured
   ONCE as static fixture files under `tests/golden/` (pure data, committed). Batch tests run the .NET
   program over seeded data and diff its output dataset against the captured golden (timestamps masked).
3. **Numeric/date edge suites:** truncation, sign branch, credit-limit boundary, date corpus — pure unit.
4. **Online screen-parity:** scripted (AID, field-input) flows assert field values + logical attributes +
   post-turn COMMAREA + next TRANSID/XCTL — characterization-based (no CICS oracle available).
5. **Coverage matrix:** every program + paragraph + BMS map + JCL step -> >=1 test.

## Faithful-bugs: reproduced, never fixed. Logged in `_design/faithful-bugs.md` with a pinning test each.
```
