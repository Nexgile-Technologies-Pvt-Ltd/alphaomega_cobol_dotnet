# 7. Optional modules and integrations

[← Domain data model](06-Domain-Data-Model.md) · [Home](Home.md) · [Security and controls →](08-Security-and-Controls.md)

## Module boundaries

This page specifies the three optional source trees and their integration with the core CardDemo product:

| Module | Source artifacts | Runtime purpose | Core coupling |
|---|---:|---|---|
| Transaction type / Db2 | 27 | Db2-backed type/category reference data, CICS list and maintenance screens, batch maintenance and Db2-to-VSAM export | admin menu, transaction report reference files, scheduler |
| Authorization / IMS / Db2 / MQ | 40 | triggered authorization decisions, pending authorization inquiry, fraud marking, expiration and IMS migration utilities | main menu, account/customer/card cross-reference VSAM files, CICS/IMS, Db2, MQ |
| VSAM / MQ | 4 | triggered system-date and account-inquiry request/reply servers | account VSAM and CICS/MQ |

The count is **71 artifacts**, reconciled file by file in [Extension artifact coverage](#extension-artifact-coverage). Core files cited on this page are additional integration evidence and are not included in that count.

Claims follow [Documentation conventions](Documentation-Conventions.md#claim-labels). Executable COBOL and embedded SQL/DLI/MQ take precedence over extension README prose. A README conflict is recorded as a contradiction; it is never silently promoted to product behavior.

### Installation behavior

- **Observed — code:** the authorization menu is optional at runtime. Main-menu option 11 names <code>COPAUS0C</code>, and the dispatcher uses <code>INQUIRE PROGRAM</code>; an absent program produces a not-installed response rather than a transfer failure ([COMEN02Y.cpy lines 21 and 86–90](../Old_Cobol_Code/app/cpy/COMEN02Y.cpy#L21-L90), [COMEN01C.cbl lines 146–168](../Old_Cobol_Code/app/cbl/COMEN01C.cbl#L146-L168)).
- **Observed — code:** transaction-type admin options 5 and 6 directly transfer to <code>COTRTLIC</code> and <code>COTRTUPC</code>. There is no equivalent availability inquiry, so an absent program can produce <code>PGMIDERR</code> ([COADM02Y.cpy lines 22 and 46–53](../Old_Cobol_Code/app/cpy/COADM02Y.cpy#L22-L53), [COADM01C.cbl lines 121–149](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L121-L149)).
- **Observed — code:** the VSAM/MQ services have no core menu route. Their CICS transactions expect MQ trigger-monitor data and are servers, not interactive inquiry clients.

## Integration data contracts

This section is the parity boundary for message and optional-store data. Physical core VSAM layouts remain in [File and Record Layouts](Appendix-File-and-Record-Layouts.md#core-indexed-files).

### Transaction reference entities

**DATA-OPT-001 — transaction type.** The authoritative optional Db2 table is <code>CARDDEMO.TRANSACTION_TYPE</code>:

| Column | Db2 type | Null | Key |
|---|---|---:|---|
| <code>TR_TYPE</code> | <code>CHAR(2)</code> | no | primary key |
| <code>TR_DESCRIPTION</code> | <code>VARCHAR(50)</code> | no | — |

Evidence: [TRNTYPE.ddl lines 1–4](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/TRNTYPE.ddl#L1-L4) and [DCLTRTYP.dcl lines 28–46](../Old_Cobol_Code/app/app-transaction-type-db2/dcl/DCLTRTYP.dcl#L28-L46).

**DATA-OPT-002 — transaction category.** <code>CARDDEMO.TRANSACTION_TYPE_CATEGORY</code> has <code>TRC_TYPE_CODE CHAR(2)</code>, <code>TRC_TYPE_CATEGORY CHAR(4)</code>, and <code>TRC_CAT_DATA VARCHAR(50)</code>. Its primary key is <code>(TRC_TYPE_CODE, TRC_TYPE_CATEGORY)</code>; its <code>TRC_TYPE_CODE</code> foreign key references <code>CARDDEMO.TRANSACTION_TYPE (TR_TYPE)</code> with <code>ON DELETE RESTRICT</code> ([TRNTYCAT.ddl lines 1–7](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/TRNTYCAT.ddl#L1-L7), [DCLTRCAT.dcl lines 28–51](../Old_Cobol_Code/app/app-transaction-type-db2/dcl/DCLTRCAT.dcl#L28-L51)).

The optional seed scripts define seven type rows and eighteen category rows. Type <code>06</code> is spelled <code>REVERAL</code> in the Db2 seed, while core fixtures use “Reversal”; this is supplied-data inconsistency, not an additional code value ([DB2LTTYP.ctl lines 15–24](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2LTTYP.ctl#L15-L24), [DB2LTCAT.ctl lines 15–39](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2LTCAT.ctl#L15-L39)).

| Type/category | Seed description |
|---|---|
| <code>01</code> | PURCHASE |
| <code>01/0001</code> | regular sales draft |
| <code>01/0002</code> | regular cash advance |
| <code>01/0003</code> | convenience check debit |
| <code>01/0004</code> | ATM cash advance |
| <code>01/0005</code> | interest amount |
| <code>02</code> | PAYMENT |
| <code>02/0001</code> | cash payment |
| <code>02/0002</code> | electronic payment |
| <code>02/0003</code> | check payment |
| <code>03</code> | CREDIT |
| <code>03/0001</code> | credit to account |
| <code>03/0002</code> | credit to purchase balance |
| <code>03/0003</code> | credit to cash balance |
| <code>04</code> | AUTHORIZATION |
| <code>04/0001</code> | zero dollar authorization |
| <code>04/0002</code> | online purchase authorization |
| <code>04/0003</code> | travel booking authorization |
| <code>05</code> | REFUND |
| <code>05/0001</code> | refund credit |
| <code>06</code> | REVERAL |
| <code>06/0001</code> | fraud reversal |
| <code>06/0002</code> | non fraud reversal |
| <code>07</code> | ADJUSTMENT |
| <code>07/0001</code> | sales draft credit adjustment |

The core report-facing representation remains fixed 60 bytes:

- type: 2-byte code + 50-byte description + 8 filler ([CVTRA03Y.cpy lines 4–7](../Old_Cobol_Code/app/cpy/CVTRA03Y.cpy#L4-L7));
- category: 2-byte type + 4-byte category + 50-byte description + 4 filler ([CVTRA04Y.cpy lines 4–9](../Old_Cobol_Code/app/cpy/CVTRA04Y.cpy#L4-L9)).

### Authorization MQ request

**DATA-OPT-010 — request wire format.** The copybook group is 153 display bytes, but the consumer treats the MQ payload as comma-delimited text. Fields occur in this exact order ([CCPAURQY.cpy lines 19–36](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAURQY.cpy#L19-L36), [COPAUA0C.cbl lines 351–379](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L351-L379)):

| # | Field | COBOL picture / maximum text width |
|---:|---|---|
| 1 | authorization date | <code>X(6)</code> |
| 2 | authorization time | <code>X(6)</code> |
| 3 | card number | <code>X(16)</code> |
| 4 | authorization type | <code>X(4)</code> |
| 5 | card expiry | <code>X(4)</code> |
| 6 | message type | <code>X(6)</code> |
| 7 | message source | <code>X(6)</code> |
| 8 | processing code | <code>9(6)</code> |
| 9 | requested amount | signed display, ten integral and two fractional digits |
| 10 | merchant category code | <code>X(4)</code> |
| 11 | acquiring country | <code>X(3)</code> |
| 12 | POS entry mode | <code>9(2)</code> |
| 13 | merchant ID | <code>X(15)</code> |
| 14 | merchant name | <code>X(22)</code> |
| 15 | merchant city | <code>X(13)</code> |
| 16 | merchant state | <code>X(2)</code> |
| 17 | merchant ZIP | <code>X(9)</code> |
| 18 | transaction ID | <code>X(15)</code> |

There is no escaping/quoting convention for commas inside text fields. The COBOL does not count tokens, reject extras, validate dates/codes, or test numeric class before applying <code>NUMVAL</code>. Those omissions are defects, not permission for the safe target to accept malformed messages.

### Authorization MQ response

**DATA-OPT-011 — response wire format.** The logical response is:

~~~text
card-number,transaction-id,authorization-id,response-code,reason-code,approved-amount,
~~~

Field definitions are in [CCPAURLY.cpy lines 19–24](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAURLY.cpy#L19-L24); construction and the trailing comma are in [COPAUA0C.cbl lines 720–731](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L720-L731). Authorization ID is the six-character request time. Approved amount uses a 14-character edited numeric value. Response code is <code>00</code> for approval and <code>05</code> for decline.

The six fields plus six commas occupy 63 bytes. The COBOL STRING pointer starts at 1 and is passed as the MQ length after STRING, so the first reply attempts to send 64 bytes, including one byte after the logical CSV. The pointer is not reset for subsequent requests; that defect is specified under [Authorization compatibility defects](#authorization-compatibility-defects).

### Pending-authorization IMS segments

**DATA-OPT-012 — summary root.** <code>PAUTSUM0</code> is a 100-byte IMS root keyed by packed account ID. Its layout is ([CIPAUSMY.cpy lines 19–31](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CIPAUSMY.cpy#L19-L31)):

- account ID <code>S9(11) COMP-3</code>;
- customer ID <code>9(9)</code>;
- one authorization-status byte;
- five occurrences of two-character account status;
- packed credit limit, cash limit, credit balance and cash balance;
- binary approved and declined counts;
- packed approved and declined totals;
- 34 filler bytes.

**DATA-OPT-013 — detail child.** <code>PAUTDTL1</code> is 200 bytes. It begins with an eight-byte packed reverse timestamp key, followed by original request date/time, card/request data, decision data, merchant data, transaction ID, match status, fraud status/date, and filler ([CIPAUDTY.cpy lines 19–54](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CIPAUDTY.cpy#L19-L54)).

Match status values named by source are <code>P</code> pending, <code>D</code> authorization declined, <code>E</code> pending expired, and <code>M</code> matched with transaction. Fraud values are <code>F</code> confirmed and <code>R</code> removed. The authorization processor initially writes <code>P</code> for an approval and <code>D</code> for a decline.

The physical IMS model is HIDAM with root <code>PAUTSUM0</code>, child <code>PAUTDTL1</code>, and a secondary index on account ID ([DBPAUTP0.dbd lines 18–37](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DBPAUTP0.dbd#L18-L37), [DBPAUTX0.dbd lines 18–31](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DBPAUTX0.dbd#L18-L31)).

The detail sort key is:

~~~text
reverse date = 99999 - current YYDDD
reverse time = 999999999 - current HHMMSSmmm
~~~

This causes newer children to be returned before older children ([COPAUA0C.cbl lines 857–875](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L857-L875)).

### Fraud Db2 row

**DATA-OPT-014 — fraud history.** <code>CARDDEMO.AUTHFRDS</code> has primary key <code>(CARD_NUM, AUTH_TS)</code> and 26 columns ([AUTHFRDS.ddl lines 1–28](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/AUTHFRDS.ddl#L1-L28), [AUTHFRDS.dcl lines 24–86](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/dcl/AUTHFRDS.dcl#L24-L86)):

| Group | Columns |
|---|---|
| Identity | card number CHAR(16), timestamp |
| Request | auth type/expiry, message type/source, processing code, requested amount |
| Decision | auth ID, response code/reason, approved amount |
| Merchant | MCC, country, POS mode, ID, VARCHAR(22) name, city/state/ZIP |
| Reconciliation | transaction ID, match status |
| Fraud | fraud flag, report date |
| Ownership | account ID DECIMAL(11), customer ID DECIMAL(9) |

The supplied index orders card ascending and timestamp descending ([XAUTHFRD.ddl lines 1–4](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/XAUTHFRD.ddl#L1-L4)).

### VSAM/MQ request and reply contracts

**DATA-OPT-020 — date inquiry.** Any input message is accepted; its body is ignored. The reply begins:

~~~text
SYSTEM DATE : MM-DD-YYYYSYSTEM TIME : HH:MM:SS
~~~

The source payload places <code>SYSTEM TIME</code> immediately after the ten-character date, with no additional delimiter. The 46 meaningful characters are padded to the 1,000-byte MQPUT length ([CODATE01.cbl lines 339–403](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L339-L403)).

**DATA-OPT-021 — account inquiry.** Input is fixed-position, not CSV:

| Bytes | Meaning |
|---|---|
| 1–4 | exact uppercase function <code>INQA</code> |
| 5–15 | eleven-digit account ID |
| 16–1000 | ignored |

Evidence: [COACCT01.cbl request layout lines 109–112](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L109-L112) and [dispatch lines 390–457](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L390-L457).

On success, the service reads the 300-byte <code>ACCTDAT</code> record and produces a contiguous labeled group:

~~~text
ACCOUNT ID : <11>
ACCOUNT STATUS : <1>
BALANCE : <signed display>
CREDIT LIMIT : <signed display>
CASH LIMIT : <signed display>
OPEN DATE : <10>
EXPR DATE : <10>
REIS DATE : <10>
CREDIT BAL : <signed display>
DEBIT BAL : <signed display>
GROUP ID : <10>
~~~

The line breaks above are documentation only. The actual response has no newlines between fields and is padded to a 1,000-byte MQPUT ([COACCT01.cbl response layout lines 130–171](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L130-L171), [processing/put lines 390–499](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L390-L499)). The base account layout is [CVACT01Y.cpy lines 4–17](../Old_Cobol_Code/app/cpy/CVACT01Y.cpy#L4-L17).

## Transaction-type Db2 module

### CTTU maintenance workflow

**FR-OPT-001 — maintain a type.** <code>COTRTUPC</code>, transaction <code>CTTU</code>, renders map <code>COTRTUP.CTRTUPA</code> and maintains one transaction type ([program constants](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L201-L224), [BMS map](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L20-L135)).

The pseudo-conversational 2,000-byte state supports:

1. initial type lookup;
2. found-record update;
3. not-found create;
4. delete request and F4 confirmation;
5. F5 validation/save;
6. F12 cancellation;
7. F3 return to the supplied caller or admin.

Evidence: [COTRTUPC.cbl state lines 294–336](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L294-L336) and [control flow lines 345–573](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L345-L573). Accepted keys are state-dependent ([lines 577–608](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L577-L608)).

Validation is exact:

- blank or asterisk becomes low values before interpretation ([lines 625–684](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L625-L684));
- type is required, numeric, and nonzero in <code>1245-EDIT-NUM-REQD</code> ([validation lines 907–974](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L907-L974)), then normalized to two zero-padded digits in <code>1210-EDIT-TRANTYPE</code> ([normalization lines 834–841](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L834-L841));
- description is required, maximum 50, and accepts only ASCII letters, digits, and spaces ([lines 849–903](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L849-L903));
- changed-value comparison trims and ignores case ([lines 783–810](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L783-L810)).

Lookup, update/insert and delete SQL are at [lines 1473–1664](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1473-L1664). Successful mutation performs CICS <code>SYNCPOINT</code>. Delete SQLCODE <code>-532</code> is treated as an existing-category dependency.

Screen type is row 12 width 2; description is row 14 width 50; information/error and key footer follow ([COTRTUP.bms lines 79–135](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L79-L135)). The generated symbolic map is retained as evidence, not a separate target UI model ([COTRTUP.cpy](../Old_Cobol_Code/app/app-transaction-type-db2/cpy-bms/COTRTUP.cpy#L17-L200)).

Strict terminal snapshots use the source's literal information/error catalog, including create, delete-confirm, save, cancel, no-record and SQL failure messages ([COTRTUPC.cbl lines 142–196](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L142-L196)). One literal says the name accepts only alphabets/spaces, while validation also accepts digits.

### CTLI list workflow

**FR-OPT-002 — list/filter/page/update/delete.** <code>COTRTLIC</code>, transaction <code>CTLI</code>, displays seven managed rows per page (`WS-MAX-SCREEN-LINES VALUE 7`) ([program constants](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L43-L60), [BMS rows](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L133-L300)).

- Forward cursor: type key greater than or equal to start, optional exact type, optional description <code>LIKE %value%</code>, ascending.
- Backward cursor: type key less than start, same filters, descending.
- Type blank/zero means no filter; otherwise it must be exactly two numeric characters.
- Description filter has no character whitelist.
- Row action is <code>U</code> or <code>D</code>; only one row action is allowed.
- An update description is required and limited to ASCII letters, digits, and spaces.
- Enter receives filters/actions; F2 transfers to CTTU; F3 returns; F7/F8 page; F10 confirms a pending mutation.

Evidence: cursors [COTRTLIC.cbl lines 338–368](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L338-L368), retained state [lines 377–420](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L377-L420), key/control flow [lines 498–916](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L498-L916), validation [lines 919–1269](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L919-L1269), fetch algorithms [lines 1603–1799](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1603-L1799), mutations [lines 1837–1940](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1837-L1940).

The exact action/confirmation/paging message literals are [COTRTLIC.cbl lines 239–262](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L239-L262).

The BMS contains an eighth/dummy row whose field names carry the suffix <code>A</code> (<code>TRTSELA</code>/<code>TRTTYPA</code>/<code>TRTDSCA</code>, versus suffixes <code>1</code>–<code>7</code> for the managed rows) — the <code>A</code> is a field-name suffix, not a selectable action value. The COBOL redefines it as <code>WS-DUMMY</code> and loops over only seven rows; it must not become an eighth business row ([COTRTLI.bms lines 280–300](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L280-L300), [generated map lines 199–216](../Old_Cobol_Code/app/app-transaction-type-db2/cpy-bms/COTRTLI.cpy#L199-L216)).

### Batch maintenance and synchronization

**FR-OPT-003 — apply fixed maintenance records.** <code>COBTUPDT</code> consumes exactly 53 bytes:

| Byte range | Meaning |
|---|---|
| 1 | <code>A</code>, <code>U</code>, <code>D</code>, or comment <code>*</code> |
| 2–3 | type code |
| 4–53 | description |

It executes INSERT, UPDATE or DELETE and continues after record-level errors with return code 4. It performs no equivalent online validation and no explicit COMMIT ([COBTUPDT.cbl record lines 31–46](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COBTUPDT.cbl#L31-L46), [operation/error lines 109–233](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COBTUPDT.cbl#L109-L233), [MNTTRDB2.jcl lines 5–30](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/MNTTRDB2.jcl#L5-L30)).

**FR-OPT-004 — export Db2 references for core reporting.** <code>TRANEXTR</code> uses <code>DSNTIAUL</code> to write the 60-byte type/category sequential files, ordered by key, after backup/delete steps ([TRANEXTR.jcl lines 31–121](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/TRANEXTR.jcl#L31-L121)).

The data flow is:

~~~mermaid
flowchart LR
    CTTU[CTTU online] --> DB2[(Db2 type/category)]
    COBT[COBTUPDT batch] --> DB2
    DB2 --> EXTR[TRANEXTR / DSNTIAUL]
    EXTR --> PS[60-byte sequential files]
    PS --> LOAD[core TRANTYPE/TRANCATG IDCAMS jobs]
    LOAD --> VSAM[(TRANTYPE/TRANCATG KSDS)]
    VSAM --> REPORT[CBTRN03C reports]
~~~

The core type loader defines key length 2 and LRECL 60 ([TRANTYPE.jcl lines 36–61](../Old_Cobol_Code/app/jcl/TRANTYPE.jcl#L36-L61)); category uses key length 6. Report procedure DDs are [TRANREPT.prc lines 67–70](../Old_Cobol_Code/app/proc/TRANREPT.prc#L67-L70).

**Decision required:** <code>TRANEXTR</code> creates only sequential inputs. The supplied Control-M chain schedules [maintenance](../Old_Cobol_Code/app/scheduler/CardDemo.controlm#L26-L30) and [extract](../Old_Cobol_Code/app/scheduler/CardDemo.controlm#L57-L62), not the VSAM loaders. CA7 contains independent loaders. The required production refresh cadence and scheduler dependency are not proven.

### Transaction-type deployment

- CSD defines both maps, both programs, transactions CTLI/CTTU, DB2 entry/transactions, plan <code>CARDDEMO</code>, and no CICS resource/command security ([CRDDEMOD.csd lines 1–60](../Old_Cobol_Code/app/app-transaction-type-db2/csd/CRDDEMOD.csd#L1-L60)).
- Create control hard-codes database, stogroup, EBCDIC, tablespaces and broad public privileges ([DB2CREAT.ctl lines 15–105](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L15-L105)).
- Db2 helper controls hard-code subsystem <code>DAZ1</code> and invoke <code>DSNTIAD</code>/<code>DSNTEP4</code> ([DB2TIAD1.ctl lines 15–17](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2TIAD1.ctl#L15-L17), [DB2TEP41.ctl lines 15–18](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2TEP41.ctl#L15-L18)).
- Free control removes plan/package <code>CARDDEMO</code> and <code>COTRTLIC</code> ([DB2FREE.ctl lines 15–19](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2FREE.ctl#L15-L19)).
- Create JCL ships with <code>TYPRUN=SCAN</code>; it cannot perform creation until that is changed ([CREADB21.jcl lines 1–2](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/CREADB21.jcl#L1-L2)).
- <code>REPROCT.ctl</code> contains only IDCAMS REPRO input ([REPROCT.ctl line 15](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/REPROCT.ctl#L15)).
- SQL diagnostics require external IBM routine <code>DSNTIAC</code> ([CSDB2RPY.cpy lines 53–88](../Old_Cobol_Code/app/app-transaction-type-db2/cpy/CSDB2RPY.cpy#L53-L88)); that routine is not present in the repository.

### Transaction-type compatibility defects

| ID | Observed defect | Exact-parity characterization | Safe-target rule |
|---|---|---|---|
| OPT-TT-01 | CTTU/CTLI assign VARCHAR length from fixed <code>PIC X(50)</code>, so update length is always 50 ([COTRTUPC lines 1541–1542](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1541-L1542), [COTRTLIC lines 1841–1844](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1841-L1844)) | verify trailing-space behavior | store normalized value while preserving the 50-character legacy export |
| OPT-TT-02 | CTTU exposes dead optimistic-lock messages but has no concurrency predicate | characterize last-writer behavior | use a concurrency token and report conflict |
| OPT-TT-03 | CTTU insert error can display stale SQLCODE ([lines 1604–1617](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1604-L1617)) | snapshot legacy message only | report the actual failure |
| OPT-TT-04 | BMS advertises F6 Add, but COBOL rejects F6 ([COTRTUP.bms lines 126–130](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L126-L130), [key logic](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L577-L608)) | F6 remains invalid | remove misleading footer or approve F6 as a target enhancement |
| OPT-TT-05 | CTLI page number is one digit; more than nine pages overflows state | boundary test | use an unbounded integer |
| OPT-TT-06 | Backward cursor <code>+100</code> can enter generic error formatting ([lines 1777–1788](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1777-L1788)) | characterize edge page | return “no prior page” |
| OPT-TT-07 | Batch validation is weaker than online validation | preserve record-level result for migration testing | share one validated application service, with an explicit legacy-import mode |
| OPT-TT-08 | Control-M folder/dependency data contains a copied parent-folder value and omits VSAM loaders | do not infer schedule | require an approved run graph |

## Authorization IMS/Db2/MQ module

### Trigger and processing flow

**FR-OPT-010 — consume triggered requests.** CP00/<code>COPAUA0C</code> retrieves an MQ trigger message, obtains the input queue from <code>MQTM-QNAME</code>, opens it shared, and reads up to 500 bytes with a five-second wait ([COPAUA0C.cbl initialization/open lines 230–283](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L230-L283), [MQGET lines 386–431](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L386-L431)).

The worker loop is:

~~~mermaid
sequenceDiagram
    participant MQ as Request MQ
    participant CP00 as COPAUA0C
    participant VSAM as XREF/Account/Customer
    participant IMS as Pending auth IMS
    participant RQ as ReplyToQ
    MQ->>CP00: CSV request
    CP00->>VSAM: card xref, account, customer reads
    CP00->>IMS: summary GU
    CP00->>CP00: credit decision
    CP00->>RQ: CSV reply
    CP00->>IMS: summary REPL/ISRT and detail ISRT
    CP00->>CP00: CICS syncpoint
~~~

The sequence above is deliberately the legacy order: reply precedes IMS writes. The safe target changes the transaction boundary in [.NET 10 optional-module target](#net-10-optional-module-target).

MQGET uses <code>NO_SYNCPOINT</code>, <code>WAIT</code>, <code>CONVERT</code>, and <code>FAIL_IF_QUIESCING</code> ([COPAUA0C.cbl lines 389–431](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L389-L431)). Reply uses MQPUT1 to each request's <code>ReplyToQ</code>, preserves CorrelId, generates a new MsgId, is nonpersistent, expires after five seconds, and also uses <code>NO_SYNCPOINT</code> ([lines 738–779](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L738-L779)).

The loop limit constant is 500, but termination tests <code>processed &gt; limit</code>; one task can process 501 messages ([lines 323–344](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L323-L344)).

### Decision rules

**FR-OPT-011 — resolve identity and available credit.**

1. Resolve card through <code>CCXREF</code>.
2. Read <code>ACCTDAT</code> and <code>CUSTDAT</code>.
3. Read IMS summary by resolved account.
4. If summary exists, available = summary credit limit − summary credit balance.
5. Otherwise, if account exists, available = base credit limit − base current balance.
6. Decline when requested amount is greater than available, or when no usable account state exists.

Evidence: resolution [COPAUA0C.cbl lines 438–643](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L438-L643), decision [lines 657–718](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L657-L718).

**FR-OPT-012 — decision outputs.**

| Result | Response code | Approved amount | Initial match |
|---|---|---:|---|
| approved | <code>00</code> | full requested amount | <code>P</code> |
| declined | <code>05</code> | zero | <code>D</code> |

Decline-reason priority is:

| Condition | Reason |
|---|---|
| card XREF, account or customer not found | <code>3100</code> |
| insufficient funds | <code>4100</code> |
| card inactive | <code>4200</code> |
| account closed | <code>4300</code> |
| card fraud | <code>5100</code> |
| merchant fraud | <code>5200</code> |
| fallback | <code>9000</code> |

Only missing references and insufficient funds are actually set by the processor. The profile paragraph is a no-op ([COPAUA0C.cbl lines 647–650](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L647-L650)); no code sets the inactive/closed/fraud decision flags. Missing customer alone does not force a decline. A missing base account does not force decline if an IMS summary exists. Zero and negative amounts can be approved because there is no positive-amount rule.

### Summary and detail persistence

**FR-OPT-013 — write authorization state.** On a new root, the processor initializes summary, assigns XREF account/customer and copies limits from the base account. An approval increments approved count/total and credit balance; a decline increments declined count. It then replaces or inserts root and inserts the detail child ([COPAUA0C.cbl lines 798–935](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L798-L935)).

IMS scheduling uses <code>PSBPAUTB</code> with update capability ([PSBPAUTB.psb lines 17–20](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PSBPAUTB.psb#L17-L20)). PCB masks and DLI return constants are [PAUTBPCB.CPY](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/PAUTBPCB.CPY#L17-L26) and [IMSFUNCS.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/IMSFUNCS.cpy#L17-L27).

### CPVS pending-summary inquiry

**FR-OPT-014 — browse pending authorizations.** CPVS/<code>COPAUS0C</code> accepts an account ID, shows account/customer/summary information and five newest-first detail rows per page, and transfers one selected row to CPVD ([COPAUS0C.cbl control lines 178–257](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS0C.cbl#L178-L257), [paging/data lines 342–605](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS0C.cbl#L342-L605), [COPAU00.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L19-L512)).

- Account is mandatory numeric.
- Keys are Enter, F3, F7 and F8.
- Selection accepts <code>S</code>/<code>s</code>.
- If multiple rows contain a selection, the first nonblank selection is used rather than reporting multiple selection.
- Each row displays transaction ID, original date/time, auth type, A/D indicator, match status and approved amount.
- Previous-page key history holds only 20 entries.
- The BMS account-status field is not populated by COBOL.

The header shows customer name/address/phone, account identity, limits, and IMS approved/declined counts, balances and totals. A row shows transaction ID, original date/time, authorization type, A/D outcome, match status and approved amount; it does not show the requested amount.

Generated symbolic map evidence is [COPAU00.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy-bms/COPAU00.cpy#L17-L764).

### CPVD detail and fraud toggle

**FR-OPT-015 — inspect and mark fraud.** CPVD/<code>COPAUS1C</code> displays one authorization and supports Enter reload, F3 back, F5 fraud toggle, and F8 next authorization ([COPAUS1C.cbl lines 157–357](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L157-L357), [COPAU01.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L19-L292)). There is no previous-detail key.

Displayed detail includes approved amount, approved/declined status, response reason, processing code, POS entry mode, message source, MCC, expiry, authorization type, transaction ID, match/fraud status, and merchant identity/location. The amount shown is approved amount, not requested amount.

F5 toggles <code>F</code> and <code>R</code> and links <code>COPAUS2C</code> ([COPAUS1C.cbl lines 230–266](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L230-L266)). It replaces IMS only on Db2 success and commits Db2/IMS together through the caller's CICS UOW ([lines 520–569](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L520-L569)). Generated symbolic map evidence is [COPAU01.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy-bms/COPAU01.cpy#L17-L344).

<code>COPAUS2C</code> inserts the authorization into <code>AUTHFRDS</code>; duplicate SQLCODE <code>-803</code> updates fraud flag/date instead. Removing fraud therefore retains a row with <code>AUTH_FRAUD='R'</code> ([COPAUS2C.cbl lines 91–243](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS2C.cbl#L91-L243)). Db2 fraud date uses <code>CURRENT DATE</code>; IMS stores the screen/program's <code>MM/DD/YY</code> text.

### Purge behavior

**FR-OPT-016 — legacy expiration input.** <code>CBPAUP0C</code> reads:

~~~text
DD,FFFFF,PPPPP,Y
~~~

where <code>DD</code> is expiry days, <code>FFFFF</code> checkpoint frequency, <code>PPPPP</code> display frequency and final byte debug. Evidence: [CBPAUP0C.cbl lines 98–108](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl#L98-L108).

Expiration reconstructs processing YYDDD from the reverse key and deletes details old enough; it does not filter by match/fraud status ([lines 277–324](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl#L277-L324)). Supplied JCL passes <code>00,00001,00001,Y</code>, so all details whose day difference is nonnegative qualify ([CBPAUP0J.jcl lines 36–37](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/CBPAUP0J.jcl#L36-L37)).

The program decrements summary fields only in memory and never replaces a surviving root. Root-delete logic tests approved count twice and omits declined count. Checkpoint comparison uses greater-than, checkpoint ID never advances, and no XRST restart exists. These are defects; the safe purge contract is in [Safe atomic behavior](#safe-atomic-behavior).

### Load and unload behavior

**FR-OPT-017 — standard IMS unload/load pair.**

- <code>PAUDBUNL</code> writes roots as 100 bytes and details as 206 bytes: six-byte packed account ID plus the 200-byte child. It traverses roots with GN and children with GNP ([PAUDBUNL.CBL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/PAUDBUNL.CBL#L43-L285), [UNLDPADB.JCL lines 25–63](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/UNLDPADB.JCL#L25-L63)).
- <code>PAUDBLOD</code> expects all 100-byte roots first, then 206-byte details. Duplicate inserts are accepted ([PAUDBLOD.CBL lines 222–338](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/PAUDBLOD.CBL#L222-L338), [LOADPADB.JCL lines 26–38](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/LOADPADB.JCL#L26-L38)).
- A non-EOF file-read error in <code>PAUDBLOD</code> does not terminate its loop; nonnumeric child parent key is silently skipped.

**FR-OPT-018 — GSAM unload variant.** <code>DBUNLDGS</code> writes roots to the 100-byte GSAM stream and children to the 200-byte stream. Although its workspace contains root key plus child, the actual child write omits the parent key ([DBUNLDGS.CBL lines 300–334](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/DBUNLDGS.CBL#L300-L334), [UNLDGSAM.JCL lines 26–47](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/UNLDGSAM.JCL#L26-L47)). The two separated outputs cannot reconstruct parent ownership solely from the child file.

**Observed — data:** [AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0.dat](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/data/EBCDIC/AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0.dat) is 51,736 bytes with SHA-256 <code>CCE00A6C86B3E02EE6D3C99723D5D5C080F9C1E1AE779591E98BA873849D3569</code>. It is framed IMS utility unload data, not line-oriented 100/206 records. [DBPAUTP0.jcl lines 15–35](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/DBPAUTP0.jcl#L15-L35) invokes <code>DFSURGU0</code>; no matching reload job is supplied.

GSAM definitions are fixed 100 and 200 bytes ([PASFLDBD.DBD lines 22–27](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PASFLDBD.DBD#L22-L27), [PADFLDBD.DBD lines 22–27](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PADFLDBD.DBD#L22-L27)). Read/load/unload PSBs are [DLIGSAMP.PSB](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DLIGSAMP.PSB#L17-L24), [PAUTBUNL.PSB](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PAUTBUNL.PSB#L17-L21), and [PSBPAUTL.psb](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PSBPAUTL.psb#L17-L20).

### Authorization deployment

- CSD defines maps/programs and CP00/CPVS/CPVD. Only CPVD maps to Db2; the actual plan is <code>AWS01PLN</code> ([CRDDEMO2.csd lines 1–79](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/csd/CRDDEMO2.csd#L1-L79)).
- Transactions have <code>RESSEC(NO)</code>/<code>CMDSEC(NO)</code>.
- CP00 reads base <code>CCXREF</code>, <code>ACCTDAT</code>, and <code>CUSTDAT</code>. CPVS additionally uses account alternate path <code>CXACAIX</code>; core file resources are [CARDDEMO.CSD lines 1–75](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L1-L75).
- Error records follow [CCPAUERY.cpy lines 19–40](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAUERY.cpy#L19-L40) and are written to transient-data queue <code>CSSL</code> ([COPAUA0C.cbl lines 983–1010](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L983-L1010)); no CSSL definition is supplied.
- The 122-byte error contract contains date 6, time 6, application 8, program 8, location 4, severity, subsystem, two nine-byte codes, 50-byte message and 20-byte event key.
- IBM MQ copybooks <code>CMQODV</code>, <code>CMQMDV</code>, <code>CMQGMOV</code>, <code>CMQPMOV</code>, <code>CMQV</code> and <code>CMQTML</code> are external dependencies.
- Missing deployment evidence: MQ queue/process/trigger/initiation-queue/channel definitions, CICS-IMS/DBCTL attachment, Db2 database/tablespace/create/grant JCL for <code>AUTHFRDS</code>, DBDGEN/PSBGEN/database allocation and initial load, CSSL TDQ, and compile/link JCL.
- JCL contains <code>IMS.*</code> and <code>XXXXXXXX.PROD.LOADLIB</code> placeholders and therefore is not deployable without environment decisions.

### Authorization compatibility defects

| ID | Observed defect | Safe-target disposition |
|---|---|---|
| OPT-AUTH-01 | MQ request and reply use <code>NO_SYNCPOINT</code>; reply is sent before IMS writes, so request/reply/IMS are not atomic ([MQGET lines 389–431](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L389-L431), [reply/write order lines 738–791](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L738-L791)) | inbox/outbox and idempotency; never intentionally reproduce data loss |
| OPT-AUTH-02 | Critical return can commit a root update after child insert failure; no rollback | one database transaction for summary + detail + outbox |
| OPT-AUTH-03 | Declined total adds <code>PA-TRANSACTION-AMT</code> before detail population ([lines 819–821](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L819-L821)) | add validated request amount |
| OPT-AUTH-04 | New summary ignores base current balance after making the first decision | define an approved migration rule; do not infer accounting intent |
| OPT-AUTH-05 | response pointer/buffer are never reset; replies accumulate and overflow after several messages ([pointer definition lines 40–66](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L40-L66), [construction/put lines 720–763](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L720-L763)) | create a fresh response buffer per request |
| OPT-AUTH-06 | customer/root found flags persist between messages and can reuse a previous summary ([flag definitions lines 125–139](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L125-L139), [per-message initialization lines 438–465](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L438-L465)) | request-scoped immutable state |
| OPT-AUTH-07 | account NOTFND can still write stale account limits | prohibit persistence without resolved account, unless an approved summary-only rule exists |
| OPT-AUTH-08 | no validation of field count, numeric class, dates, expiry, codes, status, or positive amount | reject malformed requests with typed reason/dead-letter policy |
| OPT-AUTH-09 | inactive/closed/fraud decline reasons are unreachable | decision required: implement intended checks or document codes as reserved |
| OPT-AUTH-10 | CPVS NOTFND paths send an error then continue with stale records; multiple selections are not rejected | stop on failed lookup and reject multiple selection |
| OPT-AUTH-11 | CPVS previous-key array has only 20 entries | unbounded/cursor-based paging |
| OPT-AUTH-12 | <code>COPAUS2C</code> merchant VARCHAR length is always 22 ([line 130](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS2C.cbl#L130)) and SQLSTATE host display is incorrectly numeric ([error paths lines 208–241](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS2C.cbl#L208-L241)) | use trimmed string and five-character SQLSTATE |
| OPT-AUTH-13 | purge does not persist adjusted surviving root, root test omits declined count, checkpoint/restart is incomplete | atomic, resumable purge with persisted aggregates |
| OPT-AUTH-14 | unload detail counters update summary counters; GSAM child omits parent; loader can loop on I/O error | corrected codecs plus strict legacy-reader tests |
| OPT-AUTH-15 | fraud removal retains an <code>R</code> history row | preserve as intended audit behavior unless a retention decision changes it |

## VSAM/MQ account and date servers

### Triggered server topology

**FR-OPT-020 — start from MQ trigger data.** Both programs execute CICS RETRIEVE into <code>MQTM</code> and derive the input queue from <code>MQTM-QNAME</code> ([COACCT01.cbl lines 178–218](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L178-L218), [CODATE01.cbl lines 127–167](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L127-L167)). Direct terminal invocation cannot supply this contract.

Hard-coded output names are:

| Service | Reply queue | Error queue |
|---|---|---|
| account | <code>CARD.DEMO.REPLY.ACCT</code> | <code>CARD.DEMO.ERROR</code> |
| date | <code>CARD.DEMO.REPLY.DATE</code> | <code>CARD.DEMO.ERROR</code> |

Evidence: account [trigger/reply lines 191–198](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L191-L198) and [error queue lines 289–319](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L289-L319); date [trigger/reply lines 140–147](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L140-L147) and [error queue lines 238–268](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L238-L268).

Both services:

- open triggered input shared and fixed output/error queues;
- MQGET up to 1,000 bytes using <code>SYNCPOINT + WAIT(5000) + CONVERT + FAIL_IF_QUIESCING</code>;
- save request MsgId, CorrelId and ReplyToQ;
- ignore ReplyToQ and put to the fixed output;
- use request MsgId and request CorrelId in the reply;
- send 1,000 bytes for success, invalid request and error;
- syncpoint before fetching the next message;
- stop only after a five-second empty wait, with no message-count cap.

Account evidence: [COACCT01 open/get lines 222–388](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L222-L388) and [put/error lines 462–537](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L462-L537). Date evidence: [CODATE01 open/get lines 171–337](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L171-L337) and [put/error lines 366–441](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L366-L441).

### Service behavior

**FR-OPT-021 — date service.** Request function/key workspace exists but is never tested ([CODATE01.cbl lines 109–112](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L109-L112)); every message receives the current CICS-formatted date/time response ([lines 339–361](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L339-L361)).

**FR-OPT-022 — account service.** Function must be uppercase <code>INQA</code> and key greater than zero. The service CICS READs <code>ACCTDAT</code> by the eleven-byte account key and returns the labeled data specified in [VSAM/MQ request and reply contracts](#vsammq-request-and-reply-contracts). NOTFND and invalid function/key return an “INVALID REQUEST PARAMETERS” text reply ([COACCT01.cbl lines 390–457](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L390-L457)).

There is no numeric-class check before the numeric comparison; nonnumeric key bytes can produce invalid numeric behavior.

### VSAM/MQ deployment and defects

- CSD defines programs COACCT01/CODATE01, transactions CDRA/CDRD and a hard-coded <code>CARDDLIB</code>; it defines no MQ resources ([CRDDEMOM.csd lines 1–41](../Old_Cobol_Code/app/app-vsam-mq/csd/CRDDEMOM.csd#L1-L41)).
- Input queue names are unresolved because no MQ PROCESS/trigger definitions are present.
- README queue names <code>CARDDEMO.REQUEST.QUEUE</code>/<code>CARDDEMO.RESPONSE.QUEUE</code> are not referenced by code.
- README describes terminal clients that send then display a response. The programs are triggered servers that consume and reply.
- README's DATE/ACCT request-ID layouts do not exist in source.
- CICS RETRIEVE error formatting moves RESP2 to itself rather than its display field ([COACCT01 lines 199–208](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L199-L208), [CODATE01 lines 148–157](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L148-L157)).
- MQCLOSE failure re-enters termination while the corresponding open flag remains true, allowing recursive close attempts.
- The safe target must honor ReplyToQ/correlation by default, bound a worker batch, validate account request bytes, and avoid 1,000-byte padding unless a strict legacy endpoint requires it.

## .NET 10 optional-module target

This section is a **target recommendation**. It does not rewrite the observed contracts above. The application remains the one <code>net10.0</code> console executable specified in [.NET 10 target architecture](09-DotNet-Target-Architecture.md#target-constraints).

### Console commands

Existing target command names remain normative:

~~~text
carddemo interactive

carddemo authorization process
carddemo authorization purge-expired
carddemo worker authorization
carddemo worker account-inquiry
carddemo worker system-date
~~~

The following additions are recommended so every optional batch/migration function has a testable noninteractive entry point. They require approval before scripts depend on the names:

~~~text
carddemo reference-data apply-transaction-types --input <53-byte-file>
carddemo reference-data export-transaction-references --types <path> --categories <path>

carddemo authorization unload --summary <path> --details <path> --format legacy-206
carddemo authorization load --summary <path> --details <path> --format legacy-206
carddemo authorization inspect-unload --input <dfsurg-output>
~~~

Command responsibilities:

| Command/mode | Legacy coverage | Required adapter behavior |
|---|---|---|
| <code>interactive</code> admin CTLI/CTTU | list/filter/page/create/update/delete | transaction-type repository + terminal controller |
| <code>reference-data apply-transaction-types</code> | COBTUPDT | exact 53-byte codec, per-record result, optional strict validation profile |
| <code>reference-data export-transaction-references</code> | TRANEXTR and VSAM-loader input | ordered 60-byte type/category codecs |
| <code>authorization process</code> | one bounded CP00 drain | queue, repositories, inbox/outbox, clock |
| <code>worker authorization</code> | long-lived triggered CP00 role | hosted worker, graceful cancellation, retry/dead-letter |
| <code>authorization purge-expired</code> | CBPAUP0C | explicit expiry days/checkpoint/progress options; resumable |
| authorization load/unload | PAUDBLOD/PAUDBUNL | packed-decimal root key and 100/206-byte codecs |
| <code>worker account-inquiry</code> | CDRA/COACCT01 | inquiry queue + account repository |
| <code>worker system-date</code> | CDRD/CODATE01 | inquiry queue + injected <code>TimeProvider</code> |

All commands follow the common exit codes and TTY rules in [Console command surface](09-DotNet-Target-Architecture.md#console-command-surface).

### Ports and adapters

| Port | Default adapter | Optional interoperability adapter | Contract |
|---|---|---|---|
| <code>ITransactionTypeRepository</code> | EF Core/SQLite | Db2 | type/category composite identity and restricted delete |
| <code>IPendingAuthorizationRepository</code> | relational summary/detail tables | IMS bridge during migration | one account root, reverse-ordered details |
| <code>IFraudHistoryRepository</code> | relational fraud-history table | Db2 | card/timestamp identity and retained F/R audit |
| <code>IAuthorizationRequestQueue</code> | durable SQLite queue | IBM MQ | raw payload, MsgId, CorrelId, ReplyToQ |
| <code>IAuthorizationReplyQueue</code> | durable outbox dispatcher | IBM MQ | correlation and destination |
| <code>IInquiryQueue</code> | durable SQLite request/reply tables | IBM MQ | account/date payload profiles |
| <code>IAccountRepository</code> | EF Core/SQLite | read-only VSAM bridge | eleven-character account identity |
| <code>ILegacyRecordCodec</code> | span/stream codecs | same | exact 53, 60, 100, 153, 200, 206 and 1,000-byte formats |
| clock | <code>TimeProvider</code> | none | frozen time in characterization tests |

IBM MQ adapter configuration supplies queue manager, channel/TLS credentials, input queues, fixed legacy replies only when enabled, error/dead-letter queues, wait interval and maximum messages per drain. Queue names are configuration; unresolved legacy input names must not be invented as defaults.

IMS and Db2 become relational entities in the default store. Import adapters preserve packed-decimal and raw payload evidence; domain code must not depend on COMP-3 bytes.

### Safe atomic behavior

**NFR-OPT-001 — authorization exactly-once effect.** A broker receive and relational commit cannot be assumed to share one transaction. The safe target uses:

1. a durable inbox key based on adapter message identity plus queue;
2. validation and complete decision computation in request-scoped state;
3. one database transaction for inbox status, summary, detail, fraud/audit state and reply outbox;
4. broker acknowledgement only after database commit;
5. idempotent redelivery lookup;
6. an outbox dispatcher that retries reply delivery without repeating the business mutation.

If an approved IBM MQ/XA deployment later supplies distributed transaction support, it may optimize delivery but does not replace inbox/outbox idempotency.

**NFR-OPT-002 — summary/detail invariant.** Summary counters/totals and one detail insert commit together. Approved total and credit balance use the validated approved amount; declined total uses the validated requested amount. The invariant is recomputable from details and checked by <code>database verify</code>.

**NFR-OPT-003 — fraud toggle.** Pending detail and fraud-history upsert commit in one relational transaction. Removing a fraud marker retains an audit row with state <code>R</code> unless retention policy explicitly changes the source behavior.

**NFR-OPT-004 — purge.** A purge run records its cutoff and cursor. Each bounded account transaction deletes qualifying details, recomputes persisted summary values, and deletes the root only when no details remain and both counts are zero. It never changes Db2/fraud retention without a separate policy.

**NFR-OPT-005 — transaction references.** Type mutation and dependent-category enforcement occur in one transaction. Export reads a consistent snapshot and writes temporary outputs before atomically publishing both files, preventing type/category generation skew.

**NFR-OPT-006 — inquiry reply.** Safe workers validate payload, honor ReplyToQ and correlation, use a new reply message identity, cap processing per drain, and send logical length. A named strict endpoint can reproduce fixed queues and 1,000-byte padding for interoperability.

### Compatibility profiles

There is no aggregate “all bugs” flag. Characterization policies are individually named:

| Policy | Strict legacy effect | Safe default |
|---|---|---|
| <code>TransactionTypes.FixedVarcharLength50</code> | retain padded Db2 host length | off |
| <code>TransactionTypes.BatchWeakValidation</code> | accept COBTUPDT's weak record checks | off |
| <code>Authorization.AllowUnvalidatedCsv</code> | characterize malformed-token paths | off; never production |
| <code>Authorization.LegacyDecisionRules</code> | only available-credit/missing-state declines | on until intended status/fraud rules are approved |
| <code>Authorization.LegacyResponseLength</code> | test first 64-byte pointer result | off |
| <code>Inquiry.FixedReplyQueues</code> | use CARD.DEMO.REPLY.* | off |
| <code>Inquiry.FixedBuffer1000</code> | pad replies to 1,000 | off |
| <code>Inquiry.ReuseRequestMessageId</code> | reproduce source descriptor attempt | off |

Nonatomic processing, stale state, infinite/recursive loops, and data-loss behavior are fault-injection characterization tests, not production compatibility switches.

## Contradictions and unresolved facts

| ID | Repository claim or gap | Source-backed conclusion / required decision |
|---|---|---|
| OPT-DEC-001 | Authorization README names plan <code>DB201PLN</code> | CSD uses <code>AWS01PLN</code>; source SQL uses schema <code>CARDDEMO</code> ([CRDDEMO2.csd lines 69–79](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/csd/CRDDEMO2.csd#L69-L79)) |
| OPT-DEC-002 | Authorization README names request/reply queues | request queue comes from MQTM; reply comes from MQMD ReplyToQ; no MQ definitions prove names |
| OPT-DEC-003 | Authorization README says compile templates are provided | supplied JCL is runtime/load/unload only; compile/link jobs are absent |
| OPT-DEC-004 | README implies purge adjusts summaries | COBOL changes working storage but never REPLs a surviving summary |
| OPT-DEC-005 | VSAM/MQ README describes interactive clients | programs are triggered servers |
| OPT-DEC-006 | VSAM/MQ README supplies DATE/ACCT request-ID structures | neither layout is used; actual contracts are DATA-OPT-020/021 |
| OPT-DEC-007 | VSAM/MQ README queue names differ from hard-coded replies | input names remain unknown; output names are CARD.DEMO.REPLY.DATE/ACCT |
| OPT-DEC-008 | Transaction extract is described as synchronization | extract does not run VSAM loaders; production ordering/cadence requires approval |
| OPT-DEC-009 | Db2/environment identifiers are hard-coded across artifacts | all subsystem, schema, plan, stogroup, dataset and load-library names become validated configuration/migrations |
| OPT-DEC-010 | Binary IMS fixture has no supplied inverse load path | retain hash/raw file and build a separately validated DFSURGU0 parser only if migration scope requires it |
| OPT-DEC-011 | Inactive/closed/fraud response reasons exist but are unreachable | decide whether they are intended future rules; do not invent predicates |
| OPT-DEC-012 | Input MQ queue/process names and CSSL TDQ definition are absent | deployment cannot claim exact legacy resource names |

## Extension artifact coverage

Every extension artifact is linked below. “Prose” means it was reviewed for claims but was subordinate to executable/declarative evidence.

### Transaction-type / Db2 — 27

| Artifact | Role |
|---|---|
| [README.md](../Old_Cobol_Code/app/app-transaction-type-db2/README.md#L1) | prose/install claims |
| [COTRTLI.bms](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L1) | CTLI map |
| [COTRTUP.bms](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L1) | CTTU map |
| [COBTUPDT.cbl](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COBTUPDT.cbl#L1) | 53-byte batch maintenance |
| [COTRTLIC.cbl](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1) | list/filter/page/mutate |
| [COTRTUPC.cbl](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1) | single-type maintenance |
| [CSDB2RPY.cpy](../Old_Cobol_Code/app/app-transaction-type-db2/cpy/CSDB2RPY.cpy#L1) | DSNTIAC SQL diagnostic routine |
| [CSDB2RWY.cpy](../Old_Cobol_Code/app/app-transaction-type-db2/cpy/CSDB2RWY.cpy#L1) | SQL diagnostic workspace |
| [COTRTLI.cpy](../Old_Cobol_Code/app/app-transaction-type-db2/cpy-bms/COTRTLI.cpy#L1) | generated list symbolic map |
| [COTRTUP.cpy](../Old_Cobol_Code/app/app-transaction-type-db2/cpy-bms/COTRTUP.cpy#L1) | generated maintenance symbolic map |
| [CRDDEMOD.csd](../Old_Cobol_Code/app/app-transaction-type-db2/csd/CRDDEMOD.csd#L1) | CICS maps/programs/transactions/Db2 |
| [DB2CREAT.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L1) | database/tablespace/table/grant creation |
| [DB2FREE.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2FREE.ctl#L1) | plan/package free |
| [DB2LTCAT.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2LTCAT.ctl#L1) | category seeds |
| [DB2LTTYP.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2LTTYP.ctl#L1) | type seeds |
| [DB2TEP41.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2TEP41.ctl#L1) | DSNTEP4 invocation |
| [DB2TIAD1.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2TIAD1.ctl#L1) | DSNTIAD invocation |
| [REPROCT.ctl](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/REPROCT.ctl#L1) | IDCAMS REPRO input |
| [DCLTRCAT.dcl](../Old_Cobol_Code/app/app-transaction-type-db2/dcl/DCLTRCAT.dcl#L1) | category DCLGEN |
| [DCLTRTYP.dcl](../Old_Cobol_Code/app/app-transaction-type-db2/dcl/DCLTRTYP.dcl#L1) | type DCLGEN |
| [TRNTYCAT.ddl](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/TRNTYCAT.ddl#L1) | category table |
| [TRNTYPE.ddl](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/TRNTYPE.ddl#L1) | type table |
| [XTRNTYCAT.ddl](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/XTRNTYCAT.ddl#L1) | category index |
| [XTRNTYPE.ddl](../Old_Cobol_Code/app/app-transaction-type-db2/ddl/XTRNTYPE.ddl#L1) | type index |
| [CREADB21.jcl](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/CREADB21.jcl#L1) | create/seed orchestration |
| [MNTTRDB2.jcl](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/MNTTRDB2.jcl#L1) | COBTUPDT execution |
| [TRANEXTR.jcl](../Old_Cobol_Code/app/app-transaction-type-db2/jcl/TRANEXTR.jcl#L1) | Db2 unload to 60-byte references |

### Authorization / IMS / Db2 / MQ — 40

| Artifact | Role |
|---|---|
| [README.md](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/README.md#L1) | prose/install claims |
| [COPAU00.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L1) | summary map |
| [COPAU01.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L1) | detail map |
| [CBPAUP0C.cbl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl#L1) | expiration/purge |
| [COPAUA0C.cbl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L1) | triggered authorization processor |
| [COPAUS0C.cbl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS0C.cbl#L1) | pending summary inquiry |
| [COPAUS1C.cbl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L1) | detail/fraud controller |
| [COPAUS2C.cbl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS2C.cbl#L1) | Db2 fraud insert/update |
| [DBUNLDGS.CBL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/DBUNLDGS.CBL#L1) | GSAM unload |
| [PAUDBLOD.CBL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/PAUDBLOD.CBL#L1) | root/detail loader |
| [PAUDBUNL.CBL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/PAUDBUNL.CBL#L1) | root/detail unload |
| [CCPAUERY.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAUERY.cpy#L1) | TDQ error record |
| [CCPAURLY.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAURLY.cpy#L1) | response fields |
| [CCPAURQY.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAURQY.cpy#L1) | request fields |
| [CIPAUDTY.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CIPAUDTY.cpy#L1) | 200-byte detail |
| [CIPAUSMY.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CIPAUSMY.cpy#L1) | 100-byte summary |
| [IMSFUNCS.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/IMSFUNCS.cpy#L1) | DLI statuses |
| [PADFLPCB.CPY](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/PADFLPCB.CPY#L1) | detail GSAM PCB mask |
| [PASFLPCB.CPY](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/PASFLPCB.CPY#L1) | summary GSAM PCB mask |
| [PAUTBPCB.CPY](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/PAUTBPCB.CPY#L1) | pending DB PCB mask |
| [COPAU00.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy-bms/COPAU00.cpy#L1) | generated summary symbolic map |
| [COPAU01.cpy](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy-bms/COPAU01.cpy#L1) | generated detail symbolic map |
| [CRDDEMO2.csd](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/csd/CRDDEMO2.csd#L1) | CICS/Db2 resources |
| [DBPAUTP0.dat](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/data/EBCDIC/AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0.dat) | binary IMS utility unload |
| [AUTHFRDS.dcl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/dcl/AUTHFRDS.dcl#L1) | fraud DCLGEN |
| [AUTHFRDS.ddl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/AUTHFRDS.ddl#L1) | fraud table |
| [XAUTHFRD.ddl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/XAUTHFRD.ddl#L1) | fraud index |
| [DBPAUTP0.dbd](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DBPAUTP0.dbd#L1) | HIDAM database |
| [DBPAUTX0.dbd](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DBPAUTX0.dbd#L1) | secondary index |
| [DLIGSAMP.PSB](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/DLIGSAMP.PSB#L1) | DB + GSAM unload PSB |
| [PADFLDBD.DBD](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PADFLDBD.DBD#L1) | 200-byte GSAM DBD |
| [PASFLDBD.DBD](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PASFLDBD.DBD#L1) | 100-byte GSAM DBD |
| [PAUTBUNL.PSB](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PAUTBUNL.PSB#L1) | unload PSB |
| [PSBPAUTB.psb](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PSBPAUTB.psb#L1) | update PSB |
| [PSBPAUTL.psb](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PSBPAUTL.psb#L1) | load PSB |
| [CBPAUP0J.jcl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/CBPAUP0J.jcl#L1) | purge execution |
| [DBPAUTP0.jcl](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/DBPAUTP0.jcl#L1) | DFSURGU0 unload |
| [LOADPADB.JCL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/LOADPADB.JCL#L1) | PAUDBLOD execution |
| [UNLDGSAM.JCL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/UNLDGSAM.JCL#L1) | GSAM unload execution |
| [UNLDPADB.JCL](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/jcl/UNLDPADB.JCL#L1) | 100/206 unload execution |

### VSAM / MQ — 4

| Artifact | Role |
|---|---|
| [README.md](../Old_Cobol_Code/app/app-vsam-mq/README.md#L1) | prose; materially contradicted |
| [COACCT01.cbl](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L1) | account inquiry server |
| [CODATE01.cbl](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L1) | date inquiry server |
| [CRDDEMOM.csd](../Old_Cobol_Code/app/app-vsam-mq/csd/CRDDEMOM.csd#L1) | CICS resources |

## Acceptance criteria

- **FR-OPT-001 through FR-OPT-022** each have characterization tests against the linked source behavior.
- Request/reply codecs have byte- and field-level golden tests, including trailing comma, fixed 1,000-byte inquiry response, COMP-3 root key and 100/206 unload records.
- Safe authorization processing survives crash points before/after database commit, broker acknowledgement and reply dispatch without duplicate financial effect or lost reply.
- Summary counters/totals reconcile to details after authorization and purge.
- Transaction type/category export is a consistent pair and reproduces exact 60-byte legacy outputs.
- CTLI/CTTU virtual-terminal tests prove seven managed rows, key-state rules, validation and navigation.
- IBM MQ integration tests prove ReplyToQ/CorrelId behavior; strict tests separately prove fixed legacy queues/padding.
- Deployment validation rejects unresolved queue names, placeholder datasets/load libraries, missing CSSL equivalent and inconsistent Db2 plan/schema configuration.
- Every decision in [Contradictions and unresolved facts](#contradictions-and-unresolved-facts) is resolved before production cutover; no README-only claim is implemented as parity without executable evidence.

---

[← Domain data model](06-Domain-Data-Model.md) · [Home](Home.md) · [Security and controls →](08-Security-and-Controls.md)
