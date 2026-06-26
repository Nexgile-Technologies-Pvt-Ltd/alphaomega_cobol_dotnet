# JOB SPEC: TRANIDX

**Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/TRANIDX.jcl`
**Base record copybook:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cpy/CVTRA05Y.cpy` (TRAN-RECORD, RECLN = 350)

## Overall Purpose

**File setup — create (and populate) an Alternate Index over the Transaction Master VSAM KSDS, keyed on the processed timestamp.**

The JOB-card description is *"Define AIX on Transaction Master"*. This job does **not** post, report, or back up data. It is a pure VSAM-catalog / file-setup job that adds a secondary access path to an already-existing, already-loaded base cluster (`AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS`). In order it:

1. **STEP20** — `DEFINE ALTERNATEINDEX`: defines the AIX object (`...TRANSACT.VSAM.AIX`) related to the base KSDS, with a non-unique key on the *processed timestamp* field, marked `UPGRADE` (kept in sync automatically by VSAM on base-cluster updates).
2. **STEP25** — `DEFINE PATH`: defines a PATH (`...TRANSACT.VSAM.AIX.PATH`) that ties the AIX back to the base cluster so applications/CICS can open the alternate access path.
3. **STEP30** — `BLDINDEX`: builds (populates) the AIX from the current contents of the base KSDS.

This job is a standalone subset of `TRANFILE.jcl` — specifically the AIX-creation steps (STEP20 / STEP25 / STEP30), which carry the same step names there. It is typically run after the base Transaction Master cluster has been defined and loaded (e.g. by `TRANFILE` STEP10/STEP15) when the AIX needs to be (re)created on its own.

Every step invokes the VSAM access-method-services utility **IDCAMS**. No COBOL `CB*` program and no `SORT` are involved.

## Environment / Setup

- JOB card: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`.
- No JOBLIB / JCLLIB; no PROC references; all three steps run inline IDCAMS with inline `SYSIN`.
- Header is the standard Apache-2.0 Amazon license banner (lines 3–18); no executable content.

## Datasets / Tables Involved

| Dataset (DSN) | Type | Read/Write | Maps to |
|---|---|---|---|
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | VSAM KSDS, 16-byte primary key at offset 0, RECSIZE 350 | **Read** (BLDINDEX `INDATASET`; RELATE target of the AIX) | **Transaction Master** — relational table `TRANSACTION` (a.k.a. TRANSACT). Primary key = 16-char `TRAN-ID`. Must already exist and be populated. |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX` | VSAM Alternate Index over the KSDS | **Define + Write/Build** | Alternate (secondary) access path on the TRANSACTION table, keyed on `TRAN-PROC-TS` (processed timestamp). Non-unique. |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.DATA` | DATA component of the AIX | Define | Physical DATA component of the AIX |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.INDEX` | INDEX component of the AIX | Define | Physical INDEX component of the AIX |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.PATH` | VSAM PATH | Define | Logical PATH name applications/CICS open to read TRANSACTION rows via the processed-timestamp order (CICS file is `CXACAIX` in `TRANFILE.jcl`). |

**No GDG usage** in this job. No sequential/flat files. All objects are VSAM catalog entities on volume `AWSHJ1`.

### Alternate-index key derivation (verified against CVTRA05Y.cpy)

`KEYS(26 304)` = key length **26**, key offset **304** (0-based) → starts at byte **305** (1-based) of the 350-byte `TRAN-RECORD`. Field offsets within `TRAN-RECORD`:

| Field | PIC | 1-based bytes |
|---|---|---|
| TRAN-ID | X(16) | 1–16 |
| TRAN-TYPE-CD | X(02) | 17–18 |
| TRAN-CAT-CD | 9(04) | 19–22 |
| TRAN-SOURCE | X(10) | 23–32 |
| TRAN-DESC | X(100) | 33–132 |
| TRAN-AMT | S9(09)V99 (11 disp digits) | 133–143 |
| TRAN-MERCHANT-ID | 9(09) | 144–152 |
| TRAN-MERCHANT-NAME | X(50) | 153–202 |
| TRAN-MERCHANT-CITY | X(50) | 203–252 |
| TRAN-MERCHANT-ZIP | X(10) | 253–262 |
| TRAN-CARD-NUM | X(16) | 263–278 |
| TRAN-ORIG-TS | X(26) | 279–304 |
| **TRAN-PROC-TS** | **X(26)** | **305–330** |
| FILLER | X(20) | 331–350 |

→ The AIX key **`KEYS(26 304)` is exactly `TRAN-PROC-TS`**, the 26-char processed timestamp, matching the comment "CREATE ALTERNATE INDEX ON PROCESSED TIMESTAMP". `NONUNIQUEKEY` is correct because multiple transactions can share a processed timestamp.

---

## Steps (in execution order)

### STEP20 — EXEC PGM=IDCAMS (DEFINE ALTERNATEINDEX)

- **Program/utility:** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** none (runs unconditionally; first step).
- **DDs:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — inline control statements.
- **IDCAMS control statements (inline):**
  ```
  DEFINE ALTERNATEINDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX)-
  RELATE(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS)                    -
  KEYS(26 304)                                                  -
  NONUNIQUEKEY                                                  -
  UPGRADE                                                       -
  RECORDSIZE(350,350)                                           -
  VOLUMES(AWSHJ1)                                               -
  CYLINDERS(5,1))                                               -
  DATA (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.DATA))           -
  INDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.INDEX))
  ```
  Defines the alternate index object:
  - `NAME` = `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX`.
  - `RELATE` = base cluster `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` (the AIX is built over the Transaction Master).
  - `KEYS(26 304)` = alternate key length 26 at offset 304 (0-based) = `TRAN-PROC-TS`.
  - `NONUNIQUEKEY` = many base records may share the same alternate key value.
  - `UPGRADE` = the AIX belongs to the base cluster's upgrade set, so VSAM keeps it current automatically on subsequent base-cluster inserts/updates/deletes.
  - `RECORDSIZE(350,350)` = avg/max AIX record size 350.
  - `VOLUMES(AWSHJ1)`, `CYLINDERS(5,1)` = primary 5 / secondary 1 cylinders on volume AWSHJ1.
  - Separate `DATA` (`...AIX.DATA`) and `INDEX` (`...AIX.INDEX`) component names.

### STEP25 — EXEC PGM=IDCAMS (DEFINE PATH)

- **Program/utility:** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** none (no COND coded; runs after STEP20).
- **DDs:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — inline control statements.
- **IDCAMS control statements (inline):**
  ```
  DEFINE PATH                                           -
   (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.PATH)        -
    PATHENTRY(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX))
  ```
  Defines the PATH that relates the alternate index to the base cluster:
  - `NAME` = `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.PATH` — the openable name applications/CICS use to access the TRANSACTION table via the AIX.
  - `PATHENTRY` = `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX` — the alternate index this path is built on. Opening the PATH gives access to base-cluster records in alternate-key (processed-timestamp) order.

### STEP30 — EXEC PGM=IDCAMS (BLDINDEX)

- **Program/utility:** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** none.
- **DDs:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — inline control statements.
  - (No explicit work-file DDs coded; IDCAMS allocates internal sort/work space by default for BLDINDEX.)
- **IDCAMS control statements (inline):**
  ```
  BLDINDEX                                                      -
  INDATASET(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS)                 -
  OUTDATASET(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX)
  ```
  Populates the alternate index from the base cluster:
  - `INDATASET` = base KSDS `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` — source records read in full.
  - `OUTDATASET` = the AIX `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX` — the index being built.
  - Effect: reads every base record, extracts the alternate key (`TRAN-PROC-TS`), and writes the AIX entries (each alternate key → set of base primary keys). After this step the AIX is usable through the PATH.

---

## .NET JobControl Mapping Notes

- This is a **VSAM-catalog / file-setup job**; in the .NET model it maps to creating a **secondary index** over the `TRANSACTION` table store (the VSAM-KSDS abstraction), keyed on the `TRAN-PROC-TS` (processed-timestamp) column, allowing duplicates.
  - **STEP20 (DEFINE ALTERNATEINDEX)** → declare/create a non-unique secondary index named `...TRANSACT.VSAM.AIX` on the processed-timestamp field (offset 304, length 26 of the 350-byte record), in the table's upgrade set so it is maintained automatically on writes.
  - **STEP25 (DEFINE PATH)** → register a logical access path `...TRANSACT.VSAM.AIX.PATH` that exposes the `TRANSACTION` rows ordered by / looked up via the alternate key (the CICS file name on this path is `CXACAIX`).
  - **STEP30 (BLDINDEX)** → perform the initial backfill/build of that index from the current `TRANSACTION` rows. In a relational/SQLite backend, STEP20+STEP30 collapse to a single `CREATE INDEX` over the processed-timestamp column (non-unique); STEP25 is the registration of the named read path (no data movement).
- **Ordering/dependencies:** STEP30 requires STEP20 to have created the AIX; STEP25/STEP30 both require the base KSDS to exist and be loaded. No COND gating is coded, so a failure in an earlier step would still attempt later steps on a real system — the .NET runner should treat a STEP20 failure as fatal to STEP30 (the AIX would not exist to build).
- **No GDG**, **no SORT** card, **no sequential files**, **no COBOL program**. All three steps are IDCAMS catalog operations.
