# PORT SPEC — CODATE01 (Inquire System Date via MQ, ONLINE / MQ-triggered CICS)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-vsam-mq/cbl/CODATE01.cbl`
> Module: `app-vsam-mq` (CardDemo "Account Extractions using MQ and VSAM" extension).
> Target: `src/CardDemo.Mq` (transport/loop shell + handler). **No relational table is touched by this program** — it has zero VSAM/file/DB access (it only answers with the current date/time). The relational schema in `_design/ARCHITECTURE.md` is therefore not exercised here.
> All line citations below refer to `CODATE01.cbl` unless otherwise noted.
> Cross-refs: `_design/specs/optional/MQ_SHIM.md` (queue/message envelope contract — §1 queues, §3 MQMD, §4 GET/PUT semantics, §5.1 date payload), `_design/specs/COACCT01.md` / `_design/specs/COPAUA0C.md` (the other two MQ-triggered servers), `CRDDEMOM.csd` (CICS resource definitions), `app-vsam-mq/README.md`.

---

## 1. Purpose & Invocation

**Purpose.** CODATE01 is the **System-Date inquiry server** of the VSAM-MQ extension. It is an MQ-triggered CICS transaction that **drains a request queue**, and for every request message it (1) gets the message (5 s wait), (2) obtains the current CICS date & time, (3) builds a fixed free-text reply `'SYSTEM DATE : MM-DD-YYYY' + 'SYSTEM TIME : HH:MM:SS'`, and (4) PUTs that reply onto the (literal) reply queue `CARD.DEMO.REPLY.DATE`, echoing the request's MsgId/CorrelId. It **ignores the request body entirely** — it always answers with the current date/time. It loops until the request queue is empty (the 5 s GET wait expires → `MQRC-NO-MSG-AVAILABLE`), taking a CICS `SYNCPOINT` before each subsequent GET. It opens an error queue (`CARD.DEMO.ERROR`) at startup and PUTs a diagnostic block there on MQ/CICS failures. // source: CODATE01.cbl:1-2, 127-169, 274-280, 283-337, 339-364, 366-403

**Invocation.** CICS transaction **`CDRD`**, program `CODATE01`, **started by an MQ trigger** (the CICS-MQ adapter starts the transaction; the program reads the MQ Trigger Message via `EXEC CICS RETRIEVE INTO(MQTM)`). It is **not** pseudo-conversational and has **no terminal I/O / no BMS map** — the only external surface is the MQ request→reply envelope. The program is declared `IS INITIAL` (fresh WORKING-STORAGE on every invocation). It ends via `EXEC CICS RETURN` followed by `GOBACK` in `8000-TERMINATION`. // source: CODATE01.cbl:2, 125, 140-159, 442-454; CSD `CRDDEMOM.csd:9-16,27-36`; README:31-34,77-84

**External-object (queue) mapping:**
| External object | Name (origin) | Role | Runtime target |
|---|---|---|---|
| Input/request MQ queue | `INPUT-QUEUE-NAME` ← `MQTM-QNAME` (trigger; README/CSD conventional name `CARD.DEMO.REQUEST.DATE` / `CARDDEMO.REQUEST.QUEUE`) | GET requests (input-shared) | MQ shim inbound queue (trigger-driven name). // source: CODATE01.cbl:130,146,176,289; MQ_SHIM.md §1 |
| Reply MQ queue | `REPLY-QUEUE-NAME` = **hard-coded literal** `'CARD.DEMO.REPLY.DATE'` | MQPUT reply (output, pre-opened handle) | MQ shim outbound literal queue. // source: CODATE01.cbl:147,210,371-390 |
| Error MQ queue | `ERROR-QUEUE-NAME` = **hard-coded literal** `'CARD.DEMO.ERROR'` | MQPUT diagnostic block | MQ shim error sink. // source: CODATE01.cbl:243,409-440; MQ_SHIM.md §5.4 |

> **No DD / no VSAM / no DB2 / no IMS / no file access.** `LIT-ACCTFILENAME = 'ACCTDAT '` is declared in WORKING-STORAGE but is **never referenced** in the PROCEDURE DIVISION — a vestigial copy-paste from the COACCT01 sibling. Do **not** create any ACCOUNT (or other table) read path for this program. // source: CODATE01.cbl:115-116

---

## 2. FILE / TABLE ACCESS TABLE

**This program performs NO file/table/DB operations.** There are no `EXEC CICS READ/WRITE/STARTBR`, no `EXEC SQL`, and no `EXEC DLI` verbs anywhere. The only data-store interactions are MQ queue verbs and two CICS time services. For completeness, the full external-interaction inventory:

| COBOL access | Verb / op | Operand(s) | Relational target | Mapping notes |
|---|---|---|---|---|
| `EXEC CICS RETRIEVE INTO(MQTM)` | CICS RETRIEVE (read trigger msg) | `MQTM` (IBM `CMQTML`) → `MQTM-QNAME` | n/a | Read the trigger record to learn the request-queue name. RESP NORMAL → use `MQTM-QNAME`; else error path. Shim: `TriggerMessage.QueueName`. // source: CODATE01.cbl:140-159; MQ_SHIM.md §2 |
| `CALL 'MQOPEN'` (×3) | MQ open queue | request (input-shared), reply (output), error (output) | n/a | Open the three queues. Map to shim "attach to named queue". // source: CODATE01.cbl:182-202,216-236,251-271 |
| `CALL 'MQGET'` | MQ get message (wait 5 s, syncpoint, convert) | `MQ-HCONN`, `MQ-HOBJ`, MD, GMO, buffer 1000 | n/a | Get next request; `MQCC-OK`→process; `MQRC-NO-MSG-AVAILABLE`→stop loop; other→error. // source: CODATE01.cbl:283-337 |
| `EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME)` | CICS time | → `WS-ABS-TIME S9(15) COMP-3` | n/a | Current absolute time. Map to injected `IClock`. // source: CODATE01.cbl:343-345 |
| `EXEC CICS FORMATTIME` | CICS format time | `MMDDYYYY(WS-MMDDYYYY) DATESEP('-') TIME(WS-TIME) TIMESEP` | n/a | Format date `MM-DD-YYYY` and time `HH:MM:SS`. // source: CODATE01.cbl:347-353 |
| `CALL 'MQPUT'` (reply) | MQ put message (syncpoint) | `OUTPUT-QUEUE-HANDLE`, MD, PMO, buffer 1000 | n/a | Put reply to pre-opened reply-queue handle. // source: CODATE01.cbl:383-390 |
| `CALL 'MQPUT'` (error) | MQ put message (syncpoint) | `ERROR-QUEUE-HANDLE`, MD, PMO, buffer 1000 | n/a | Put diagnostic block to error queue. // source: CODATE01.cbl:420-427 |
| `EXEC CICS SYNCPOINT` | CICS commit | — | n/a | Commit unit of work before each subsequent GET. Map to shim syncpoint boundary / `COMMIT`. // source: CODATE01.cbl:275-277 |
| `CALL 'MQCLOSE'` (×3) | MQ close queue | input / output / error handles, `MQCO-NONE` | n/a | Close the three queues at termination. // source: CODATE01.cbl:456-523 |
| `EXEC CICS RETURN` | CICS return | — | n/a | End the transaction (no TRANSID, no COMMAREA). // source: CODATE01.cbl:453 |

**Repository/status contract:** none needed (no `IVsamFile`/repository is used). MQ verbs map to the in-proc shim per `MQ_SHIM.md` §4; `ASKTIME`/`FORMATTIME` map to `IClock`; `SYNCPOINT` to a commit boundary. // source: ARCHITECTURE.md:80-89; MQ_SHIM.md §4

---

## 3. WORKING-STORAGE / record layouts (typed)

**Control flags (`X(01)`, 88-levels):**
- `WS-MQ-MSG-FLAG X(1) VALUE 'N'`; 88 `NO-MORE-MSGS VALUE 'Y'` — loop terminator. // source: CODATE01.cbl:13-14
- `WS-RESP-QUEUE-STS X(1) VALUE 'N'`; 88 `RESP-QUEUE-OPEN VALUE 'Y'` — set TRUE when the **OUTPUT/reply** queue opens (note name says "RESP"). // source: CODATE01.cbl:16-17, 228
- `WS-ERR-QUEUE-STS X(1) VALUE 'N'`; 88 `ERR-QUEUE-OPEN VALUE 'Y'` — set TRUE when the error queue opens. // source: CODATE01.cbl:19-20, 263
- `WS-REPLY-QUEUE-STS X(1) VALUE 'N'`; 88 `REPLY-QUEUE-OPEN VALUE 'Y'` — set TRUE when the **INPUT** queue opens (note the mismatch: opening the *input* queue sets the *REPLY* flag — see §7). // source: CODATE01.cbl:22-23, 194

**CICS response codes:** `WS-CICS-RESP-CDS` = `WS-CICS-RESP1-CD S9(8) COMP`, `WS-CICS-RESP2-CD S9(8) COMP`, `WS-CICS-RESP1-CD-D 9(8)` (display), `WS-CICS-RESP2-CD-D 9(8)` (display). // source: CODATE01.cbl:26-30

**Date/time work (`WS-DATE-TIME`):** `WS-ABS-TIME S9(15) COMP-3 VALUE ZERO`, `WS-MMDDYYYY X(10) VALUE SPACES`, `WS-TIME X(8) VALUE SPACES`. // source: CODATE01.cbl:35-38

**MQ scalar fields:** `MQ-QUEUE X(48)`, `MQ-QUEUE-REPLY X(48)`, `MQ-HCONN S9(9) BINARY VALUE 0`, `MQ-CONDITION-CODE S9(9) BINARY VALUE 0`, `MQ-REASON-CODE S9(9) BINARY VALUE 0`, `MQ-HOBJ S9(9) BINARY VALUE 0`, `MQ-OPTIONS S9(9) BINARY VALUE 0`, `MQ-BUFFER-LENGTH S9(9) BINARY`, `MQ-BUFFER X(1000)`, `MQ-DATA-LENGTH S9(9) BINARY`, `MQ-CORRELID X(24)`, `MQ-MSG-ID X(24)`, `MQ-MSG-COUNT 9(9)`, `SAVE-CORELID X(24)`, `SAVE-MSGID X(24)`, `SAVE-REPLY2Q X(48)`. // source: CODATE01.cbl:42-57

**Error display block (`MQ-ERR-DISPLAY`, the `CARD.DEMO.ERROR` payload):** `MQ-ERROR-PARA X(25)`, FILLER X(2) spaces, `MQ-APPL-RETURN-MESSAGE X(25)`, FILLER X(2) spaces, `MQ-APPL-CONDITION-CODE 9(2)`, FILLER X(2) spaces, `MQ-APPL-REASON-CODE 9(5)`, FILLER X(2) spaces, `MQ-APPL-QUEUE-NAME X(48)`. // source: CODATE01.cbl:58-67; MQ_SHIM.md §5.4

**MQ IBM copybooks (not in repo — IBM-supplied):** `MQ-GET-MESSAGE-OPTIONS` ← `CMQGMOV` (GMO), `MQ-PUT-MESSAGE-OPTIONS` ← `CMQPMOV` (PMO), `MQ-MESSAGE-DESCRIPTOR` ← `CMQMDV` (MD), `MQ-OBJECT-DESCRIPTOR` ← `CMQODV` (OD), `MQ-CONSTANTS` ← `CMQV` (MQ* literal constants), `MQ-GET-QUEUE-MESSAGE` ← `CMQTML` (provides `MQTM` trigger structure / `MQTM-QNAME`). // source: CODATE01.cbl:70-90

**Queue-name group (`QUEUE-INFO`):** `QMGR-NAME X(48) SPACES`, `INPUT-QUEUE-NAME X(48) SPACES`, `REPLY-QUEUE-NAME X(48) SPACES`, `ERROR-QUEUE-NAME X(48) SPACES`. // source: CODATE01.cbl:92-96

**Queue handles & buffers:** `INPUT-QUEUE-HANDLE S9(9) BINARY VALUE 0`, `OUTPUT-QUEUE-HANDLE S9(9) BINARY VALUE 0`, `ERROR-QUEUE-HANDLE S9(9) BINARY VALUE 0`, `QMGR-HANDLE-CONN S9(9) BINARY VALUE 0`, `QUEUE-MESSAGE X(1000)`, `REQUEST-MESSAGE X(1000)`, `REPLY-MESSAGE X(1000)`, `ERROR-MESSAGE X(1000)`. // source: CODATE01.cbl:98-108

**Request copy (`REQUEST-MSG-COPY`, 1000 bytes — parsed but unused):** `WS-FUNC X(04) SPACES`, `WS-KEY 9(11) ZEROES`, `WS-FILLER X(985) SPACES`. (Same layout as COACCT01's request; CODATE01 never inspects any of these fields.) // source: CODATE01.cbl:109-112

**Misc (`WS-VARIABLES`):** `LIT-ACCTFILENAME X(8) VALUE 'ACCTDAT '` (unused — see §1), `WS-RESP-CD S9(9) COMP VALUE ZEROS`, `WS-REAS-CD S9(9) COMP VALUE ZEROS` (both unused). // source: CODATE01.cbl:114-120

**LINKAGE SECTION:** empty (no `DFHCOMMAREA`; no COMMAREA flow). // source: CODATE01.cbl:123

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = a method)

**1000-CONTROL** (entry / driver) // source: CODATE01.cbl:127-169
1. `MOVE SPACES` to `INPUT-QUEUE-NAME`, `QMGR-NAME`, `QUEUE-MESSAGE`; `INITIALIZE MQ-ERR-DISPLAY`. // 129-134
2. `PERFORM 2100-OPEN-ERROR-QUEUE` (open the error queue first, so failures can be reported). // 136
3. `EXEC CICS RETRIEVE INTO(MQTM) RESP(WS-CICS-RESP1-CD) RESP2(WS-CICS-RESP2-CD)`. // 140-144
4. If RESP1 = `DFHRESP(NORMAL)`: `MOVE MQTM-QNAME → INPUT-QUEUE-NAME`; `MOVE 'CARD.DEMO.REPLY.DATE' → REPLY-QUEUE-NAME`. // 145-147
5. Else (RETRIEVE failed): `MOVE 'CICS RETRIEVE' → MQ-ERROR-PARA`; `MOVE WS-CICS-RESP1-CD → WS-CICS-RESP1-CD-D`; `MOVE WS-CICS-RESP2-CD → WS-CICS-RESP2-CD` (sic — moves RESP2 onto itself, **not** onto the display field; see §7); `STRING 'RESP: ', WS-CICS-RESP1-CD-D, WS-CICS-RESP2-CD-D, 'END' DELIMITED BY SIZE INTO MQ-APPL-RETURN-MESSAGE`; `PERFORM 9000-ERROR`; `PERFORM 8000-TERMINATION`. // 148-159
6. `PERFORM 2300-OPEN-INPUT-QUEUE`; `PERFORM 2400-OPEN-OUTPUT-QUEUE`. // 161-162
7. `PERFORM 3000-GET-REQUEST` (prime the first GET). // 163
8. `PERFORM 4000-MAIN-PROCESS UNTIL NO-MORE-MSGS`. // 164-165
9. `PERFORM 8000-TERMINATION`. // 167

**2300-OPEN-INPUT-QUEUE** (open request queue for GET) // source: CODATE01.cbl:171-202
1. `MOVE SPACES → MQOD-OBJECTQMGRNAME`; `MOVE INPUT-QUEUE-NAME → MQOD-OBJECTNAME`. // 175-176
2. `COMPUTE MQ-OPTIONS = MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. // 178-180
3. `CALL 'MQOPEN' USING QMGR-HANDLE-CONN, MQ-OBJECT-DESCRIPTOR, MQ-OPTIONS, MQ-HOBJ, MQ-CONDITION-CODE, MQ-REASON-CODE`. // 182-187
4. EVALUATE `MQ-CONDITION-CODE`: `MQCC-OK` → copy cond/reason to APPL fields, `MOVE MQ-HOBJ → INPUT-QUEUE-HANDLE`, `SET REPLY-QUEUE-OPEN TO TRUE` (sic — opening *input* sets *REPLY* flag, §7). WHEN OTHER → copy cond/reason + queue name + `'INP MQOPEN ERR'` to APPL fields, `PERFORM 9000-ERROR`, `PERFORM 8000-TERMINATION`. // 189-202

**2400-OPEN-OUTPUT-QUEUE** (open reply queue for PUT) // source: CODATE01.cbl:204-236
1. `MOVE SPACES → MQOD-OBJECTQMGRNAME`; `MOVE REPLY-QUEUE-NAME → MQOD-OBJECTNAME`. // 209-210
2. `COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. // 212-214
3. `CALL 'MQOPEN'` (same arg list). // 216-221
4. EVALUATE: `MQCC-OK` → copy cond/reason, `MOVE MQ-HOBJ → OUTPUT-QUEUE-HANDLE`, `SET RESP-QUEUE-OPEN TO TRUE`. OTHER → copy cond/reason + reply-queue name + `'OUT MQOPEN ERR'`, `PERFORM 9000-ERROR`, `PERFORM 8000-TERMINATION`. // 223-236

**2100-OPEN-ERROR-QUEUE** (open error queue for PUT) // source: CODATE01.cbl:238-271
1. `MOVE 'CARD.DEMO.ERROR' → ERROR-QUEUE-NAME`; `MOVE SPACES → MQOD-OBJECTQMGRNAME`; `MOVE ERROR-QUEUE-NAME → MQOD-OBJECTNAME`. // 243-245
2. `COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. // 247-249
3. `CALL 'MQOPEN'`. // 251-256
4. EVALUATE: `MQCC-OK` → copy cond/reason, `MOVE MQ-HOBJ → ERROR-QUEUE-HANDLE`, `SET ERR-QUEUE-OPEN TO TRUE`. OTHER → copy cond/reason + error-queue name + `'ERR MQOPEN ERR'`, `DISPLAY MQ-ERR-DISPLAY`, `PERFORM 8000-TERMINATION` (note: does **not** call 9000-ERROR — the error queue itself failed to open, so it just DISPLAYs and terminates). // 258-271

**4000-MAIN-PROCESS** (per-iteration body of the UNTIL loop) // source: CODATE01.cbl:274-280
1. `EXEC CICS SYNCPOINT` (commit prior unit of work). // 275-277
2. `PERFORM 3000-GET-REQUEST` (get next message). // 279

**3000-GET-REQUEST** (MQGET + dispatch) // source: CODATE01.cbl:283-337
1. `MOVE 5000 → MQGMO-WAITINTERVAL` (5 s wait). // 286
2. `MOVE SPACES → MQ-CORRELID, MQ-MSG-ID`; `MOVE INPUT-QUEUE-NAME → MQ-QUEUE`; `MOVE INPUT-QUEUE-HANDLE → MQ-HOBJ`; `MOVE 1000 → MQ-BUFFER-LENGTH`. // 287-291
3. `MOVE MQMI-NONE → MQMD-MSGID`; `MOVE MQCI-NONE → MQMD-CORRELID` (take any next message). // 292-293
4. `INITIALIZE REQUEST-MSG-COPY REPLACING NUMERIC BY ZEROES`. // 294
5. `COMPUTE MQGMO-OPTIONS = MQGMO-SYNCPOINT + MQGMO-FAIL-IF-QUIESCING + MQGMO-CONVERT + MQGMO-WAIT`. // 296-299
6. `CALL 'MQGET' USING MQ-HCONN, MQ-HOBJ, MQ-MESSAGE-DESCRIPTOR, MQ-GET-MESSAGE-OPTIONS, MQ-BUFFER-LENGTH, MQ-BUFFER, MQ-DATA-LENGTH, MQ-CONDITION-CODE, MQ-REASON-CODE` — **note: GET uses `MQ-HCONN` (value 0), not `QMGR-HANDLE-CONN`** (§7). // 301-309
7. If `MQ-CONDITION-CODE = MQCC-OK`:
   - `MOVE MQMD-MSGID → MQ-MSG-ID`; `MOVE MQMD-CORRELID → MQ-CORRELID`; `MOVE MQMD-REPLYTOQ → MQ-QUEUE-REPLY`. // 313-315
   - copy cond/reason to APPL fields; `MOVE MQ-BUFFER → REQUEST-MESSAGE`. // 316-318
   - `MOVE MQ-CORRELID → SAVE-CORELID`; `MOVE MQ-QUEUE-REPLY → SAVE-REPLY2Q`; `MOVE MQ-MSG-ID → SAVE-MSGID`. // 319-321
   - `MOVE REQUEST-MESSAGE → REQUEST-MSG-COPY` (parses request into the copy — then never used, §7). // 322
   - `PERFORM 4000-PROCESS-REQUEST-REPLY`; `ADD 1 → MQ-MSG-COUNT` (dead counter). // 323-324
8. Else if `MQ-REASON-CODE = MQRC-NO-MSG-AVAILABLE`: `SET NO-MORE-MSGS TO TRUE` (terminate loop). // 326-327
9. Else (other MQGET error): copy cond/reason + input-queue name + `'INP MQGET ERR:'` to APPL fields; `PERFORM 9000-ERROR`; `PERFORM 8000-TERMINATION`. // 329-336

**4000-PROCESS-REQUEST-REPLY** (build the date/time reply) // source: CODATE01.cbl:339-364
1. `MOVE SPACES → REPLY-MESSAGE`; `INITIALIZE WS-DATE-TIME REPLACING NUMERIC BY ZEROES`. // 340-341
2. `EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME)`. // 343-345
3. `EXEC CICS FORMATTIME ABSTIME(WS-ABS-TIME) MMDDYYYY(WS-MMDDYYYY) DATESEP('-') TIME(WS-TIME) TIMESEP` → `WS-MMDDYYYY` = `MM-DD-YYYY`, `WS-TIME` = `HH:MM:SS`. // 347-353
4. `STRING 'SYSTEM DATE : ' WS-MMDDYYYY 'SYSTEM TIME : ' WS-TIME DELIMITED BY SIZE INTO REPLY-MESSAGE` — produces `SYSTEM DATE : MM-DD-YYYYSYSTEM TIME : HH:MM:SS` (no separator between the date value and the next label — §7). // 355-360
5. `PERFORM 4100-PUT-REPLY`. // 361

**4100-PUT-REPLY** (MQPUT the reply) // source: CODATE01.cbl:366-403
1. `MOVE REPLY-MESSAGE → MQ-BUFFER`; `MOVE 1000 → MQ-BUFFER-LENGTH`. // 371-372
2. `MOVE SAVE-MSGID → MQMD-MSGID`; `MOVE SAVE-CORELID → MQMD-CORRELID` (echo request correlation). // 373-374
3. `MOVE MQFMT-STRING → MQMD-FORMAT`; `COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR`. // 375-377
4. `COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING`. // 379-381
5. `CALL 'MQPUT' USING MQ-HCONN, OUTPUT-QUEUE-HANDLE, MQ-MESSAGE-DESCRIPTOR, MQ-PUT-MESSAGE-OPTIONS, MQ-BUFFER-LENGTH, MQ-BUFFER, MQ-CONDITION-CODE, MQ-REASON-CODE` — **PUT targets the pre-opened `OUTPUT-QUEUE-HANDLE` (literal reply queue), NOT `SAVE-REPLY2Q`** (§7); also uses `MQ-HCONN` (0). // 383-390
6. EVALUATE: `MQCC-OK` → copy cond/reason. OTHER → copy cond/reason + reply-queue name + `'MQPUT ERR'`; `PERFORM 9000-ERROR`; `PERFORM 8000-TERMINATION`. // 392-403

**9000-ERROR** (MQPUT the diagnostic block to the error queue) // source: CODATE01.cbl:405-441
1. `MOVE MQ-ERR-DISPLAY → ERROR-MESSAGE`; `MOVE ERROR-MESSAGE → MQ-BUFFER`; `MOVE 1000 → MQ-BUFFER-LENGTH`. // 409-411
2. `MOVE MQFMT-STRING → MQMD-FORMAT`; `COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR`. // 412-414
3. `COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING`. // 416-418
4. `CALL 'MQPUT' USING MQ-HCONN, ERROR-QUEUE-HANDLE, ...`. // 420-427
5. EVALUATE: `MQCC-OK` → copy cond/reason. OTHER → copy cond/reason + error-queue name + `'MQPUT ERR'`; `DISPLAY MQ-ERR-DISPLAY`; `PERFORM 8000-TERMINATION` (no recursive 9000-ERROR). // 429-440

**8000-TERMINATION** (close queues + return) // source: CODATE01.cbl:442-454
1. If `REPLY-QUEUE-OPEN` (= input queue opened, §7) → `PERFORM 5000-CLOSE-INPUT-QUEUE`. // 444-446
2. If `RESP-QUEUE-OPEN` (= reply/output queue opened) → `PERFORM 5100-CLOSE-OUTPUT-QUEUE`. // 447-449
3. If `ERR-QUEUE-OPEN` → `PERFORM 5200-CLOSE-ERROR-QUEUE`. // 450-452
4. `EXEC CICS RETURN END-EXEC`; `GOBACK`. // 453-454

**5000-CLOSE-INPUT-QUEUE** // source: CODATE01.cbl:456-477
1. `MOVE INPUT-QUEUE-NAME → MQ-QUEUE`; `MOVE INPUT-QUEUE-HANDLE → MQ-HOBJ`; `COMPUTE MQ-OPTIONS = MQCO-NONE`. // 457-459
2. `CALL 'MQCLOSE' USING MQ-HCONN, MQ-HOBJ, MQ-OPTIONS, MQ-CONDITION-CODE, MQ-REASON-CODE`. // 461-465
3. EVALUATE: `MQCC-OK` → copy cond/reason. OTHER → copy cond/reason + input-queue name + `'MQCLOSE ERR'`; `PERFORM 8000-TERMINATION` (recursion back to terminate — §7). // 467-477

**5100-CLOSE-OUTPUT-QUEUE** // source: CODATE01.cbl:478-499
1. `MOVE REPLY-QUEUE-NAME → MQ-QUEUE`; `MOVE OUTPUT-QUEUE-HANDLE → MQ-HOBJ`; `COMPUTE MQ-OPTIONS = MQCO-NONE`. // 479-481
2. `CALL 'MQCLOSE'`. // 483-487
3. EVALUATE: `MQCC-OK` → copy cond/reason. OTHER → copy cond/reason + **input-queue** name (sic — uses `INPUT-QUEUE-NAME`, not the reply name, §7) + `'MQCLOSE ERR'`; `PERFORM 8000-TERMINATION`. // 489-499

**5200-CLOSE-ERROR-QUEUE** // source: CODATE01.cbl:501-523
1. `MOVE ERROR-QUEUE-NAME → MQ-QUEUE`; `MOVE ERROR-QUEUE-HANDLE → MQ-HOBJ`; `COMPUTE MQ-OPTIONS = MQCO-NONE`. // 502-504
2. `CALL 'MQCLOSE'`. // 506-510
3. EVALUATE: `MQCC-OK` → copy cond/reason. OTHER → copy cond/reason + error-queue name + `'MQCLOSE ERR'`; `PERFORM 9000-ERROR`; `PERFORM 8000-TERMINATION` (recursion — §7). // 512-523

### Control-flow summary
`1000-CONTROL → 2100 open error Q → CICS RETRIEVE (get input Q name, set literal reply Q) → 2300 open input Q → 2400 open reply Q → 3000 prime GET → 4000-MAIN-PROCESS loop[SYNCPOINT, 3000 GET → on msg: 4000-PROCESS-REQUEST-REPLY{ASKTIME, FORMATTIME, STRING reply, 4100 PUT reply}, +1 count; on empty-after-5s: NO-MORE-MSGS] → 8000-TERMINATION{close input/reply/error Qs, CICS RETURN, GOBACK}`. Any MQ/CICS failure routes to 9000-ERROR (PUT diagnostic to error queue) then 8000-TERMINATION. // source: CODATE01.cbl:127-169, 274-280, 283-337, 442-454

---

## 5. ONLINE / MQ flow specifics (no BMS screen)

- **No pseudo-conversation, no SEND/RECEIVE MAP, no EIBAID/PFKey handling, no BMS map / mapset.** This is a non-terminal, MQ-triggered server transaction. The "online" surface is the **MQ request→reply** envelope (see `MQ_SHIM.md` §3–§5.1). There are no `DFHxxx` map verbs in the source. // source: CODATE01.cbl (no map verbs present)
- **No COMMAREA.** LINKAGE SECTION is empty; `EXEC CICS RETURN` carries no `TRANSID`/`COMMAREA`. // source: CODATE01.cbl:123, 453
- **Trigger start:** `EXEC CICS RETRIEVE INTO(MQTM)` reads `MQTM-QNAME → INPUT-QUEUE-NAME` (queue to drain). `MQTM-TRIGGERDATA` is not used. Reply queue is the **literal** `CARD.DEMO.REPLY.DATE`. // source: CODATE01.cbl:140-147; MQ_SHIM.md §2, §1
- **Request payload (in):** GET 1000-byte buffer → `REQUEST-MESSAGE` → `REQUEST-MSG-COPY`. **The request body is parsed but never inspected**; the program always answers with the current date/time. Shim date request body may be empty/any. // source: CODATE01.cbl:318,322; MQ_SHIM.md §5.1
- **Reply payload (out):** free-text `'SYSTEM DATE : ' + WS-MMDDYYYY + 'SYSTEM TIME : ' + WS-TIME`, padded to 1000 bytes in `MQ-BUFFER`, `MQMD-FORMAT = MQFMT-STRING`, `MQMD-CODEDCHARSETID = MQCCSI-Q-MGR`. (This is the **code** output; it differs from the README's structured `DATE-RESPONSE-MSG` — honor the code, see §7.) // source: CODATE01.cbl:355-360,371-377; MQ_SHIM.md §5.1
- **Correlation:** request `MQMD-MSGID → SAVE-MSGID`, `MQMD-CORRELID → SAVE-CORELID`, `MQMD-REPLYTOQ → SAVE-REPLY2Q`; reply sets `MQMD-MSGID = SAVE-MSGID` and `MQMD-CORRELID = SAVE-CORELID`. **`SAVE-REPLY2Q` is captured but never used** — PUT always goes to the literal reply queue handle (faithful bug §7). // source: CODATE01.cbl:313-321,373-374,383-390; MQ_SHIM.md §3, §6.5
- **GET options:** `MQGMO-SYNCPOINT + MQGMO-FAIL-IF-QUIESCING + MQGMO-CONVERT + MQGMO-WAIT`, `MQGMO-WAITINTERVAL = 5000`. **PUT options:** `MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING`. **Open options:** input `MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`; reply/error `MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING`. **Close:** `MQCO-NONE`. // source: CODATE01.cbl:178-180,212-214,247-249,296-299,379-381,459; MQ_SHIM.md §4
- **Loop / commit:** drain until `MQRC-NO-MSG-AVAILABLE` (after 5 s wait). `EXEC CICS SYNCPOINT` is issued at the top of `4000-MAIN-PROCESS` (before each subsequent GET). No 500-message cap (unlike COPAUA0C). // source: CODATE01.cbl:164-165,275-277,326-327; MQ_SHIM.md §4
- **XCTL/LINK targets:** **none.** No `EXEC CICS XCTL`/`LINK`. The only external calls are IBM MQ stubs (`MQOPEN`/`MQGET`/`MQPUT`/`MQCLOSE`) and CICS services (`RETRIEVE`/`ASKTIME`/`FORMATTIME`/`SYNCPOINT`/`RETURN`). // source: CODATE01.cbl (no XCTL/LINK present)

---

## 6. VALIDATION RULES & exact literal messages

**Business validation:** **none.** The program performs no input validation and no business rules — it unconditionally answers every request with the current date/time. // source: CODATE01.cbl:339-364

**Reply text (exact, built by `STRING ... DELIMITED BY SIZE`):**
- Literal segment 1: `'SYSTEM DATE : '` (14 chars incl. trailing space). // source: CODATE01.cbl:355
- Then `WS-MMDDYYYY` (10 chars, `MM-DD-YYYY`). // source: CODATE01.cbl:355,349
- Literal segment 2: `'SYSTEM TIME : '` (14 chars incl. trailing space). // source: CODATE01.cbl:356
- Then `WS-TIME` (8 chars, `HH:MM:SS`). // source: CODATE01.cbl:356,351
- Net string: `SYSTEM DATE : MM-DD-YYYYSYSTEM TIME : HH:MM:SS` (no delimiter between the date value and the `'SYSTEM TIME : '` label — §7), padded with spaces to 1000 bytes. // source: CODATE01.cbl:355-360,371

**Error/diagnostic literal messages (`MQ-APPL-RETURN-MESSAGE`, X(25), PUT in the `MQ-ERR-DISPLAY` block to `CARD.DEMO.ERROR`):**
| Literal text (verbatim) | `MQ-ERROR-PARA` | Paragraph / condition |
|---|---|---|
| built `'RESP: ' + RESP1-D + RESP2-D + 'END'` | `'CICS RETRIEVE'` | 1000-CONTROL — CICS RETRIEVE not NORMAL. // source: CODATE01.cbl:149-154 |
| `'INP MQOPEN ERR'` | (unset by this path) | 2300-OPEN-INPUT-QUEUE — input MQOPEN failed. // source: CODATE01.cbl:199 |
| `'OUT MQOPEN ERR'` | (unset by this path) | 2400-OPEN-OUTPUT-QUEUE — reply MQOPEN failed. // source: CODATE01.cbl:233 |
| `'ERR MQOPEN ERR'` | (unset) | 2100-OPEN-ERROR-QUEUE — error-queue MQOPEN failed (DISPLAYed, not PUT). // source: CODATE01.cbl:268 |
| `'INP MQGET ERR:'` | (unset) | 3000-GET-REQUEST — MQGET failed (not no-msg). // source: CODATE01.cbl:333 |
| `'MQPUT ERR'` | (unset) | 4100-PUT-REPLY — reply MQPUT failed. // source: CODATE01.cbl:400 |
| `'MQPUT ERR'` | (unset) | 9000-ERROR — error-queue MQPUT failed (DISPLAYed). // source: CODATE01.cbl:437 |
| `'MQCLOSE ERR'` | (unset) | 5000/5100/5200-CLOSE-*-QUEUE — MQCLOSE failed. // source: CODATE01.cbl:475,497,520 |

`MQ-APPL-CONDITION-CODE 9(2)` carries `MQ-CONDITION-CODE`; `MQ-APPL-REASON-CODE 9(5)` carries `MQ-REASON-CODE`; `MQ-APPL-QUEUE-NAME X(48)` carries the offending queue name. // source: CODATE01.cbl:63,65,67,191-198 etc.

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **Request body read but never used.** `3000-GET-REQUEST` copies the GET buffer into `REQUEST-MESSAGE` and then `REQUEST-MSG-COPY` (`WS-FUNC`/`WS-KEY`/`WS-FILLER`), but no field of the request is ever inspected — the reply is always the current date/time regardless of input. (README documents a `DATE`/`REQUEST-ID` request that the code ignores.) // source: CODATE01.cbl:318,322,339-364
2. **Reply `MQMD-REPLYTOQ` captured and ignored.** The request's ReplyToQ is saved to `MQ-QUEUE-REPLY`/`SAVE-REPLY2Q`, but the reply MQPUT targets the **pre-opened literal** `CARD.DEMO.REPLY.DATE` handle (`OUTPUT-QUEUE-HANDLE`), never `SAVE-REPLY2Q`. A requester that asked for a different reply queue is ignored. // source: CODATE01.cbl:315,320,371-390
3. **Wrong handle variable on MQGET/MQPUT/MQCLOSE.** `MQGET`/`MQPUT`/`MQCLOSE` pass `MQ-HCONN` (left at its `VALUE 0` initializer) as the connection handle, whereas `MQOPEN` passes `QMGR-HANDLE-CONN` (also 0). Both are 0 so it works under the CICS-MQ adapter, but they are different variables. Treat the connection as a single ambient in-proc context; do not rely on these being the same field. // source: CODATE01.cbl:182,301-302,383-384,461,182 vs 301
4. **Flag/queue naming inversion.** Opening the **input** queue sets `REPLY-QUEUE-OPEN` (88 of `WS-REPLY-QUEUE-STS`), and opening the **reply/output** queue sets `RESP-QUEUE-OPEN`. Consistently, `8000-TERMINATION` closes the *input* queue when `REPLY-QUEUE-OPEN` is true and the *output* queue when `RESP-QUEUE-OPEN` is true. The behavior is internally consistent (it closes the right queues) but the flag names are swapped relative to their meaning — preserve the mapping exactly. // source: CODATE01.cbl:194,228,444-449
5. **`STRING` produces no separator between the date value and the next label.** The `STRING` concatenates `'SYSTEM DATE : '`, `WS-MMDDYYYY`, `'SYSTEM TIME : '`, `WS-TIME` with no delimiter, yielding `...MM-DD-YYYYSYSTEM TIME :...` (the date value butts directly against the next label). Reproduce this exact byte layout. // source: CODATE01.cbl:355-360
6. **RESP2 display move is a no-op (self-move).** In the CICS-RETRIEVE error path: `MOVE WS-CICS-RESP1-CD TO WS-CICS-RESP1-CD-D` correctly populates the display field, but the next line `MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD` moves RESP2 onto **itself** (intended target was almost certainly `WS-CICS-RESP2-CD-D`). Consequently the `STRING` that emits `WS-CICS-RESP2-CD-D` emits its uninitialized/zero value, never the real RESP2. Preserve verbatim (do not redirect to the -D field). // source: CODATE01.cbl:150-151,152-154
7. **`5100-CLOSE-OUTPUT-QUEUE` error path reports the wrong queue name.** On an MQCLOSE failure of the *output/reply* queue it does `MOVE INPUT-QUEUE-NAME TO MQ-APPL-QUEUE-NAME` (the input queue name), not `REPLY-QUEUE-NAME`. The diagnostic block therefore names the wrong queue. // source: CODATE01.cbl:496
8. **Re-entrant/recursive termination on close failure.** `5000`/`5200` close-queue error paths `PERFORM 8000-TERMINATION` (and `5200` also `PERFORM 9000-ERROR`), and `8000-TERMINATION` re-PERFORMs the close paragraphs — a potential recursive PERFORM if a close keeps failing (COBOL PERFORM is not re-entrant; under CICS this risks an abend loop). Model the `.NET` close path as idempotent/guarded but keep the call ordering observable. Note `8000-TERMINATION` also gates each close on its (swapped) open flag, so a queue that never opened is not closed. // source: CODATE01.cbl:444-452,476,498,521-522
9. **README vs code payload divergence.** The README's structured `DATE-RESPONSE-MSG` (`RESPONSE-TYPE X(4)`, `RESPONSE-ID X(8)`, `SYSTEM-DATE X(10)`) is **not** what the code emits; the code emits the free-text `SYSTEM DATE : ... SYSTEM TIME : ...` form. Match the **code**. // source: CODATE01.cbl:355-360; README:106-112; MQ_SHIM.md §5.1
10. **Dead WORKING-STORAGE.** `LIT-ACCTFILENAME ('ACCTDAT ')`, `WS-RESP-CD`, `WS-REAS-CD`, `MQ-MSG-COUNT` (incremented but never read), `SAVE-REPLY2Q` (set, never used), `MQ-QUEUE`/`MQ-QUEUE-REPLY` set in spots but not load-bearing — vestigial. Do not introduce file access or counters with external effect. // source: CODATE01.cbl:114-120,324,320

---

## 8. PORT NOTES (relational-access translation plan + COBOL semantics)

- **No relational tables.** Implement CODATE01 as a `CardDemo.Mq` server handler with **no** repository/`IVsamFile` dependency. The only injected service is an `IClock` (for `ASKTIME`/`FORMATTIME`) plus the in-proc MQ shim (request/reply/error queues) per `MQ_SHIM.md` §6.2. // source: CODATE01.cbl (no file verbs); ARCHITECTURE.md:28
- **Trigger → queue name.** Handler is invoked with `TriggerMessage { QueueName }`; bind `QueueName → INPUT-QUEUE-NAME`. Reply queue is the literal `CARD.DEMO.REPLY.DATE`; error queue is the literal `CARD.DEMO.ERROR`. // source: CODATE01.cbl:146-147,243; MQ_SHIM.md §1,§2
- **Date/time formatting.** `FORMATTIME ... MMDDYYYY DATESEP('-')` → `MM-DD-YYYY` (note: month-day-year, hyphen separator — not the ISO `CCYY-MM-DD` used elsewhere in CardDemo). `TIME ... TIMESEP` (default `:`) → `HH:MM:SS`. Both derive from the same `ABSTIME`. Format these from the `IClock` value with fixed `MM-dd-yyyy` and `HH:mm:ss` patterns. The fields are space-initialized X(10)/X(8); keep exactly those widths. // source: CODATE01.cbl:37-38,347-353
- **Reply string build.** Build `"SYSTEM DATE : " + mmddyyyy + "SYSTEM TIME : " + hhmmss` (no separator between date value and the second label — faithful bug §7), then place into a 1000-byte space-padded buffer. `MQMD-FORMAT = MQFMT-STRING`, charset = QMgr default. // source: CODATE01.cbl:355-360,371-377
- **Correlation echo.** Reply `MsgId = request.MsgId`, `CorrelId = request.CorrelId`. Reply always to the literal queue (ignore `request.ReplyToQueue`) — see MQ_SHIM.md §6.5 invariant #2 for VSAM-MQ. // source: CODATE01.cbl:373-374,383-390
- **Loop + syncpoint.** Drain loop: GET (5 s wait); on message build+PUT reply; before each subsequent GET issue a syncpoint/commit. Stop on `MQRC-NO-MSG-AVAILABLE`. The shim "wait" may return no-message immediately when empty (the 5 s is a real-MQ blocking detail). // source: CODATE01.cbl:164-165,274-280,326-327; MQ_SHIM.md §4,§6.2
- **`INITIALIZE ... REPLACING NUMERIC BY ZEROES`.** Used on `REQUEST-MSG-COPY` and `WS-DATE-TIME`: set numeric subfields to 0 and alphanumeric subfields to spaces. For `WS-DATE-TIME` this zeroes `WS-ABS-TIME` and spaces `WS-MMDDYYYY`/`WS-TIME`. Reproduce as field re-initialization (no behavioral impact since both are then overwritten). // source: CODATE01.cbl:294,341
- **`IS INITIAL`.** The program restarts with fresh WORKING-STORAGE each invocation; in .NET, construct fresh handler state per trigger (no static carry-over). // source: CODATE01.cbl:2
- **No edited PIC / no REDEFINES / no OCCURS / no arithmetic of consequence.** The only `COMPUTE`s are bit-OR-style additions of MQ option constants (`MQGMO-*`, `MQPMO-*`, `MQOO-*`, `MQCO-NONE`) — model these as the corresponding option enum/flag combinations, not as runtime decimal arithmetic. There is no money/decimal computation in this program. // source: CODATE01.cbl:178-180,212-214,247-249,296-299,379-381,416-418,459

---

## 9. OPEN QUESTIONS / RISKS

1. **IBM MQ copybooks not in repo.** `CMQTML`, `CMQV`, `CMQGMOV`, `CMQPMOV`, `CMQMDV`, `CMQODV` are IBM-supplied and absent here; exact field names/values (e.g. `MQMI-NONE`, `MQCI-NONE`, `MQCCSI-Q-MGR`, `MQGMO-*`) are taken as the standard IBM constants. The shim treats them as enumerated options/sentinels per `MQ_SHIM.md` — fine for a self-contained re-implementation, but a byte-exact MQMD on the wire cannot be characterized against a real queue manager here. // source: CODATE01.cbl:70-90
2. **Date/time non-determinism.** Reply content is the current clock; characterization tests must inject a fixed `IClock` and mask/pin the date & time (consistent with ARCHITECTURE.md verification "timestamps masked"). // source: CODATE01.cbl:343-360; ARCHITECTURE.md:97
3. **CICS RETRIEVE precedes useful queue names.** If RETRIEVE fails, `INPUT-QUEUE-NAME` stays spaces (set at 130) and the program errors out via 9000+8000; the error queue is already open (opened first at 136). The .NET handler should mirror this ordering (open error sink before reading the trigger). // source: CODATE01.cbl:130,136,140-159
4. **Recursive PERFORM on persistent close failure** (faithful bug §7 #8) could loop; pin a guard test so the .NET model terminates deterministically while preserving the observable close ordering. // source: CODATE01.cbl:444-452,476,521-522
