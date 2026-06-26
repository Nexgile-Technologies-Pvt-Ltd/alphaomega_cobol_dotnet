-- CardDemo .NET 10 — Relational Re-Architecture: SQLite schema (DDL)
-- Authoritative source: New_Dotnet_Code/_design/ARCHITECTURE.md
--
-- Type mapping (per ARCHITECTURE.md "COBOL -> C#/SQL type mapping"):
--   9(n) unsigned            -> INTEGER  (counts, codes, ids)
--   S9(p)V(s) money          -> NUMERIC  (text-exact decimal; truncate-toward-zero, never float)
--   X(n)                     -> TEXT     (exact n chars incl. trailing spaces)
--   dates X(8)/X(10)         -> TEXT     (literal CCYY-MM-DD / CCYYMMDD form)
--   FILLER / COMP-3 / COMP   -> (no column; serializer-only concern)
--
-- 14 tables total: 11 base-app + TRANSACTION_TYPE + TRANSACTION_TYPE_CATEGORY + AUTHFRDS.
-- Primary keys (incl. composite) declared; alternate keys exposed via CREATE INDEX.

-- =====================================================================
-- Base-app relational schema (11 tables)
-- =====================================================================

-- ACCOUNT (CVACT01Y/300) PK acct_id 9(11)
CREATE TABLE IF NOT EXISTS ACCOUNT (
    acct_id           INTEGER NOT NULL,  -- ACCT-ID            9(11)
    active_status     TEXT    NOT NULL,  -- ACCT-ACTIVE-STATUS X(1)
    curr_bal          NUMERIC NOT NULL,  -- ACCT-CURR-BAL      S9(10)V99
    credit_limit      NUMERIC NOT NULL,  -- ACCT-CREDIT-LIMIT  S9(10)V99
    cash_credit_limit NUMERIC NOT NULL,  -- ACCT-CASH-CREDIT-LIMIT S9(10)V99
    open_date         TEXT    NOT NULL,  -- ACCT-OPEN-DATE     X(10)
    expiration_date   TEXT    NOT NULL,  -- ACCT-EXPIRAION-DATE X(10) (COBOL field name EXPIRAION)
    reissue_date      TEXT    NOT NULL,  -- ACCT-REISSUE-DATE  X(10)
    curr_cyc_credit   NUMERIC NOT NULL,  -- ACCT-CURR-CYC-CREDIT S9(10)V99
    curr_cyc_debit    NUMERIC NOT NULL,  -- ACCT-CURR-CYC-DEBIT  S9(10)V99
    addr_zip          TEXT    NOT NULL,  -- ACCT-ADDR-ZIP      X(10)
    group_id          TEXT    NOT NULL,  -- ACCT-GROUP-ID      X(10)
    PRIMARY KEY (acct_id)
);

-- CARD (CVACT02Y/150) PK card_num X16; alt idx acct_id 9(11)
CREATE TABLE IF NOT EXISTS CARD (
    card_num        TEXT    NOT NULL,  -- CARD-NUM           X(16)
    acct_id         INTEGER NOT NULL,  -- CARD-ACCT-ID       9(11)
    cvv_cd          INTEGER NOT NULL,  -- CARD-CVV-CD        9(3)
    embossed_name   TEXT    NOT NULL,  -- CARD-EMBOSSED-NAME X(50)
    expiration_date TEXT    NOT NULL,  -- CARD-EXPIRAION-DATE X(10)
    active_status   TEXT    NOT NULL,  -- CARD-ACTIVE-STATUS X(1)
    PRIMARY KEY (card_num)
);
CREATE INDEX IF NOT EXISTS IX_CARD_acct_id ON CARD (acct_id);

-- CARD_XREF (CVACT03Y/50) PK xref_card_num X16; alt idx acct_id 9(11)
CREATE TABLE IF NOT EXISTS CARD_XREF (
    xref_card_num TEXT    NOT NULL,  -- XREF-CARD-NUM X(16)
    cust_id       INTEGER NOT NULL,  -- XREF-CUST-ID  9(9)
    acct_id       INTEGER NOT NULL,  -- XREF-ACCT-ID  9(11)
    PRIMARY KEY (xref_card_num)
);
CREATE INDEX IF NOT EXISTS IX_CARD_XREF_acct_id ON CARD_XREF (acct_id);

-- CUSTOMER (CVCUS01Y/500) PK cust_id 9(9)
CREATE TABLE IF NOT EXISTS CUSTOMER (
    cust_id             INTEGER NOT NULL,  -- CUST-ID                9(9)
    first_name          TEXT    NOT NULL,  -- CUST-FIRST-NAME        X(25)
    middle_name         TEXT    NOT NULL,  -- CUST-MIDDLE-NAME       X(25)
    last_name           TEXT    NOT NULL,  -- CUST-LAST-NAME         X(25)
    addr_line_1         TEXT    NOT NULL,  -- CUST-ADDR-LINE-1       X(50)
    addr_line_2         TEXT    NOT NULL,  -- CUST-ADDR-LINE-2       X(50)
    addr_line_3         TEXT    NOT NULL,  -- CUST-ADDR-LINE-3       X(50)
    addr_state_cd       TEXT    NOT NULL,  -- CUST-ADDR-STATE-CD     X(2)
    addr_country_cd     TEXT    NOT NULL,  -- CUST-ADDR-COUNTRY-CD   X(3)
    addr_zip            TEXT    NOT NULL,  -- CUST-ADDR-ZIP          X(10)
    phone_num_1         TEXT    NOT NULL,  -- CUST-PHONE-NUM-1       X(15)
    phone_num_2         TEXT    NOT NULL,  -- CUST-PHONE-NUM-2       X(15)
    ssn                 INTEGER NOT NULL,  -- CUST-SSN               9(9)
    govt_issued_id      TEXT    NOT NULL,  -- CUST-GOVT-ISSUED-ID    X(20)
    dob_yyyy_mm_dd      TEXT    NOT NULL,  -- CUST-DOB-YYYY-MM-DD    X(10)
    eft_account_id      TEXT    NOT NULL,  -- CUST-EFT-ACCOUNT-ID    X(10)
    pri_card_holder_ind TEXT    NOT NULL,  -- CUST-PRI-CARD-HOLDER-IND X(1)
    fico_credit_score   INTEGER NOT NULL,  -- CUST-FICO-CREDIT-SCORE 9(3)
    PRIMARY KEY (cust_id)
);

-- TRANSACTION (CVTRA05Y/350) PK tran_id X16
CREATE TABLE IF NOT EXISTS "TRANSACTION" (
    tran_id       TEXT    NOT NULL,  -- TRAN-ID            X(16)
    type_cd       TEXT    NOT NULL,  -- TRAN-TYPE-CD       X(2)
    cat_cd        INTEGER NOT NULL,  -- TRAN-CAT-CD        9(4)
    source        TEXT    NOT NULL,  -- TRAN-SOURCE        X(10)
    "desc"        TEXT    NOT NULL,  -- TRAN-DESC          X(100)
    amt           NUMERIC NOT NULL,  -- TRAN-AMT           S9(9)V99
    merchant_id   INTEGER NOT NULL,  -- TRAN-MERCHANT-ID   9(9)
    merchant_name TEXT    NOT NULL,  -- TRAN-MERCHANT-NAME X(50)
    merchant_city TEXT    NOT NULL,  -- TRAN-MERCHANT-CITY X(50)
    merchant_zip  TEXT    NOT NULL,  -- TRAN-MERCHANT-ZIP  X(10)
    card_num      TEXT    NOT NULL,  -- TRAN-CARD-NUM      X(16)
    orig_ts       TEXT    NOT NULL,  -- TRAN-ORIG-TS       X(26)
    proc_ts       TEXT    NOT NULL,  -- TRAN-PROC-TS       X(26)
    PRIMARY KEY (tran_id)
);

-- DAILY_TRANSACTION (CVTRA06Y/350) seq input; same columns as TRANSACTION (DALYTRAN-*); PK tran_id
CREATE TABLE IF NOT EXISTS DAILY_TRANSACTION (
    tran_id       TEXT    NOT NULL,  -- DALYTRAN-ID            X(16)
    type_cd       TEXT    NOT NULL,  -- DALYTRAN-TYPE-CD       X(2)
    cat_cd        INTEGER NOT NULL,  -- DALYTRAN-CAT-CD        9(4)
    source        TEXT    NOT NULL,  -- DALYTRAN-SOURCE        X(10)
    "desc"        TEXT    NOT NULL,  -- DALYTRAN-DESC          X(100)
    amt           NUMERIC NOT NULL,  -- DALYTRAN-AMT           S9(9)V99
    merchant_id   INTEGER NOT NULL,  -- DALYTRAN-MERCHANT-ID   9(9)
    merchant_name TEXT    NOT NULL,  -- DALYTRAN-MERCHANT-NAME X(50)
    merchant_city TEXT    NOT NULL,  -- DALYTRAN-MERCHANT-CITY X(50)
    merchant_zip  TEXT    NOT NULL,  -- DALYTRAN-MERCHANT-ZIP  X(10)
    card_num      TEXT    NOT NULL,  -- DALYTRAN-CARD-NUM      X(16)
    orig_ts       TEXT    NOT NULL,  -- DALYTRAN-ORIG-TS       X(26)
    proc_ts       TEXT    NOT NULL,  -- DALYTRAN-PROC-TS       X(26)
    PRIMARY KEY (tran_id)
);

-- TRAN_CAT_BAL (CVTRA01Y/50) composite PK (acct_id 9(11), type_cd X2, cat_cd 9(4))  [TRAN-CAT-KEY = 17 bytes]
CREATE TABLE IF NOT EXISTS TRAN_CAT_BAL (
    acct_id      INTEGER NOT NULL,  -- TRANCAT-ACCT-ID    9(11)
    type_cd      TEXT    NOT NULL,  -- TRANCAT-TYPE-CD    X(2)
    cat_cd       INTEGER NOT NULL,  -- TRANCAT-CD         9(4)
    tran_cat_bal NUMERIC NOT NULL,  -- TRAN-CAT-BAL       S9(9)V99
    PRIMARY KEY (acct_id, type_cd, cat_cd)
);

-- DISCLOSURE_GROUP (CVTRA02Y/50) composite PK (acct_group_id X10, tran_type_cd X2, tran_cat_cd 9(4))  [DIS-GROUP-KEY = 16 bytes]
CREATE TABLE IF NOT EXISTS DISCLOSURE_GROUP (
    acct_group_id TEXT    NOT NULL,  -- DIS-ACCT-GROUP-ID  X(10)
    tran_type_cd  TEXT    NOT NULL,  -- DIS-TRAN-TYPE-CD   X(2)
    tran_cat_cd   INTEGER NOT NULL,  -- DIS-TRAN-CAT-CD    9(4)
    int_rate      NUMERIC NOT NULL,  -- DIS-INT-RATE       S9(4)V99
    PRIMARY KEY (acct_group_id, tran_type_cd, tran_cat_cd)
);

-- TRAN_TYPE (CVTRA03Y/60) PK tran_type X2
CREATE TABLE IF NOT EXISTS TRAN_TYPE (
    tran_type      TEXT NOT NULL,  -- TRAN-TYPE      X(2)
    tran_type_desc TEXT NOT NULL,  -- TRAN-TYPE-DESC X(50)
    PRIMARY KEY (tran_type)
);

-- TRAN_CATEGORY (CVTRA04Y/60) composite PK (tran_type_cd X2, tran_cat_cd 9(4))
CREATE TABLE IF NOT EXISTS TRAN_CATEGORY (
    tran_type_cd       TEXT    NOT NULL,  -- TRAN-TYPE-CD       X(2)
    tran_cat_cd        INTEGER NOT NULL,  -- TRAN-CAT-CD        9(4)
    tran_cat_type_desc TEXT    NOT NULL,  -- TRAN-CAT-TYPE-DESC X(50)
    PRIMARY KEY (tran_type_cd, tran_cat_cd)
);

-- USER_SECURITY (CSUSR01Y/80) PK usr_id X8
CREATE TABLE IF NOT EXISTS USER_SECURITY (
    usr_id     TEXT NOT NULL,  -- SEC-USR-ID         X(8)
    first_name TEXT NOT NULL,  -- SEC-USR-FNAME      X(20)
    last_name  TEXT NOT NULL,  -- SEC-USR-LNAME      X(20)
    pwd        TEXT NOT NULL,  -- SEC-USR-PWD        X(8)
    usr_type   TEXT NOT NULL,  -- SEC-USR-TYPE       X(1)
    PRIMARY KEY (usr_id)
);

-- =====================================================================
-- Optional-module tables (from DB2 DDL — already relational)
-- =====================================================================

-- TRANSACTION_TYPE (TRNTYPE.ddl): TR_TYPE CHAR(2) PK, TR_DESCRIPTION VARCHAR(50)
CREATE TABLE IF NOT EXISTS TRANSACTION_TYPE (
    TR_TYPE        TEXT NOT NULL,  -- CHAR(2)
    TR_DESCRIPTION TEXT NOT NULL,  -- VARCHAR(50)
    PRIMARY KEY (TR_TYPE)
);

-- TRANSACTION_TYPE_CATEGORY (TRNTYCAT.ddl): (TRC_TYPE_CODE CHAR2, TRC_TYPE_CATEGORY CHAR4) PK, TRC_CAT_DATA VARCHAR(50)
CREATE TABLE IF NOT EXISTS TRANSACTION_TYPE_CATEGORY (
    TRC_TYPE_CODE     TEXT NOT NULL,  -- CHAR(2)
    TRC_TYPE_CATEGORY TEXT NOT NULL,  -- CHAR(4)
    TRC_CAT_DATA      TEXT NOT NULL,  -- VARCHAR(50)
    PRIMARY KEY (TRC_TYPE_CODE, TRC_TYPE_CATEGORY),
    FOREIGN KEY (TRC_TYPE_CODE) REFERENCES TRANSACTION_TYPE (TR_TYPE)
);

-- AUTHFRDS (AUTHFRDS.ddl): PK (CARD_NUM CHAR16, AUTH_TS TIMESTAMP) + cols incl. DECIMAL(12,2) amounts
CREATE TABLE IF NOT EXISTS AUTHFRDS (
    CARD_NUM               TEXT    NOT NULL,  -- CHAR(16)
    AUTH_TS                TEXT    NOT NULL,  -- TIMESTAMP
    AUTH_TYPE              TEXT,              -- CHAR(4)
    CARD_EXPIRY_DATE       TEXT,              -- CHAR(4)
    MESSAGE_TYPE           TEXT,              -- CHAR(6)
    MESSAGE_SOURCE         TEXT,              -- CHAR(6)
    AUTH_ID_CODE           TEXT,              -- CHAR(6)
    AUTH_RESP_CODE         TEXT,              -- CHAR(2)
    AUTH_RESP_REASON       TEXT,              -- CHAR(4)
    PROCESSING_CODE        TEXT,              -- CHAR(6)
    TRANSACTION_AMT        NUMERIC,           -- DECIMAL(12,2)
    APPROVED_AMT           NUMERIC,           -- DECIMAL(12,2)
    MERCHANT_CATAGORY_CODE TEXT,              -- CHAR(4)
    ACQR_COUNTRY_CODE      TEXT,              -- CHAR(3)
    POS_ENTRY_MODE         INTEGER,           -- SMALLINT
    MERCHANT_ID            TEXT,              -- CHAR(15)
    MERCHANT_NAME          TEXT,              -- VARCHAR(22)
    MERCHANT_CITY          TEXT,              -- CHAR(13)
    MERCHANT_STATE         TEXT,              -- CHAR(02)
    MERCHANT_ZIP           TEXT,              -- CHAR(09)
    TRANSACTION_ID         TEXT,              -- CHAR(15)
    MATCH_STATUS           TEXT,              -- CHAR(1)
    AUTH_FRAUD             TEXT,              -- CHAR(1)
    FRAUD_RPT_DATE         TEXT,              -- DATE
    ACCT_ID                NUMERIC,           -- DECIMAL(11)
    CUST_ID                NUMERIC,           -- DECIMAL(9)
    PRIMARY KEY (CARD_NUM, AUTH_TS)
);
-- XAUTHFRD.ddl alt index on AUTHFRDS (alternate access path)
CREATE INDEX IF NOT EXISTS IX_AUTHFRDS_ACCT_ID ON AUTHFRDS (ACCT_ID);
