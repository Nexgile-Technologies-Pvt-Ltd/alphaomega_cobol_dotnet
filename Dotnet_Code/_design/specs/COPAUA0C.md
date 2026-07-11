# PORT SPEC — COPAUA0C (Card Authorization Decision Processor, ONLINE/MQ-triggered CICS)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl`
> Module: `app-authorization-ims-db2-mq` (CardDemo Authorization extension — IMS + DB2 + MQ).
> Target: `src/CardDemo.Mq` (transport/loop shell) + `src/CardDemo.Ims` (PAUT_SUMMARY/PAUT_DETAIL writes) + `src/CardDemo.Data` (VSAM→SQL reads of ACCOUNT/CUSTOMER/CARD_XREF), over the relational SQLite schema in `_design/ARCHITECTURE.md`.
> All line citations below refer to `COPAUA0C.cbl` unless otherwise noted.
> Cross-refs: `_design/specs/optional/MQ_SHIM.md` (queue/message envelope contract), `_design/specs/optional/IMS_SCHEMA.md` (PAUT_SUMMARY/PAUT_DETAIL relational schema + DL/I→SQL), `_design/specs/jobs/CBPAUP0J.md` (purge job that deletes what this program writes).

---

## 1. Purpose & Invocation

**Purpose.** COPAUA0C is the **card-authorization decision processor**. It is an MQ-triggered CICS transaction that **drains a request queue** of credit-card authorization requests (CSV messages), and for each request: (1) schedules its IMS PSB; (2) reads the card cross-reference (XREF) by card number to resolve the account/customer ids; (3) reads the ACCOUNT master and CUSTOMER master; (4) reads the IMS *Pending-Authorization Summary* root segment for that account; (5) makes an **approve/decline decision** — decline if `transaction-amt > available credit` (available = credit limit − credit balance, using the IMS summary if present, otherwise the account master), or if the card was not found in XREF; (6) builds and **PUTs a CSV reply** to the requester's reply queue; and (7) if the card was found, **persists** the result to IMS by updating/inserting the summary segment running totals and inserting a new *Pending-Authorization Details* child segment. It loops until the request queue is empty (5 s wait expiry) or 500 messages have been processed, taking a CICS SYNCPOINT after each message. // source: COPAUA0C.cbl:1-6, 220-227, 326-345, 438-466

**Invocation.** CICS transaction **`CP00`** (`WS-CICS-TRANID = 'CP00'`), program `COPAUA0C`, **started by an MQ trigger** (the CICS-MQ adapter starts the transaction; the program reads the MQ Trigger Message via `EXEC CICS RETRIEVE INTO(MQTM)`). Pseudo-conversational/screen handling is **not** used — there is **no BMS map and no terminal I/O**; the mapsets `COPAU00`/`COPAU01` belong to the *display* transactions `CPVS`/`CPVD` (`COPAUS0C`/`COPAUS1C`), not to this program. It ends with `EXEC CICS RETURN` (no TRANSID, no COMMAREA passback). // source: COPAUA0C.cbl:33-34, 226-227, 233-236; CSD `CRDDEMO2.csd:11-17,59-68`; README:168-171,206

**DD / external object mapping:**
| External object | Name (literal) | Role | Relational/runtime target |
|---|---|---|---|
| Request MQ queue | `WS-REQUEST-QNAME` ← `MQTM-QNAME` (README `AWS.M2.CARDDEMO.PAUTH.REQUEST`) | GET CSV requests | MQ shim inbound queue (trigger-driven name). // source: COPAUA0C.cbl:43,238,258; README:278 |
| Reply MQ queue | `WS-REPLY-QNAME` ← `MQMD-REPLYTOQ` of the request (README `AWS.M2.CARDDEMO.PAUTH.REPLY`) | MQPUT1 CSV reply | MQ shim outbound queue (dynamic name from request). // source: COPAUA0C.cbl:44,413-414,742; README:279 |
| ACCTDAT (VSAM KSDS) | `WS-ACCTFILENAME='ACCTDAT '` | READ by acct id | `ACCOUNT` table. // source: COPAUA0C.cbl:35,526 |
| CUSTDAT (VSAM KSDS) | `WS-CUSTFILENAME='CUSTDAT '` | READ by cust id | `CUSTOMER` table. // source: COPAUA0C.cbl:36,574 |
| CCXREF (VSAM KSDS) | `WS-CCXREF-FILE='CCXREF  '` | READ by card num | `CARD_XREF` table. // source: COPAUA0C.cbl:39,478 |
| CARDDAT / CARDAIX | `WS-CARDFILENAME='CARDDAT '`, `WS-CARDFILENAME-ACCT-PATH='CARDAIX '` | **declared but never referenced** in PROCEDURE DIVISION | n/a (dead constants). // source: COPAUA0C.cbl:37-38 |
| IMS DB | PSB `PSBPAUTB`, PCB #1 `PAUT-PCB-NUM=+1`, DBD `DBPAUTP0`, segments `PAUTSUM0`(root)/`PAUTDTL1`(child) | SCHD/GU/REPL/ISRT/TERM | `PAUT_SUMMARY` / `PAUT_DETAIL` tables (see IMS_SCHEMA.md). // source: COPAUA0C.cbl:82-84,620-624,825-833,913-919; PSBPAUTB.psb:17-20 |
| CICS TD queue | `'CSSL'` | WRITEQ TD error log | injected `IErrorLog` sink (not an MQ queue, not a base table). // source: COPAUA0C.cbl:1001-1006 |

> Note: the **CARDDAT/CARDAIX** filenames are declared in WORKING-STORAGE but never used by any EXEC CICS in this program — they are vestigial. Do not create a CARD read path. // source: COPAUA0C.cbl:37-38

---

## 2. FILE / TABLE ACCESS TABLE

| COBOL access | Org / op | Key | Relational target | SQL mapping |
|---|---|---|---|---|
| `EXEC CICS READ DATASET(WS-CCXREF-FILE) RIDFLD(XREF-CARD-NUM)` | VSAM KSDS, **READ key** | `XREF-CARD-NUM` X(16) ← `PA-RQ-CARD-NUM` | `CARD_XREF` | `SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF WHERE xref_card_num = @cardnum`. RESP NORMAL→found ('00'); RESP NOTFND→not found ('23'); other→critical CICS error. // source: COPAUA0C.cbl:475-513 |
| `EXEC CICS READ DATASET(WS-ACCTFILENAME) RIDFLD(WS-CARD-RID-ACCT-ID-X)` | VSAM KSDS, **READ key** | acct id 9(11) (as X(11)) ← `XREF-ACCT-ID` | `ACCOUNT` | `SELECT * FROM ACCOUNT WHERE acct_id = @acctid`. NORMAL→found; NOTFND→not found; other→critical. // source: COPAUA0C.cbl:523-561 |
| `EXEC CICS READ DATASET(WS-CUSTFILENAME) RIDFLD(WS-CARD-RID-CUST-ID-X)` | VSAM KSDS, **READ key** | cust id 9(9) (as X(9)) ← `XREF-CUST-ID` | `CUSTOMER` | `SELECT * FROM CUSTOMER WHERE cust_id = @custid`. NORMAL→found; NOTFND→not found; other→critical. Result is **read but never used by the decision** (see §7 faithful bug). // source: COPAUA0C.cbl:571-609 |
| `EXEC DLI GU SEGMENT(PAUTSUM0) WHERE(ACCNTID=PA-ACCT-ID)` | IMS root **Get-Unique** | `PA-ACCT-ID` S9(11) COMP-3 ← `XREF-ACCT-ID` | `PAUT_SUMMARY` | `SELECT * FROM PAUT_SUMMARY WHERE ACCT_ID = @acctid LIMIT 1`. status `'  '`/`FW`→found; `GE`→not found; other→critical IMS error. // source: COPAUA0C.cbl:619-640; IMS_SCHEMA.md §3.1 |
| `EXEC DLI REPL SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY)` | IMS root **Replace** | (held position) acct id | `PAUT_SUMMARY` | `UPDATE PAUT_SUMMARY SET CUST_ID, AUTH_STATUS, ACCOUNT_STATUS_1..5, CREDIT_LIMIT, CASH_LIMIT, CREDIT_BALANCE, CASH_BALANCE, APPROVED_AUTH_CNT, DECLINED_AUTH_CNT, APPROVED_AUTH_AMT, DECLINED_AUTH_AMT WHERE ACCT_ID=@id` (do **not** change PK). Executed when summary already existed. // source: COPAUA0C.cbl:824-828; IMS_SCHEMA.md §3.5 |
| `EXEC DLI ISRT SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY)` | IMS root **Insert** | acct id | `PAUT_SUMMARY` | `INSERT INTO PAUT_SUMMARY (...) VALUES (...)`. Dup → `II`. Executed when summary was absent. // source: COPAUA0C.cbl:829-833; IMS_SCHEMA.md §3.7 |
| `EXEC DLI ISRT SEGMENT(PAUTSUM0) WHERE(ACCNTID=PA-ACCT-ID) SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS) SEGLENGTH(...)` | IMS **path insert** of child under located parent | parent acct id + child key `PA-AUTHORIZATION-KEY` (8 bytes) | `PAUT_DETAIL` | `INSERT INTO PAUT_DETAIL (ACCT_ID, AUTH_KEY, AUTH_DATE_9C, AUTH_TIME_9C, ...all detail cols...) VALUES (...)`; the `WHERE(ACCNTID=)` only locates/validates the parent (FK enforces it). Dup key → `II`; missing parent → FK violation. // source: COPAUA0C.cbl:913-919; IMS_SCHEMA.md §3.7 |
| `EXEC CICS WRITEQ TD QUEUE('CSSL') FROM(ERROR-LOG-RECORD)` | CICS TD queue write | (append) | error-log sink | append a 119-byte `ERROR-LOG-RECORD` to the log sink (`IErrorLog`). NOHANDLE — failures ignored. // source: COPAUA0C.cbl:1001-1006 |
| MQ `MQOPEN`/`MQGET`/`MQPUT1`/`MQCLOSE` | MQ verbs (CALL) | request/reply queues | MQ shim | open request queue (input-shared); GET next (wait 5 s); PUT1 reply by name; close request queue. See MQ_SHIM.md §4. // source: COPAUA0C.cbl:262-268,400-409,758-766,956-961 |

**Repository / status contract (per ARCHITECTURE.md §VSAM→SQL and IMS_SCHEMA.md §3):**
- CICS READ key → SELECT by PK; map RESP `DFHRESP(NORMAL)`→'00', `DFHRESP(NOTFND)`→'23', anything else→critical.
- DL/I GU → SELECT … LIMIT 1; 1 row→`'  '`, 0 rows→`GE`, error→other.
- DL/I REPL → UPDATE non-key columns by PK; ISRT → INSERT (dup→`II`); path ISRT → INSERT child with FK to located parent.
- `EXEC CICS SYNCPOINT` → COMMIT (one per processed message). // source: COPAUA0C.cbl:334-336; ARCHITECTURE.md:80-89

---

## 3. WORKING-STORAGE / record layouts (typed)

**Request (CCPAURQY → `PENDING-AUTH-REQUEST`, parsed from CSV):** `PA-RQ-AUTH-DATE X(6)`, `PA-RQ-AUTH-TIME X(6)`, `PA-RQ-CARD-NUM X(16)`, `PA-RQ-AUTH-TYPE X(4)`, `PA-RQ-CARD-EXPIRY-DATE X(4)`, `PA-RQ-MESSAGE-TYPE X(6)`, `PA-RQ-MESSAGE-SOURCE X(6)`, `PA-RQ-PROCESSING-CODE 9(6)`, `PA-RQ-TRANSACTION-AMT` edited `+9(10).99`, `PA-RQ-MERCHANT-CATAGORY-CODE X(4)` *(sic 'CATAGORY')*, `PA-RQ-ACQR-COUNTRY-CODE X(3)`, `PA-RQ-POS-ENTRY-MODE 9(2)`, `PA-RQ-MERCHANT-ID X(15)`, `PA-RQ-MERCHANT-NAME X(22)`, `PA-RQ-MERCHANT-CITY X(13)`, `PA-RQ-MERCHANT-STATE X(2)`, `PA-RQ-MERCHANT-ZIP X(9)`, `PA-RQ-TRANSACTION-ID X(15)`. // source: CCPAURQY.cpy:19-36

**Reply (CCPAURLY → `PENDING-AUTH-RESPONSE`, serialized to CSV):** `PA-RL-CARD-NUM X(16)`, `PA-RL-TRANSACTION-ID X(15)`, `PA-RL-AUTH-ID-CODE X(6)`, `PA-RL-AUTH-RESP-CODE X(2)`, `PA-RL-AUTH-RESP-REASON X(4)`, `PA-RL-APPROVED-AMT` edited `+9(10).99`. // source: CCPAURLY.cpy:19-24

**Error log (CCPAUERY → `ERROR-LOG-RECORD`, 119 bytes):** `ERR-DATE X(6)`, `ERR-TIME X(6)`, `ERR-APPLICATION X(8)`, `ERR-PROGRAM X(8)`, `ERR-LOCATION X(4)`, `ERR-LEVEL X(1)` (88 L/I/W/C), `ERR-SUBSYSTEM X(1)` (88 A/C/I/D/M/F), `ERR-CODE-1 X(9)`, `ERR-CODE-2 X(9)`, `ERR-MESSAGE X(50)`, `ERR-EVENT-KEY X(20)`. // source: CCPAUERY.cpy:19-41

**IMS summary segment (CIPAUSMY → `PENDING-AUTH-SUMMARY`, 100 bytes) → `PAUT_SUMMARY`:** `PA-ACCT-ID S9(11) COMP-3` (PK), `PA-CUST-ID 9(9)`, `PA-AUTH-STATUS X(1)`, `PA-ACCOUNT-STATUS X(2) OCCURS 5` (→ 5 cols), `PA-CREDIT-LIMIT S9(9)V99 COMP-3`, `PA-CASH-LIMIT S9(9)V99 COMP-3`, `PA-CREDIT-BALANCE S9(9)V99 COMP-3`, `PA-CASH-BALANCE S9(9)V99 COMP-3`, `PA-APPROVED-AUTH-CNT S9(4) COMP`, `PA-DECLINED-AUTH-CNT S9(4) COMP`, `PA-APPROVED-AUTH-AMT S9(9)V99 COMP-3`, `PA-DECLINED-AUTH-AMT S9(9)V99 COMP-3`, FILLER X(34). // source: CIPAUSMY.cpy:19-31

**IMS detail segment (CIPAUDTY → `PENDING-AUTH-DETAILS`, 200 bytes) → `PAUT_DETAIL`:** `PA-AUTHORIZATION-KEY` { `PA-AUTH-DATE-9C S9(5) COMP-3` + `PA-AUTH-TIME-9C S9(9) COMP-3` } (8-byte child seq key), then `PA-AUTH-ORIG-DATE X(6)`, `PA-AUTH-ORIG-TIME X(6)`, `PA-CARD-NUM X(16)`, `PA-AUTH-TYPE X(4)`, `PA-CARD-EXPIRY-DATE X(4)`, `PA-MESSAGE-TYPE X(6)`, `PA-MESSAGE-SOURCE X(6)`, `PA-AUTH-ID-CODE X(6)`, `PA-AUTH-RESP-CODE X(2)` (88 `PA-AUTH-APPROVED='00'`), `PA-AUTH-RESP-REASON X(4)`, `PA-PROCESSING-CODE 9(6)`, `PA-TRANSACTION-AMT S9(10)V99 COMP-3`, `PA-APPROVED-AMT S9(10)V99 COMP-3`, `PA-MERCHANT-CATAGORY-CODE X(4)`, `PA-ACQR-COUNTRY-CODE X(3)`, `PA-POS-ENTRY-MODE 9(2)`, `PA-MERCHANT-ID X(15)`, `PA-MERCHANT-NAME X(22)`, `PA-MERCHANT-CITY X(13)`, `PA-MERCHANT-STATE X(2)`, `PA-MERCHANT-ZIP X(9)`, `PA-TRANSACTION-ID X(15)`, `PA-MATCH-STATUS X(1)` (88 P/D/E/M), `PA-AUTH-FRAUD X(1)` (88 F/R), `PA-FRAUD-RPT-DATE X(8)`, FILLER X(17). // source: CIPAUDTY.cpy:19-54

**VSAM record layouts read:** `CARD-XREF-RECORD` (CVACT03Y, RECLN 50): `XREF-CARD-NUM X(16)`, `XREF-CUST-ID 9(9)`, `XREF-ACCT-ID 9(11)`, FILLER X(14) → `CARD_XREF`. `ACCOUNT-RECORD` (CVACT01Y, RECLN 300): `ACCT-ID 9(11)`, `ACCT-ACTIVE-STATUS X(1)`, `ACCT-CURR-BAL S9(10)V99`, `ACCT-CREDIT-LIMIT S9(10)V99`, `ACCT-CASH-CREDIT-LIMIT S9(10)V99`, dates X(10)×3, `ACCT-CURR-CYC-CREDIT/DEBIT S9(10)V99`, `ACCT-ADDR-ZIP X(10)`, `ACCT-GROUP-ID X(10)`, FILLER X(178) → `ACCOUNT`. `CUSTOMER-RECORD` (CVCUS01Y, RECLN 500) → `CUSTOMER`. // source: CVACT03Y.cpy:4-8; CVACT01Y.cpy:4-17; CVCUS01Y.cpy:4-23

**Key WS fields / flags:**
- Constants: `WS-PGM-AUTH='COPAUA0C'`, `WS-CICS-TRANID='CP00'`, `WS-REQSTS-PROCESS-LIMIT S9(4) COMP VALUE 500`. // source: COPAUA0C.cbl:33-34,40
- Counters/state: `WS-MSG-PROCESSED S9(4) COMP`, `WS-WAIT-INTERVAL` (set 5000), `WS-SAVE-CORRELID X(24)`, `WS-RESP-LENGTH S9(4) VALUE 1`, `WS-RESP-CD`/`WS-REAS-CD S9(9) COMP` (CICS RESP/RESP2). // source: COPAUA0C.cbl:42,46-48,242
- Amounts: `WS-AVAILABLE-AMT S9(9)V99 COMP-3`, `WS-TRANSACTION-AMT-AN X(13)`, `WS-TRANSACTION-AMT S9(10)V99`, `WS-APPROVED-AMT S9(10)V99`, `WS-APPROVED-AMT-DIS` edited `-zzzzzzzzz9.99`. // source: COPAUA0C.cbl:62-66
- Time work: `WS-ABS-TIME S9(15) COMP-3`, `WS-CUR-DATE-X6 X(6)`, `WS-CUR-TIME-X6 X(6)`, `WS-CUR-TIME-N6 9(6)`, `WS-CUR-TIME-MS S9(8) COMP`, `WS-YYDDD 9(5)`, `WS-TIME-WITH-MS S9(9) COMP-3`. // source: COPAUA0C.cbl:50-56
- `WS-XREF-RID` group with `WS-CARD-RID-CUST-ID 9(9)` REDEFINES `…-X X(9)`, `WS-CARD-RID-ACCT-ID 9(11)` REDEFINES `…-X X(11)` — used to pass numeric ids as the X-form RIDFLD to CICS READ. // source: COPAUA0C.cbl:72-79
- `WS-CODE-DISPLAY 9(9)` — scratch numeric used to convert binary RESP/COMPCODE/REASON to displayable digits before moving to `ERR-CODE-1/2`. // source: COPAUA0C.cbl:61,276-279
- IMS vars: `PSB-NAME='PSBPAUTB'`, `PAUT-PCB-NUM S9(4) COMP VALUE +1`, `IMS-RETURN-CODE X(2)` with 88s `STATUS-OK='  '|'FW'`, `SEGMENT-NOT-FOUND='GE'`, etc.; `WS-IMS-PSB-SCHD-FLG` (88 IMS-PSB-SCHD='Y'). // source: COPAUA0C.cbl:81-97
- Switch flags (`WS-SWITCHES`): `WS-AUTH-RESP-FLG` (88 AUTH-RESP-APPROVED='A'/AUTH-RESP-DECLINED='D'), `WS-MSG-LOOP-FLG` (88 WS-LOOP-END='E'; init 'N'), `WS-MSG-AVAILABLE-FLG` (88 NO-MORE-MSG-AVAILABLE='N'/MORE-MSG-AVAILABLE='M'; init 'M'), `WS-REQUEST-MQ-FLG` (O/C), `WS-XREF-READ-FLG` (88 CARD-NFOUND-XREF='N'/CARD-FOUND-XREF='Y'), `WS-ACCT-MASTER-READ-FLG` (Y/N), `WS-CUST-MASTER-READ-FLG` (Y/N), `WS-PAUT-SMRY-SEG-FLG` (Y/N), `WS-DECLINE-FLG` (88 APPROVE-AUTH='A'/DECLINE-AUTH='D'), `WS-DECLINE-REASON-FLG` (88 INSUFFICIENT-FUND='I'/CARD-NOT-ACTIVE='A'/ACCOUNT-CLOSED='C'/CARD-FRAUD='F'/MERCHANT-FRAUD='M'). // source: COPAUA0C.cbl:110-145
- MQ buffers: `W01-GET-BUFFER X(500)` + `W01-DATALEN`/`W01-BUFFLEN S9(9) BINARY`; `W02-PUT-BUFFER X(200)` + `W02-BUFFLEN`. MQ copybooks `CMQODV/CMQMDV/CMQV/CMQTML/CMQPMOV/CMQGMOV` (IBM-supplied, two OD/MD pairs: REQUEST + REPLY). // source: COPAUA0C.cbl:99-108,148-170
- LINKAGE: `DFHCOMMAREA` with `LK-COMMAREA X(4096)` — declared but **never referenced** in the procedure division (no COMMAREA flow). // source: COPAUA0C.cbl:214-215

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = a method)

**MAIN-PARA** // source: COPAUA0C.cbl:220-227
1. PERFORM 1000-INITIALIZE, 2000-MAIN-PROCESS, 9000-TERMINATE in order.
2. `EXEC CICS RETURN` (end transaction — no TRANSID, no COMMAREA).

**1000-INITIALIZE** // source: COPAUA0C.cbl:230-250
1. `EXEC CICS RETRIEVE INTO(MQTM) NOHANDLE`; if `EIBRESP = DFHRESP(NORMAL)` move `MQTM-QNAME → WS-REQUEST-QNAME` and `MQTM-TRIGGERDATA → WS-TRIGGER-DATA`. (If RETRIEVE failed, queue name is left as low-values/spaces — faithful: not validated.)
2. `MOVE 5000 TO WS-WAIT-INTERVAL`.
3. PERFORM 1100-OPEN-REQUEST-QUEUE, then 3100-READ-REQUEST-MQ (prime the first GET). Note PSB is **not** scheduled here.

**1100-OPEN-REQUEST-QUEUE** // source: COPAUA0C.cbl:255-287
1. Set `MQOD-OBJECTTYPE=MQOT-Q`, `MQOD-OBJECTNAME=WS-REQUEST-QNAME` (request OD); `WS-OPTIONS = MQOO-INPUT-SHARED`.
2. `CALL 'MQOPEN'` (request hconn, OD, options, hobj, compcode, reason).
3. If `WS-COMPCODE = MQCC-OK` set WS-REQUEST-MQ-OPEN; else build error `'M001'`/critical/MQ, `'REQ MQ OPEN ERROR'`, PERFORM 9500-LOG-ERROR (which abends because critical).

**1200-SCHEDULE-PSB** // source: COPAUA0C.cbl:292-321
1. `EXEC DLI SCHD PSB((PSB-NAME)) NODHABEND`; `MOVE DIBSTAT → IMS-RETURN-CODE`.
2. If `PSB-SCHEDULED-MORE-THAN-ONCE ('TC')` then `EXEC DLI TERM`, re-`SCHD`, re-capture DIBSTAT. (close+reopen unit of work.)
3. If `STATUS-OK` set IMS-PSB-SCHD; else error `'I001'`/critical/IMS, code1=IMS-RETURN-CODE, `'IMS SCHD FAILED'`, 9500-LOG-ERROR. Called per message from 5000.

**2000-MAIN-PROCESS** // source: COPAUA0C.cbl:323-348
1. `PERFORM UNTIL NO-MORE-MSG-AVAILABLE OR WS-LOOP-END`:
   - 2100-EXTRACT-REQUEST-MSG; 5000-PROCESS-AUTH; `ADD 1 TO WS-MSG-PROCESSED`; `EXEC CICS SYNCPOINT`; `SET IMS-PSB-NOT-SCHD TO TRUE`.
   - If `WS-MSG-PROCESSED > WS-REQSTS-PROCESS-LIMIT (500)` set WS-LOOP-END; else 3100-READ-REQUEST-MQ (get next).
2. Note: the SYNCPOINT commits MQ-reply (already PUT non-syncpoint) + IMS writes; PSB-scheduled flag is reset to 'N' each loop (so 5000 re-schedules every message).

**2100-EXTRACT-REQUEST-MSG** // source: COPAUA0C.cbl:351-383
1. `UNSTRING W01-GET-BUFFER(1:W01-DATALEN) DELIMITED BY ','` into the 18 request fields (field #9 amount goes into `WS-TRANSACTION-AMT-AN`, not directly into the edited PIC).
2. `COMPUTE PA-RQ-TRANSACTION-AMT = FUNCTION NUMVAL(WS-TRANSACTION-AMT-AN)` (text→numeric, edited store).
3. `MOVE PA-RQ-TRANSACTION-AMT → WS-TRANSACTION-AMT` (S9(10)V99 working copy for comparison).

**3100-READ-REQUEST-MQ** // source: COPAUA0C.cbl:386-435
1. `MQGMO-OPTIONS = MQGMO-NO-SYNCPOINT + MQGMO-WAIT + MQGMO-CONVERT + MQGMO-FAIL-IF-QUIESCING`; `MQGMO-WAITINTERVAL = WS-WAIT-INTERVAL (5000)`.
2. Set request MD `MSGID=MQMI-NONE`, `CORRELID=MQCI-NONE`, `FORMAT=MQFMT-STRING`; `W01-BUFFLEN = LENGTH OF W01-GET-BUFFER (500)`.
3. `CALL 'MQGET'`.
4. If `MQCC-OK`: save `MQMD-CORRELID → WS-SAVE-CORRELID` and `MQMD-REPLYTOQ → WS-REPLY-QNAME`.
5. Else if `WS-REASON = MQRC-NO-MSG-AVAILABLE` set NO-MORE-MSG-AVAILABLE (loop ends). Else error `'M003'`/critical/**CICS** (sic — subsystem set to CICS, not MQ), `'FAILED TO READ REQUEST MQ'`, event-key = `PA-CARD-NUM`, 9500-LOG-ERROR.

**5000-PROCESS-AUTH** // source: COPAUA0C.cbl:438-469
1. `SET APPROVE-AUTH TO TRUE` (default decision = approve).
2. PERFORM 1200-SCHEDULE-PSB (re-schedule the PSB for this message).
3. `SET CARD-FOUND-XREF TO TRUE` and `SET FOUND-ACCT-IN-MSTR TO TRUE` (optimistic preset before the reads).
4. PERFORM 5100-READ-XREF-RECORD.
5. If `CARD-FOUND-XREF`: PERFORM 5200-READ-ACCT-RECORD, 5300-READ-CUST-RECORD, 5500-READ-AUTH-SUMMRY, 5600-READ-PROFILE-DATA.
6. PERFORM 6000-MAKE-DECISION, then 7100-SEND-RESPONSE.
7. If `CARD-FOUND-XREF`: PERFORM 8000-WRITE-AUTH-TO-DB.

**5100-READ-XREF-RECORD** // source: COPAUA0C.cbl:472-517
1. `MOVE PA-RQ-CARD-NUM → XREF-CARD-NUM`.
2. `EXEC CICS READ DATASET(CCXREF) INTO(CARD-XREF-RECORD) RIDFLD(XREF-CARD-NUM) KEYLENGTH(16) RESP/RESP2`.
3. EVALUATE WS-RESP-CD: NORMAL→`SET CARD-FOUND-XREF`; NOTFND→`SET CARD-NFOUND-XREF` + `SET NFOUND-ACCT-IN-MSTR`, error `'A001'`/warning/app `'CARD NOT FOUND IN XREF'` key=XREF-CARD-NUM, 9500-LOG-ERROR (warning ⇒ no abend); OTHER→`'C001'`/critical/CICS `'FAILED TO READ XREF FILE'`, 9500-LOG-ERROR (abends).

**5200-READ-ACCT-RECORD** // source: COPAUA0C.cbl:520-565
1. `MOVE XREF-ACCT-ID → WS-CARD-RID-ACCT-ID` (numeric redefine; the X(11) alias becomes the RIDFLD).
2. `EXEC CICS READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X) KEYLENGTH(11) INTO(ACCOUNT-RECORD)`.
3. EVALUATE: NORMAL→`SET FOUND-ACCT-IN-MSTR`; NOTFND→`SET NFOUND-ACCT-IN-MSTR`, error `'A002'`/warning/app `'ACCT NOT FOUND IN XREF'` *(message text says XREF, sic — it is the ACCT file)*; OTHER→`'C002'`/critical/CICS `'FAILED TO READ ACCT FILE'`.

**5300-READ-CUST-RECORD** // source: COPAUA0C.cbl:568-613
1. `MOVE XREF-CUST-ID → WS-CARD-RID-CUST-ID`.
2. `EXEC CICS READ DATASET(CUSTDAT) RIDFLD(WS-CARD-RID-CUST-ID-X) KEYLENGTH(9) INTO(CUSTOMER-RECORD)`.
3. EVALUATE: NORMAL→`SET FOUND-CUST-IN-MSTR`; NOTFND→`SET NFOUND-CUST-IN-MSTR`, error `'A003'`/warning/app `'CUST NOT FOUND IN XREF'` *(sic)* key=WS-CARD-RID-CUST-ID; OTHER→`'C003'`/critical/CICS `'FAILED TO READ CUST FILE'`. The customer record is **not** used by the decision (faithful — read for side-effect/logging only).

**5500-READ-AUTH-SUMMRY** // source: COPAUA0C.cbl:616-644
1. `MOVE XREF-ACCT-ID → PA-ACCT-ID`.
2. `EXEC DLI GU SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) WHERE(ACCNTID=PA-ACCT-ID)`; `MOVE DIBSTAT → IMS-RETURN-CODE`.
3. EVALUATE: STATUS-OK→`SET FOUND-PAUT-SMRY-SEG`; SEGMENT-NOT-FOUND→`SET NFOUND-PAUT-SMRY-SEG`; OTHER→`'I002'`/critical/IMS `'IMS GET SUMMARY FAILED'` key=PA-CARD-NUM, 9500-LOG-ERROR.

**5600-READ-PROFILE-DATA** // source: COPAUA0C.cbl:647-654
1. `CONTINUE` — stub (no logic). Profile/fraud-profile lookup not implemented in this version.

**6000-MAKE-DECISION** // source: COPAUA0C.cbl:657-735
1. Echo into reply: `PA-RQ-CARD-NUM→PA-RL-CARD-NUM`, `PA-RQ-TRANSACTION-ID→PA-RL-TRANSACTION-ID`, `PA-RQ-AUTH-TIME→PA-RL-AUTH-ID-CODE` (the auth-id code is the request time).
2. Available-credit / insufficient-funds branch:
   - If `FOUND-PAUT-SMRY-SEG`: `COMPUTE WS-AVAILABLE-AMT = PA-CREDIT-LIMIT - PA-CREDIT-BALANCE`; if `WS-TRANSACTION-AMT > WS-AVAILABLE-AMT` set DECLINE-AUTH + INSUFFICIENT-FUND.
   - Else if `FOUND-ACCT-IN-MSTR`: `COMPUTE WS-AVAILABLE-AMT = ACCT-CREDIT-LIMIT - ACCT-CURR-BAL`; same compare → DECLINE-AUTH + INSUFFICIENT-FUND.
   - Else (no summary and no account, i.e. XREF not found): set DECLINE-AUTH (no specific reason flag set here).
   - **Arithmetic note:** `WS-AVAILABLE-AMT` is `S9(9)V99 COMP-3` but `PA-CREDIT-LIMIT`/`ACCT-CREDIT-LIMIT` go to 9/10 integer digits; the subtraction result is truncated to 9 integer digits (silent high-order truncation if the available credit ≥ 1e9). `WS-TRANSACTION-AMT` is S9(10)V99 — comparison is COBOL numeric (sign-aware), truncate-toward-zero on the COMPUTE. Reproduce with CobolDecimal (no rounding, silent overflow).
3. Build response code & amount:
   - If `DECLINE-AUTH`: `SET AUTH-RESP-DECLINED`; `MOVE '05' → PA-RL-AUTH-RESP-CODE`; `MOVE 0 → PA-RL-APPROVED-AMT, WS-APPROVED-AMT`.
   - Else: `SET AUTH-RESP-APPROVED`; `MOVE '00' → PA-RL-AUTH-RESP-CODE`; `MOVE PA-RQ-TRANSACTION-AMT → PA-RL-APPROVED-AMT, WS-APPROVED-AMT`.
4. `MOVE '0000' → PA-RL-AUTH-RESP-REASON`. If `AUTH-RESP-DECLINED` EVALUATE TRUE: (CARD-NFOUND-XREF | NFOUND-ACCT-IN-MSTR | NFOUND-CUST-IN-MSTR)→`'3100'`; INSUFFICIENT-FUND→`'4100'`; CARD-NOT-ACTIVE→`'4200'`; ACCOUNT-CLOSED→`'4300'`; CARD-FRAUD→`'5100'`; MERCHANT-FRAUD→`'5200'`; OTHER→`'9000'`. (4200/4300/5100/5200 never fire — their flags are never set in this program; see §7.)
5. `MOVE WS-APPROVED-AMT → WS-APPROVED-AMT-DIS` (edit into `-zzzzzzzzz9.99`).
6. `STRING PA-RL-CARD-NUM ',' PA-RL-TRANSACTION-ID ',' PA-RL-AUTH-ID-CODE ',' PA-RL-AUTH-RESP-CODE ',' PA-RL-AUTH-RESP-REASON ',' WS-APPROVED-AMT-DIS ',' DELIMITED BY SIZE INTO W02-PUT-BUFFER WITH POINTER WS-RESP-LENGTH` — builds CSV with a **trailing comma** after the amount; the pointer ends at length+1 of the built string (see §7 pointer bug).

**7100-SEND-RESPONSE** // source: COPAUA0C.cbl:738-783
1. Reply OD: `OBJECTTYPE=MQOT-Q`, `OBJECTNAME=WS-REPLY-QNAME`.
2. Reply MD: `MSGTYPE=MQMT-REPLY`, `CORRELID=WS-SAVE-CORRELID`, `MSGID=MQMI-NONE`, `REPLYTOQ=SPACES`, `REPLYTOQMGR=SPACES`, `PERSISTENCE=MQPER-NOT-PERSISTENT`, `EXPIRY=50` (5.0 s), `FORMAT=MQFMT-STRING`.
3. `MQPMO-OPTIONS = MQPMO-NO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT`; `W02-BUFFLEN = WS-RESP-LENGTH`.
4. `CALL 'MQPUT1'` (reply hconn, reply OD, reply MD, PMO, bufflen, put-buffer).
5. If `WS-COMPCODE NOT = MQCC-OK`: error `'M004'`/critical/MQ `'FAILED TO PUT ON REPLY MQ'` key=PA-CARD-NUM, 9500-LOG-ERROR.

**8000-WRITE-AUTH-TO-DB** // source: COPAUA0C.cbl:786-795
1. PERFORM 8400-UPDATE-SUMMARY then 8500-INSERT-AUTH. (Only called when CARD-FOUND-XREF.)

**8400-UPDATE-SUMMARY** // source: COPAUA0C.cbl:798-851
1. If `NFOUND-PAUT-SMRY-SEG`: `INITIALIZE PENDING-AUTH-SUMMARY REPLACING NUMERIC DATA BY ZERO`; `MOVE XREF-ACCT-ID → PA-ACCT-ID`; `MOVE XREF-CUST-ID → PA-CUST-ID`.
2. Always: `MOVE ACCT-CREDIT-LIMIT → PA-CREDIT-LIMIT`; `MOVE ACCT-CASH-CREDIT-LIMIT → PA-CASH-LIMIT`. (If the account read failed, these carry whatever was last in ACCOUNT-RECORD — faithful: no guard.)
3. If `AUTH-RESP-APPROVED`: `ADD 1 → PA-APPROVED-AUTH-CNT`; `ADD WS-APPROVED-AMT → PA-APPROVED-AUTH-AMT`; `ADD WS-APPROVED-AMT → PA-CREDIT-BALANCE`; `MOVE 0 → PA-CASH-BALANCE`. Else: `ADD 1 → PA-DECLINED-AUTH-CNT`; `ADD PA-TRANSACTION-AMT → PA-DECLINED-AUTH-AMT` — **note: `PA-TRANSACTION-AMT` (detail field) has not been populated yet at this point** (8500 sets it later); it holds its prior value (0 after INITIALIZE on a new summary, or the previous message's value on an existing one). Faithful bug — see §7.
4. If `FOUND-PAUT-SMRY-SEG`: `EXEC DLI REPL SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY)`; else `EXEC DLI ISRT SEGMENT(PAUTSUM0) FROM(...)`. `MOVE DIBSTAT → IMS-RETURN-CODE`.
5. If `STATUS-OK` CONTINUE; else `'I003'`/critical/IMS `'IMS UPDATE SUMRY FAILED'` key=PA-CARD-NUM, 9500-LOG-ERROR.
   - **Arithmetic:** all PA-* amounts are COMP-3 S9(9)V99 (count fields S9(4) COMP). `ADD` truncates to 9 integer digits, silent overflow; counters wrap at 9999 (S9(4)). Reproduce with CobolDecimal/short semantics.

**8500-INSERT-AUTH** // source: COPAUA0C.cbl:854-936
1. `EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME)`; `EXEC CICS FORMATTIME ABSTIME YYDDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6) MILLISECONDS(WS-CUR-TIME-MS)`.
2. `MOVE WS-CUR-DATE-X6(1:5) → WS-YYDDD`; `MOVE WS-CUR-TIME-X6 → WS-CUR-TIME-N6`.
3. `COMPUTE WS-TIME-WITH-MS = (WS-CUR-TIME-N6 * 1000) + WS-CUR-TIME-MS`.
4. `COMPUTE PA-AUTH-DATE-9C = 99999 - WS-YYDDD`; `COMPUTE PA-AUTH-TIME-9C = 999999999 - WS-TIME-WITH-MS` (9s-complement descending keys → newest-first scan order; preserve verbatim, do NOT "fix").
5. Copy all request fields into the detail segment: orig-date/time, card-num, auth-type, expiry, message-type/source, processing-code, transaction-amt, merchant-catagory/country/pos/id/name/city/state/zip, transaction-id.
6. Copy reply fields: `PA-RL-AUTH-ID-CODE→PA-AUTH-ID-CODE`, `PA-RL-AUTH-RESP-CODE→PA-AUTH-RESP-CODE`, `PA-RL-AUTH-RESP-REASON→PA-AUTH-RESP-REASON`, `PA-RL-APPROVED-AMT→PA-APPROVED-AMT`.
7. If `AUTH-RESP-APPROVED` `SET PA-MATCH-PENDING ('P')` else `SET PA-MATCH-AUTH-DECLINED ('D')`. `MOVE SPACE → PA-AUTH-FRAUD, PA-FRAUD-RPT-DATE`. `MOVE XREF-ACCT-ID → PA-ACCT-ID`.
8. `EXEC DLI ISRT SEGMENT(PAUTSUM0) WHERE(ACCNTID=PA-ACCT-ID) SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS) SEGLENGTH(LENGTH OF PENDING-AUTH-DETAILS)` (path insert of the child under the located parent). `MOVE DIBSTAT → IMS-RETURN-CODE`.
9. If `STATUS-OK` CONTINUE; else `'I004'`/critical/IMS `'IMS INSERT DETL FAILED'` key=PA-CARD-NUM, 9500-LOG-ERROR.

**9000-TERMINATE** // source: COPAUA0C.cbl:940-951
1. If `IMS-PSB-SCHD` `EXEC DLI TERM`. (Usually false here because 2000 sets IMS-PSB-NOT-SCHD after each message; so TERM normally is a no-op unless an error path left it scheduled.)
2. PERFORM 9100-CLOSE-REQUEST-QUEUE.

**9100-CLOSE-REQUEST-QUEUE** // source: COPAUA0C.cbl:953-980
1. If `WS-REQUEST-MQ-OPEN`: `CALL 'MQCLOSE'` (request hconn/hobj, MQCO-NONE).
2. If OK set WS-REQUEST-MQ-CLSE; else `'M005'`/**warning**/MQ `'FAILED TO CLOSE REQUEST MQ'`, 9500-LOG-ERROR (warning ⇒ no abend).

**9500-LOG-ERROR** // source: COPAUA0C.cbl:983-1013
1. `EXEC CICS ASKTIME`; `EXEC CICS FORMATTIME ABSTIME YYMMDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6)`.
2. `MOVE WS-CICS-TRANID→ERR-APPLICATION`, `WS-PGM-AUTH→ERR-PROGRAM`, `WS-CUR-DATE-X6→ERR-DATE`, `WS-CUR-TIME-X6→ERR-TIME`.
3. `EXEC CICS WRITEQ TD QUEUE('CSSL') FROM(ERROR-LOG-RECORD) NOHANDLE`.
4. If `ERR-CRITICAL` PERFORM 9990-END-ROUTINE (terminate + RETURN — abort the whole transaction).

**9990-END-ROUTINE** // source: COPAUA0C.cbl:1016-1025
1. PERFORM 9000-TERMINATE; `EXEC CICS RETURN`. (Hard exit of the transaction after a critical error.)

### Control-flow summary
`MAIN → 1000(RETRIEVE, set wait, 1100 open, 3100 prime GET) → 2000 loop[2100 parse, 5000 decide{1200 SCHD, 5100 xref, (5200 acct,5300 cust,5500 IMS GU,5600 stub), 6000 decision, 7100 PUT reply, 8000 write{8400 summary REPL/ISRT, 8500 detail path ISRT}}, +1, SYNCPOINT, set PSB-not-schd, 3100 next GET] → 9000(TERM if sched, 9100 close) → CICS RETURN`. Any 9500-LOG-ERROR with ERR-CRITICAL short-circuits via 9990 (TERM + RETURN). // source: COPAUA0C.cbl:220-227,323-345,438-466

---

## 5. ONLINE / MQ flow specifics (no BMS screen)

- **No pseudo-conversation, no SEND/RECEIVE MAP, no EIBAID/PFKey handling, no BMS map.** This is a non-terminal, MQ-triggered server transaction. The "online" surface is the **MQ request→reply** envelope, fully specified in `MQ_SHIM.md` §3–§5. // source: COPAUA0C.cbl (no DFHxxx map verbs present)
- **Trigger start:** `EXEC CICS RETRIEVE INTO(MQTM)` reads `MQTM-QNAME`→`WS-REQUEST-QNAME` (the queue to drain) and `MQTM-TRIGGERDATA`→`WS-TRIGGER-DATA` (captured, unused). // source: COPAUA0C.cbl:233-240
- **Request payload (in):** 18-field comma-delimited CSV, `UNSTRING … DELIMITED BY ','` into CCPAURQY (order and PICs per §3 / MQ_SHIM.md §5.3); amount field parsed via `FUNCTION NUMVAL`. GET buffer max **500** bytes; uses `W01-DATALEN` length sub-string. // source: COPAUA0C.cbl:354-379
- **Reply payload (out):** 6-field CSV with trailing comma, `STRING … WITH POINTER WS-RESP-LENGTH` into `W02-PUT-BUFFER` (max **200**); reply length = `WS-RESP-LENGTH` (= W02-BUFFLEN). // source: COPAUA0C.cbl:722-731,756
- **Correlation:** request `MQMD-CORRELID`→`WS-SAVE-CORRELID`; reply sets `MQMD-CORRELID = WS-SAVE-CORRELID` and `MQMD-MSGID = MQMI-NONE` (fresh). Reply target queue = request `MQMD-REPLYTOQ` (dynamic). // source: COPAUA0C.cbl:411-414,744-746,742
- **Loop / commit:** drain until `MQRC-NO-MSG-AVAILABLE` (after 5 s wait) or 500 messages; one `EXEC CICS SYNCPOINT` per message (commit). Reply PUT is `MQPMO-NO-SYNCPOINT` (committed immediately, before the IMS write is committed). // source: COPAUA0C.cbl:326-344,416-417,753-754
- **XCTL/LINK targets:** **none.** This program neither XCTLs nor LINKs to any other program. (It calls IBM MQ stubs `MQOPEN/MQGET/MQPUT1/MQCLOSE` and uses EXEC DLI/EXEC CICS only.) // source: COPAUA0C.cbl (no EXEC CICS XCTL/LINK present)

---

## 6. VALIDATION RULES & exact literal messages

**Decision rule (the only business validation):** approve unless `WS-TRANSACTION-AMT > WS-AVAILABLE-AMT` (insufficient funds) or the card was not found in XREF (then decline with no specific reason flag → reason `'3100'` because CARD-NFOUND-XREF / NFOUND-ACCT-IN-MSTR / NFOUND-CUST-IN-MSTR are set). Available credit = (summary credit-limit − summary credit-balance) if an IMS summary exists, else (account credit-limit − account current-balance). // source: COPAUA0C.cbl:665-696

**Auth response code (`PA-RL-AUTH-RESP-CODE`):** `'00'` approved, `'05'` declined. // source: COPAUA0C.cbl:688,693
**Auth response reason (`PA-RL-AUTH-RESP-REASON`):** `'0000'` approved (default); declined → `'3100'` (not found in XREF/acct/cust), `'4100'` (insufficient funds), `'4200'` (card not active), `'4300'` (account closed), `'5100'` (card fraud), `'5200'` (merchant fraud), `'9000'` (other). // source: COPAUA0C.cbl:698-717

**Error-log literal messages (50-char `ERR-MESSAGE`), with location code / level / subsystem:**
| Location | Level | Subsys | Message (verbatim) | Paragraph |
|---|---|---|---|---|
| `M001` | C | M | `REQ MQ OPEN ERROR` | 1100. // source: COPAUA0C.cbl:273-281 |
| `I001` | C | I | `IMS SCHD FAILED` | 1200. // source: COPAUA0C.cbl:311-315 |
| `M003` | C | **C** *(set CICS, an MQ error — sic)* | `FAILED TO READ REQUEST MQ` | 3100. // source: COPAUA0C.cbl:419-427 |
| `A001` | W | A | `CARD NOT FOUND IN XREF` | 5100. // source: COPAUA0C.cbl:494-498 |
| `C001` | C | C | `FAILED TO READ XREF FILE` | 5100. // source: COPAUA0C.cbl:502-509 |
| `A002` | W | A | `ACCT NOT FOUND IN XREF` *(text says XREF; it is the ACCT file — sic)* | 5200. // source: COPAUA0C.cbl:541-545 |
| `C002` | C | C | `FAILED TO READ ACCT FILE` | 5200. // source: COPAUA0C.cbl:550-557 |
| `A003` | W | A | `CUST NOT FOUND IN XREF` *(text says XREF; it is the CUST file — sic)* | 5300. // source: COPAUA0C.cbl:589-593 |
| `C003` | C | C | `FAILED TO READ CUST FILE` | 5300. // source: COPAUA0C.cbl:598-605 |
| `I002` | C | I | `IMS GET SUMMARY FAILED` | 5500. // source: COPAUA0C.cbl:633-637 |
| `M004` | C | M | `FAILED TO PUT ON REPLY MQ` | 7100. // source: COPAUA0C.cbl:768-776 |
| `I003` | C | I | `IMS UPDATE SUMRY FAILED` | 8400. // source: COPAUA0C.cbl:840-844 |
| `I004` | C | I | `IMS INSERT DETL FAILED` | 8500. // source: COPAUA0C.cbl:925-929 |
| `M005` | W | M | `FAILED TO CLOSE REQUEST MQ` | 9100. // source: COPAUA0C.cbl:966-973 |

- `ERR-CODE-1`/`ERR-CODE-2` carry the converted numeric COMPCODE/RESP and REASON/RESP2 (via `WS-CODE-DISPLAY 9(9)`). `ERR-EVENT-KEY` carries the offending key (card num, acct id, or cust id). Level `C` (critical) → the program **abends/RETURNs** via 9990; level `W` (warning) → logs and continues. // source: COPAUA0C.cbl:276-279,1008-1010

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **`PA-TRANSACTION-AMT` used before it is set (declined-amount accumulation).** In 8400-UPDATE-SUMMARY the declined branch does `ADD PA-TRANSACTION-AMT TO PA-DECLINED-AUTH-AMT`, but `PA-TRANSACTION-AMT` (the *detail* segment field) is not populated until 8500-INSERT-AUTH (which runs *after* 8400). So the declined-amount total accumulates the **prior** value of `PA-TRANSACTION-AMT` (zero on a freshly-INITIALIZEd new summary; the previous message's amount on an existing summary), not the current request amount. Approved totals correctly use `WS-APPROVED-AMT`. // source: COPAUA0C.cbl:820-821 vs 885,790-791
2. **CUSTOMER record read but never used.** 5300-READ-CUST-RECORD reads CUSTDAT into CUSTOMER-RECORD, but no customer field participates in the decision (6000) or the writes (8400/8500). The read exists only to set the found/not-found flag (which influences reason `'3100'`) and to log a warning. // source: COPAUA0C.cbl:571-609,665-717
3. **Decline reasons 4200/4300/5100/5200 are dead.** The flags `CARD-NOT-ACTIVE`, `ACCOUNT-CLOSED`, `CARD-FRAUD`, `MERCHANT-FRAUD` are declared but never `SET` anywhere; the corresponding EVALUATE arms can never execute. Card active-status / account-closed / fraud checks are not implemented in this version (5600-READ-PROFILE-DATA is a `CONTINUE` stub). // source: COPAUA0C.cbl:140-145,647-650,707-714
4. **Wrong subsystem flag on the MQGET error.** The `M003` "FAILED TO READ REQUEST MQ" error sets `ERR-CICS` (subsystem 'C') instead of `ERR-MQ` ('M'), even though it is an MQ failure. // source: COPAUA0C.cbl:419-421
5. **Misleading "XREF" wording in ACCT/CUST not-found messages.** `A002`='ACCT NOT FOUND IN XREF' and `A003`='CUST NOT FOUND IN XREF' both say "IN XREF" although they report the ACCT and CUST master reads respectively. // source: COPAUA0C.cbl:544,592
6. **STRING POINTER starts at 1, not reset per message.** `WS-RESP-LENGTH PIC S9(4) VALUE 1`; 6000 builds the reply `WITH POINTER WS-RESP-LENGTH` so after the STRING it equals (built-length + 1) and `W02-BUFFLEN = WS-RESP-LENGTH` is used as the PUT length. It is never re-initialized to 1 before each message's STRING, so on the **2nd and subsequent** messages the STRING begins writing at the leftover pointer position (past the start of the buffer) and the PUT length keeps growing. Faithful: preserve the cumulative-pointer behavior exactly (the .NET STRING-emulation must thread the same non-reset pointer). // source: COPAUA0C.cbl:46,722-731,756
7. **Optimistic presets mask the no-account path on the decision.** 5000 presets `CARD-FOUND-XREF` and `FOUND-ACCT-IN-MSTR` to TRUE before the reads; if XREF is found but the ACCT read is NOT executed (only runs under CARD-FOUND-XREF) the presets are corrected by each read. But if XREF is *not* found, the acct/cust/summary reads are skipped and `FOUND-ACCT-IN-MSTR` is forced to `N` inside 5100 — so 6000's "else if FOUND-ACCT-IN-MSTR" is false and it declines via the bare `SET DECLINE-AUTH`. This is the intended path; documented so the .NET port keeps the exact flag-preset ordering (do not "clean up" the presets). // source: COPAUA0C.cbl:441-457,490-492,672-682
8. **`MOVE 0 TO PA-CASH-BALANCE` only on approve.** The cash balance is zeroed on every approved auth but left untouched on a decline (it keeps its prior/INITIALIZEd value). // source: COPAUA0C.cbl:818
9. **Cash limit/credit limit copied from ACCOUNT unconditionally.** 8400 copies `ACCT-CREDIT-LIMIT`/`ACCT-CASH-CREDIT-LIMIT` into the summary even when the account read failed (NFOUND-ACCT-IN-MSTR), using stale ACCOUNT-RECORD contents. No guard. // source: COPAUA0C.cbl:810-811

> Log each of the above in `_design/faithful-bugs.md` with a pinning test.

---

## 8. PORT NOTES (relational-access translation plan + tricky COBOL semantics)

**Project placement.** Implement as a `CardDemo.Mq` server handler (the drain loop + MQ envelope per MQ_SHIM.md §6.2, request queue `AWS.M2.CARDDEMO.PAUTH.REQUEST`, reply via `request.ReplyToQueue`). Business reads use `CardDemo.Data` repositories (`ICardXrefRepo`, `IAccountRepo`, `ICustomerRepo`) → `CARD_XREF`/`ACCOUNT`/`CUSTOMER`. IMS writes use `CardDemo.Ims` repositories over `PAUT_SUMMARY`/`PAUT_DETAIL` (IMS_SCHEMA.md §2–§3). The error log is an injected `IErrorLog` (not a queue). // source: §1 mapping; MQ_SHIM.md §6; IMS_SCHEMA.md §2

**VSAM → SQL (CICS READ key):**
- XREF: `SELECT cust_id, acct_id FROM CARD_XREF WHERE xref_card_num=@c` → '00'/'23'.
- ACCOUNT: `SELECT * FROM ACCOUNT WHERE acct_id=@a` → '00'/'23'.
- CUSTOMER: `SELECT * FROM CUSTOMER WHERE cust_id=@u` → '00'/'23' (result discarded by logic; still execute for parity flag + log).
RESP NORMAL→found, NOTFND→not-found, other RESP→critical (abend path). // source: COPAUA0C.cbl:475-513,523-561,571-609

**IMS DL/I → SQL:**
- GU summary: `SELECT * FROM PAUT_SUMMARY WHERE ACCT_ID=@a LIMIT 1` (`'  '`/`GE`).
- REPL summary: `UPDATE PAUT_SUMMARY SET <all non-key cols> WHERE ACCT_ID=@a` (must NOT touch ACCT_ID).
- ISRT summary: `INSERT INTO PAUT_SUMMARY (...)` (dup→`II`).
- path ISRT detail: `INSERT INTO PAUT_DETAIL (ACCT_ID, AUTH_KEY, AUTH_DATE_9C, AUTH_TIME_9C, ...)` with `AUTH_KEY` = 8-byte packed concatenation of the two 9C fields; FK to PAUT_SUMMARY enforces the `WHERE(ACCNTID=)` parent locate.
- SCHD/TERM → open/close unit of work; `TC` (scheduled twice) → close+reopen; SYNCPOINT → COMMIT (one per message). // source: COPAUA0C.cbl:619-624,824-833,913-919,292-318,334-336; IMS_SCHEMA.md §3

**9s-complement keys.** `PA-AUTH-DATE-9C = 99999 - yyddd`, `PA-AUTH-TIME-9C = 999999999 - (hhmmss*1000 + ms)`. Store the complemented values as-is so ascending `(ACCT_ID, AUTH_KEY)` order == newest-first (matches COPAUS0C paging and the CBPAUP0C purge). Derive `WS-YYDDD` from the **first 5 chars** of CICS FORMATTIME `YYDDD` (`WS-CUR-DATE-X6(1:5)`), and `WS-CUR-TIME-N6` from FORMATTIME `TIME` (HHMMSS). // source: COPAUA0C.cbl:861-875; IMS_SCHEMA.md §3.7

**INITIALIZE … REPLACING NUMERIC DATA BY ZERO.** When creating a new summary, all numeric subfields (COMP/COMP-3) → 0, alphanumeric fields → SPACES (default INITIALIZE), then ACCT-ID/CUST-ID are overlaid. Reproduce: construct a fresh `PautSummary` with all numeric = 0m / 0 and char fields = spaces, then set acct/cust id. // source: COPAUA0C.cbl:802-806

**REDEFINES for RIDFLD.** `WS-CARD-RID-ACCT-ID 9(11)` / `WS-CARD-RID-CUST-ID 9(9)` are moved the numeric XREF ids, then their `…-X` X-alias is passed as the CICS RIDFLD (zoned-display digits as a character key). In the relational port just bind the numeric `acct_id`/`cust_id` to the PK parameter (the X-alias is only a CICS keying mechanism). // source: COPAUA0C.cbl:72-79,523,571

**Edited PIC / numeric conversions.**
- Request amount: text `WS-TRANSACTION-AMT-AN X(13)` → `FUNCTION NUMVAL` → `PA-RQ-TRANSACTION-AMT +9(10).99` (signed edited) → `WS-TRANSACTION-AMT S9(10)V99`. Use Runtime NUMVAL-equivalent (strip edit chars, parse decimal). // source: COPAUA0C.cbl:354-379
- Reply amount: `WS-APPROVED-AMT S9(10)V99` → `WS-APPROVED-AMT-DIS -zzzzzzzzz9.99` (CobolEditedNumeric: leading-zero suppression to one mandatory digit, floating sign as leading space for non-negative, '.' fixed). This edited string is what goes into the CSV reply. // source: COPAUA0C.cbl:66,720,727
- `WS-CODE-DISPLAY 9(9)`: binary RESP/COMPCODE/REASON moved to a 9-digit display field then to `ERR-CODE-1/2 X(9)` (zoned digits, leading zeros). // source: COPAUA0C.cbl:276-279

**Arithmetic / sign / truncation.** `WS-AVAILABLE-AMT` and the PA-* amounts are `S9(9)V99 COMP-3` (9 integer digits); `WS-TRANSACTION-AMT`/`WS-APPROVED-AMT` are `S9(10)V99` (10 integer digits). All COMPUTE/ADD truncate toward zero with silent high-order overflow; counters are `S9(4)` (wrap at 9999). Comparisons are signed numeric. Use `CobolDecimal` with the per-field digit/scale of the target and no rounding. // source: COPAUA0C.cbl:62-65,666-679,814-821; CIPAUSMY.cpy:23-30

**UNSTRING/STRING semantics.** UNSTRING `DELIMITED BY ','` with 18 receivers — fewer commas leaves trailing receivers unchanged (not space-filled); a value longer than its receiver truncates to the receiver size. STRING `DELIMITED BY SIZE` writes every source incl. trailing spaces, plus a literal ',' after each — including a trailing comma after the amount. Honor the **non-reset `WS-RESP-LENGTH` pointer** (faithful bug #6). // source: COPAUA0C.cbl:354-374,722-731

**Commit ordering.** Reply PUT is committed independently (NO-SYNCPOINT) **before** the IMS summary/detail writes are committed by the per-message `EXEC CICS SYNCPOINT`. Preserve this ordering so a failure between PUT and SYNCPOINT replays the IMS write but not the (already-sent) reply. // source: COPAUA0C.cbl:416-417,461-465,334-336,753-754

---

## 9. OPEN QUESTIONS / RISKS

1. **STRING pointer non-reset (bug #6) interaction with a fixed-size buffer.** On a real run, after the first message `WS-RESP-LENGTH` ≈ (len+1); subsequent STRINGs start writing mid-buffer and `W02-BUFFLEN` grows, eventually overrunning `W02-PUT-BUFFER X(200)` (COBOL truncates STRING on overflow without ON OVERFLOW). The exact observable reply for messages 2..N depends on this; the characterization fixture must be captured against the COBOL semantics (cumulative pointer, 200-byte cap, STRING overflow = silent stop). Flag for a dedicated pinning test.
2. **`PA-DECLINED-AUTH-AMT` accumulation (bug #1)** depends on whether the summary pre-existed (carries prior `PA-TRANSACTION-AMT`) vs. new (zero). The golden fixture must cover both: first decline on a new account (adds 0) and a decline following a prior detail on an existing account (adds the prior detail's amount).
3. **CICS FORMATTIME field widths.** `YYDDD` returns a value whose first 5 chars are taken (`(1:5)`); confirm the runtime FORMATTIME emulation returns YYDDD in the same byte layout, and that `MILLISECONDS` is 0–999 (drives the time-9C key). Timestamps must be masked/seeded in tests for determinism (per ARCHITECTURE.md §Verification).
4. **Two PCB OD/MD pairs share one HCONN.** The program uses `W01-HCONN-REQUEST` (GET) and `W02-HCONN-REPLY` (PUT1), both left at `VALUE 0`. Under CICS-MQ the adapter supplies the handle; the in-proc shim treats it as a single ambient context (MQ_SHIM.md note). No action beyond using the shim.
5. **`DFHCOMMAREA` unused.** Confirmed no COMMAREA flow; safe to ignore the LINKAGE area entirely. // source: COPAUA0C.cbl:214-215
