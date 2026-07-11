# Appendix: file and record layouts

[← Domain data model](06-Domain-Data-Model.md) · [Home](Home.md) · [Batch processing →](05-Batch-Processing.md)

## Layout index

- [Storage conventions](#storage-conventions)
- [Core indexed files](#core-indexed-files)
- [Account](#account-record--300-bytes)
- [Card](#card-record--150-bytes)
- [Customer](#customer-record--500-bytes)
- [Card cross-reference](#card-cross-reference-record--50-bytes)
- [Transaction and daily transaction](#transaction-and-daily-transaction-records--350-bytes)
- [Transaction category balance](#transaction-category-balance-record--50-bytes)
- [Disclosure group](#disclosure-group-record--50-bytes)
- [Transaction type and category](#transaction-reference-records--60-bytes)
- [Security user](#security-user-record--80-bytes)
- [Daily reject](#daily-reject-record--430-bytes)
- [Report and statement records](#report-and-statement-records)
- [Branch export/import](#branch-exportimport-record--500-bytes)
- [Supplied fixtures](#supplied-fixtures)

## Storage conventions

All positions below are **one-based and inclusive**. The base master layouts use fixed-length COBOL `DISPLAY` storage. `PIC X(n)` and unsigned `PIC 9(n)` occupy `n` bytes. A signed display value such as `S9(09)V99` occupies eleven bytes with two implied decimal places and the sign encoded in the final digit position. The supplied ASCII fixtures retain overpunch characters: `{`/`A`–`I` encode positive final digits 0–9 and `}`/`J`–`R` encode negative final digits 0–9. For example, `0000005047G` represents `+504.77`; this is confirmed by the amount layout and the fixture in [`dailytran.txt`](../Old_Cobol_Code/app/data/ASCII/dailytran.txt).

Identifiers that are declared numeric still require leading-zero preservation. The .NET model should therefore use string-backed value objects for account, customer, CVV, merchant and similar identifiers unless arithmetic is actually performed. Currency and rates require `decimal`; binary floating point is not parity-safe.

The 500-byte branch export is an exception: it deliberately mixes `DISPLAY`, `COMP` and `COMP-3`. Its byte encoding must be handled explicitly rather than by the base fixed-width parser.

## Core indexed files

| Logical data set | Runtime/JCL name | Length | Primary key | Alternate access | Evidence |
|---|---|---:|---|---|---|
| Account | `ACCTDATA.VSAM.KSDS`; CICS `ACCTDAT` | 300 | account ID, 11 bytes at offset 0 | none declared | [`ACCTFILE.jcl` lines 36–48](../Old_Cobol_Code/app/jcl/ACCTFILE.jcl#L36-L48), [`CARDDEMO.CSD` lines 1–11](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L1-L11) |
| Card | `CARDDATA.VSAM.KSDS`; CICS `CARDDAT` | 150 | card number, 16 bytes at offset 0 | non-unique account ID, 11 bytes at zero-based offset 16, CICS path `CARDAIX` | [`CARDFILE.jcl` lines 50–102](../Old_Cobol_Code/app/jcl/CARDFILE.jcl#L50-L102), [`CARDDEMO.CSD` lines 13–35](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L13-L35) |
| Customer | `CUSTDATA.VSAM.KSDS`; CICS `CUSTDAT` | 500 | customer ID, 9 bytes at offset 0 | none declared | [`CUSTFILE.jcl` lines 46–58](../Old_Cobol_Code/app/jcl/CUSTFILE.jcl#L46-L58), [`CARDDEMO.CSD` lines 50–61](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L50-L61) |
| Card cross-reference | `CARDXREF.VSAM.KSDS`; CICS `CCXREF` | 50 | card number, 16 bytes at offset 0 | non-unique account ID, 11 bytes at zero-based offset 25, CICS path `CXACAIX` | [`XREFFILE.jcl` lines 39–92](../Old_Cobol_Code/app/jcl/XREFFILE.jcl#L39-L92), [`CARDDEMO.CSD` lines 37–74](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L37-L74) |
| Transaction | `TRANSACT.VSAM.KSDS`; CICS `TRANSACT` | 350 | transaction ID, 16 bytes at offset 0 | non-unique processing timestamp, 26 bytes at zero-based offset 304; no corresponding CICS path is defined in the base CSD | [`TRANFILE.jcl` lines 49–101](../Old_Cobol_Code/app/jcl/TRANFILE.jcl#L49-L101), [`CARDDEMO.CSD` lines 76–86](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L76-L86) |
| User security | `USRSEC.VSAM.KSDS`; CICS `USRSEC` | 80 | user ID, 8 bytes at offset 0 | none declared | [`DUSRSECJ.jcl` lines 55–73](../Old_Cobol_Code/app/jcl/DUSRSECJ.jcl#L55-L73), [`CARDDEMO.CSD` lines 88–98](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L88-L98) |
| Transaction category balance | `TCATBALF.VSAM.KSDS` | 50 | account + type + category, 17 bytes | none | [`TCATBALF.jcl` lines 36–48](../Old_Cobol_Code/app/jcl/TCATBALF.jcl#L36-L48) |
| Disclosure group | `DISCGRP.VSAM.KSDS` | 50 | group + type + category, 16 bytes | none | [`DISCGRP.jcl` lines 36–48](../Old_Cobol_Code/app/jcl/DISCGRP.jcl#L36-L48) |
| Transaction type | `TRANTYPE.VSAM.KSDS` | 60 | type code, 2 bytes | none | [`TRANTYPE.jcl` lines 36–48](../Old_Cobol_Code/app/jcl/TRANTYPE.jcl#L36-L48) |
| Transaction category | `TRANCATG.VSAM.KSDS` | 60 | type + category, 6 bytes | none | [`TRANCATG.jcl` lines 36–48](../Old_Cobol_Code/app/jcl/TRANCATG.jcl#L36-L48) |

### Account record — 300 bytes

Source: [`CVACT01Y.cpy`](../Old_Cobol_Code/app/cpy/CVACT01Y.cpy#L1-L20).

| Position | Length | COBOL field | PIC | Meaning / parity note |
|---:|---:|---|---|---|
| 1–11 | 11 | `ACCT-ID` | `9(11)` | Primary identifier |
| 12 | 1 | `ACCT-ACTIVE-STATUS` | `X` | Status code; accepted values are determined by online rules, not by this layout |
| 13–24 | 12 | `ACCT-CURR-BAL` | `S9(10)V99` | Current balance |
| 25–36 | 12 | `ACCT-CREDIT-LIMIT` | `S9(10)V99` | Credit limit |
| 37–48 | 12 | `ACCT-CASH-CREDIT-LIMIT` | `S9(10)V99` | Cash credit limit |
| 49–58 | 10 | `ACCT-OPEN-DATE` | `X(10)` | Text date; supplied values are ISO `yyyy-MM-dd` |
| 59–68 | 10 | `ACCT-EXPIRAION-DATE` | `X(10)` | Source spelling retained; text date |
| 69–78 | 10 | `ACCT-REISSUE-DATE` | `X(10)` | Text date |
| 79–90 | 12 | `ACCT-CURR-CYC-CREDIT` | `S9(10)V99` | Current-cycle credit accumulator |
| 91–102 | 12 | `ACCT-CURR-CYC-DEBIT` | `S9(10)V99` | Current-cycle debit accumulator; posting adds negative transaction amounts as implemented |
| 103–112 | 10 | `ACCT-ADDR-ZIP` | `X(10)` | Account postal code |
| 113–122 | 10 | `ACCT-GROUP-ID` | `X(10)` | Joins to disclosure-group pricing |
| 123–300 | 178 | filler | `X(178)` | Must be retained by a byte-compatible export |

### Card record — 150 bytes

Source: [`CVACT02Y.cpy`](../Old_Cobol_Code/app/cpy/CVACT02Y.cpy#L1-L14).

| Position | Length | COBOL field | PIC | Meaning / parity note |
|---:|---:|---|---|---|
| 1–16 | 16 | `CARD-NUM` | `X(16)` | Primary identifier; preserve leading zeroes |
| 17–27 | 11 | `CARD-ACCT-ID` | `9(11)` | Account relationship and alternate-index key |
| 28–30 | 3 | `CARD-CVV-CD` | `9(3)` | Sensitive verification value |
| 31–80 | 50 | `CARD-EMBOSSED-NAME` | `X(50)` | Embossed name |
| 81–90 | 10 | `CARD-EXPIRAION-DATE` | `X(10)` | Source spelling retained; text date |
| 91 | 1 | `CARD-ACTIVE-STATUS` | `X` | Active status |
| 92–150 | 59 | filler | `X(59)` | Compatibility padding |

### Customer record — 500 bytes

Primary source: [`CVCUS01Y.cpy`](../Old_Cobol_Code/app/cpy/CVCUS01Y.cpy#L1-L26). [`CUSTREC.cpy`](../Old_Cobol_Code/app/cpy/CUSTREC.cpy#L1-L26) is the same physical layout used by statement generation; it spells the date-of-birth field differently.

| Position | Length | COBOL field | PIC |
|---:|---:|---|---|
| 1–9 | 9 | `CUST-ID` | `9(9)` |
| 10–34 | 25 | `CUST-FIRST-NAME` | `X(25)` |
| 35–59 | 25 | `CUST-MIDDLE-NAME` | `X(25)` |
| 60–84 | 25 | `CUST-LAST-NAME` | `X(25)` |
| 85–134 | 50 | `CUST-ADDR-LINE-1` | `X(50)` |
| 135–184 | 50 | `CUST-ADDR-LINE-2` | `X(50)` |
| 185–234 | 50 | `CUST-ADDR-LINE-3` | `X(50)` |
| 235–236 | 2 | `CUST-ADDR-STATE-CD` | `X(2)` |
| 237–239 | 3 | `CUST-ADDR-COUNTRY-CD` | `X(3)` |
| 240–249 | 10 | `CUST-ADDR-ZIP` | `X(10)` |
| 250–264 | 15 | `CUST-PHONE-NUM-1` | `X(15)` |
| 265–279 | 15 | `CUST-PHONE-NUM-2` | `X(15)` |
| 280–288 | 9 | `CUST-SSN` | `9(9)` |
| 289–308 | 20 | `CUST-GOVT-ISSUED-ID` | `X(20)` |
| 309–318 | 10 | `CUST-DOB-YYYY-MM-DD` | `X(10)` |
| 319–328 | 10 | `CUST-EFT-ACCOUNT-ID` | `X(10)` |
| 329 | 1 | `CUST-PRI-CARD-HOLDER-IND` | `X` |
| 330–332 | 3 | `CUST-FICO-CREDIT-SCORE` | `9(3)` |
| 333–500 | 168 | filler | `X(168)` |

### Card cross-reference record — 50 bytes

Source: [`CVACT03Y.cpy`](../Old_Cobol_Code/app/cpy/CVACT03Y.cpy#L1-L11).

| Position | Length | COBOL field | PIC | Key role |
|---:|---:|---|---|---|
| 1–16 | 16 | `XREF-CARD-NUM` | `X(16)` | Primary key |
| 17–25 | 9 | `XREF-CUST-ID` | `9(9)` | Customer relationship |
| 26–36 | 11 | `XREF-ACCT-ID` | `9(11)` | Non-unique alternate key |
| 37–50 | 14 | filler | `X(14)` | Compatibility padding |

The ASCII fixture contains only the 36 meaningful characters per record; the EBCDIC fixture contains the declared 50 bytes. A strict text importer must right-pad the ASCII form before byte-compatible output.

### Transaction and daily transaction records — 350 bytes

Sources: [`CVTRA05Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA05Y.cpy#L1-L21) and the field-for-field daily variant [`CVTRA06Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA06Y.cpy#L1-L21).

| Position | Length | Transaction field | Daily field | PIC |
|---:|---:|---|---|---|
| 1–16 | 16 | `TRAN-ID` | `DALYTRAN-ID` | `X(16)` |
| 17–18 | 2 | `TRAN-TYPE-CD` | `DALYTRAN-TYPE-CD` | `X(2)` |
| 19–22 | 4 | `TRAN-CAT-CD` | `DALYTRAN-CAT-CD` | `9(4)` |
| 23–32 | 10 | `TRAN-SOURCE` | `DALYTRAN-SOURCE` | `X(10)` |
| 33–132 | 100 | `TRAN-DESC` | `DALYTRAN-DESC` | `X(100)` |
| 133–143 | 11 | `TRAN-AMT` | `DALYTRAN-AMT` | `S9(9)V99` |
| 144–152 | 9 | `TRAN-MERCHANT-ID` | `DALYTRAN-MERCHANT-ID` | `9(9)` |
| 153–202 | 50 | `TRAN-MERCHANT-NAME` | `DALYTRAN-MERCHANT-NAME` | `X(50)` |
| 203–252 | 50 | `TRAN-MERCHANT-CITY` | `DALYTRAN-MERCHANT-CITY` | `X(50)` |
| 253–262 | 10 | `TRAN-MERCHANT-ZIP` | `DALYTRAN-MERCHANT-ZIP` | `X(10)` |
| 263–278 | 16 | `TRAN-CARD-NUM` | `DALYTRAN-CARD-NUM` | `X(16)` |
| 279–304 | 26 | `TRAN-ORIG-TS` | `DALYTRAN-ORIG-TS` | `X(26)` |
| 305–330 | 26 | `TRAN-PROC-TS` | `DALYTRAN-PROC-TS` | `X(26)` |
| 331–350 | 20 | filler | filler | `X(20)` |

The timestamp text produced by posting and interest uses `yyyy-MM-dd-HH.mm.ss` plus fractional/trailing characters to fill 26 bytes; filtering code compares the first ten characters lexically. The exact timestamp assembly is in [`CBTRN02C.cbl` lines 692–704](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L692-L704).

### Transaction category balance record — 50 bytes

Source: [`CVTRA01Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA01Y.cpy#L1-L13).

| Position | Length | COBOL field | PIC | Role |
|---:|---:|---|---|---|
| 1–11 | 11 | `TRANCAT-ACCT-ID` | `9(11)` | Composite key |
| 12–13 | 2 | `TRANCAT-TYPE-CD` | `X(2)` | Composite key |
| 14–17 | 4 | `TRANCAT-CD` | `9(4)` | Composite key |
| 18–28 | 11 | `TRAN-CAT-BAL` | `S9(9)V99` | Accumulated category amount |
| 29–50 | 22 | filler | `X(22)` | Padding |

### Disclosure group record — 50 bytes

Source: [`CVTRA02Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA02Y.cpy#L1-L13).

| Position | Length | COBOL field | PIC | Role |
|---:|---:|---|---|---|
| 1–10 | 10 | `DIS-ACCT-GROUP-ID` | `X(10)` | Composite key; `DEFAULT` is the fallback group used by interest calculation |
| 11–12 | 2 | `DIS-TRAN-TYPE-CD` | `X(2)` | Composite key |
| 13–16 | 4 | `DIS-TRAN-CAT-CD` | `9(4)` | Composite key |
| 17–22 | 6 | `DIS-INT-RATE` | `S9(4)V99` | Annual percentage rate divided by 1200 by the monthly calculation |
| 23–50 | 28 | filler | `X(28)` | Padding |

### Transaction reference records — 60 bytes

Transaction type source: [`CVTRA03Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA03Y.cpy#L1-L10).

| Position | Length | Field | PIC |
|---:|---:|---|---|
| 1–2 | 2 | `TRAN-TYPE` | `X(2)` |
| 3–52 | 50 | `TRAN-TYPE-DESC` | `X(50)` |
| 53–60 | 8 | filler | `X(8)` |

Transaction category source: [`CVTRA04Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA04Y.cpy#L1-L12).

| Position | Length | Field | PIC |
|---:|---:|---|---|
| 1–2 | 2 | `TRAN-TYPE-CD` | `X(2)` |
| 3–6 | 4 | `TRAN-CAT-CD` | `9(4)` |
| 7–56 | 50 | `TRAN-CAT-TYPE-DESC` | `X(50)` |
| 57–60 | 4 | filler | `X(4)` |

### Security user record — 80 bytes

Source: [`CSUSR01Y.cpy`](../Old_Cobol_Code/app/cpy/CSUSR01Y.cpy#L17-L26).

| Position | Length | COBOL field | PIC |
|---:|---:|---|---|
| 1–8 | 8 | `SEC-USR-ID` | `X(8)` |
| 9–28 | 20 | `SEC-USR-FNAME` | `X(20)` |
| 29–48 | 20 | `SEC-USR-LNAME` | `X(20)` |
| 49–56 | 8 | `SEC-USR-PWD` | `X(8)` |
| 57 | 1 | `SEC-USR-TYPE` | `X`; `A` and `U` are the role values used by the commarea |
| 58–80 | 23 | filler | `X(23)` |

The plaintext eight-character password field is an observed legacy contract, not a safe-target recommendation. See [Credential decisions](08-Security-and-Controls.md#credential-storage-and-migration).

### Daily reject record — 430 bytes

Source: [`CBTRN02C.cbl` lines 81–84 and 176–182](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L81-L84).

| Position | Length | Field | Meaning |
|---:|---:|---|---|
| 1–350 | 350 | rejected daily transaction | Original record without mutation |
| 351–354 | 4 | validation failure reason | `0100`, `0101`, `0102`, or `0103` for the implemented validation paths |
| 355–430 | 76 | validation description | Space-padded reason text |

The JCL allocates a fixed 430-byte generation data set and sets job return code 4 when any transaction is rejected ([`POSTTRAN.jcl` lines 23–42](../Old_Cobol_Code/app/jcl/POSTTRAN.jcl#L23-L42), [`CBTRN02C.cbl` lines 227–231](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L227-L231)). Only validation reasons `0100`–`0103` reach the reject record (written by `2500-WRITE-REJECT-REC`); reason `109` is moved into the same working field on an account-`REWRITE` failure, but that record is on the accepted path and is still posted rather than rejected (see [DEF-BAT-002](14-Known-Defects-and-Open-Decisions.md#batch-and-reporting-decisions)).

## Report and statement records

| Output/input | Record length | Contract | Evidence |
|---|---:|---|---|
| Transaction report | 133 | Header, detail, page total, account total and grand total layouts; line-oriented fixed output | [`CVTRA07Y.cpy`](../Old_Cobol_Code/app/cpy/CVTRA07Y.cpy#L1-L73), [`CBTRN03C.cbl` lines 274–373](../Old_Cobol_Code/app/cbl/CBTRN03C.cbl#L274-L373) |
| Report date parameter | 80 | first 10 bytes start date, one separator byte, next 10 bytes end date; remaining bytes ignored | [`CBTRN03C.cbl` lines 87–125](../Old_Cobol_Code/app/cbl/CBTRN03C.cbl#L87-L125) |
| Text statement | 80 | per-card statement lines with customer/address/account/FICO/transactions/total | [`CBSTM03A.CBL` lines 44–146](../Old_Cobol_Code/app/cbl/CBSTM03A.CBL#L44-L146) |
| HTML statement | 100 | fixed 100-byte HTML lines | [`CBSTM03A.CBL` lines 46–47 and 148–223](../Old_Cobol_Code/app/cbl/CBSTM03A.CBL#L46-L47), [`CREASTMT.JCL` lines 79–96](../Old_Cobol_Code/app/jcl/CREASTMT.JCL#L79-L96) |
| Statement transaction work file | 350 | transaction reordered as card number + transaction ID key followed by remaining fields | [`COSTM01.CPY` lines 20–36](../Old_Cobol_Code/app/cpy/COSTM01.CPY#L20-L36), [`CREASTMT.JCL` lines 22–61](../Old_Cobol_Code/app/jcl/CREASTMT.JCL#L22-L61) |

## Branch export/import record — 500 bytes

Source layout: [`CVEXPORT.cpy`](../Old_Cobol_Code/app/cpy/CVEXPORT.cpy#L1-L103). Export/import behavior: [`CBEXPORT.cbl`](../Old_Cobol_Code/app/cbl/CBEXPORT.cbl) and [`CBIMPORT.cbl`](../Old_Cobol_Code/app/cbl/CBIMPORT.cbl).

### Common header

| Position | Mainframe bytes | Field | Storage |
|---:|---:|---|---|
| 1 | 1 | record type | `X`; `C` customer, `A` account, `X` cross-reference, `T` transaction, `D` card |
| 2–27 | 26 | timestamp | `X(26)`; generated `yyyy-MM-dd HH:mm:ss.000000` |
| 28–31 | 4 | sequence | `9(9) COMP`; big-endian binary in supplied data |
| 32–35 | 4 | branch | `X(4)`; exporter hard-codes `0001` |
| 36–40 | 5 | region | `X(5)`; exporter hard-codes `NORTH` |
| 41–500 | 460 | type-specific payload | `REDEFINES` union |

The supplied 250,000-byte fixture has exactly 500 records: 50 `C`, 50 `A`, 50 `X`, 300 `T`, and 50 `D`. The first record proves header offsets and contains sequence bytes `00 00 00 01`.

### Payload storage differences

This interchange format is not a byte copy of each master record. It uses packed/binary fields for selected values:

- Customer ID is `COMP`; FICO score is `COMP-3`.
- Account current balance and cash limit are `COMP-3`; cycle debit is `COMP`; other account amounts remain display.
- Transaction amount is `COMP-3`; merchant ID is `COMP`.
- Cross-reference account ID is `COMP`.
- Card account ID and CVV are `COMP`.

Import expands the payload back to the base fixed-width layouts. Unknown record types are written as 132-byte error records containing timestamp, type, seven-digit sequence, message and padding ([`CBIMPORT.cbl` lines 151–160 and 425–445](../Old_Cobol_Code/app/cbl/CBIMPORT.cbl#L151-L160)).

### Export-key discrepancy

`EXPORT-SEQUENCE-NUM` begins at one-based byte 28 (zero-based offset 27), but [`CBEXPORT.jcl` lines 30–33](../Old_Cobol_Code/app/jcl/CBEXPORT.jcl#L30-L33) defines the VSAM key as four bytes at zero-based offset **28**. This one-byte mismatch is a source contradiction. The .NET import/export command must use the declared sequence field at bytes 28–31; any requirement to reproduce the malformed VSAM key definition is a [decision required](14-Known-Defects-and-Open-Decisions.md#export-key-offset).

## Supplied fixtures

| Fixture | Records | Declared data length | Notable observation |
|---|---:|---:|---|
| Account | 50 | 300 | Both `ACCDATA.PS` and `ACCTDATA.PS` exist and are byte-identical |
| Card | 50 | 150 | ASCII and EBCDIC forms supplied |
| Cross-reference | 50 | 50 | ASCII rows are 36 characters and omit filler |
| Customer | 50 | 500 | Contains sensitive demonstration data |
| Daily transaction | 300 | 350 | Includes signed overpunch amounts |
| Daily initialization | 1 | 350 | EBCDIC-only initialization record |
| Disclosure group | 51 | 50 | Includes account-group/category rates and defaults |
| Category balance | 50 | 50 | Initial balances |
| Transaction category | 18 | 60 | Reference descriptions |
| Transaction type | 7 | 60 | Reference descriptions |
| User security | 10 | 80 | EBCDIC-only and contains plaintext demo credentials |
| Branch export | 500 | 500 | Mixed binary/packed/display encoding |

Fixture counts are derived from fixed record sizes and file lengths and, for ASCII, verified by line counts. They are test-vector facts, not production cardinality constraints.

Optional IMS, Db2 and MQ layouts are specified on [Optional Modules and Integrations](07-Optional-Modules-and-Integrations.md#integration-data-contracts) and will not be conflated with the core VSAM layouts.

---

[← Domain data model](06-Domain-Data-Model.md) · [Home](Home.md) · [Batch processing →](05-Batch-Processing.md)
