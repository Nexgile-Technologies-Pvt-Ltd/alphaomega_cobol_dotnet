# DB2 → SQLite Schema Spec (optional CardDemo modules)

Consolidated translation of the **optional** DB2 DDL shipped with CardDemo into a single
SQLite schema. These tables are *already* relational in the mainframe source (they are real
DB2 tables, not VSAM files), so the port keeps the exact column names, nullability, and
primary keys; only the **physical types** and **storage clauses** are translated for SQLite.

## Scope / sources

| Source file | Object defined | Role |
| --- | --- | --- |
| `app/app-transaction-type-db2/ddl/TRNTYPE.ddl` | `CARDDEMO.TRANSACTION_TYPE` (table) | Transaction-type reference data |
| `app/app-transaction-type-db2/ddl/TRNTYCAT.ddl` | `CARDDEMO.TRANSACTION_TYPE_CATEGORY` (table) | Transaction-type / category reference data |
| `app/app-transaction-type-db2/ddl/XTRNTYPE.ddl` | `CARDDEMO.XTRAN_TYPE` (unique index) | Unique index on `TRANSACTION_TYPE` |
| `app/app-transaction-type-db2/ddl/XTRNTYCAT.ddl` | `CARDDEMO.X_TRAN_TYPE_CATG` (unique index) | Unique index on `TRANSACTION_TYPE_CATEGORY` |
| `app/app-transaction-type-db2/ctl/DB2CREAT.ctl` | DATABASE / TABLESPACE / both tables + indexes + FK + GRANTs | Full create-control deck (authoritative consolidation) |
| `app/app-authorization-ims-db2-mq/ddl/AUTHFRDS.ddl` | `CARDDEMO.AUTHFRDS` (table) | Authorization / fraud detail |
| `app/app-authorization-ims-db2-mq/ddl/XAUTHFRD.ddl` | `CARDDEMO.XAUTHFRD` (unique index) | Unique index / alt access path on `AUTHFRDS` |

The DB2 schema/qualifier `CARDDEMO.` and all DB2 physical clauses
(`STOGROUP`, `BUFFERPOOL BP0`, `CCSID EBCDIC`, `TABLESPACE`, `SEGSIZE`, `LOCKSIZE`,
`ERASE NO`, `CLOSE NO`, `COPY YES`, `GRANT ... TO PUBLIC`) are **dropped** — they have no
SQLite analogue and do not affect data semantics. Table names are kept unqualified.

## DB2 → SQLite type-translation rules (applied below)

| DB2 type | SQLite type | Notes |
| --- | --- | --- |
| `CHAR(n)` | `TEXT` | Fixed-width on the host; the `(n)` is kept only as a comment. App layer is responsible for any blank-padding/length policy. |
| `VARCHAR(n)` | `TEXT` | Variable-width; `(n)` kept as a comment. |
| `DECIMAL(p,s)` | `NUMERIC` | Mapped to C# `decimal`. `DECIMAL(11)` / `DECIMAL(9)` = scale 0 (integral) but stay `NUMERIC` to preserve full precision. |
| `SMALLINT` / `INTEGER` | `INTEGER` | — |
| `TIMESTAMP` | `TEXT` | Stored as the DB2 timestamp string `YYYY-MM-DD-HH.MM.SS.NNNNNN` (CardDemo's `DB2-FORMAT-TS`). |
| `DATE` | `TEXT` | Stored as `YYYY-MM-DD`. |

`NOT NULL` is preserved verbatim. Columns without `NOT NULL` in the DDL are nullable.
SQLite has no native `BOOLEAN`/`DATE`/`TIMESTAMP` affinity, so date/time values live in
`TEXT` columns and the application formats/parses them.

---

## Table 1 — `TRANSACTION_TYPE`

Source: `TRNTYPE.ddl` (and `DB2CREAT.ctl` lines 35-40). Backs C# entity **`TransactionType`**.

```sql
CREATE TABLE IF NOT EXISTS TRANSACTION_TYPE (
    TR_TYPE        TEXT NOT NULL,   -- DB2 CHAR(2)
    TR_DESCRIPTION TEXT NOT NULL,   -- DB2 VARCHAR(50)
    PRIMARY KEY (TR_TYPE)
);

-- XTRNTYPE.ddl: CREATE UNIQUE INDEX CARDDEMO.XTRAN_TYPE ON TRANSACTION_TYPE (TR_TYPE ASC)
-- Index is redundant with the PRIMARY KEY (SQLite auto-indexes the PK), so it is
-- represented by the PK above. Optional explicit form (harmless duplicate):
-- CREATE UNIQUE INDEX IF NOT EXISTS XTRAN_TYPE ON TRANSACTION_TYPE (TR_TYPE ASC);
```

- **Primary key:** `TR_TYPE`.
- **Indexes:** `XTRAN_TYPE` UNIQUE on `(TR_TYPE ASC)` — same columns as the PK ⇒ satisfied by the PK index.
- **Foreign keys:** none.
- **Referenced by:** `TRANSACTION_TYPE_CATEGORY.TRC_TYPE_CODE` (see FK note on Table 2).

## Table 2 — `TRANSACTION_TYPE_CATEGORY`

Source: `TRNTYCAT.ddl` (and `DB2CREAT.ctl` lines 75-99). Backs C# entity **`TransactionTypeCategory`**.

```sql
CREATE TABLE IF NOT EXISTS TRANSACTION_TYPE_CATEGORY (
    TRC_TYPE_CODE     TEXT NOT NULL,   -- DB2 CHAR(2)
    TRC_TYPE_CATEGORY TEXT NOT NULL,   -- DB2 CHAR(4)
    TRC_CAT_DATA      TEXT NOT NULL,   -- DB2 VARCHAR(50)
    PRIMARY KEY (TRC_TYPE_CODE, TRC_TYPE_CATEGORY),
    -- FK note (see below) — enforced only when PRAGMA foreign_keys=ON:
    FOREIGN KEY (TRC_TYPE_CODE) REFERENCES TRANSACTION_TYPE (TR_TYPE)
);

-- XTRNTYCAT.ddl: CREATE UNIQUE INDEX CARDDEMO.X_TRAN_TYPE_CATG
--   ON TRANSACTION_TYPE_CATEGORY (TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC)
-- Same columns/order as the composite PK ⇒ satisfied by the PK index.
-- Optional explicit form (harmless duplicate):
-- CREATE UNIQUE INDEX IF NOT EXISTS X_TRAN_TYPE_CATG
--     ON TRANSACTION_TYPE_CATEGORY (TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC);
```

- **Primary key:** composite `(TRC_TYPE_CODE, TRC_TYPE_CATEGORY)`.
- **Indexes:** `X_TRAN_TYPE_CATG` UNIQUE on `(TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC)` — same columns/order as the PK ⇒ satisfied by the PK index.
- **Foreign keys (NOTE):** `TRC_TYPE_CODE → TRANSACTION_TYPE(TR_TYPE)`, originally declared
  `ON DELETE RESTRICT` (inline in `TRNTYCAT.ddl`; added by `ALTER TABLE` in `DB2CREAT.ctl`
  lines 96-99). SQLite's default for an undecorated FK reference is **RESTRICT/NO ACTION**,
  which is the equivalent behavior — a parent `TRANSACTION_TYPE` row cannot be deleted while a
  child category row references it. SQLite enforces FKs **only when `PRAGMA foreign_keys = ON`**
  is set per-connection (off by default). EF Core's SQLite provider enables it automatically.
  The original DB2 declares the FK with the named constraint `TRC_TYPE_CODE`.

## Table 3 — `AUTHFRDS`

Source: `AUTHFRDS.ddl`. Backs C# entity **`AuthFraud`** (authorization/fraud record).

```sql
CREATE TABLE IF NOT EXISTS AUTHFRDS (
    CARD_NUM               TEXT    NOT NULL,  -- DB2 CHAR(16)
    AUTH_TS                TEXT    NOT NULL,  -- DB2 TIMESTAMP
    AUTH_TYPE              TEXT,              -- DB2 CHAR(4)
    CARD_EXPIRY_DATE       TEXT,              -- DB2 CHAR(4)
    MESSAGE_TYPE           TEXT,              -- DB2 CHAR(6)
    MESSAGE_SOURCE         TEXT,              -- DB2 CHAR(6)
    AUTH_ID_CODE           TEXT,              -- DB2 CHAR(6)
    AUTH_RESP_CODE         TEXT,              -- DB2 CHAR(2)
    AUTH_RESP_REASON       TEXT,              -- DB2 CHAR(4)
    PROCESSING_CODE        TEXT,              -- DB2 CHAR(6)
    TRANSACTION_AMT        NUMERIC,           -- DB2 DECIMAL(12,2)
    APPROVED_AMT           NUMERIC,           -- DB2 DECIMAL(12,2)
    MERCHANT_CATAGORY_CODE TEXT,              -- DB2 CHAR(4)   [sic: 'CATAGORY' spelling from DDL]
    ACQR_COUNTRY_CODE      TEXT,              -- DB2 CHAR(3)
    POS_ENTRY_MODE         INTEGER,           -- DB2 SMALLINT
    MERCHANT_ID            TEXT,              -- DB2 CHAR(15)
    MERCHANT_NAME          TEXT,              -- DB2 VARCHAR(22)
    MERCHANT_CITY          TEXT,              -- DB2 CHAR(13)
    MERCHANT_STATE         TEXT,              -- DB2 CHAR(02)
    MERCHANT_ZIP           TEXT,              -- DB2 CHAR(09)
    TRANSACTION_ID         TEXT,              -- DB2 CHAR(15)
    MATCH_STATUS           TEXT,              -- DB2 CHAR(1)
    AUTH_FRAUD             TEXT,              -- DB2 CHAR(1)
    FRAUD_RPT_DATE         TEXT,              -- DB2 DATE
    ACCT_ID                NUMERIC,           -- DB2 DECIMAL(11)
    CUST_ID                NUMERIC,           -- DB2 DECIMAL(9)
    PRIMARY KEY (CARD_NUM, AUTH_TS)
);

-- XAUTHFRD.ddl: CREATE UNIQUE INDEX CARDDEMO.XAUTHFRD
--   ON AUTHFRDS (CARD_NUM ASC, AUTH_TS DESC)
-- Same column SET as the PK but AUTH_TS is DESC (alternate access path: latest auth per card
-- first). The PK alone does NOT cover this ordering, so create it explicitly:
CREATE UNIQUE INDEX IF NOT EXISTS XAUTHFRD ON AUTHFRDS (CARD_NUM ASC, AUTH_TS DESC);
```

- **Primary key:** composite `(CARD_NUM, AUTH_TS)`.
- **Indexes:** `XAUTHFRD` UNIQUE on `(CARD_NUM ASC, AUTH_TS DESC)`. Unlike the other two tables,
  this index is **not** redundant with the PK — it adds the `AUTH_TS DESC` ordering used to fetch
  a card's most-recent authorization first, so it is materialized as a real SQLite index.
  (DB2 `COPY YES` is a backup/recovery attribute and is dropped.)
- **Foreign keys:** none declared in the DDL. `ACCT_ID` / `CUST_ID` are logical references to the
  base-app `ACCOUNT` / `CUSTOMER` data but are intentionally **not** declared as DB2 FKs (the
  authorization module is a separate IMS/DB2/MQ subsystem); they are left unconstrained here too.
- **Faithful-port note:** the column name `MERCHANT_CATAGORY_CODE` keeps the original misspelling
  from the DDL — do not "correct" it.

---

## DB2 table → C# entity mapping

| DB2 table (source) | SQLite table | C# entity (CardDemo.Domain) | Notes |
| --- | --- | --- | --- |
| `CARDDEMO.TRANSACTION_TYPE` (TRNTYPE.ddl) | `TRANSACTION_TYPE` | **`TransactionType`** | PK `TR_TYPE`. Reference data; seeded by the `CREADB21` job (7 type codes). Distinct from the base-app VSAM `TRAN_TYPE` (CVTRA03Y) entity — same shape, different module/source. |
| `CARDDEMO.TRANSACTION_TYPE_CATEGORY` (TRNTYCAT.ddl) | `TRANSACTION_TYPE_CATEGORY` | **`TransactionTypeCategory`** | Composite PK `(TRC_TYPE_CODE, TRC_TYPE_CATEGORY)`; FK to `TransactionType`. Seeded by `CREADB21` (`LDTCCAT`). |
| `CARDDEMO.AUTHFRDS` (AUTHFRDS.ddl) | `AUTHFRDS` | **`AuthFraud`** | Composite PK `(CARD_NUM, AUTH_TS)`; from the authorization IMS/DB2/MQ module. |

These three tables live in the **same** SQLite database / EF Core `DbContext` as the base-app
tables (per ARCHITECTURE.md: `CardDemo.Db2` maps optional DB2 modules onto the shared context as
extra tables). They are already mirrored in `_design/schema.sql` (lines 158-209); this spec is the
authoritative per-table breakdown for the optional DB2 modules.

## Consolidated `PRAGMA` / connection notes

- Enable `PRAGMA foreign_keys = ON;` per connection so the `TRANSACTION_TYPE_CATEGORY → TRANSACTION_TYPE`
  RESTRICT relationship is enforced (EF Core SQLite does this automatically).
- DB2 `COMMIT` statements in `DB2CREAT.ctl` are transaction boundaries of the create deck and have
  no schema-shape meaning; ignored for SQLite.
- DB2 object-creation order (DATABASE → TABLESPACE → TABLE → INDEX → FK via ALTER) collapses to a
  single set of `CREATE TABLE` + `CREATE INDEX` statements; `TRANSACTION_TYPE` must be created
  before `TRANSACTION_TYPE_CATEGORY` so the FK target exists.
