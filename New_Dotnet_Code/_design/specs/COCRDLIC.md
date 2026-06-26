# PORT SPEC — COCRDLIC (Credit Card List, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COCRDLIC.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COCRDLI.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COCRDLI.CPY`
Logic copybooks: `CVCRD01Y.cpy` (CC-WORK-AREA + AID flags), `COCOM01Y.cpy` (CARDDEMO-COMMAREA), `CVACT02Y.cpy` (CARD-RECORD), `CSSTRPFY.cpy` (PF-key store), `COCRDLI.cpy` (symbolic map).
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.Data` (CARD repository) per ARCHITECTURE.md.

All line citations use the form `// source: COCRDLIC.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COCRDLIC is the CICS pseudo-conversational **"List Credit Cards"** transaction. It browses the CARD master file forward/backward in primary-key (card-number) order and displays up to **7 cards per page** on a 24×80 BMS screen, with optional **account-number** and/or **card-number filters** entered on the screen. For each listed card the user can type **`S`** (view detail) or **`U`** (update) in a per-row selection field; pressing ENTER on a selected row XCTLs to the card-detail (COCRDSLC) or card-update (COCRDUPC) program. The header comment states it lists (a) all cards when no context is passed and the user is admin, or (b) only the cards for the ACCT in COMMAREA when the user is not admin — but note the implemented filtering is purely by the on-screen filter fields, not by `CDEMO-USER-TYPE` (see Faithful Bugs). `// source: COCRDLIC.cbl:1-8`

**Invocation.**
- CICS TRANSID **`CCLI`** (`LIT-THISTRANID`). `// source: COCRDLIC.cbl:181-182`
- Program id **`COCRDLIC`** (`LIT-THISPGM`). `// source: COCRDLIC.cbl:179-180`
- Mapset **`COCRDLI`** (`LIT-THISMAPSET`) / map **`CCRDLIA`** (`LIT-THISMAP`). `// source: COCRDLIC.cbl:183-186`
- Reached by `EXEC CICS XCTL` from the main menu (`COMEN01C`/`CM00`) — on entry `CDEMO-FROM-PROGRAM` ≠ this program triggers a fresh start. Pseudo-conversational: re-drives itself via `RETURN TRANSID('CCLI')`. `// source: COCRDLIC.cbl:615-619`
- It is **not** a called subroutine; all flow is via COMMAREA + XCTL/RETURN.
- XCTL targets out: `COMEN01C` (PF3 exit → menu) `// source: COCRDLIC.cbl:402-405`; `COCRDSLC` (ENTER + row `S`) `// source: COCRDLIC.cbl:538-541`; `COCRDUPC` (ENTER + row `U`) `// source: COCRDLIC.cbl:566-569`.

---

## 2. FILE / TABLE ACCESS

Only **one** file is accessed: the CARD master, by **primary-key browse** (forward and backward). No write/rewrite/delete. The account alternate-path literal `CARDAIX` (`LIT-CARD-FILE-ACCT-PATH`) is declared `// source: COCRDLIC.cbl:215-217` but **never used** — account filtering is done in-program by comparing `CARD-ACCT-ID` to the filter (see §9, 9500-FILTER-RECORDS). Do not implement an alt-index browse.

| COBOL DATASET (DDNAME literal) | Logical file | ARCH table | Key (RIDFLD) | CICS op | SQL equivalent |
|---|---|---|---|---|---|
| `CARDDAT` (`LIT-CARD-FILE`) | Card master | **CARD** | `WS-CARD-RID-CARDNUM` X(16) | STARTBR GTEQ | position cursor: ordered scan `card_num >= @startKey` |
| `CARDDAT` | Card master | **CARD** | `WS-CARD-RID-CARDNUM` | READNEXT | forward cursor `... ORDER BY card_num ASC` |
| `CARDDAT` | Card master | **CARD** | `WS-CARD-RID-CARDNUM` | READPREV | reverse cursor `... ORDER BY card_num DESC` |
| `CARDDAT` | Card master | **CARD** | — | ENDBR | close cursor |

Citations: STARTBR fwd `// source: COCRDLIC.cbl:1129-1136`; READNEXT (loop + look-ahead) `// source: COCRDLIC.cbl:1146-1154, 1197-1205`; ENDBR `// source: COCRDLIC.cbl:1258-1259`; STARTBR back `// source: COCRDLIC.cbl:1273-1280`; READPREV (priming + loop) `// source: COCRDLIC.cbl:1294-1302, 1322-1330`; ENDBR back `// source: COCRDLIC.cbl:1375-1377`.

**Repository contract notes (per ARCHITECTURE.md §VSAM→SQL).**
- `STARTBR … GTEQ RIDFLD(WS-CARD-RID-CARDNUM)` = open an ordered forward cursor positioned at the first `card_num >= startKey`. Because the start key is a CHAR(16) image (right is space/low-value padded), use **ordinal/byte string comparison** on `card_num`, not numeric. When `WS-CARD-RID-CARDNUM = LOW-VALUES` (fresh start / first page), GTEQ from low-values returns the first record in the file. `// source: COCRDLIC.cbl:1131-1133`
- RESP mapping: `DFHRESP(NORMAL)` and `DFHRESP(DUPREC)` are both treated as a good read; `DFHRESP(ENDFILE)` = no more rows (end of cursor); `WHEN OTHER` = hard error → build file-error message and stop. `// source: COCRDLIC.cbl:1156-1158, 1207-1209, 1233, 1246`
- The browse reads the **full 150-byte CARD-RECORD** (`INTO(CARD-RECORD)`), but only `CARD-NUM`, `CARD-ACCT-ID`, `CARD-ACTIVE-STATUS` are consumed. `// source: COCRDLIC.cbl:1148, 1165-1171`
- READPREV ordering: the .NET reverse cursor must be EBCDIC-collation-equivalent to the forward cursor; for the digit/space/low-value key space CardDemo uses, ordinal string compare reversed is correct (guard with a pin test). `// source: ARCHITECTURE.md §VSAM-semantics`

---

## 3. DATA STRUCTURES USED

- **CARD-RECORD** (`CVACT02Y`, RECLN 150): `CARD-NUM X(16)`, `CARD-ACCT-ID 9(11)`, `CARD-CVV-CD 9(3)`, `CARD-EMBOSSED-NAME X(50)`, `CARD-EXPIRAION-DATE X(10)`, `CARD-ACTIVE-STATUS X(1)`, FILLER X(59). Only NUM / ACCT-ID / ACTIVE-STATUS used. `// source: CVACT02Y.cpy:4-11`
- **CC-WORK-AREA** (`CVCRD01Y`): holds AID flags (`CCARD-AID`, 88s ENTER/CLEAR/PA1-2/PFK01-12), `CCARD-NEXT-PROG X8`, `CCARD-NEXT-MAPSET X7`, `CCARD-NEXT-MAP X7`, `CCARD-ERROR-MSG X75`, `CCARD-RETURN-MSG X75`, filter inputs `CC-ACCT-ID X11` (redef `CC-ACCT-ID-N 9(11)`), `CC-CARD-NUM X16` (redef `CC-CARD-NUM-N 9(16)`), `CC-CUST-ID X9`. `// source: CVCRD01Y.cpy:3-42`
- **WS-THIS-PROGCOMMAREA** (program-private commarea tail): `WS-CA-LAST-CARDKEY` = (`WS-CA-LAST-CARD-NUM X16` + `WS-CA-LAST-CARD-ACCT-ID 9(11)`); `WS-CA-FIRST-CARDKEY` = (`WS-CA-FIRST-CARD-NUM X16` + `WS-CA-FIRST-CARD-ACCT-ID 9(11)`); `WS-CA-SCREEN-NUM 9(1)` (88 CA-FIRST-PAGE=1); `WS-CA-LAST-PAGE-DISPLAYED 9(1)` (88 CA-LAST-PAGE-SHOWN=0, CA-LAST-PAGE-NOT-SHOWN=9); `WS-CA-NEXT-PAGE-IND X1` (88 CA-NEXT-PAGE-NOT-EXISTS=LOW-VALUES, CA-NEXT-PAGE-EXISTS='Y'); `WS-RETURN-FLAG X1`. `// source: COCRDLIC.cbl:229-248`
- **WS-SCREEN-DATA / WS-ALL-ROWS** (196 bytes = 28×7): `WS-SCREEN-ROWS OCCURS 7`, each `WS-EACH-ROW` = `WS-EACH-CARD` (`WS-ROW-ACCTNO X11` + `WS-ROW-CARD-NUM X16` + `WS-ROW-CARD-STATUS X1`). This is the page buffer. `// source: COCRDLIC.cbl:252-260`
- **WS-EDIT-SELECT-FLAGS X(7)** (redef `WS-EDIT-SELECT OCCURS 7`): per-row selection chars; 88s SELECT-OK={'S','U'}, VIEW-REQUESTED-ON='S', UPDATE-REQUESTED-ON='U', SELECT-BLANK={' ',LOW-VALUES}. Init LOW-VALUES. `// source: COCRDLIC.cbl:72-82`
- **WS-EDIT-SELECT-ERROR-FLAGS X(7)** (redef `WS-EDIT-SELECT-ERRORS OCCURS 7` → `WS-ROW-CRDSELECT-ERROR X1`, 88 WS-ROW-SELECT-ERROR='1'): per-row error markers. `// source: COCRDLIC.cbl:83-88`
- **CICS-OUTPUT-EDIT-VARS**: `CARD-ACCT-ID-X X11`/`CARD-ACCT-ID-N 9(11)`, `CARD-CVV-CD-X X3`/`-N 9(3)`, `FLG-PROTECT-SELECT-ROWS X1` (88 NO='0', YES='1'). `// source: COCRDLIC.cbl:98-107`
- **WS-FILE-ERROR-MESSAGE** group: `'File Error:'` + ERROR-OPNAME X8 + `' on '` + ERROR-FILE X9 + `' returned RESP '` + ERROR-RESP X10 + `,RESP2 ` + ERROR-RESP2 X10. `// source: COCRDLIC.cbl:153-171`

---

## 4. COMMAREA FIELDS (CARDDEMO-COMMAREA, COCOM01Y)

On-the-wire COMMAREA = `WS-COMMAREA PIC X(2000)` split as `[CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA]`; `LENGTH OF CARDDEMO-COMMAREA` is used as the split offset. Port: model COMMAREA as a typed object whose first segment is CARDDEMO-COMMAREA and second is the program-private tail; preserve the 2000-byte length on RETURN. `// source: COCRDLIC.cbl:262, 327-331, 609-619`

Fields used by this program:
- `CDEMO-FROM-TRANID` X4, `CDEMO-FROM-PROGRAM` X8 — origin check & set. `// source: COCRDLIC.cbl:318-319, 336-337, 357-358`
- `CDEMO-TO-PROGRAM` X8 — set to menu pgm on PF3 exit. `// source: COCRDLIC.cbl:392`
- `CDEMO-USER-TYPE` (88 CDEMO-USRTYP-USER='U') — always SET to USER (admin path never taken). `// source: COCRDLIC.cbl:320, 388, 466, 522, 550`
- `CDEMO-PGM-CONTEXT` 9(1): `CDEMO-PGM-ENTER`=0, `CDEMO-PGM-REENTER`=1 — fresh-start & dispatch driver. `// source: COCOM01Y.cpy:29-31; COCRDLIC.cbl:321, 336, 459`
- `CDEMO-ACCT-ID` 9(11) — written from edited filter / selected row. `// source: COCRDLIC.cbl:1011, 1024, 1027, 531-532`
- `CDEMO-CARD-NUM` 9(16) — written from edited filter / selected row. `// source: COCRDLIC.cbl:1046, 1061, 1064, 533-534`
- `CDEMO-LAST-MAP` X7, `CDEMO-LAST-MAPSET` X7 — set throughout. `// source: COCRDLIC.cbl:322-323, 340, 390-391`

Program-private tail (`WS-THIS-PROGCOMMAREA`): the paging state (first/last card keys, screen number, next-page indicator, last-page-shown flag) is carried across pseudo-conversational turns. `// source: COCRDLIC.cbl:229-248`

---

## 5. SCREEN (BMS map CCRDLIA / mapset COCRDLI)

24×80, `CTRL=(FREEKB)`, `SIZE=(24,80)`, DSATTS/MAPATTS=(COLOR,HILIGHT,PS,VALIDN). `// source: COCRDLI.bms:25-28`

### Header / footer (output-only, written every send)
| Field | Len | Pos | Notes |
|---|---|---|---|
| `TRNNAME` | 4 | (1,7) | = `CCLI` `// source: COCRDLIC.cbl:649` |
| `TITLE01` | 40 | (1,21) | from CCDA-TITLE01 `// source: COCRDLIC.cbl:647` |
| `CURDATE` | 8 | (1,71) | mm/dd/yy from CURRENT-DATE `// source: COCRDLIC.cbl:658` |
| `PGMNAME` | 8 | (2,7) | = `COCRDLIC` `// source: COCRDLIC.cbl:650` |
| `TITLE02` | 40 | (2,21) | from CCDA-TITLE02 `// source: COCRDLIC.cbl:648` |
| `CURTIME` | 8 | (2,71) | hh:mm:ss `// source: COCRDLIC.cbl:664` |
| `PAGENO` | 3 | (4,76) | = `WS-CA-SCREEN-NUM` `// source: COCRDLIC.cbl:667` |
| (literal) | | (24,1) | `'  F3=Exit F7=Backward  F8=Forward'` `// source: COCRDLI.bms:339` |

### Filter input fields (read on RECEIVE, also written back)
| Field IN / OUT | PIC | BMS attrs | Notes |
|---|---|---|---|
| `ACCTSIDI` / `ACCTSIDO` | X(11) | `FSET,IC,NORM,UNPROT`, GREEN, UNDERLINE, LEN 11, POS (6,44) | Account-number filter. IC = initial cursor. `// source: COCRDLI.bms:89-93; COCRDLIC.cbl:969` |
| `CARDSIDI` / `CARDSIDO` | X(16) | `FSET,NORM,UNPROT`, GREEN, UNDERLINE, LEN 16, POS (7,44) | Card-number filter. `// source: COCRDLI.bms:101-105; COCRDLIC.cbl:970` |

### Per-row fields (7 rows, suffix 1–7), read+written
| Field | PIC | BMS attrs | Notes |
|---|---|---|---|
| `CRDSELn` IN/OUT | X(1) | row 1 `FSET,NORM,PROT`; rows 2–7 same, POS (10+n,12) | Selection char S/U. Attrib byte (`CRDSELnA`) reset each send; see §8. `// source: COCRDLI.bms:140-323; COCRDLIC.cbl:972-978` |
| `ACCTNOn` OUT | X(11) | `NORM,PROT`, POS (10+n,22) | Account number of listed card. `// source: COCRDLIC.cbl:684,693,...` |
| `CRDNUMn` OUT | X(16) | `NORM,PROT`, POS (10+n,43) | Card number. `// source: COCRDLIC.cbl:685,694,...` |
| `CRDSTSn` OUT | X(1) | `NORM,PROT`, POS (10+n,67) | Active status. `// source: COCRDLIC.cbl:686,695,...` |
| `CRDSTPn` (rows 2–7) | X(1) | `ASKIP,DRK,FSET`, POS (10+n,14) | Hidden shadow field; not used by program logic. `// source: COCRDLI.bms:169-...` |

### Message fields (output)
| Field | Len | Pos | Notes |
|---|---|---|---|
| `INFOMSG` | 45 | (20,19) | informational message (e.g. action prompt). `// source: COCRDLIC.cbl:670, 928` |
| `ERRMSG` | 78 | (23,1) | red error / status message. `// source: COCRDLIC.cbl:924` |

On RECEIVE only `ACCTSIDI`, `CARDSIDI`, and `CRDSEL1I..CRDSEL7I` are consumed; `EIBAID` (PF key) and `EIBCALEN` (commarea length) are also read. `// source: COCRDLIC.cbl:962-978`

### Color/attribute symbols (DFHBMSCA) referenced
`DFHBMDAR` (dark) `// :671`; `DFHBMPRF`/`DFHBMPRO` (protect) `// :753,766`; `DFHBMFSE` (unprotect+FSET) `// :761,772`; `DFHRED` (red color byte) `// :756,873`; `DFHNEUTR` (neutral) `// :929`; `MOVE -1 TO …L` to force cursor. The `O`/`C`/`L` suffix fields = output data / color attribute / cursor-length per the symbolic map (`COCRDLI.cpy`). `// source: COCRDLI.cpy:289-561`

---

## 6. PSEUDO-CONVERSATIONAL FLOW & EIBAID/PFKey HANDLING

PF keys are mapped from `EIBAID` to `CCARD-AID-*` 88s by copybook `CSSTRPFY` (PERFORM YYYY-STORE-PFKEY). Note DFHPF13–PF24 fold back onto PFK01–PFK12. `// source: CSSTRPFY.cpy:21-78; COCRDLIC.cbl:349-350`

**Valid keys at this screen:** ENTER, PF03, PF07, PF08. Any other AID is coerced to ENTER. `// source: COCRDLIC.cbl:370-380`
```
SET PFK-INVALID
IF AID in {ENTER, PFK03, PFK07, PFK08} -> SET PFK-VALID
IF PFK-INVALID -> SET CCARD-AID-ENTER   // remap invalid key to ENTER
```

**Turn lifecycle** (0000-MAIN):
1. INITIALIZE work areas; MOVE `CCLI`→WS-TRANID; clear error msg. `// source: COCRDLIC.cbl:300-311`
2. If `EIBCALEN = 0` (first entry): INITIALIZE commarea + private tail, set FROM=this tran/pgm, USER type, PGM-ENTER, LAST-MAP/MAPSET, FIRST-PAGE, LAST-PAGE-NOT-SHOWN. Else copy the two commarea segments out of DFHCOMMAREA by offset. `// source: COCRDLIC.cbl:315-332`
3. If `CDEMO-PGM-ENTER` AND `CDEMO-FROM-PROGRAM ≠ COCRDLIC` (arriving from menu): INITIALIZE private tail, re-set PGM-ENTER/LAST-MAP/FIRST-PAGE/LAST-PAGE-NOT-SHOWN (forget paging state). `// source: COCRDLIC.cbl:336-343`
4. PERFORM YYYY-STORE-PFKEY (map AID). `// source: COCRDLIC.cbl:349-350`
5. If `EIBCALEN > 0` AND `CDEMO-FROM-PROGRAM = COCRDLIC` (re-entry from self): PERFORM 2000-RECEIVE-MAP (read+validate inputs). `// source: COCRDLIC.cbl:357-362`
6. Validate AID (above). `// source: COCRDLIC.cbl:370-380`
7. PF3-from-self short-circuit XCTL to menu (see §7). `// source: COCRDLIC.cbl:384-406`
8. If AID ≠ PF8: reset LAST-PAGE-NOT-SHOWN. `// source: COCRDLIC.cbl:410-414`
9. Main `EVALUATE TRUE` dispatch (see §7). `// source: COCRDLIC.cbl:418-583`
10. Post-dispatch: if INPUT-ERROR set error msg & next-prog and GO TO COMMON-RETURN; else MOVE this pgm to CCARD-NEXT-PROG, GO TO COMMON-RETURN. `// source: COCRDLIC.cbl:586-601`
11. COMMON-RETURN: set FROM tran/pgm, LAST-MAP/MAPSET, repack the 2000-byte commarea, `EXEC CICS RETURN TRANSID('CCLI') COMMAREA(WS-COMMAREA) LENGTH(2000)`. `// source: COCRDLIC.cbl:604-620`

---

## 7. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = a method)

### 0000-MAIN `// source: COCRDLIC.cbl:298-602`
Driver. See §6 steps 1–10. Key sub-blocks of the main `EVALUATE TRUE` (first true WHEN wins):
- **WHEN INPUT-ERROR** `// :419-438`: set CCARD-ERROR-MSG, next-prog/map = this; if BOTH filters not-NOT-OK (i.e. neither filter flagged invalid) PERFORM 9000-READ-FORWARD; PERFORM 1000-SEND-MAP; GO TO COMMON-RETURN. (See Faithful Bug B-2 re double-negative.)
- **WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE** (declared twice; first WHEN has no body, falls into the second) `// :439-454`: MOVE WS-CA-FIRST-CARD-NUM→RID; 9000-READ-FORWARD; 1000-SEND-MAP; GO TO COMMON-RETURN. (Page-up while already on page 1 → just re-list page 1.)
- **WHEN CCARD-AID-PFK03 / WHEN CDEMO-PGM-REENTER AND FROM≠this** `// :458-482`: INITIALIZE both commareas; reset FROM/USER/ENTER/maps/FIRST-PAGE; MOVE FIRST-CARD-NUM→RID; 9000-READ-FORWARD; 1000-SEND-MAP; GO TO COMMON-RETURN.
- **WHEN CCARD-AID-PFK08 AND CA-NEXT-PAGE-EXISTS** `// :486-497`: MOVE WS-CA-LAST-CARD-NUM→RID; `ADD +1 TO WS-CA-SCREEN-NUM`; 9000-READ-FORWARD; 1000-SEND-MAP; GO TO COMMON-RETURN. (Page down.)
- **WHEN CCARD-AID-PFK07 AND NOT CA-FIRST-PAGE** `// :501-513`: MOVE WS-CA-FIRST-CARD-NUM→RID; `SUBTRACT 1 FROM WS-CA-SCREEN-NUM`; 9100-READ-BACKWARDS; 1000-SEND-MAP; GO TO COMMON-RETURN. (Page up.)
- **WHEN CCARD-AID-ENTER AND VIEW-REQUESTED-ON(I-SELECTED) AND FROM=this** `// :517-541`: set FROM/USER/ENTER/maps; next-prog=`COCRDSLC`, next-mapset=`COCRDSL`, next-map=`CCRDSLA`; MOVE WS-ROW-ACCTNO(I-SELECTED)→CDEMO-ACCT-ID, WS-ROW-CARD-NUM(I-SELECTED)→CDEMO-CARD-NUM; `EXEC CICS XCTL PROGRAM(CCARD-NEXT-PROG) COMMAREA(CARDDEMO-COMMAREA)`. (Note: subscript `I-SELECTED` may be 0 — see Faithful Bug B-1.)
- **WHEN CCARD-AID-ENTER AND UPDATE-REQUESTED-ON(I-SELECTED) AND FROM=this** `// :545-569`: same, but next-prog=`COCRDUPC`/`COCRDUP`/`CCRDUPA`; XCTL to it.
- **WHEN OTHER** `// :572-582`: MOVE WS-CA-FIRST-CARD-NUM→RID; 9000-READ-FORWARD; 1000-SEND-MAP; GO TO COMMON-RETURN. (Plain ENTER with no row selected → (re)list from first key.)

### COMMON-RETURN `// source: COCRDLIC.cbl:604-620`
Set FROM-TRANID/FROM-PROGRAM/LAST-MAPSET/LAST-MAP; copy CARDDEMO-COMMAREA→WS-COMMAREA(1:); copy WS-THIS-PROGCOMMAREA→WS-COMMAREA(offset:); `EXEC CICS RETURN TRANSID('CCLI') COMMAREA(WS-COMMAREA) LENGTH(2000)`.

### 0000-MAIN-EXIT `// :621-623` — EXIT.

### 1000-SEND-MAP `// source: COCRDLIC.cbl:624-641`
Orchestrator: PERFORM 1100-SCREEN-INIT, 1200-SCREEN-ARRAY-INIT, 1250-SETUP-ARRAY-ATTRIBS, 1300-SETUP-SCREEN-ATTRS, 1400-SETUP-MESSAGE, 1500-SEND-SCREEN.

### 1100-SCREEN-INIT `// source: COCRDLIC.cbl:642-676`
MOVE LOW-VALUES→CCRDLIAO (clear map); fill titles, TRNNAME, PGMNAME; CURRENT-DATE→CURDATE (mm/dd/yy built from MM/DD/YY(3:2)); CURRENT-TIME→CURTIME (hh:mm:ss); WS-CA-SCREEN-NUM→PAGENO; set no-info-message; INFOMSG attr = DFHBMDAR (dark).

### 1100-SCREEN-INIT-EXIT `// :674-676`.

### 1200-SCREEN-ARRAY-INIT `// source: COCRDLIC.cbl:678-747`
For each of the 7 rows: if `WS-EACH-CARD(n) = LOW-VALUES` (empty slot) → leave blank; else MOVE WS-EDIT-SELECT(n)→CRDSELnO, WS-ROW-ACCTNO(n)→ACCTNOnO, WS-ROW-CARD-NUM(n)→CRDNUMnO, WS-ROW-CARD-STATUS(n)→CRDSTSnO. (Unrolled, one IF per row.)

### 1200-SCREEN-ARRAY-INIT-EXIT `// :745-747`.

### 1250-SETUP-ARRAY-ATTRIBS `// source: COCRDLIC.cbl:748-836`
For each row n: if `WS-EACH-CARD(n)=LOW-VALUES` OR `FLG-PROTECT-SELECT-ROWS-YES` → set CRDSELnA = protect (row 1 uses `DFHBMPRF`, rows 2–7 use `DFHBMPRO`); else if `WS-ROW-CRDSELECT-ERROR(n)='1'` → CRDSELnC=DFHRED and (row 1: if select char blank/low MOVE '*'; rows 2–7: `MOVE -1 TO CRDSELnL` to position cursor) then CRDSELnA = `DFHBMFSE` (unprotect+FSET). (See Faithful Bug B-4 re stray `I` token at line 790.)

### 1250-SETUP-ARRAY-ATTRIBS-EXIT `// :834-836`.

### 1300-SETUP-SCREEN-ATTRS `// source: COCRDLIC.cbl:837-892`
Repopulate filter fields & position cursor. If first entry OR (PGM-ENTER from menu) → CONTINUE (leave filters blank). Else:
- Account filter EVALUATE: if filter valid OR not-ok → MOVE CC-ACCT-ID→ACCTSIDO + unprotect; elif CDEMO-ACCT-ID=0 → MOVE LOW-VALUES→ACCTSIDO; else MOVE CDEMO-ACCT-ID→ACCTSIDO + unprotect. `// :844-854`
- Card filter EVALUATE: symmetrical with CC-CARD-NUM / CDEMO-CARD-NUM. `// :856-867`
Then: if FLG-ACCTFILTER-NOT-OK → ACCTSIDC=DFHRED + cursor; if FLG-CARDFILTER-NOT-OK → CARDSIDC=DFHRED + cursor; if INPUT-OK → cursor at ACCTSID (`MOVE -1 TO ACCTSIDL`). `// :872-886`

### 1300-SETUP-SCREEN-ATTRS-EXIT `// :890-892`.

### 1400-SETUP-MESSAGE `// source: COCRDLIC.cbl:895-935`
`EVALUATE TRUE` to choose status message (first true wins):
- ACCTFILTER-NOT-OK / CARDFILTER-NOT-OK → CONTINUE (keep the field-level error already in WS-ERROR-MSG). `// :898-900`
- PF07 AND CA-FIRST-PAGE → `'NO PREVIOUS PAGES TO DISPLAY'`. `// :901-904`
- PF08 AND NEXT-PAGE-NOT-EXISTS AND LAST-PAGE-SHOWN → `'NO MORE PAGES TO DISPLAY'`. `// :905-909`
- PF08 AND NEXT-PAGE-NOT-EXISTS → set WS-INFORM-REC-ACTIONS; if LAST-PAGE-NOT-SHOWN AND NEXT-PAGE-NOT-EXISTS → set CA-LAST-PAGE-SHOWN. `// :910-916`
- WS-NO-INFO-MESSAGE / CA-NEXT-PAGE-EXISTS → set WS-INFORM-REC-ACTIONS. `// :917-919`
- OTHER → set WS-NO-INFO-MESSAGE. `// :920-921`
Then MOVE WS-ERROR-MSG→ERRMSGO; if NOT no-info-message AND NOT no-records-found → MOVE WS-INFO-MSG→INFOMSGO + INFOMSGC=DFHNEUTR. `// :924-930`

### 1400-SETUP-MESSAGE-EXIT `// :933-935`.

### 1500-SEND-SCREEN `// source: COCRDLIC.cbl:938-950`
`EXEC CICS SEND MAP('CCRDLIA') MAPSET('COCRDLI') FROM(CCRDLIAO) CURSOR ERASE RESP(WS-RESP-CD) FREEKB`.

### 1500-SEND-SCREEN-EXIT `// :948-950`.

### 2000-RECEIVE-MAP `// source: COCRDLIC.cbl:951-961`
PERFORM 2100-RECEIVE-SCREEN then 2200-EDIT-INPUTS.

### 2100-RECEIVE-SCREEN `// source: COCRDLIC.cbl:962-983`
`EXEC CICS RECEIVE MAP('CCRDLIA') MAPSET('COCRDLI') INTO(CCRDLIAI) RESP(WS-RESP-CD)`; MOVE ACCTSIDI→CC-ACCT-ID, CARDSIDI→CC-CARD-NUM, CRDSEL1I..CRDSEL7I→WS-EDIT-SELECT(1..7).

### 2100-RECEIVE-SCREEN-EXIT `// :981-983`.

### 2200-EDIT-INPUTS `// source: COCRDLIC.cbl:985-1001`
SET INPUT-OK; SET FLG-PROTECT-SELECT-ROWS-NO; PERFORM 2210-EDIT-ACCOUNT, 2220-EDIT-CARD, 2250-EDIT-ARRAY.

### 2210-EDIT-ACCOUNT `// source: COCRDLIC.cbl:1003-1034`
SET FLG-ACCTFILTER-BLANK. If `CC-ACCT-ID = LOW-VALUES OR SPACES OR CC-ACCT-ID-N = ZEROS` → blank, MOVE ZEROES→CDEMO-ACCT-ID, exit. Elif `CC-ACCT-ID IS NOT NUMERIC` → SET INPUT-ERROR, FLG-ACCTFILTER-NOT-OK, FLG-PROTECT-SELECT-ROWS-YES, error msg `'ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER'`, MOVE ZERO→CDEMO-ACCT-ID, exit. Else MOVE CC-ACCT-ID→CDEMO-ACCT-ID, SET FLG-ACCTFILTER-ISVALID.

### 2210-EDIT-ACCOUNT-EXIT `// :1032-1034`.

### 2220-EDIT-CARD `// source: COCRDLIC.cbl:1036-1071`
SET FLG-CARDFILTER-BLANK. If `CC-CARD-NUM = LOW-VALUES OR SPACES OR CC-CARD-NUM-N = ZEROS` → blank, MOVE ZEROES→CDEMO-CARD-NUM, exit. Elif `CC-CARD-NUM IS NOT NUMERIC` → SET INPUT-ERROR, FLG-CARDFILTER-NOT-OK, FLG-PROTECT-SELECT-ROWS-YES; **if WS-ERROR-MSG-OFF** set msg `'CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER'` (only if acct error did not already set it); MOVE ZERO→CDEMO-CARD-NUM, exit. Else MOVE CC-CARD-NUM-N→CDEMO-CARD-NUM, SET FLG-CARDFILTER-ISVALID.

### 2220-EDIT-CARD-EXIT `// :1069-1071`.

### 2250-EDIT-ARRAY `// source: COCRDLIC.cbl:1073-1121`
If INPUT-ERROR already → exit. `INSPECT WS-EDIT-SELECT-FLAGS TALLYING I FOR ALL 'S' ALL 'U'` (count selections). If `I > 1` → SET INPUT-ERROR + WS-MORE-THAN-1-ACTION; build WS-EDIT-SELECT-ERROR-FLAGS by replacing 'S'/'U'→'1' and all others→'0'. MOVE 0→I-SELECTED. PERFORM VARYING I 1..7: `SELECT-OK(I)` → I-SELECTED=I (and if MORE-THAN-1-ACTION mark row error); `SELECT-BLANK(I)` → continue; OTHER → SET INPUT-ERROR, mark row error, and if WS-ERROR-MSG-OFF set `'INVALID ACTION CODE'`.

### 2250-EDIT-ARRAY-EXIT `// :1119-1121`.

### 9000-READ-FORWARD `// source: COCRDLIC.cbl:1123-1263`
MOVE LOW-VALUES→WS-ALL-ROWS (clear page buffer). `STARTBR CARDDAT RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) GTEQ`. Init WS-SCRN-COUNTER=0, CA-NEXT-PAGE-EXISTS, MORE-RECORDS-TO-READ. Loop UNTIL READ-LOOP-EXIT:
- READNEXT into CARD-RECORD.
- NORMAL/DUPREC: PERFORM 9500-FILTER-RECORDS; if not excluded → ADD 1→counter, store CARD-NUM/ACCT-ID/STATUS into WS-ROW-*(counter); if counter=1 record first-card key & (if SCREEN-NUM=0 ADD +1→SCREEN-NUM). If counter = WS-MAX-SCREEN-LINES (7): SET READ-LOOP-EXIT, save last-card key, do a **look-ahead READNEXT** → NORMAL/DUPREC: CA-NEXT-PAGE-EXISTS + update last-card key; ENDFILE: CA-NEXT-PAGE-NOT-EXISTS + `'NO MORE RECORDS TO SHOW'` (if msg off); OTHER: error message + stop.
- ENDFILE (main): SET READ-LOOP-EXIT + CA-NEXT-PAGE-NOT-EXISTS; save last-card key; `'NO MORE RECORDS TO SHOW'` (if msg off); if SCREEN-NUM=1 AND counter=0 → SET WS-NO-RECORDS-FOUND. `// :1233-1245`
- OTHER: error message + stop. `// :1246-1254`
After loop: `ENDBR CARDDAT`.

### 9000-READ-FORWARD-EXIT `// :1261-1263`.

### 9100-READ-BACKWARDS `// source: COCRDLIC.cbl:1264-1380`
MOVE LOW-VALUES→WS-ALL-ROWS; `MOVE WS-CA-FIRST-CARDKEY → WS-CA-LAST-CARDKEY`. `STARTBR CARDDAT RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) GTEQ`. `COMPUTE WS-SCRN-COUNTER = WS-MAX-SCREEN-LINES + 1` (= 8). Set CA-NEXT-PAGE-EXISTS, MORE-RECORDS. **Priming READPREV** (skips the current first record): NORMAL/DUPREC → `SUBTRACT 1 FROM counter` (→7); OTHER → error + GO TO exit. Then loop UNTIL READ-LOOP-EXIT:
- READPREV into CARD-RECORD.
- NORMAL/DUPREC: PERFORM 9500-FILTER-RECORDS; if not excluded → store into WS-ROW-*(counter), `SUBTRACT 1 FROM counter`; if counter=0 → SET READ-LOOP-EXIT + record first-card key.
- OTHER: error + stop.

### 9100-READ-BACKWARDS-EXIT `// :1374-1380`
`ENDBR CARDDAT`; EXIT. (Note: ENDBR lives in the EXIT paragraph.)

### 9500-FILTER-RECORDS `// source: COCRDLIC.cbl:1382-1411`
SET WS-DONOT-EXCLUDE-THIS-RECORD. If FLG-ACCTFILTER-ISVALID: if `CARD-ACCT-ID = CC-ACCT-ID` continue else SET WS-EXCLUDE + exit. If FLG-CARDFILTER-ISVALID: if `CARD-NUM = CC-CARD-NUM-N` continue else SET WS-EXCLUDE + exit. (Account compares 9(11)=X(11); card compares X(16)=9(16) — see Port Notes.)

### 9500-FILTER-RECORDS-EXIT `// :1409-1411`.

### YYYY-STORE-PFKEY / -EXIT (copybook CSSTRPFY) `// source: CSSTRPFY.cpy:17-82`
EVALUATE EIBAID → set matching `CCARD-AID-*` 88. PF13–24 fold to PF01–12.

### SEND-PLAIN-TEXT / SEND-LONG-TEXT (debug, dead) `// source: COCRDLIC.cbl:1422-1453`
`SEND TEXT` + `RETURN`. Not reachable from normal flow (no PERFORM/GO TO targets them). Do not port except as inert.

---

## 8. ARITHMETIC / COMPUTE / SUBSCRIPT NOTES

- `ADD +1 TO WS-CA-SCREEN-NUM` (PF8 page-down) `// :492`; `ADD +1 TO WS-CA-SCREEN-NUM` (first record of forward page when SCREEN-NUM=0) `// :1178`. `WS-CA-SCREEN-NUM` is `9(1)` → wraps/truncates at 10 (a 2-digit page would lose the high digit; PAGENO field is 3 long but source is 1 digit). Reproduce as a single decimal digit with silent truncation. `// source: COCRDLIC.cbl:237`
- `SUBTRACT 1 FROM WS-CA-SCREEN-NUM` (PF7 page-up) `// :508`.
- `COMPUTE WS-SCRN-COUNTER = WS-MAX-SCREEN-LINES + 1` = 8, then primed down to 7 (`SUBTRACT 1`), then decremented per stored row down to 0 in backward read. `WS-SCRN-COUNTER` is `S9(4) COMP`. `// :1284-1286, 1307, 1346`
- `INSPECT … TALLYING I FOR ALL 'S' ALL 'U'` — counts S+U chars across the 7 selection bytes into `I` (`S9(4) COMP`). `// :1079-1082`
- Subscripts: `WS-ROW-*(WS-SCRN-COUNTER)`, `VIEW/UPDATE-REQUESTED-ON(I-SELECTED)`, `WS-ROW-CRDSELECT-ERROR(I)`. `I-SELECTED` ranges 0..7; **0 is used as a subscript** in the ENTER dispatch (Faithful Bug B-1). Bounds for the OCCURS-7 arrays are 1..7. `// :518, 546, 531, 1097, 1102`

---

## 9. VALIDATION RULES & EXACT LITERAL MESSAGES

Validation order: account filter → card filter → selection array. `// source: COCRDLIC.cbl:985-997`

| Rule | Condition | Action | Exact message |
|---|---|---|---|
| Acct blank | `CC-ACCT-ID` = LOW-VALUES/SPACES or `CC-ACCT-ID-N`=0 | filter blank; CDEMO-ACCT-ID=0; no error | (none) `// :1007-1013` |
| Acct non-numeric | `CC-ACCT-ID IS NOT NUMERIC` | INPUT-ERROR; ACCTFILTER-NOT-OK; PROTECT-ROWS-YES | `ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER` `// :1021-1023` |
| Acct valid | numeric & non-blank | ACCTFILTER-ISVALID; CDEMO-ACCT-ID=value | (none) `// :1027-1028` |
| Card blank | `CC-CARD-NUM` = LOW-VALUES/SPACES or `CC-CARD-NUM-N`=0 | filter blank; CDEMO-CARD-NUM=0; no error | (none) `// :1042-1048` |
| Card non-numeric | `CC-CARD-NUM IS NOT NUMERIC` | INPUT-ERROR; CARDFILTER-NOT-OK; PROTECT-ROWS-YES; set msg **only if** WS-ERROR-MSG-OFF | `CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER` `// :1057-1059` |
| Card valid | numeric & non-blank | CARDFILTER-ISVALID; CDEMO-CARD-NUM=value | (none) `// :1064-1065` |
| >1 selection | `INSPECT TALLY I > 1` | INPUT-ERROR; MORE-THAN-1-ACTION; mark all S/U rows error | `PLEASE SELECT ONLY ONE RECORD TO VIEW OR UPDATE` `// :123-124, 1086` |
| Bad action char | row char not in {S,U,blank,low} | INPUT-ERROR; mark row error; set msg if msg-off | `INVALID ACTION CODE` `// :125-126, 1112` |

Status/info messages (1400-SETUP-MESSAGE / 9000):
- `NO PREVIOUS PAGES TO DISPLAY` `// :903`
- `NO MORE PAGES TO DISPLAY` `// :908`
- `NO MORE RECORDS TO SHOW` `// :1219, 1239`
- `NO RECORDS FOUND FOR THIS SEARCH CONDITION.` (88 WS-NO-RECORDS-FOUND) `// :121-122`
- `PF03 PRESSED.EXITING` (88 WS-EXIT-MESSAGE) `// :119-120`
- `TYPE S FOR DETAIL, U TO UPDATE ANY RECORD` (88 WS-INFORM-REC-ACTIONS, INFOMSG) `// :115-116`
- File error message group (assembled) `// :153-171, 1226-1230`

---

## 10. FAITHFUL BUGS — reproduce verbatim, DO NOT FIX

- **B-1 — `I-SELECTED = 0` used as subscript.** In 0000-MAIN the ENTER dispatch tests `VIEW-REQUESTED-ON(I-SELECTED)` / `UPDATE-REQUESTED-ON(I-SELECTED)` `// :518, 546`, and `I-SELECTED` is initialized to 0 in 2250-EDIT-ARRAY and only set to a row number if a valid S/U was found `// :1097, 1102`. When the user presses ENTER with **no** row selected, `I-SELECTED=0`; the 88 `DETAIL-WAS-REQUESTED VALUES 1 THRU 7` guard exists `// :94` but is **not** consulted before subscripting. In COBOL this is an out-of-range (0) subscript read; in practice the two WHENs are false (the byte at offset 0 of the OCCURS is not 'S'/'U') so the OTHER branch lists from first key. Port: reproduce by treating `I-SELECTED=0` as "no selection" → fall to the OTHER list branch; do NOT throw on the 0 index. `// source: COCRDLIC.cbl:518, 546, 1097`
- **B-2 — Double-negative filter guard.** The INPUT-ERROR WHEN re-reads forward only `IF NOT FLG-ACCTFILTER-NOT-OK AND NOT FLG-CARDFILTER-NOT-OK` `// :431-432`. Because `FLG-ACCTFILTER-NOT-OK` is the `'0'` value of WS-EDIT-ACCT-FLAG and the program sets it via `SET FLG-ACCTFILTER-NOT-OK TO TRUE` on bad input, `NOT FLG-...-NOT-OK` is true whenever the flag ≠ '0' — including the blank `' '` state. The net effect (a re-list happens for selection-array errors but is skipped for filter-format errors) is intended but the naming inverts intuition. Reproduce the boolean exactly. `// source: COCRDLIC.cbl:431-435`
- **B-3 — Card-filter message suppressed by acct error.** 2220-EDIT-CARD only sets its message `IF WS-ERROR-MSG-OFF` `// :1056-1060`. If the account filter already set an error message, an additionally-bad card filter is flagged (NOT-OK) but its message is **not** shown. Account message wins. Preserve. `// source: COCRDLIC.cbl:1056-1060`
- **B-4 — Stray `I` token in 1250-SETUP-ARRAY-ATTRIBS.** Line 790 contains a lone `I` between the row-4 protect MOVE and its ELSE `// :790`. Under the mainframe compiler this parses as a continuation/no-op (or the original had a typo); the row-4 branch otherwise mirrors rows 2,3,5,6,7. Functionally inert. Port row 4 identically to the other rows; record the anomaly. `// source: COCRDLIC.cbl:787-797`
- **B-5 — Row-1 protect uses `DFHBMPRF`, rows 2–7 use `DFHBMPRO`.** Row 1's protected-empty attribute is `DFHBMPRF` (protect + FSET) `// :753` while rows 2–7 use `DFHBMPRO` (protect, no FSET) `// :766,777,789,801,812,824`. Asymmetric by design/accident; reproduce per-row. `// source: COCRDLIC.cbl:753, 766`
- **B-6 — Row-1 error highlight differs.** On a selection error, row 1 writes `'*'` into CRDSEL1O when the char is blank `// :757-759` whereas rows 2–7 instead `MOVE -1 TO CRDSELnL` (cursor) `// :770, 782, 794, 805, 817, 828`. Reproduce the row-1-special behavior. `// source: COCRDLIC.cbl:755-761`
- **B-7 — Admin/non-admin listing not implemented.** Header says admin sees all cards, non-admin only their ACCT's cards `// :4-8`, but `CDEMO-USER-TYPE` is never tested in PROCEDURE DIVISION; filtering is solely by the on-screen ACCT/CARD filters, and USER-TYPE is hard-SET to `USER` `// :320,388,466,522,550`. Do NOT add admin gating. `// source: COCRDLIC.cbl:4-8, 320`
- **B-8 — Unused `CARDAIX` alt-path literal.** `LIT-CARD-FILE-ACCT-PATH = 'CARDAIX'` `// :215-217` is declared but never referenced. Account filtering is in-program. Do not implement an alt index. `// source: COCRDLIC.cbl:215-217`
- **B-9 — `WS-CA-SCREEN-NUM` is 1 digit.** Page number is `9(1)` `// :237`; beyond 9 pages it truncates. Reproduce single-digit silent truncation. `// source: COCRDLIC.cbl:237`

---

## 11. PORT NOTES (relational-access translation plan & COBOL semantics)

- **CARD browse → ordered SQL cursor.** Model 9000-READ-FORWARD as: open a forward iterator over `CARD` `WHERE card_num >= @startKey ORDER BY card_num ASC` (ordinal/byte compare on the 16-char key), pull rows, applying 9500-FILTER-RECORDS in-memory, collecting up to 7. The "look-ahead READNEXT" after row 7 = peek one more row to set the next-page-exists flag and record the last key (without displaying it). 9100-READ-BACKWARDS = `WHERE card_num <= @firstKey ORDER BY card_num DESC`, prime one READPREV to skip the current first row, then fill the 7-row buffer bottom-up (indices 7→1). The repository must expose a stable bidirectional ordered cursor; the `WS-CARD-RID-CARDNUM` start key (X16, GTEQ) is the seek anchor. `// source: COCRDLIC.cbl:1123-1380`
- **GTEQ from LOW-VALUES.** When the RID is LOW-VALUES (fresh first page or OTHER/ENTER list), `card_num >= x'00…00'` returns the lowest key → first record. Implement LOW-VALUES start as "from beginning". `// source: COCRDLIC.cbl:574, 1131`
- **Filter comparisons / REDEFINES.** `CC-ACCT-ID` is X(11) but compared to `CARD-ACCT-ID` 9(11) (`IF CARD-ACCT-ID = CC-ACCT-ID` `// :1386`) and the card filter compares `CARD-NUM` X(16) to `CC-CARD-NUM-N` 9(16) (`// :1397`). In .NET, normalize both sides to a zero-padded numeric string (or numeric value) so the comparison matches COBOL's numeric/alphanumeric coercion (zoned-decimal vs char). The redefines `CC-ACCT-ID-N`/`CC-CARD-NUM-N` give the numeric view; `IS NOT NUMERIC` tests the character view. `// source: CVCRD01Y.cpy:34-39; COCRDLIC.cbl:1017, 1052, 1386, 1397`
- **`IS NOT NUMERIC`.** True if any of the 11/16 chars is not a digit (spaces/low-values count as non-numeric). Implement as `!Regex.IsMatch(field, "^[0-9]+$")` over the fixed-width chars (note: a field of all spaces is caught earlier by the blank test, so the NOT-NUMERIC branch sees partially-filled input). `// source: COCRDLIC.cbl:1017, 1052`
- **INITIALIZE semantics.** `INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA` sets group to spaces (alphanumeric) / zeros (numeric) / and 88-defined LOW-VALUES are NOT honored by INITIALIZE — numeric `WS-CA-SCREEN-NUM` → 0, `WS-CA-NEXT-PAGE-IND` X(1) → space (not low-values). Match field-by-field type defaults. `// source: COCRDLIC.cbl:316-317, 338, 462-463`
- **OCCURS / page buffer.** `WS-ALL-ROWS X(196)` cleared to LOW-VALUES marks empty slots; the SEND paragraphs test `WS-EACH-CARD(n) = LOW-VALUES` to skip empty rows. Model the page as a `Row?[7]` (null = empty) or a 28-byte fixed image with a low-values sentinel. `// source: COCRDLIC.cbl:252-260, 680, 1124`
- **Edited PIC / date-time build.** CURDATE = mm/dd/yy assembled from CURRENT-DATE via `WS-CURDATE-YEAR(3:2)` (last 2 digits) `// :654-658`; CURTIME = hh:mm:ss `// :660-664`. Use `IClock` + the Runtime fixed-width formatter; no money editing in this program.
- **Pseudo-conversational state.** Persist the full 2000-byte COMMAREA (CARDDEMO-COMMAREA + program tail with first/last keys + page state) across turns; the Online shim's COMMAREA store handles this. RETURN TRANSID is always `CCLI`. `// source: COCRDLIC.cbl:609-619`
- **AID remap.** PF13–24 → PF01–12 (CSSTRPFY); invalid AIDs at this screen coerced to ENTER. Implement both folds. `// source: CSSTRPFY.cpy:54-77; COCRDLIC.cbl:378-380`
- **Attribute bytes.** Implement DFHBMSCA constants (DFHBMPRF/PRO/FSE, DFHRED/NEUTR/DAR) as the BMS attribute model in `CardDemo.Online`; the per-row `A`/`C`/`L` symbolic fields drive protect/color/cursor. `// source: COCRDLI.cpy:289-561`

---

## 12. OPEN QUESTIONS / RISKS

- **EBCDIC vs ASCII key collation for READPREV/READNEXT.** The CARD key is X(16) of digits; for the digit subset ordinal compare matches EBCDIC ordering, but if any card number ever contains spaces/low-values the forward/backward ordering must be pinned by a guard test (per ARCHITECTURE.md). `// source: ARCHITECTURE.md §VSAM-semantics`
- **Look-ahead key bookkeeping at page boundary.** In 9000, when row 7 is the last record before ENDFILE, `WS-CA-LAST-CARD-*` is set from the look-ahead path's ENDFILE branch leaving the *displayed* row 7's key as last; verify the next/prev key math round-trips through PF7/PF8 against captured screen-parity fixtures (no CICS oracle). `// source: COCRDLIC.cbl:1191-1232`
- **`CRDSTPn` shadow fields** (rows 2–7, ASKIP/DRK) appear in the map but are never written by the program; confirm they remain blank on the rendered screen. `// source: COCRDLI.bms:169-...`
- **`SEND-PLAIN-TEXT` / `SEND-LONG-TEXT`** are unreachable debug paragraphs; confirm omission is acceptable (they are explicitly "Dont use in production"). `// source: COCRDLIC.cbl:1419-1453`
