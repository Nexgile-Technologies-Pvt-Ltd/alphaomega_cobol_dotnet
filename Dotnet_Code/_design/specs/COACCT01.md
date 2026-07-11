# COACCT01 — Port Spec (MQ-triggered Account-Inquiry server)

> Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-vsam-mq/cbl/COACCT01.cbl`
> Companion: `_design/specs/optional/MQ_SHIM.md` (the MQ transport/envelope contract — authoritative
> for queues, MQMD correlation, GET/PUT options). This spec pins COACCT01's **own** behavior: how it
> opens its queues, reads the **ACCOUNT** table, builds the reply, and its paragraph flow.
> Architecture: `_design/ARCHITECTURE.md` (type map, VSAM→SQL repository contract, faithful-bug rule).

Target project: `src/CardDemo.Mq` (server handler) + uses `src/CardDemo.Data` ACCOUNT repository and
`src/CardDemo.Domain` `Account` entity. **No C# is written here — spec only.**

---

## 1. Purpose

COACCT01 is a **server-side, MQ-triggered CICS transaction** that answers account-detail inquiries
asynchronously over IBM MQ. It is started by the CICS-MQ adapter (trigger), retrieves the triggering
queue name from the MQ Trigger Message (`MQTM`), opens an error queue, the trigger-supplied request
queue (for GET), and a hard-coded reply queue (for PUT). It then **drains** the request queue in a
loop: for each request message it expects function `INQA` + an 11-digit account id, performs a keyed
**READ of the `ACCTDAT` VSAM file** (the relational **ACCOUNT** table), formats a labeled fixed-width
text snapshot of the account into the reply, and PUTs it to the reply queue. Invalid function/key or
not-found requests get an `INVALID REQUEST PARAMETERS ...` text reply; other read errors are written
to the MQ error queue and the program terminates.
`// source: COACCT01.cbl:2 (PROGRAM-ID COACCT01 IS INITIAL)`, `// source: COACCT01.cbl:325-460`.

### How it is invoked
- **CICS TRANSID: `CDRA`** → `PROGRAM(COACCT01)`. `// source: csd/CRDDEMOM.csd:5,17-18`.
- **Trigger:** MQ trigger monitor starts CDRA; program does `EXEC CICS RETRIEVE INTO(MQTM)` to learn
  the request queue name. `// source: COACCT01.cbl:191-195`.
- **`IS INITIAL`** clause → fresh program state on every invocation (WORKING-STORAGE re-initialized to
  its VALUE clauses each start). `// source: COACCT01.cbl:2`.
- Not LINKed/XCTLed by another CardDemo program; the requester is external (a POS/MQ client, not in
  the repo — the shim supplies the requester side; see MQ_SHIM §6.3).
- Ends with `EXEC CICS RETURN` + `GOBACK`. `// source: COACCT01.cbl:549-550`.

---

## 2. FILE / TABLE access

| COBOL file (DDNAME/DATASET) | Operation | RIDFLD / key | Relational target (ARCHITECTURE.md) | SQL equivalent |
| --- | --- | --- | --- | --- |
| `ACCTDAT ` (`LIT-ACCTFILENAME PIC X(8) VALUE 'ACCTDAT '`) | `EXEC CICS READ` (keyed, direct) | `WS-CARD-RID-ACCT-ID-X` (11-char acct id), `KEYLENGTH 11` | **ACCOUNT** (PK `acct_id` 9(11)) | `SELECT * FROM ACCOUNT WHERE acct_id = @id` |

`// source: COACCT01.cbl:115-116 (LIT-ACCTFILENAME)`, `// source: COACCT01.cbl:396-404 (EXEC CICS READ)`.

- This is a **read-only** program: no WRITE/REWRITE/DELETE/STARTBR/READNEXT. Single keyed direct READ.
- The READ target structure is `ACCOUNT-RECORD` from copybook `CVACT01Y` (300-byte record, 122 used +
  178 FILLER). `// source: COACCT01.cbl:171 (COPY CVACT01Y)`, `// source: cpy/CVACT01Y.cpy:4-17`.
- **RIDFLD note:** the key is supplied as the **alphanumeric redefinition** `WS-CARD-RID-ACCT-ID-X`
  (`PIC X(11)`), which redefines the numeric `WS-CARD-RID-ACCT-ID` (`PIC 9(11)`). The numeric form is
  loaded from `WS-KEY` first, then the X(11) view is passed as the key. `// source: COACCT01.cbl:121-128`,
  `// source: COACCT01.cbl:394,398-399`. For VSAM the key is the zoned-display image of the 11 digits;
  on the relational side this is simply the integer `acct_id` (zero-padded to 11 only matters for the
  reply text, not the lookup).

### MQ "files" (queues) — see MQ_SHIM.md for full envelope
| Queue (logical) | COACCT01 use | Origin |
| --- | --- | --- |
| `CARD.DEMO.ERROR` | output (errors) — opened first | literal `// source: COACCT01.cbl:294` |
| request queue (trigger-supplied) | input GET (drained in loop) | `MQTM-QNAME` → `INPUT-QUEUE-NAME` `// source: COACCT01.cbl:197` |
| `CARD.DEMO.REPLY.ACCT` | output PUT (replies) | hard-coded literal `// source: COACCT01.cbl:198` |

MQ open/get/put/close option flags, MQMD correlation echo, and the faithful MQ bugs are all pinned in
`MQ_SHIM.md` §3-§5; not repeated in full here. The **business** translation (ACCOUNT read + reply
text) is this spec's responsibility.

---

## 3. WORKING-STORAGE highlights (fields with logic significance)

- `WS-MQ-MSG-FLAG` X(1) VALUE 'N'; 88 `NO-MORE-MSGS` VALUE 'Y' — loop terminator.
  `// source: COACCT01.cbl:13-14`.
- `WS-RESP-QUEUE-STS` 88 `RESP-QUEUE-OPEN` (output/reply queue open flag — note name mismatch, see
  faithful bug FB-1). `// source: COACCT01.cbl:16-17`.
- `WS-ERR-QUEUE-STS` 88 `ERR-QUEUE-OPEN`. `// source: COACCT01.cbl:19-20`.
- `WS-REPLY-QUEUE-STS` 88 `REPLY-QUEUE-OPEN` (input queue open flag — see FB-1). `// source: COACCT01.cbl:22-23`.
- `WS-CICS-RESP1-CD`/`-RESP2-CD` `S9(8) COMP`; display copies `-CD-D` `9(8)`. `// source: COACCT01.cbl:26-30`.
- `REQUEST-MSG-COPY`: `WS-FUNC X(04)`, `WS-KEY 9(11)`, `WS-FILLER X(985)` — the request layout.
  `// source: COACCT01.cbl:109-112`.
- `LIT-ACCTFILENAME PIC X(8) VALUE 'ACCTDAT '` (note trailing space). `// source: COACCT01.cbl:115-116`.
- `WS-RESP-CD`/`WS-REAS-CD` `S9(09) COMP` — CICS READ RESP/RESP2. `// source: COACCT01.cbl:117-120`.
- `WS-XREF-RID` group: `WS-CARD-RID-CARDNUM X(16)`, `WS-CARD-RID-CUST-ID 9(9)` (+X redef),
  `WS-CARD-RID-ACCT-ID 9(11)` (+`-X` redef X(11)). Only the ACCT-ID + its X redef are used.
  `// source: COACCT01.cbl:121-128`.
- `WS-ACCT-RESPONSE` group — the labeled reply block (see §6 for exact layout). `// source: COACCT01.cbl:130-169`.
- `ACCOUNT-RECORD` (COPY CVACT01Y) — READ target. `// source: COACCT01.cbl:171`, `cpy/CVACT01Y.cpy`.

---

## 4. ACCOUNT-RECORD layout (CVACT01Y) → ACCOUNT table columns

| COBOL field | PIC | ACCOUNT column | Moved into reply field |
| --- | --- | --- | --- |
| `ACCT-ID` | 9(11) | `acct_id` | `WS-ACCT-ID` |
| `ACCT-ACTIVE-STATUS` | X(01) | `active_status` | `WS-ACCT-ACTIVE-STATUS` |
| `ACCT-CURR-BAL` | S9(10)V99 | `curr_bal` | `WS-ACCT-CURR-BAL` |
| `ACCT-CREDIT-LIMIT` | S9(10)V99 | `credit_limit` | `WS-ACCT-CREDIT-LIMIT` |
| `ACCT-CASH-CREDIT-LIMIT` | S9(10)V99 | `cash_credit_limit` | `WS-ACCT-CASH-CREDIT-LIMIT` |
| `ACCT-OPEN-DATE` | X(10) | `open_date` | `WS-ACCT-OPEN-DATE` |
| `ACCT-EXPIRAION-DATE` | X(10) | `expiration_date` (COBOL name `EXPIRAION`, sic) | `WS-ACCT-EXPIRAION-DATE` |
| `ACCT-REISSUE-DATE` | X(10) | `reissue_date` | `WS-ACCT-REISSUE-DATE` |
| `ACCT-CURR-CYC-CREDIT` | S9(10)V99 | `curr_cyc_credit` | `WS-ACCT-CURR-CYC-CREDIT` |
| `ACCT-CURR-CYC-DEBIT` | S9(10)V99 | `curr_cyc_debit` | `WS-ACCT-CURR-CYC-DEBIT` |
| `ACCT-ADDR-ZIP` | X(10) | `addr_zip` | *(not used in reply)* |
| `ACCT-GROUP-ID` | X(10) | `group_id` | `WS-ACCT-GROUP-ID` |
| `FILLER` | X(178) | — | (spaces) |

`// source: cpy/CVACT01Y.cpy:4-17`. The MOVE mapping is at `// source: COACCT01.cbl:408-425`.
**Not copied to the reply:** `ACCT-ADDR-ZIP` (read but never surfaced). The reply omits ZIP.

---

## 5. PARAGRAPH-BY-PARAGRAPH outline (each = one method)

> Method names below mirror the paragraph names. Statement order, PERFORM chains, and EVALUATE/IF
> branches are preserved exactly. MQ CALL details are summarized; full option semantics in MQ_SHIM §4.

### 1000-CONTROL  `// source: COACCT01.cbl:178-220`
Entry point / driver.
1. `MOVE SPACES TO INPUT-QUEUE-NAME, QMGR-NAME, QUEUE-MESSAGE`; `INITIALIZE MQ-ERR-DISPLAY`. `// source: COACCT01.cbl:180-185`.
2. `PERFORM 2100-OPEN-ERROR-QUEUE` (open `CARD.DEMO.ERROR` first). `// source: COACCT01.cbl:187`.
3. `EXEC CICS RETRIEVE INTO(MQTM)` with RESP/RESP2. `// source: COACCT01.cbl:191-195`.
4. If RESP1 = `DFHRESP(NORMAL)`: `MOVE MQTM-QNAME TO INPUT-QUEUE-NAME`; `MOVE 'CARD.DEMO.REPLY.ACCT' TO REPLY-QUEUE-NAME`. `// source: COACCT01.cbl:196-198`.
5. Else: build a `'CICS RETREIVE'` (sic) error label + STRING `'RESP: '...'END'` into
   `MQ-APPL-RETURN-MESSAGE`, `PERFORM 9000-ERROR` then `PERFORM 8000-TERMINATION`. `// source: COACCT01.cbl:199-210`.
   - Note FB-3: `MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD` (no-op self-move; intended `-CD-D`). `// source: COACCT01.cbl:202`.
6. `PERFORM 2300-OPEN-INPUT-QUEUE`; `PERFORM 2400-OPEN-OUTPUT-QUEUE`. `// source: COACCT01.cbl:212-213`.
7. `PERFORM 3000-GET-REQUEST` (prime: get first message). `// source: COACCT01.cbl:214`.
8. `PERFORM 4000-MAIN-PROCESS UNTIL NO-MORE-MSGS`. `// source: COACCT01.cbl:215-216`.
9. `PERFORM 8000-TERMINATION`. `// source: COACCT01.cbl:218`.

### 2300-OPEN-INPUT-QUEUE  `// source: COACCT01.cbl:222-253`
Open the trigger-supplied request queue for GET.
- `MOVE SPACES TO MQOD-OBJECTQMGRNAME`; `MOVE INPUT-QUEUE-NAME TO MQOD-OBJECTNAME`. `// source: COACCT01.cbl:226-227`.
- `COMPUTE MQ-OPTIONS = MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. `// source: COACCT01.cbl:229-231`.
- `CALL 'MQOPEN' USING QMGR-HANDLE-CONN ...`. `// source: COACCT01.cbl:233-238`.
- EVALUATE MQ-CONDITION-CODE: WHEN `MQCC-OK` → save handle to `INPUT-QUEUE-HANDLE`, `SET REPLY-QUEUE-OPEN TO TRUE` (FB-1: input queue sets the *reply* flag). WHEN OTHER → `'INP MQOPEN ERR'`, `9000-ERROR`, `8000-TERMINATION`. `// source: COACCT01.cbl:240-253`.

### 2400-OPEN-OUTPUT-QUEUE  `// source: COACCT01.cbl:255-287`
Open `CARD.DEMO.REPLY.ACCT` (REPLY-QUEUE-NAME) for PUT.
- `MOVE REPLY-QUEUE-NAME TO MQOD-OBJECTNAME`. `// source: COACCT01.cbl:261`.
- `COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. `// source: COACCT01.cbl:263-265`.
- `CALL 'MQOPEN'`. WHEN `MQCC-OK` → handle to `OUTPUT-QUEUE-HANDLE`, `SET RESP-QUEUE-OPEN TO TRUE`. WHEN OTHER → `'OUT MQOPEN ERR'`, `9000-ERROR`, `8000-TERMINATION`. `// source: COACCT01.cbl:267-287`.

### 2100-OPEN-ERROR-QUEUE  `// source: COACCT01.cbl:289-322`
Open `CARD.DEMO.ERROR` for PUT (error sink).
- `MOVE 'CARD.DEMO.ERROR' TO ERROR-QUEUE-NAME`; → `MQOD-OBJECTNAME`. `// source: COACCT01.cbl:294-296`.
- `COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. `// source: COACCT01.cbl:298-300`.
- `CALL 'MQOPEN'`. WHEN `MQCC-OK` → handle to `ERROR-QUEUE-HANDLE`, `SET ERR-QUEUE-OPEN TO TRUE`. WHEN OTHER → `'ERR MQOPEN ERR'`, **`DISPLAY MQ-ERR-DISPLAY`** then `8000-TERMINATION` (note: no `9000-ERROR` here — can't write errors if the error queue itself failed). `// source: COACCT01.cbl:309-322`.

### 4000-MAIN-PROCESS  `// source: COACCT01.cbl:325-331`
Per-message loop body (performed UNTIL NO-MORE-MSGS).
- `EXEC CICS SYNCPOINT END-EXEC` (commit previous message's UOW). `// source: COACCT01.cbl:326-328`.
- `PERFORM 3000-GET-REQUEST` (get next message; sets NO-MORE-MSGS on empty). `// source: COACCT01.cbl:330`.

### 3000-GET-REQUEST  `// source: COACCT01.cbl:334-388`
GET one request message and dispatch it.
1. `MOVE 5000 TO MQGMO-WAITINTERVAL` (5 s wait). `// source: COACCT01.cbl:337`.
2. `MOVE SPACES TO MQ-CORRELID, MQ-MSG-ID`; `MOVE INPUT-QUEUE-NAME TO MQ-QUEUE`; `MOVE INPUT-QUEUE-HANDLE TO MQ-HOBJ`; `MOVE 1000 TO MQ-BUFFER-LENGTH`. `// source: COACCT01.cbl:338-342`.
3. `MOVE MQMI-NONE TO MQMD-MSGID`; `MOVE MQCI-NONE TO MQMD-CORRELID` (take any next msg). `// source: COACCT01.cbl:343-344`.
4. `INITIALIZE REQUEST-MSG-COPY REPLACING NUMERIC BY ZEROES` (clears WS-FUNC→spaces, WS-KEY→0, WS-FILLER→spaces). `// source: COACCT01.cbl:345`.
5. `COMPUTE MQGMO-OPTIONS = MQGMO-SYNCPOINT + MQGMO-FAIL-IF-QUIESCING + MQGMO-CONVERT + MQGMO-WAIT`. `// source: COACCT01.cbl:347-350`.
6. `CALL 'MQGET' USING MQ-HCONN ...` (FB-2: `MQ-HCONN`, not `QMGR-HANDLE-CONN`). `// source: COACCT01.cbl:352-360`.
7. IF `MQ-CONDITION-CODE = MQCC-OK`:
   - save `MQMD-MSGID`→`MQ-MSG-ID`, `MQMD-CORRELID`→`MQ-CORRELID`, `MQMD-REPLYTOQ`→`MQ-QUEUE-REPLY`. `// source: COACCT01.cbl:364-366`.
   - `MOVE MQ-BUFFER TO REQUEST-MESSAGE`; save MsgId/CorrelId/ReplyToQ to `SAVE-*`. `// source: COACCT01.cbl:369-372`.
   - `MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY` (re-overlays the X(1000) buffer onto FUNC/KEY/FILLER). `// source: COACCT01.cbl:373`.
   - `PERFORM 4000-PROCESS-REQUEST-REPLY`. `// source: COACCT01.cbl:374`.
   - `ADD 1 TO MQ-MSG-COUNT` (FB-4: dead counter, never read). `// source: COACCT01.cbl:375`.
8. ELSE: IF `MQ-REASON-CODE = MQRC-NO-MSG-AVAILABLE` → `SET NO-MORE-MSGS TO TRUE`. ELSE → `'INP MQGET ERR:'`, `9000-ERROR`, `8000-TERMINATION`. `// source: COACCT01.cbl:376-388`.

### 4000-PROCESS-REQUEST-REPLY  `// source: COACCT01.cbl:390-460` — **core business logic**
Build the reply for one parsed request.
1. `MOVE SPACES TO REPLY-MESSAGE`; `INITIALIZE WS-DATE-TIME REPLACING NUMERIC BY ZEROES`. `// source: COACCT01.cbl:391-392`.
2. **IF `WS-FUNC = 'INQA' AND WS-KEY > ZEROES`** → valid inquiry: `// source: COACCT01.cbl:393`
   a. `MOVE WS-KEY TO WS-CARD-RID-ACCT-ID` (numeric → loads the 11-digit zoned image; X-redef view used as RIDFLD). `// source: COACCT01.cbl:394`.
   b. `EXEC CICS READ DATASET(LIT-ACCTFILENAME) RIDFLD(WS-CARD-RID-ACCT-ID-X) KEYLENGTH(11) INTO(ACCOUNT-RECORD) LENGTH(LENGTH OF ACCOUNT-RECORD) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. `// source: COACCT01.cbl:396-404`.
   c. **EVALUATE WS-RESP-CD:** `// source: COACCT01.cbl:406`
      - **WHEN `DFHRESP(NORMAL)`** → MOVE 11 account fields (ID, ACTIVE-STATUS, CURR-BAL, CREDIT-LIMIT, CASH-CREDIT-LIMIT, OPEN-DATE, EXPIRAION-DATE, REISSUE-DATE, CURR-CYC-CREDIT, CURR-CYC-DEBIT, GROUP-ID) into the `WS-ACCT-*` reply fields; `MOVE WS-ACCT-RESPONSE TO REPLY-MESSAGE`; `PERFORM 4100-PUT-REPLY`. `// source: COACCT01.cbl:407-427`.
        (numeric S9(10)V99 → S9(10)V99 moves: same scale, no truncation. ADDR-ZIP intentionally not moved.)
      - **WHEN `DFHRESP(NOTFND)`** → `STRING 'INVALID REQUEST PARAMETERS ' 'ACCT ID : ' WS-KEY DELIMITED BY SIZE INTO REPLY-MESSAGE`; `PERFORM 4100-PUT-REPLY`. `// source: COACCT01.cbl:428-435`.
      - **WHEN OTHER** → set `MQ-APPL-CONDITION-CODE = WS-RESP-CD`, `MQ-APPL-REASON-CODE = WS-REAS-CD`, `MQ-APPL-QUEUE-NAME = INPUT-QUEUE-NAME`, `MQ-APPL-RETURN-MESSAGE = 'ERROR WHILE READING ACCTFILE'`; `PERFORM 9000-ERROR`; `PERFORM 8000-TERMINATION`. `// source: COACCT01.cbl:437-447`.
3. **ELSE** (bad func or key not > 0) → `STRING 'INVALID REQUEST PARAMETERS ' 'ACCT ID : ' WS-KEY 'FUNCTION : ' WS-FUNC DELIMITED BY SIZE INTO REPLY-MESSAGE`; `PERFORM 4100-PUT-REPLY`. `// source: COACCT01.cbl:448-456`.

### 4100-PUT-REPLY  `// source: COACCT01.cbl:462-499`
PUT the built reply to `CARD.DEMO.REPLY.ACCT`.
- `MOVE REPLY-MESSAGE TO MQ-BUFFER`; `MOVE 1000 TO MQ-BUFFER-LENGTH`. `// source: COACCT01.cbl:467-468`.
- `MOVE SAVE-MSGID TO MQMD-MSGID`; `MOVE SAVE-CORELID TO MQMD-CORRELID` (echo correlation). `// source: COACCT01.cbl:469-470`.
- `MOVE MQFMT-STRING TO MQMD-FORMAT`; `COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR`. `// source: COACCT01.cbl:471-473`.
- `COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING`. `// source: COACCT01.cbl:475-477`.
- `CALL 'MQPUT' USING MQ-HCONN OUTPUT-QUEUE-HANDLE ...` (FB-2: MQ-HCONN). `// source: COACCT01.cbl:479-486`.
- EVALUATE: WHEN `MQCC-OK` → record codes. WHEN OTHER → `'MQPUT ERR'`, `9000-ERROR`, `8000-TERMINATION`. `// source: COACCT01.cbl:488-499`.

### 9000-ERROR  `// source: COACCT01.cbl:501-537`
PUT the `MQ-ERR-DISPLAY` block to `CARD.DEMO.ERROR`.
- `MOVE MQ-ERR-DISPLAY TO ERROR-MESSAGE`; `MOVE ERROR-MESSAGE TO MQ-BUFFER`; `MOVE 1000 TO MQ-BUFFER-LENGTH`. `// source: COACCT01.cbl:505-507`.
- `MOVE MQFMT-STRING TO MQMD-FORMAT`; `COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR`. `// source: COACCT01.cbl:508-510`.
- `COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING`. `// source: COACCT01.cbl:512-514`.
- `CALL 'MQPUT' USING MQ-HCONN ERROR-QUEUE-HANDLE ...`. `// source: COACCT01.cbl:516-523`.
- EVALUATE: WHEN `MQCC-OK` → record codes. WHEN OTHER → `'MQPUT ERR'`, `DISPLAY MQ-ERR-DISPLAY`, `8000-TERMINATION` (no recursion into 9000). `// source: COACCT01.cbl:525-536`.

### 8000-TERMINATION  `// source: COACCT01.cbl:538-550`
Close open queues and return.
- IF `REPLY-QUEUE-OPEN` → `5000-CLOSE-INPUT-QUEUE` (closes the **input** queue — flag/name mismatch, FB-1). `// source: COACCT01.cbl:540-542`.
- IF `RESP-QUEUE-OPEN` → `5100-CLOSE-OUTPUT-QUEUE`. `// source: COACCT01.cbl:543-545`.
- IF `ERR-QUEUE-OPEN` → `5200-CLOSE-ERROR-QUEUE`. `// source: COACCT01.cbl:546-548`.
- `EXEC CICS RETURN END-EXEC`; `GOBACK`. `// source: COACCT01.cbl:549-550`.

### 5000-CLOSE-INPUT-QUEUE  `// source: COACCT01.cbl:552-573`
`MOVE INPUT-QUEUE-NAME/INPUT-QUEUE-HANDLE`; `COMPUTE MQ-OPTIONS = MQCO-NONE`; `CALL 'MQCLOSE'`; on error `'MQCLOSE ERR'` → **`8000-TERMINATION`** (re-enters termination — see FB-5 recursion risk). `// source: COACCT01.cbl:552-573`.

### 5100-CLOSE-OUTPUT-QUEUE  `// source: COACCT01.cbl:574-595`
Closes `REPLY-QUEUE-NAME`/`OUTPUT-QUEUE-HANDLE` with `MQCO-NONE`; error → `'MQCLOSE ERR'` → `8000-TERMINATION` (uses `INPUT-QUEUE-NAME` in the error msg — copy/paste, harmless). `// source: COACCT01.cbl:574-595`.

### 5200-CLOSE-ERROR-QUEUE  `// source: COACCT01.cbl:597-619`
Closes `ERROR-QUEUE-NAME`/`ERROR-QUEUE-HANDLE` with `MQCO-NONE`; error → `'MQCLOSE ERR'` → **`9000-ERROR`** then `8000-TERMINATION`. `// source: COACCT01.cbl:597-619`.

---

## 6. Reply message layout (WS-ACCT-RESPONSE) — exact bytes

Built by direct MOVEs into a fixed group, then `MOVE WS-ACCT-RESPONSE TO REPLY-MESSAGE` (which is then
padded to 1000 in `MQ-BUFFER`). The block is a concatenation of label literals + edited values in
declared order. `// source: COACCT01.cbl:130-169`.

| Offset field | Literal / value | PIC | Source line |
| --- | --- | --- | --- |
| `WS-ACCT-LBL` | `'ACCOUNT ID : '` | X(13) | :132-133 |
| `WS-ACCT-ID` | acct id | 9(11) | :134 |
| `WS-STATUS-LBL` | `'ACCOUNT STATUS : '` | X(17) | :135-136 |
| `WS-ACCT-ACTIVE-STATUS` | status | X(01) | :137 |
| `WS-CURR-BAL-LBL` | `'BALANCE : '` | X(10) | :138-139 |
| `WS-ACCT-CURR-BAL` | balance | S9(10)V99 | :140-141 |
| `WS-CRDT-LMT-LBL` | `'CREDIT LIMIT : '` | X(15) | :142-143 |
| `WS-ACCT-CREDIT-LIMIT` | limit | S9(10)V99 | :144-145 |
| `WS-CASH-LIMIT-LBL` | `'CASH LIMIT : '` | X(13) | :146-147 |
| `WS-ACCT-CASH-CREDIT-LIMIT` | cash limit | S9(10)V99 | :148-149 |
| `WS-OPEN-DATE-LBL` | `'OPEN DATE : '` | X(12) | :150-151 |
| `WS-ACCT-OPEN-DATE` | open date | X(10) | :152 |
| `WS-EXPR-DATE-LBL` | `'EXPR DATE : '` | X(12) | :153-154 |
| `WS-ACCT-EXPIRAION-DATE` | expiry date | X(10) | :155 |
| `WS-REISSUE-DT-LBL` | `'REIS DATE : '` | X(12) | :156-157 |
| `WS-ACCT-REISSUE-DATE` | reissue date | X(10) | :158 |
| `WS-CURR-CYC-CREDIT-LBL` | `'CREDIT BAL : '` | X(13) | :159-160 |
| `WS-ACCT-CURR-CYC-CREDIT` | cyc credit | S9(10)V99 | :161-162 |
| `WS-CURR-CYC-DEBIT-LBL` | `'DEBIT BAL : '` | X(12) | :163-164 |
| `WS-ACCT-CURR-CYC-DEBIT` | cyc debit | S9(10)V99 | :165-166 |
| `WS-ACCT-GRP-LBL` | `'GROUP ID : '` | X(11) | :167-168 |
| `WS-ACCT-GROUP-ID` | group id | X(10) | :169 |

**Numeric serialization detail (critical for byte-parity):** `WS-ACCT-CURR-BAL` etc. are declared
`PIC S9(10)V99` (NOT edited — no insertion characters). In the reply block, a `S9(p)V(s)` USAGE
DISPLAY field with no SIGN clause stores as **zoned decimal**: 12 digit bytes, the implied decimal
point is *not* present in the bytes, and the trailing (rightmost) digit carries the **embedded sign
overpunch** (positive → `0..9`, negative → an overpunched character). So `1234.56` (positive) serializes
as the 12 bytes `000000123456`; `-1234.56` serializes as `00000012345O` (the final `6` overpunched to
`O` for a negative sign on EBCDIC; on the ASCII subset CardDemo uses, the codec must reproduce the
mainframe overpunch). The reply contains **no decimal point and no sign character** as separate bytes.
The .NET port MUST format these with the zoned-decimal/overpunch serializer (Runtime), not as a plain
decimal string. `// source: COACCT01.cbl:140-141,144-166`.

`WS-ACCT-ID` `PIC 9(11)` (unsigned zoned) → 11 digit bytes, zero-padded, no sign. `// source: COACCT01.cbl:134`.

---

## 7. VALIDATION RULES and exact literal messages

1. **Function + key gate:** request is processed only if `WS-FUNC = 'INQA' AND WS-KEY > ZEROES`.
   `// source: COACCT01.cbl:393`.
2. **Account not found** (`DFHRESP(NOTFND)` from the READ):
   reply = `'INVALID REQUEST PARAMETERS ACCT ID : '` followed by `WS-KEY` (11 digits).
   `// source: COACCT01.cbl:428-435`. (The `STRING ... DELIMITED BY SIZE` concatenates
   `'INVALID REQUEST PARAMETERS '` (27 chars incl. trailing space) + `'ACCT ID : '` (10 chars) +
   the 11-digit `WS-KEY` zoned image.)
3. **Bad function or non-positive key** (ELSE of the gate):
   reply = `'INVALID REQUEST PARAMETERS ACCT ID : ' + WS-KEY + 'FUNCTION : ' + WS-FUNC`.
   `// source: COACCT01.cbl:448-456`. (Exact pieces: `'INVALID REQUEST PARAMETERS '`, `'ACCT ID : '`,
   `WS-KEY` 9(11), `'FUNCTION : '` (11 chars), `WS-FUNC` X(4).)
4. **Other READ error** (EVALUATE WHEN OTHER): no reply text — instead `MQ-APPL-RETURN-MESSAGE =
   'ERROR WHILE READING ACCTFILE'`, write `MQ-ERR-DISPLAY` to `CARD.DEMO.ERROR`, and terminate.
   `// source: COACCT01.cbl:437-447`.

### Other literal error texts (MQ ops → CARD.DEMO.ERROR via `MQ-APPL-RETURN-MESSAGE`)
- `'CICS RETREIVE'` (sic) — CICS RETRIEVE failed. `// source: COACCT01.cbl:200`.
- `'INP MQOPEN ERR'` — request-queue open failed. `// source: COACCT01.cbl:250`.
- `'OUT MQOPEN ERR'` — reply-queue open failed. `// source: COACCT01.cbl:284`.
- `'ERR MQOPEN ERR'` — error-queue open failed (DISPLAYed, not enqueued). `// source: COACCT01.cbl:319`.
- `'INP MQGET ERR:'` — GET failed (not the empty-queue case). `// source: COACCT01.cbl:384`.
- `'MQPUT ERR'` — reply/error PUT failed. `// source: COACCT01.cbl:496,533`.
- `'MQCLOSE ERR'` — close failed. `// source: COACCT01.cbl:571,593,616`.

---

## 8. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

- **FB-1: Open/close flag-name vs queue mismatch.** Opening the **input** (request) queue does
  `SET REPLY-QUEUE-OPEN TO TRUE`; opening the **output** (reply) queue does `SET RESP-QUEUE-OPEN`.
  At termination `IF REPLY-QUEUE-OPEN PERFORM 5000-CLOSE-INPUT-QUEUE` and
  `IF RESP-QUEUE-OPEN PERFORM 5100-CLOSE-OUTPUT-QUEUE`. The flag names are swapped relative to their
  intuitive meaning but **are internally consistent** (input flag → close input), so behavior is
  correct; preserve the naming so the port reads identically.
  `// source: COACCT01.cbl:245 (input→REPLY-QUEUE-OPEN)`, `:279 (output→RESP-QUEUE-OPEN)`,
  `:540-545`.
- **FB-2: Wrong/duplicate HCONN handle.** `MQGET`/`MQPUT` use `MQ-HCONN` (a separate field at VALUE 0),
  while `MQOPEN`/`MQCLOSE` use `QMGR-HANDLE-CONN` (also VALUE 0). Both are 0 so it works under the
  CICS-MQ adapter (ambient connection). Preserve as a single in-proc connection; do not assume the two
  fields are the same variable. `// source: COACCT01.cbl:233 (MQOPEN→QMGR-HANDLE-CONN)`,
  `:352 (MQGET→MQ-HCONN)`, `:479 (MQPUT→MQ-HCONN)`.
- **FB-3: No-op self-move.** `MOVE WS-CICS-RESP1-CD TO WS-CICS-RESP1-CD-D` is correct, but the next
  line `MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD` moves RESP2 onto itself (intended `-CD-D`), so the
  STRING'd error message references `WS-CICS-RESP2-CD-D` which was never populated (stays 0).
  `// source: COACCT01.cbl:201-203`.
- **FB-4: Dead message counter.** `ADD 1 TO MQ-MSG-COUNT` is never read or output anywhere.
  `// source: COACCT01.cbl:375`.
- **FB-5: ReplyToQ captured then ignored.** `MQMD-REPLYTOQ` is saved to `MQ-QUEUE-REPLY`/`SAVE-REPLY2Q`
  but the PUT always targets the pre-opened literal `CARD.DEMO.REPLY.ACCT` (`OUTPUT-QUEUE-HANDLE`),
  never the requester's ReplyToQ. `// source: COACCT01.cbl:366,371,480`. (Also pinned in MQ_SHIM §1.2.)
- **FB-6: Termination recursion on close error.** `5000-/5100-CLOSE-*` on MQCLOSE error call
  `8000-TERMINATION`, which can re-enter the same close paragraph (the flags are not reset before the
  re-call), risking re-close/recursion. `5200-CLOSE-ERROR-QUEUE` additionally calls `9000-ERROR` which
  itself can call `8000-TERMINATION`. Reproduce the call graph as written; in .NET model termination as
  idempotent (a queue already closed should not loop). `// source: COACCT01.cbl:572,594,617-618,540-548`.
- **FB-7: `'CICS RETREIVE'` misspelling** in the error label. `// source: COACCT01.cbl:200`.
- **FB-8: ACCT-ADDR-ZIP read but dropped** — present in the record, never moved to the reply.
  `// source: cpy/CVACT01Y.cpy:15` (read), absent from reply moves `:407-425`.
- **FB-9: README vs code request/reply divergence.** The README documents a `REQUEST-TYPE 'ACCT'` /
  `RESPONSE-TYPE 'ACCT'` structured layout; the code uses `WS-FUNC = 'INQA'` + 11-digit key and a
  free-text labeled reply. Honor the **code**. `// source: COACCT01.cbl:393`, README lines 114-128.

---

## 9. PORT NOTES (relational-access + COBOL semantics)

- **The whole MQ envelope is delegated to `CardDemo.Mq`** per MQ_SHIM.md. COACCT01's port is the
  *server handler*: it receives a `TriggerMessage`, GETs `REQUEST-MSG-COPY`-shaped bodies, and replies.
  Reuse the shim's queue/correlation contract; this spec only adds the ACCOUNT read + reply formatting.
- **ACCOUNT read → SQL:** the single `EXEC CICS READ ... RIDFLD(acct-id) RESP(...)` maps to the
  ACCOUNT repository keyed read: `SELECT ... FROM ACCOUNT WHERE acct_id = @id`. Map FileStatus/RESP:
  row found → `DFHRESP(NORMAL)`; no row → `DFHRESP(NOTFND)`; any other repo error → WHEN OTHER path
  (write error, terminate). Per ARCHITECTURE.md VSAM→SQL contract (READ key → '00'/'23').
- **Request parsing (REDEFINES-by-MOVE):** the 1000-byte buffer is re-overlaid onto `WS-FUNC X(4)` +
  `WS-KEY 9(11)` + `WS-FILLER X(985)` via `MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY`. In .NET, parse
  the first 4 chars as FUNC, next 11 as a numeric key. `WS-KEY 9(11)` is an unsigned **zoned** field:
  non-numeric content in those 11 bytes would make the `> ZEROES` test behave on the raw zoned value;
  treat as: parse 11 digits, compare > 0. (`INITIALIZE ... REPLACING NUMERIC BY ZEROES` first sets KEY
  to 0 / FUNC,FILLER to spaces, then the overlay replaces them.) `// source: COACCT01.cbl:345,373,393-394`.
- **RIDFLD redefinition:** `WS-CARD-RID-ACCT-ID 9(11)` ← `WS-KEY`, then the `WS-CARD-RID-ACCT-ID-X X(11)`
  redefinition is the actual RIDFLD. For SQL just pass the integer; the X(11) image only matters for
  byte-faithful re-serialization, not the lookup. `// source: COACCT01.cbl:121-128,394,398`.
- **Reply numeric formatting — use the zoned/overpunch serializer.** As detailed in §6, the
  `S9(10)V99` fields in `WS-ACCT-RESPONSE` are **non-edited DISPLAY** → 12 raw digit bytes with no
  decimal point and a signed overpunch on the last digit. Do NOT emit `"-1234.56"` or `"1234.56"`.
  Use `CardDemo.Runtime` fixed-width zoned-decimal formatting to reproduce mainframe bytes for the
  golden screen-parity fixtures. `WS-ACCT-ID` 9(11) → 11 zero-padded digit bytes.
- **String concatenation:** the two `INVALID REQUEST PARAMETERS` paths use `STRING ... DELIMITED BY
  SIZE` (full declared width of each operand, trailing spaces included). The 9(11) `WS-KEY` and X(4)
  `WS-FUNC` contribute their full fixed widths. Reproduce exact widths/positions (no trimming).
- **INITIALIZE semantics:** `INITIALIZE REQUEST-MSG-COPY REPLACING NUMERIC BY ZEROES` → numeric items
  (WS-KEY) to 0, alphanumeric (WS-FUNC, WS-FILLER) to spaces. `INITIALIZE WS-DATE-TIME REPLACING
  NUMERIC BY ZEROES` zeros WS-ABS-TIME and spaces the X fields (WS-DATE-TIME is otherwise unused in
  this program — date/time is never actually formatted; only the date module CODATE01 uses it).
  `// source: COACCT01.cbl:345,392,35-38`.
- **Per-message UOW / SYNCPOINT:** `4000-MAIN-PROCESS` issues `EXEC CICS SYNCPOINT` per message before
  GETting the next; GET/PUT use `MQGMO-SYNCPOINT`/`MQPMO-SYNCPOINT`. The port should commit
  GET+read+PUT together per message (shim syncpoint boundary). `// source: COACCT01.cbl:326-328`.
- **Drain loop:** prime GET (`3000` in 1000-CONTROL) then `PERFORM 4000-MAIN-PROCESS UNTIL
  NO-MORE-MSGS`. Process 0..N messages; stop when GET returns `MQRC-NO-MSG-AVAILABLE` after the 5 s
  wait. `// source: COACCT01.cbl:214-216,376-378`.
- **`IS INITIAL`:** re-initialize working storage to VALUE clauses on each invocation — the handler
  must be stateless across triggers (fresh flags/handles each run). `// source: COACCT01.cbl:2`.

---

## 10. OPEN QUESTIONS / risks

- **MQ copybooks not in repo** (`CMQGMOV`, `CMQPMOV`, `CMQMDV`, `CMQODV`, `CMQV`, `CMQTML`) — standard
  IBM-supplied. The exact byte layout of `MQTM`/MQMD doesn't matter for the relational port (the shim
  abstracts it); only the consumed fields (§ MQ_SHIM §2-§3) matter. `// source: COACCT01.cbl:71,75,79,83,87,90`.
- **Overpunch in reply text:** confirm the golden-fixture harness reproduces the EBCDIC zoned-decimal
  overpunch for the signed `S9(10)V99` reply fields. ARCHITECTURE.md notes the ASCII subset CardDemo
  uses coincides for digits/space/punct; signed overpunch is the one place where EBCDIC vs ASCII
  differs — pin it with a guard test (one positive + one negative balance).
- **`WS-KEY > ZEROES` with non-numeric request bytes:** if a malformed request puts non-digits in the
  key position, COBOL's numeric comparison on a zoned field is implementation-dependent. Treat realistic
  inputs (numeric) only; document the edge in a test. `// source: COACCT01.cbl:393`.
- **Reply queue vs ReplyToQ (FB-5):** confirm tests assert the reply lands on the literal
  `CARD.DEMO.REPLY.ACCT`, never the request's ReplyToQ.
```
```
