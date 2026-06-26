# JOB SPEC: INTCALC

## Source
- JCL member: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/INTCALC.jcl`
- Version stamp: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19 23:23:06 CDT)
- Invoked program source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT04C.cbl` (`CardDemo_v2.0-25-gdb72e6b-235`, 2025-04-29)

## JOB Card
- **JOB name:** `INTCALC`
- **Description:** `'INTEREST CALCULATOR'`
- **CLASS:** `A`
- **MSGCLASS:** `0`
- **NOTIFY:** `&SYSUID`

## Overall Purpose
**Interest & fee calculation / posting job (batch posting sequence).** This single-step job runs the interest-calculator batch program `CBACT04C`. It walks the **Transaction Category Balance** file in key order, and for each account:
- looks up the account's disclosure (interest-rate) group and the applicable interest rate,
- computes monthly interest per transaction-category balance (`balance * rate / 1200`),
- writes one new interest **transaction** record per balance into a **new GDG generation** of the system-transaction file, and
- rewrites the **account master** record to add the accrued interest to the current balance and to zero the current-cycle credit/debit totals.

It is a **posting / accrual job**, not a file-setup, report, or backup job. It reads several reference/master VSAM files (input or update mode) and produces a brand-new sequential transaction output dataset cataloged as the next generation of a GDG. There is no IDCAMS, SORT, or IEFBR14 step; there is no COND/RC gating and no inter-step dependency (only one EXEC step exists).

## Logical Data Entities
- **Transaction Category Balance** (driver, read sequentially) — per-account / per-transaction-type / per-transaction-category accrued balance; key = account id (11) + type code (2) + category code (4). → table `TRANSACTION_CATEGORY_BALANCE` (a.k.a. TCATBAL).
- **Card Cross-Reference** (read random + via alternate index by account id) — maps card number ↔ customer ↔ account. → table `CARD_XREF`.
- **Account Master** (read + rewrite/update) — account balances and group id. → table `ACCOUNT`.
- **Disclosure Group** (read random; falls back to `DEFAULT` group) — interest-rate lookup. → table `DISCLOSURE_GROUP`.
- **System Transactions** (write, new GDG generation) — newly generated interest posting transactions. → table `TRANSACTION` (loaded from this sequential file).

---

## Steps (in order)

### STEP15 — Run interest calculator (CBACT04C)
- **EXEC:** `PGM=CBACT04C`
- **PARM:** `'2022071800'` — a 10-character processing/run date string. In `CBACT04C` it is received via the `LINKAGE` `EXTERNAL-PARMS` (`PARM-LENGTH` + `PARM-DATE PIC X(10)`) and is used as the **prefix of every generated `TRAN-ID`** (`STRING PARM-DATE, WS-TRANID-SUFFIX` → 16-byte transaction id, suffix is a 6-digit running counter). It is NOT used as an effective/posting date for amounts; the actual `TRAN-ORIG-TS` / `TRAN-PROC-TS` come from `FUNCTION CURRENT-DATE`.
- **COND/RC gating:** none (single step; no COND parameter).
- **GDG:** **Yes** — output `TRANSACT` DD uses `DSN=AWS.M2.CARDDEMO.SYSTRAN(+1)`, i.e. it creates the **next (+1) generation** of the `AWS.M2.CARDDEMO.SYSTRAN` generation data group.
- **Load library:** `STEPLIB DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` (resolves the `CBACT04C` load module).

- **DD statements:**
  | DD | Disposition / Type | Dataset | COBOL SELECT / open mode | Logical mapping | Role |
  |----|--------------------|---------|--------------------------|-----------------|------|
  | `STEPLIB` | `DISP=SHR` | `AWS.M2.CARDDEMO.LOADLIB` | — | load library | program load module |
  | `SYSPRINT` | `SYSOUT=*` | spool | — | — | runtime/system print |
  | `SYSOUT` | `SYSOUT=*` | spool | `DISPLAY` output | — | program DISPLAY messages |
  | `TCATBALF` | `DISP=SHR` | `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` | `TCATBAL-FILE`, INDEXED, ACCESS SEQUENTIAL, OPEN **INPUT** | `TRANSACTION_CATEGORY_BALANCE` (TCATBAL) | **read** (driver, sequential by key) |
  | `XREFFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | `XREF-FILE`, INDEXED, ACCESS RANDOM, RECORD KEY card-num, OPEN **INPUT** | `CARD_XREF` (KSDS, card-number key) | **read** (random) |
  | `XREFFIL1` | `DISP=SHR` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.PATH` | alternate index PATH for `XREF-FILE` (`ALTERNATE RECORD KEY FD-XREF-ACCT-ID`) | `CARD_XREF` alternate-key (by account id) | **read** (alternate index used by `1110-GET-XREF-DATA`, `KEY IS FD-XREF-ACCT-ID`) |
  | `ACCTFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | `ACCOUNT-FILE`, INDEXED, ACCESS RANDOM, OPEN **I-O** | `ACCOUNT` (KSDS, account-id key) | **read + REWRITE (update)** |
  | `DISCGRP` | `DISP=SHR` | `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS` | `DISCGRP-FILE`, INDEXED, ACCESS RANDOM, OPEN **INPUT** | `DISCLOSURE_GROUP` | **read** (random; default-group fallback) |
  | `TRANSACT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.SYSTRAN(+1)` | `TRANSACT-FILE`, SEQUENTIAL, OPEN **OUTPUT** | `TRANSACTION` (sequential load file) | **write** (new GDG generation) |

- **`TRANSACT` output dataset attributes (from JCL):**
  | Attribute | Value | Meaning |
  |-----------|-------|---------|
  | `DISP` | `(NEW,CATLG,DELETE)` | create new, catalog on success, delete on failure |
  | `UNIT` | `SYSDA` | disk |
  | `DCB` | `RECFM=F,LRECL=350,BLKSIZE=0` | fixed-length 350-byte records (system-determined blocksize) |
  | `SPACE` | `(CYL,(1,1),RLSE)` | 1 cyl primary, 1 cyl secondary, release unused |
  | `DSN` | `AWS.M2.CARDDEMO.SYSTRAN(+1)` | next GDG generation of `AWS.M2.CARDDEMO.SYSTRAN` |

- **Notes on program behavior (`CBACT04C`):**
  - Driven by a sequential read of `TCATBALF`; EOF (file status `10`) ends the loop.
  - On an **account break** (`TRANCAT-ACCT-ID` changes), it first rewrites the previous account (`1050-UPDATE-ACCOUNT`: `ADD WS-TOTAL-INT TO ACCT-CURR-BAL`, zero `ACCT-CURR-CYC-CREDIT`/`ACCT-CURR-CYC-DEBIT`, `REWRITE` account), resets the interest total, then reads the new account (`ACCTFILE`) and its xref (`XREFFILE` via account-id alternate path).
  - For each balance row it reads the disclosure group (`1200-GET-INTEREST-RATE`); a not-found (status `23`) retries with `FD-DIS-ACCT-GROUP-ID = 'DEFAULT'`.
  - If `DIS-INT-RATE NOT = 0`: `1300-COMPUTE-INTEREST` computes `WS-MONTHLY-INT = (TRAN-CAT-BAL * DIS-INT-RATE) / 1200`, accumulates into `WS-TOTAL-INT`, and `1300-B-WRITE-TX` writes one new transaction (`TRAN-TYPE-CD='01'`, `TRAN-CAT-CD='05'`, `TRAN-SOURCE='System'`, `TRAN-DESC='Int. for a/c '+ACCT-ID`, amount = monthly interest, card number from xref, DB2-format timestamps from current date).
  - `1400-COMPUTE-FEES` is a stub ("To be implemented") — no fees are actually computed/posted in this version.
  - Any non-`00` (non-EOF) file status triggers `9999-ABEND-PROGRAM` (`CALL 'CEE3ABD'` with ABCODE 999) → step abends (no in-JCL COND handling).

---

## Datasets Summary
| Dataset | Type | Read/Write | DD(s) | Corresponds to |
|---------|------|-----------|-------|----------------|
| `AWS.M2.CARDDEMO.LOADLIB` | PDS load library | read | `STEPLIB` | program load module |
| `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` | VSAM KSDS | read (sequential) | `TCATBALF` | `TRANSACTION_CATEGORY_BALANCE` (TCATBAL) |
| `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | VSAM KSDS | read (random) | `XREFFILE` | `CARD_XREF` (card-number key) |
| `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.PATH` | VSAM alternate-index PATH | read (random by acct id) | `XREFFIL1` | `CARD_XREF` (alternate key = account id) |
| `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | VSAM KSDS | read + rewrite (I-O) | `ACCTFILE` | `ACCOUNT` |
| `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS` | VSAM KSDS | read (random) | `DISCGRP` | `DISCLOSURE_GROUP` |
| `AWS.M2.CARDDEMO.SYSTRAN(+1)` | sequential, F/350, GDG (+1) | write (new) | `TRANSACT` | `TRANSACTION` (loaded from this file) |

## PARM / COND / GDG Summary
- **PARM:** `'2022071800'` (run date string) → used as the `TRAN-ID` prefix in generated transactions; passed to `CBACT04C` via LINKAGE.
- **COND/RC gating:** none (single step, no COND clauses).
- **GDG usage:** output `AWS.M2.CARDDEMO.SYSTRAN(+1)` creates the next generation of the `SYSTRAN` GDG (relative generation `+1`, cataloged on success, deleted on abend).

## Programs / Utilities Invoked
- `CBACT04C` (application batch COBOL program — interest calculator/poster). **1 EXEC step.**

No IDCAMS, SORT, or IEFBR14 steps are present in this job — therefore no DEFINE/REPRO/DELETE or SORT FIELDS control statements apply.

## Step-Runner Notes (for .NET JobControl)
- Single step, no COND/RC gating: implement as one runner that invokes the `CBACT04C` equivalent (interest accrual + transaction posting).
- **GDG handling required:** the `TRANSACT` output must be modeled as the next generation of the `SYSTRAN` generation group (allocate a new generation, catalog on success, discard on failure). Downstream jobs reference `SYSTRAN(0)` / a resolved generation.
- Account-master file is opened **I-O** and rewritten — the runner must update the `ACCOUNT` table (add accrued interest to current balance, zero current-cycle credit/debit), not just read it.
- Cross-reference lookup uses the **account-id alternate index path** (`XREFFIL1`), not the primary card-number key — map to a query on `CARD_XREF` by account id.
- Disclosure-group lookup must implement the `DEFAULT`-group fallback when the specific account group is not found (mirrors file status `23`).
- Interest formula to preserve exactly: monthly interest = `(transaction-category balance * disclosure interest rate) / 1200`; one transaction row per non-zero-rate balance; transaction id = `PARM-DATE` + 6-digit running suffix; timestamps from current date in DB2 timestamp format.
- Fee computation is a no-op stub in this version — do not invent fee logic.
- On any unexpected I/O error the legacy program abends (CEE3ABD / code 999); the .NET runner should surface a hard failure (non-zero RC) rather than silently continue.
