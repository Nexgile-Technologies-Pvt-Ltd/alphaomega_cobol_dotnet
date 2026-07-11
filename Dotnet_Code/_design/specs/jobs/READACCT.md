# JOB SPEC: READACCT

## Source
- JCL member: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/READACCT.jcl`
- Invoked program source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT01C.cbl` (`CardDemo_v2.0-25-gdb72e6b-235`, 2025-04-29)
- Account record copybook: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cpy/CVACT01Y.cpy` (`ACCOUNT-RECORD`, RECLN 300)

## JOB Card
- **JOB name:** `READACCT`
- **Accounting/description string:** `'READACCT'`
- **REGION:** `8M`
- **CLASS:** `A`
- **MSGCLASS:** `H`
- **NOTIFY:** `&SYSUID`
- **JOB-level COND:** none.

## Overall Purpose
**Account-master extract / "read-and-fan-out" job.** This job reads the Account master VSAM KSDS sequentially and, for every account record, writes derived copies into **three different flat (sequential) output datasets** that demonstrate three different record formats:
1. a fixed-length (`FB`) "compacted" account record that includes a `COMP-3` (packed-decimal) field and a reformatted reissue date,
2. a fixed-length (`FB`) "array" record (an `OCCURS 5` table of balance/debit pairs, partially populated with hard-coded demo values), and
3. a variable-length (`VB`) record file containing two different variable-length record shapes per account.

It is essentially a **batch extract / format-demonstration job** (exercising FB, packed-decimal, OCCURS, and VB output), **not** a posting, report, or backup job in the business sense. It is **non-destructive to the Account master**: the VSAM KSDS is opened **INPUT only** and never updated. The three output PS datasets are first deleted by a pre-delete step, then re-created fresh by the program.

The job has **2 EXEC steps**:
| Step | PGM | Type | Action |
|------|-----|------|--------|
| `PREDEL` | `IEFBR14` | utility (no-op) | Delete the three prior output PS datasets via `DISP=(MOD,DELETE,DELETE)` (cleanup / make re-runnable) |
| `STEP05` | `CBACT01C` | application batch COBOL | Read Account KSDS sequentially; write the three derived PS output files |

No IDCAMS, no SORT, and no GDG are used anywhere in this job.

## Logical Data Entities
- **Account Master** (read sequentially, INPUT only) — the account record (`ACCOUNT-RECORD`, 300-byte fixed). Key = 11-digit `ACCT-ID`. → table **`ACCOUNT`** (VSAM KSDS `...ACCTDATA.VSAM.KSDS`).
- **Account compacted extract** (`OUTFILE` → `...ACCTDATA.PSCOMP`) — a derived **sequential / flat** extract of selected account fields, LRECL 107 FB, including one `COMP-3` field and a date reformatted by `COBDATFT`. → a **sequential extract file**, not a relational table (a flat export/scratch dataset).
- **Account array extract** (`ARRYFILE` → `...ACCTDATA.ARRYPS`) — a derived **sequential** record carrying an `OCCURS 5` balance table, LRECL 110 FB. → a **sequential extract file** (no direct relational table).
- **Account VB extract** (`VBRCFILE` → `...ACCTDATA.VBPS`) — a derived **variable-length** sequential file with two record formats per account, LRECL 84 VB. → a **sequential extract file** (no direct relational table).

---

## Steps (in order)

### PREDEL — Pre-delete prior output datasets (IEFBR14)
- **EXEC:** `PGM=IEFBR14` (the do-nothing utility; its only effect is the disposition processing of the DD statements).
- **PARM:** none.
- **COND/RC gating:** none coded on the EXEC. (As the first step it always runs.)
- **GDG:** none.
- **DD statements:**
  | DD | DISP | Dataset | Effect |
  |----|------|---------|--------|
  | `DD01` | `(MOD,DELETE,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.PSCOMP` | Allocate (MOD), then delete on both normal and abnormal end → removes any pre-existing copy |
  | `DD02` | `(MOD,DELETE,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.ARRYPS` | same — delete prior copy |
  | `DD03` | `(MOD,DELETE,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.VBPS` | same — delete prior copy |
- **Purpose:** make the job re-runnable. `DISP=(MOD,DELETE,DELETE)` with `IEFBR14` is the classic z/OS idiom to delete a dataset if it exists (MOD allocates/creates it if absent, then the DELETE dispositions remove it on both normal and abnormal termination). The three datasets are exactly the three PS outputs that `STEP05` will re-create with `DISP=NEW`.
- **Relational/file mapping:** these are the three derived **sequential extract files** (see Logical Data Entities). No relational table is dropped here; this is flat-file scratch cleanup.

### STEP05 — Read Account KSDS and write three extract files (CBACT01C)
- **EXEC:** `PGM=CBACT01C` (application batch COBOL — "read the account file and write into files").
- **PARM:** none.
- **COND/RC gating:** none coded on the EXEC. (No COND parameter; the step runs regardless of `PREDEL`'s RC. There is no inter-step dependency expressed in JCL.)
- **GDG:** none (all datasets are fixed-name; no `(+1)`/`(0)` generation references).
- **Load library:** `STEPLIB DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` (resolves the `CBACT01C` load module; also where the called `COBDATFT` date-format routine and the `CEE3ABD` LE service resolve from).

- **DD statements:**
  | DD | DISP / Type | Dataset | COBOL SELECT / open mode | Logical mapping | Role |
  |----|-------------|---------|--------------------------|-----------------|------|
  | `STEPLIB` | `DISP=SHR` | `AWS.M2.CARDDEMO.LOADLIB` | — | load library (PDS) | program + sub-program load modules |
  | `ACCTFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | `ACCTFILE-FILE`, INDEXED, ACCESS **SEQUENTIAL**, RECORD KEY `FD-ACCT-ID`, OPEN **INPUT** | `ACCOUNT` (KSDS, 11-digit account-id key, 300-byte record) | **read** (driver, sequential by key) |
  | `OUTFILE` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.PSCOMP` | `OUT-FILE`, SEQUENTIAL, OPEN **OUTPUT** | sequential "compacted" account extract | **write** (new) |
  | `ARRYFILE` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.ARRYPS` | `ARRY-FILE`, SEQUENTIAL, OPEN **OUTPUT** | sequential "array" account extract | **write** (new) |
  | `VBRCFILE` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.VBPS` | `VBRC-FILE`, SEQUENTIAL, RECORDING MODE V (RECORD VARYING 10–80 on `WS-RECD-LEN`), OPEN **OUTPUT** | sequential variable-length account extract | **write** (new) |
  | `SYSOUT` | `SYSOUT=*` | spool | program `DISPLAY` output | — | program DISPLAY / runtime messages |
  | `SYSPRINT` | `SYSOUT=*` | spool | — | — | runtime/system print |

- **Output dataset attributes (from JCL DCB/SPACE):**
  | DD | Dataset | RECFM | LRECL | DSORG | BLKSIZE | UNIT | SPACE |
  |----|---------|-------|-------|-------|---------|------|-------|
  | `OUTFILE` | `...ACCTDATA.PSCOMP` | `FB` | `107` | `PS` | `0` (system-determined) | `SYSAD` | `(CYL,(1,2),RLSE)` |
  | `ARRYFILE` | `...ACCTDATA.ARRYPS` | `FB` | `110` | `PS` | `0` | `SYSAD` | `(CYL,(1,2),RLSE)` |
  | `VBRCFILE` | `...ACCTDATA.VBPS` | `VB` | `84` | `PS` | `0` | `SYSAD` | `(CYL,(1,2),RLSE)` |
  - All three: `DISP=(NEW,CATLG,DELETE)` (create new, catalog on success, delete on abend) on volume unit `SYSAD`, `RLSE` releases unused space.

- **Notes on program behavior (`CBACT01C`):**
  - Opens `ACCTFILE` **INPUT**, and `OUTFILE`/`ARRYFILE`/`VBRCFILE` **OUTPUT**; loops reading the KSDS sequentially until file status `'10'` (EOF). Any unexpected non-`00`/non-`10` status triggers `9999-ABEND-PROGRAM` → `CALL 'CEE3ABD'` with ABCODE 999 (hard abend; no in-JCL COND handling). Each account is also dumped field-by-field via `DISPLAY` to `SYSOUT`.
  - **`OUTFILE` (`OUT-ACCT-REC`, 107 bytes FB):** copies most account fields; reissue date is reformatted by `CALL 'COBDATFT' USING CODATECN-REC` (assembler/date routine via copybook `CODATECN`, type/outtype `'2'`); if `ACCT-CURR-CYC-DEBIT = 0` it substitutes a literal `2525.00`; `OUT-ACCT-CURR-CYC-DEBIT` is `COMP-3` (packed decimal). **The .NET port must preserve the packed-decimal field and the COBDATFT date reformat.**
  - **`ARRYFILE` (`ARR-ARRAY-REC`, 110 bytes FB):** account id plus an `OCCURS 5 TIMES` table of `(ARR-ACCT-CURR-BAL, ARR-ACCT-CURR-CYC-DEBIT COMP-3)`. Only occurrences 1–3 are populated, with a mix of the real `ACCT-CURR-BAL` and hard-coded demo values (`1005.00`, `1525.00`, `-1025.00`, `-2500.00`); occurrences 4–5 are left at their `INITIALIZE`d zero/low-values. This is demo/sample data, not derived business state.
  - **`VBRCFILE` (`VBR-REC`, VB, 10–80 bytes):** two variable-length records are written per account — `VBRC-REC1` (length 12: account id + active status) and `VBRC-REC2` (length 39: account id + current balance + credit limit + reissue YYYY). Record length is driven by `WS-RECD-LEN` (12 then 39) under `RECORD IS VARYING`.

---

## Datasets Summary
| Dataset | Type | Read/Write | DD(s) | Corresponds to |
|---------|------|-----------|-------|----------------|
| `AWS.M2.CARDDEMO.LOADLIB` | PDS load library | read | `STEPLIB` | program load module |
| `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | VSAM KSDS (300-byte, 11-digit key) | read (sequential, INPUT) | `ACCTFILE` | `ACCOUNT` |
| `AWS.M2.CARDDEMO.ACCTDATA.PSCOMP` | sequential, FB/107 | delete then write | `DD01` (PREDEL), `OUTFILE` (STEP05) | flat "compacted" account extract (no relational table) |
| `AWS.M2.CARDDEMO.ACCTDATA.ARRYPS` | sequential, FB/110 | delete then write | `DD02` (PREDEL), `ARRYFILE` (STEP05) | flat "array" account extract (no relational table) |
| `AWS.M2.CARDDEMO.ACCTDATA.VBPS` | sequential, VB/84 | delete then write | `DD03` (PREDEL), `VBRCFILE` (STEP05) | flat variable-length account extract (no relational table) |

## PARM / COND / GDG Summary
- **PARM:** none on either step.
- **COND/RC gating:** none — neither EXEC has a `COND` parameter and the JOB card has no `COND`. `STEP05` runs irrespective of `PREDEL`'s return code. The only failure handling is inside `CBACT01C` (abend on I/O error).
- **GDG usage:** none. All datasets are fixed-name; no generation references.

## IDCAMS / SORT control statements
- **None.** This job contains no IDCAMS step and no SORT step, so there are no `DELETE`/`DEFINE`/`REPRO` control statements and no `SORT FIELDS` statements. Dataset deletion is performed via the JCL disposition idiom (`IEFBR14` + `DISP=(MOD,DELETE,DELETE)`), not via IDCAMS `DELETE`.

## Programs / Utilities Invoked
- `IEFBR14` (system utility, no-op — used purely for dataset deletion via disposition) — step `PREDEL`.
- `CBACT01C` (application batch COBOL — account read / multi-format extract writer; itself `CALL`s `COBDATFT` for date formatting and `CEE3ABD` on error) — step `STEP05`.

**2 EXEC steps total.**

## Step-Runner Notes (for .NET JobControl)
- **Two steps.** Step 1 (`PREDEL`) is a delete-if-exists of the three output extract files; in .NET implement as an idempotent "delete output files if present" (the `IEFBR14`+`DISP=(MOD,DELETE,DELETE)` idiom). Step 2 runs the `CBACT01C` equivalent.
- **No COND/RC gating, no GDG, no IDCAMS, no SORT** — straightforward sequential pipeline.
- **Account master is INPUT-only** — the runner must not modify the `ACCOUNT` table; it only reads it sequentially (in account-id key order).
- **Three distinct output record formats must be reproduced exactly:**
  - `PSCOMP`: FB LRECL 107, including a **`COMP-3` packed-decimal** `OUT-ACCT-CURR-CYC-DEBIT` field (default `2525.00` when source debit is zero) and a reissue date reformatted via the `COBDATFT`/`CODATECN` routine (type `'2'`).
  - `ARRYPS`: FB LRECL 110, an `OCCURS 5` table (only first 3 entries populated with the documented mix of real and literal demo values; entries 4–5 zero/low-value).
  - `VBPS`: **VB** LRECL 84, two variable-length record shapes per account (12-byte `VBRC-REC1`, then 39-byte `VBRC-REC2`), length driven by `WS-RECD-LEN`.
- These three outputs are **flat extract/scratch files**, not relational tables — model them as sequential file outputs, not as inserts into `ACCOUNT` or any other table.
- On unexpected I/O status the legacy program abends (`CEE3ABD`, code 999); the .NET runner should surface a hard failure (non-zero RC) rather than continue.
- Output datasets are `DISP=(NEW,CATLG,DELETE)`: create fresh, keep on success, discard on failure — and because `PREDEL` deletes them first, the job is fully re-runnable.
