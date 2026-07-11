# Faithful-Bugs Registry — CardDemo COBOL → .NET 10 Port

These are mainframe behaviors — including genuine bugs, copy-paste mistakes, dead code, misleading messages, and architecture-dependent quirks — that are reproduced verbatim in the .NET port and are deliberately **never fixed**. Each entry below must be locked by a pinning (characterization) test in the final verification so that any accidental "correction" is caught.

Each entry has: a short bug ID, the COBOL source line citation, a one-line description of the faithful behavior, and the target paragraph/method that reproduces it where the spec names one. (The per-program PORT SPECs cite COBOL sources and target paragraphs; they do not generally name concrete `.cs` files, so the C# target is given only where the spec states it.)

---

## CBACT01C — Account master print

1. **CBACT01C-FB1** — `CBACT01C.cbl:236-238` — `OUT-ACCT-CURR-CYC-DEBIT` is assigned `2525.00` ONLY when `ACCT-CURR-CYC-DEBIT = ZERO`; non-zero sources leave it stale (carries prior iteration's value); genuine stale-data bug. (1300-WRITE-ACCT-RECORD)
2. **CBACT01C-FB2** — `CBACT01C.cbl:156-160, 388-404` — OUTFILE/ARRYFILE/VBRCFILE are never CLOSEd; only `9000-ACCTFILE-CLOSE` runs; port must flush/close at end-of-run with no per-iteration close.
3. **CBACT01C-FB3** — `CBACT01C.cbl:268, 294, 309` — Wrong DISPLAY label `'ACCOUNT FILE WRITE STATUS IS:'` shown for the ARRYFILE and VBRCFILE write failures too; keep the misleading label.
4. **CBACT01C-FB4** — `CBACT01C.cbl:254-260` — `ARR-ARRAY-REC` occurrences (4) and (5) are never populated; they stay at the INITIALIZE-zeroed value. (1400)
5. **CBACT01C-FB5** — `cpy/CVACT01Y.cpy:11; CBACT01C.cbl:64, 207, 222` — Misspelled data name `ACCT-EXPIRAION-DATE` (should be EXPIRATION) kept in field names and SYSOUT label.
6. **CBACT01C-FB6** — `CBACT01C.cbl:288-290, 303-305` — VBR-REC stale tail bytes: `1550` writes only first 12 of 80 bytes, `1575` only first 39; port must emit exactly `WS-RECD-LEN` bytes (honor variable length, do not serialize whole 80-byte buffer).
7. **CBACT01C-FB7** — `CBACT01C.cbl:237, 256, 258, 259, 260` — Hard-coded magic array/debit constants (`2525.00`, `1005.00`, `1525.00`, `-1025.00`, `-2500.00`); reproduce the exact literals.
8. **CBACT01C-FB8** — `CBACT01C.cbl:131-137, 223-224, 282` — `WS-ACCT-REISSUE-DATE` has FILLER bytes at positions 5/8; the flat redefine `WS-REISSUE-DATE X(10)` makes `WS-ACCT-REISSUE-YYYY` = year only by accident of byte layout; reproduce the byte layout.

## CBACT02C — Card master print

1. **CBACT02C-FB1** — `CBACT02C.cbl:168, 172` — DISPLAY emits literal placeholder `NNNN` immediately followed by the actual `IO-STATUS-04` value (e.g. `FILE STATUS IS: NNNN0010`); keep the literal `NNNN`. (9910)
2. **CBACT02C-FB2** — `CVACT02Y.cpy:9; ARCHITECTURE.md:54` — Misspelled copybook field `CARD-EXPIRAION-DATE` (missing 'T'); keep the source typo traceable.
3. **CBACT02C-FB3** — `CBACT02C.cbl:53-56, 162-167` — Status-byte-to-number conversion relies on big-endian EBCDIC halfword aliasing (char into low byte of `PIC 9(4) BINARY`, e.g. `'0'`→240); reproduce the golden mapping, do not parse as a decimal digit. (9910)
4. **CBACT02C-FB4** — `CBACT02C.cbl:137, 140, 142` — Obfuscated constant assignment via arithmetic (`ADD 8 TO ZERO`, `SUBTRACT APPL-RESULT FROM APPL-RESULT`, `ADD 12 TO ZERO`) sets 8/0/12; keep equivalent assignments. (9000-CARDFILE-CLOSE)
5. **CBACT02C-FB5** — `CBACT02C.cbl:121-125` — OPEN treats only '00' as success; any other status (including impossible '10' on OPEN) goes to the 12/abend path; do not add EOF handling to open.
6. **CBACT02C-FB6** — `CBACT02C.cbl:116, 134, 152, 174` — Dead bare `EXIT` statements (no-op fall-through, not `EXIT PROGRAM`); methods just return.

## CBACT03C — Card cross-reference print

1. **CBACT03C-FB1** — `CBACT03C.cbl:96, 78` — Each cross-reference record is DISPLAYed TWICE (once in `1000-XREFFILE-GET-NEXT`, once in the mainline after the PERFORM); emit each record's display twice, back-to-back.
2. **CBACT03C-FB2** — `CBACT03C.cbl:53-56, 162-167` — `9910-DISPLAY-IO-STATUS` second-byte rendered as low byte of a halfword binary (0..255); reproduce the big-endian result (value = char code of `IO-STAT2`), not a little-endian misread.
3. **CBACT03C-FB3** — `CBACT03C.cbl:74-80` — Redundant inner `IF END-OF-FILE = 'N'` guards (lines 75, 77) duplicate the `PERFORM UNTIL` condition; reproduce the control structure as-is.

## CBACT04C — Interest calculator

1. **CBACT04C-FB1** — `CBACT04C.cbl:281` — `0200-DISCGRP-OPEN` prints `'ERROR OPENING DALY REJECTS FILE'` though it opens the disclosure-group file (mislabeled open error).
2. **CBACT04C-FB2** — `CBACT04C.cbl:373-389, 394-410` — In `1100-GET-ACCT-DATA`/`1110-GET-XREF-DATA`, `INVALID KEY` prints "ACCOUNT NOT FOUND" but the next status check still abends; reproduce: print not-found message, then abend.
3. **CBACT04C-FB3** — `CBACT04C.cbl:194-217` — Account/xref keys (`ACCT-GROUP-ID`, `ACCT-ID`, `XREF-CARD-NUM`) are loaded only on an account break, but interest is computed per TCATBAL record; keep the load strictly on the break (correct only because input is key-ordered).
4. **CBACT04C-FB4** — `CBACT04C.cbl:482-483` — `MOVE '05' TO TRAN-CAT-CD` (`PIC 9(4)`) yields `0005`; `MOVE '01' TO TRAN-TYPE-CD` (X(2)) yields `01`; reproduce `0005`, not `05`.
5. **CBACT04C-FB5** — `CBACT04C.cbl:621-622, 164-165` — DB2 timestamp microseconds are always `<hundredths>0000` (`DB2-MIL PIC 9(2)` + literal `'0000'` REST), never true microseconds.
6. **CBACT04C-FB6** — `CBACT04C.cbl:516-520` — `1400-COMPUTE-FEES` is a no-op; fees are never computed though called each interest-bearing record.
7. **CBACT04C-FB7** — `CBACT04C.cbl:464-465` — Interest computed without `ROUNDED`; truncate toward zero at 2 decimals; must NOT round-half-up.

## CBCUS01C — Customer master print

1. **CBCUS01C-FB1** — `CBCUS01C.cbl:96, 78` — Each customer record is DISPLAYed twice (in `1000-CUSTFILE-GET-NEXT` and again in the MAIN loop); reproduce both DISPLAYs in order.
2. **CBCUS01C-FB2** — `CBCUS01C.cbl:168, 172` — DISPLAY emits literal placeholder `NNNN` followed by formatted value (e.g. `FILE STATUS IS: NNNN0010`); keep the literal `NNNN`.
3. **CBCUS01C-FB3** — `CBCUS01C.cbl:162-168` — `Z-DISPLAY-IO-STATUS` non-numeric/`'9'` branch renders 2nd byte as raw binary-to-3-digit number; reproduce the byte-juggling, not clean formatting.

## CBEXPORT — Master-file export

1. **CBEXPORT-FB1** — `CBEXPORT.cbl:135, 191-194` — `WS-CURR-HUNDREDTH` captured but never used; 26-char timestamp always ends in literal `.00`.
2. **CBEXPORT-FB2** — `CBEXPORT.cbl:278-279, 347-348, 411-412, 466-467, 531-532` — Hard-coded `EXPORT-BRANCH-ID = '0001'` and `EXPORT-REGION-CODE = 'NORTH'` on every record; do not derive.
3. **CBEXPORT-FB3** — `CBEXPORT.cbl:579` — `CALL 'CEE3ABD'` with no arguments (malformed LE call); treat `9999-ABEND-PROGRAM` as immediate abnormal termination after the ABEND banner, not a graceful return.
4. **CBEXPORT-FB4** — `CBEXPORT.cbl:556-561` — `6000-FINALIZE` closes all six files without checking status; a close failure is silently ignored; do not add close error handling.
5. **CBEXPORT-FB5** — `CBEXPORT.cbl:271, 340, 404, 459, 524; CVEXPORT.cpy:19,42,60,79,88,100` — Per-WRITE `INITIALIZE EXPORT-RECORD` space-fills the X(460) base view, then the active REDEFINES overlays binary/packed/zoned fields; reproduce base space-fill then type overlay.
6. **CBEXPORT-FB6** — `CVEXPORT.cpy:54,98; CVACT01Y.cpy:11; CVACT02Y.cpy:9` — Misspelled `EXPIRAION-DATE` (missing second 'T') carried as the canonical field/column name.
7. **CBEXPORT-FB7** — `CBEXPORT.cbl:101,104,107,110,113, 262, 331, 395, 450, 515` — EOF keyed to status literal `'10'`; any other non-'00' READ status → ABEND; "cursor exhausted" must surface as status `'10'`.
8. **CBEXPORT-FB8** — `CBEXPORT.cbl:123, 276-277, 345-346, 409-410, 464-465, 529-530` — Sequence counter incremented before each WRITE, so first key is 1; no record ever has sequence number 0.

## CBIMPORT — Master-file import

1. **CBIMPORT-FB1** — `CBIMPORT.cbl:63,233-238,414; CBIMPORT.jcl:33-60` — `CARD-OUTPUT ASSIGN TO CARDOUT` is OPENed/written but has no JCL DD; reproduce the missing-DD condition (the .NET CARD insert succeeds — pin the divergence).
2. **CBIMPORT-FB2** — `CVEXPORT.cpy:16; CBIMPORT.cbl:157,431` — `EXPORT-SEQUENCE-NUM` `9(9)` MOVEd into `ERR-SEQUENCE` `9(7)` truncates the two high-order digits; do NOT widen.
3. **CBIMPORT-FB3** — `CBIMPORT.cbl:30-31,449-452` — `3000-VALIDATE-IMPORT` performs zero checks yet prints "Import validation completed / No validation errors detected"; reproduce as-is (no validation logic).
4. **CBIMPORT-FB4** — `CBIMPORT.cbl:429,153` — `ERR-TIMESTAMP` X(26) receives raw 21-char `FUNCTION CURRENT-DATE` left-justified (5 trailing spaces), giving a non-ISO timestamp; reproduce verbatim.
5. **CBIMPORT-FB5** — `CBIMPORT.cbl:43-66, 312,341,361,391,414` — Sequential OPEN OUTPUT + WRITE with no key/dup checks; duplicate ids produce duplicate rows (never sees FileStatus '22'); replicate append-without-PK-enforcement on this load path.

## CBTRN01C — Daily transaction validation

1. **CBTRN01C-FB1** — `CBTRN01C.cbl:158,160,162, 189,191,193` — CUSTOMER, CARD, and TRANSACT files are OPENed/CLOSEd but never READ/WRITTEN; reproduce open/close (or no-op) without any query.
2. **CBTRN01C-FB2** — `CBTRN01C.cbl:164-186, 167-169, 170-184, 203` — XREF/ACCOUNT lookup runs one extra time at EOF using the STALE last `DALYTRAN-RECORD` (only the DISPLAY is EOF-guarded; lookups are not), producing an extra set of SYSOUT lines; do not add an EOF guard.
3. **CBTRN01C-FB3** — `CBTRN01C.cbl:372` — `9000-DALYTRAN-CLOSE` displays `'ERROR CLOSING CUSTOMER FILE'` on a DALYTRAN close failure (copy-paste); keep the literal.
4. **CBTRN01C-FB4** — `CBTRN01C.cbl:373` — On DALYTRAN-close error it does `MOVE CUSTFILE-STATUS TO IO-STATUS` (customer file's status, not `DALYTRAN-STATUS`); reproduce verbatim.
5. **CBTRN01C-FB5** — `CBTRN01C.cbl:253 vs 362` — Opens prime via `MOVE 8 TO APPL-RESULT`, closes via `ADD 8 TO ZERO GIVING APPL-RESULT`; both yield 8; keep both styles.
6. **CBTRN01C-FB6** — `CBTRN01C.cbl:164-165` — Redundant inner `IF END-OF-DAILY-TRANS-FILE = 'N'` guard duplicates the `PERFORM UNTIL` condition; reproduce as-is.
7. **CBTRN01C-FB7** — `CBTRN01C.cbl:133-136, 477-482` — `Z-DISPLAY-IO-STATUS` second-byte big-endian halfword rendering (0..255); reproduce big-endian, not little-endian.

## CBTRN02C — Transaction posting

1. **CBTRN02C-FB1** — `CBTRN02C.cbl:407-420` — Credit-limit (reason 102) and expiration (reason 103) checks are sequential `IF`s with no `ELSE`; if both fail, 102 is overwritten by 103; only the last failure is reported.
2. **CBTRN02C-FB2** — `CBTRN02C.cbl:424-444, 545-559` — Posting side effects (TCATBAL/account/transaction writes) occur before the account-REWRITE INVALID-KEY check is re-examined; an INVALID KEY sets reason 109 but no reject is written and the transaction is still WRITEn; reproduce the order.
3. **CBTRN02C-FB3** — `CBTRN02C.cbl:548-552` — For `DALYTRAN-AMT < 0`, `ADD DALYTRAN-AMT TO ACCT-CURR-CYC-DEBIT` adds the negative value (debit bucket moves negative); preserve the literal ADD.
4. **CBTRN02C-FB4** — `CBTRN02C.cbl:403-405` — Credit-limit uses cycle credit/debit + amount (`ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT`), not `ACCT-CURR-BAL`; keep the formula.
5. **CBTRN02C-FB5** — `CBTRN02C.cbl:187, 403-405` — `WS-TEMP-BAL` is `S9(9)V99` while cycle fields are `S9(10)V99`; COMPUTE truncates to 9 integer digits toward zero (silent overflow).
6. **CBTRN02C-FB6** — `CBTRN02C.cbl:637-652` — `9300-DALYREJS-CLOSE` on error does `MOVE XREFFILE-STATUS TO IO-STATUS` (cross-ref file's status, not `DALYREJS-STATUS`); reproduce verbatim.
7. **CBTRN02C-FB7** — `CBTRN02C.cbl:437-438` — `TRAN-PROC-TS` always overwritten with the current-run DB2 timestamp, discarding `DALYTRAN-PROC-TS`; pin for golden-master timestamp masking.

## CBTRN03C — Transaction detail report

1. **CBTRN03C-FB1** — `CBTRN03C.cbl:173-178, 206` — Date filter `IF in-range CONTINUE ELSE NEXT SENTENCE`; `NEXT SENTENCE` jumps past the period ending the whole `PERFORM UNTIL` sentence so out-of-range records are skipped; reproduce the exact semantics.
2. **CBTRN03C-FB2** — `CBTRN03C.cbl:197-203, 249` — At EOF, `READ INTO` leaves `TRAN-RECORD` stale and the ELSE branch adds the last `TRAN-AMT` again into page & account totals (last amount double-added), inflating page and grand totals; do not guard the EOF accumulation.
3. **CBTRN03C-FB3** — `CBTRN03C.cbl:181-187, 197-203` — The final card's account total is never written (`1120-WRITE-ACCOUNT-TOTALS` runs only on a card control break, not at EOF); reproduce the omitted final account-total line.
4. **CBTRN03C-FB4** — `CBTRN03C.cbl:282-285, 299,302,311,314,327,331,335,339,373` — `WS-LINE-COUNTER` counts every report line (headers +4, totals +2, details +1), so the MOD-20 page break is not a clean 20-detail page; reproduce the counter increments exactly.
5. **CBTRN03C-FB5** — `CBTRN03C.cbl:318-322` — `1110-WRITE-GRAND-TOTALS` does not increment `WS-LINE-COUNTER` while other write paragraphs do; grand total is "uncounted"; reproduce as-is.
6. **CBTRN03C-FB6** — `CBTRN03C.cbl:282-285` — Page-total is written ahead of the header on subsequent pages, then `1120-WRITE-HEADERS` re-emits the header; reproduce the order `1110-WRITE-PAGE-TOTALS` then `1120-WRITE-HEADERS`.
7. **CBTRN03C-FB7** — `CBTRN03C.cbl:170-171` — Redundant inner `IF END-OF-FILE = 'N'` guard duplicates the `PERFORM UNTIL` condition; reproduce as-is.
8. **CBTRN03C-FB8** — `CBTRN03C.cbl:142-145, 634-640` — `9910-DISPLAY-IO-STATUS` second-byte big-endian halfword rendering (0..255); reproduce big-endian, not little-endian.
9. **CBTRN03C-FB9** — `CBTRN03C.cbl:488,498,508, 641-644` — `MOVE 23 TO IO-STATUS` (numeric literal into 2-char alphanumeric group) yields chars `'23'`, rendered as `'0023'`; reproduce the `'0023'` output.

## COSGN00C — Sign-on (CICS)

1. **COSGN00C-FB1** — `COSGN00C.cbl:118-127, 138-140, 223` — V2 (Password blank) branch never sets `WS-ERR-FLG`, so the gate `IF NOT ERR-FLG-ON` is still true and `READ-USER-SEC-FILE` runs with a blank password (blank-vs-blank compare); do not add an early return or set an error flag on this branch.
2. **COSGN00C-FB2** — `COSGN00C.cbl:127, 245/251/256` — On the blank-password path two SENDs can queue in one turn (V2 message SEND, then `READ-USER-SEC-FILE` SENDs again); preserve the double execution.
3. **COSGN00C-FB3** — `COSGN00C.cbl:132-134` — `CDEMO-USER-ID` is populated (upper-cased) unconditionally before the read, so the commarea carries the typed id even when sign-on fails; keep this.

## COMEN01C — Main menu (CICS)

1. **COMEN01C-FB1** — `COMEN01C.cbl:127-143` — In `PROCESS-ENTER-KEY` the invalid-option block does not exit; control falls through to the admin-only `IF` so the unguarded subscript `CDEMO-MENU-OPT-USRTYPE(WS-OPTION)` can be evaluated with `WS-OPTION`=0 or >11; preserve the ordering and the unguarded subscript.
2. **COMEN01C-FB2** — `COMEN01C.cbl:179-180` — `MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM` issued twice (two identical consecutive statements); reproduce.
3. **COMEN01C-FB3** — `COMEN02Y.cpy:93-98; COMEN01C.cbl:136-137` — `CDEMO-MENU-OPT` OCCURS 12 but only 11 entries initialized; the admin-only check runs even when range check already flagged an error, so subscript may be 0/out-of-range; replicate flat-array indexing without re-guarding.
4. **COMEN01C-FB4** — `COMEN02Y.cpy:67-72, …; COMEN01C.cbl:136-143` — Every option's `CDEMO-MENU-OPT-USRTYPE` is `'U'`, so the `='A'` admin-only path is unreachable; keep the dead branch and message.
5. **COMEN01C-FB5** — `COMEN01C.cbl:38, 213; COMEN01.CPY:260` — `WS-MESSAGE` X(80) assigned into `ERRMSGO` X(78) silently truncates the last 2 chars; reproduce the 78-char clamp.
6. **COMEN01C-FB6** — `COMEN01C.cbl:123-124, 127` — After `INSPECT REPLACING ALL ' ' BY '0'` into `WS-OPTION PIC 9(02)`, the `IS NOT NUMERIC` disjunct can never independently fail; keep the redundant test.
7. **COMEN01C-FB7** — `COMEN01C.cbl:147-168 vs 177-187` — Only option 11 (COPAUS0C) is `INQUIRE PROGRAM`-gated; options 1-10 XCTL unconditionally (an uninstalled 1-10 would abend at XCTL); reproduce the asymmetry.

## COADM01C — Admin menu (CICS)

1. **COADM01C-FB1** — `COADM01C.cbl:140-158` — After the in-region XCTL, control unconditionally falls through to build/SEND the green "This option is not installed ..." message inside the same `IF NOT ERR-FLG-ON` (dead for installed programs, intended for 'DUMMY'); reproduce the control flow, do not restructure into an else.
2. **COADM01C-FB2** — `COADM01C.cbl:152-156, 273-277` — `CDEMO-ADMIN-OPT-NAME(WS-OPTION)` is commented out in both "not installed" STRINGs, so the message omits the option name; keep the name out.
3. **COADM01C-FB3** — `COADM01.bms:154-155; COADM01C.cbl:151, 272` — `ERRMSGC=DFHGREEN` forces green for the informational "not installed" path even though the BMS ERRMSG is RED; reproduce the green override only on those two paths.
4. **COADM01C-FB4** — `COADM01C.cbl:38; COADM01.CPY:260` — `WS-MESSAGE` X(80) into `ERRMSGO` X(78) silent 2-char truncation; do not widen ERRMSG.
5. **COADM01C-FB5** — `COADM02Y.cpy:22, 56` — `CDEMO-ADMIN-OPT OCCURS 9` but only 6 entries populated (COUNT=6); preserve the 9-slot declaration with 6 valid rows.
6. **COADM01C-FB6** — `COADM01C.cbl:198-199` — RESP/RESP2 from RECEIVE captured but never checked (no MAPFAIL handling); do not add RECEIVE error handling.
7. **COADM01C-FB7** — `COADM01C.cbl:39, 58` — Dead literals/copybooks compiled but unused (`WS-USRSEC-FILE='USRSEC'`, COPY `CSUSR01Y`); no USRSEC I/O occurs; do not implement security-file access.

## COBIL00C — Bill payment (CICS)

1. **COBIL00C-FB1** — `COBIL00C.cbl:57,216-219` — Transaction-ID generation: `MOVE TRAN-ID (X16) TO WS-TRAN-ID-NUM (9(16))`, `ADD 1`, MOVE back; follows alphanumeric→numeric de-editing; reproduce parse-as-16-digit / +1 / re-store zero-padded, do not switch to a robust id scheme.
2. **COBIL00C-FB2** — `COBIL00C.cbl:169-195` — Balance MOVE to screen (`ACCT-CURR-BAL`→`CURBALI`) is unconditional after the CONFIRM EVALUATE whenever the empty-id check passed, even on invalid-confirm path with possibly stale/unread `ACCT-CURR-BAL`; preserve the ordering.
3. **COBIL00C-FB3** — `COBIL00C.cbl:182-184,208-240` — Blank confirm does a keyed `READ … UPDATE` (shows balance) but never REWRITEs; port must do the keyed read without holding a lasting lock and without writing.
4. **COBIL00C-FB4** — `COBIL00C.cbl:197-206` — No account active-status check and no zero-floor guard beyond `<= 0`; a closed/expired account with balance > 0 is still paid.
5. **COBIL00C-FB5** — `COBIL00C.cbl:423-426` — The xref read's NOTFND/error message is `'Account ID NOT found...'` (wrong noun — it is the xref that was not found); keep the misleading text.
6. **COBIL00C-FB6** — `COBIL00C.cbl:55,58` — `WS-TRAN-DATE PIC X(08) VALUE '00/00/00'` and `WS-TRAN-AMT PIC +99999999.99` declared but never used; do not wire them up.

## COCRDLIC — Card list (CICS)

1. **COCRDLIC-FB1** — `COCRDLIC.cbl:518, 546, 1097` — `I-SELECTED = 0` (no row selected) is used as a subscript for `VIEW-/UPDATE-REQUESTED-ON(I-SELECTED)` without consulting the `DETAIL-WAS-REQUESTED` guard; treat 0 as "no selection" → OTHER list branch; do not throw on index 0.
2. **COCRDLIC-FB2** — `COCRDLIC.cbl:431-435` — Double-negative filter guard `IF NOT FLG-ACCTFILTER-NOT-OK AND NOT FLG-CARDFILTER-NOT-OK`; reproduce the boolean exactly (naming inverts intuition).
3. **COCRDLIC-FB3** — `COCRDLIC.cbl:1056-1060` — `2220-EDIT-CARD` sets its message only `IF WS-ERROR-MSG-OFF`, so an account-filter error suppresses the card-filter message (account message wins); preserve.
4. **COCRDLIC-FB4** — `COCRDLIC.cbl:787-797` — Stray lone `I` token at line 790 in `1250-SETUP-ARRAY-ATTRIBS` (functionally inert); port row 4 identically to the other rows; record the anomaly.
5. **COCRDLIC-FB5** — `COCRDLIC.cbl:753, 766` — Row-1 protected-empty attribute is `DFHBMPRF` (protect+FSET) while rows 2-7 use `DFHBMPRO`; reproduce per-row.
6. **COCRDLIC-FB6** — `COCRDLIC.cbl:755-761` — On a selection error, row 1 writes `'*'` into CRDSEL1O when blank, whereas rows 2-7 `MOVE -1 TO CRDSELnL` (cursor); reproduce the row-1-special behavior.
7. **COCRDLIC-FB7** — `COCRDLIC.cbl:4-8, 320` — Admin/non-admin listing not implemented; `CDEMO-USER-TYPE` never tested, hard-SET to `USER`; filtering only by on-screen filters; do not add admin gating.
8. **COCRDLIC-FB8** — `COCRDLIC.cbl:215-217` — Unused `LIT-CARD-FILE-ACCT-PATH = 'CARDAIX'`; account filtering is in-program; do not implement an alt index.
9. **COCRDLIC-FB9** — `COCRDLIC.cbl:237` — `WS-CA-SCREEN-NUM` (page number) is `9(1)`; beyond 9 pages it truncates; reproduce single-digit silent truncation.

## COCRDSLC — Card detail view (CICS)

1. **COCRDSLC-FB1** — `COCRDSLC.cbl:739-740, 742-745` — Account number is format-validated but the read keys only on card number (`MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID` is commented out); a valid card with any account is displayed; read by card number alone.
2. **COCRDSLC-FB2** — `COCRDSLC.cbl:779-812` — `9150-GETCARD-BYACCT` (alt-index `CARDAIX`) and `DID-NOT-FIND-ACCT-IN-CARDXREF` are unreachable dead code (9150 never PERFORMed); keep, do not wire up.
3. **COCRDSLC-FB3** — `COCRDSLC.cbl:297-299` — Any AID other than ENTER/PF3 is silently coerced to ENTER (e.g. CLEAR re-validates instead of clearing); reproduce the coercion.
4. **COCRDSLC-FB4** — `COCRDSLC.cbl:596-603` — `2100-RECEIVE-MAP` stores RESP/RESP2 but never tests them (MAPFAIL not handled); do not add a check.
5. **COCRDSLC-FB5** — `COCRDSLC.cbl:755-758` — On card-not-found `SET FLG-ACCTFILTER-NOT-OK` reds both filters though only the card lookup failed; preserve.
6. **COCRDSLC-FB6** — `COCRDSLC.cbl:762-766` — `9100` OTHER branch sets `FLG-ACCTFILTER-NOT-OK` only when `WS-RETURN-MSG-OFF`, so the acct red highlight may not apply after a prior message; preserve the guard placement.
7. **COCRDSLC-FB7** — `COCRDSLC.cbl:522-523` — Cursor `WHEN OTHER` always lands on `ACCTSIDL`, even after a successful card display; preserve.
8. **COCRDSLC-FB8** — `COCRDSLC.cbl:167-168, 175-176, 505, 565` — `LIT-THISMAPSET = 'COCRDSL '` (8 chars) moved into X(7) targets truncates the trailing space; preserve the literal widths.

## COCRDUPC — Card update (CICS)

1. **COCRDUPC-FB1** — `COCRDUPC.cbl:1379, 1384, 1424; 253-254` — Card read by card number only (acct-id predicate commented out, `CARDAIX` never opened); the displayed/updated card may belong to a different account; do not add an `acct_id` predicate.
2. **COCRDUPC-FB2** — `COCRDUPC.cbl:1463` — `9200` rewrite sets `CARD-UPDATE-ACCT-ID = CC-ACCT-ID-N` (typed account), overwriting the card's real `CARD-ACCT-ID`; combined with FB1 can re-point the card to an unrelated account; write acct_id from `CC-ACCT-ID-N`.
3. **COCRDUPC-FB3** — `COCRDUPC.cbl:306, 586, 1464-1465` — CVV round-trips through never-collected `CCUP-NEW-CVV-CD` (LOW-VALUES after INITIALIZE), so the rewrite zeroes/garbles CVV on every save; reproduce the MOVE chain.
4. **COCRDUPC-FB4** — `COCRDUPC.cbl:1518-1519` — `9300-CHECK-CHANGE-IN-REC` mismatch branch `GO TO 9200-WRITE-PROCESSING-EXIT` (caller's exit), a cross-paragraph GO TO that unwinds out of 9200 (no REWRITE); preserve the control flow.
5. **COCRDUPC-FB5** — `COCRDUPC.cbl:813` — `1230-EDIT-NAME` treats `CCUP-NEW-CRDNAME EQUAL ZEROS` as "not supplied" (an all-zero name flagged blank); faithful.
6. **COCRDUPC-FB6** — `COCRDUPC.cbl:621, 1122-1123` — Expiry-day field is DRK/PROT (read-only) yet EXPDAYI is unconditionally received into `CCUP-NEW-EXPDAY`; `3200` always re-sends `CCUP-OLD-EXPDAY`; preserve the unconditional receive + always-old re-send.
7. **COCRDUPC-FB7** — `COCRDUPC.cbl:413-424` — Any AID other than the gated set is coerced to ENTER (no "invalid key" message); reproduce.
8. **COCRDUPC-FB8** — `COCRDUPC.cbl:96-99, 934` — Expiry year valid range is a fixed `1950 THRU 2099`, so a long-expired year (e.g. 1951) passes; faithful.

## COACTVWC — Account view (CICS)

1. **COACTVWC-FB1** — `COACTVWC.cbl:408-413` — Duplicate `0000-MAIN-EXIT` paragraph (second is unreachable); reproduce as a no-op duplicate.
2. **COACTVWC-FB2** — `COACTVWC.cbl:671-673` — Stray sequence-number text `00` after the MOVE literal (compiled as artifact); message text stays `'Account Filter must  be a non-zero 11 digit number'` (with the double space); keep exactly.
3. **COACTVWC-FB3** — `COACTVWC.cbl:125-128, 671-673` — Double-spaced / inconsistent error literals; use the literal actually MOVEd (the double-spaced one), not the unused 88 wording.
4. **COACTVWC-FB4** — `COACTVWC.cbl:839-842, 713-715` — CUSTDAT NOTFND sets `FLG-CUSTFILTER-NOT-OK` but `9000-READ-ACCT` tests the never-SET `DID-NOT-FIND-CUST-IN-CUSTDAT`; rely on INPUT-ERROR, not a customer-not-found flag.
5. **COACTVWC-FB5** — `COACTVWC.cbl:789-806, 704-706, 708-711` — ACCTDAT NOTFND guard is on an unset 88, so after an ACCTDAT NOTFND the program falls through to read CUSTDAT anyway (with `CDEMO-CUST-ID` from the xref); reproduce exactly.
6. **COACTVWC-FB6** — `COACTVWC.cbl:843-845` — In `9400` the ERROR-RESP/RESP2 MOVE is outside the `IF WS-RETURN-MSG-OFF` (unlike 9200/9300); keep the ordering.
7. **COACTVWC-FB7** — `COACTVWC.cbl:115-138` — `WS-INFORM-OUTPUT`/`WS-PROMPT-FOR-ACCT` (as info) and several 88 messages declared but never used; do not wire up.

## COACTUPC — Account update (CICS)

1. **COACTUPC-FB1** — `COACTUPC.cbl:4143-4144, 4189-4190, 3947-3948` — `9700-CHECK-CHANGE-IN-REC` on a detected change `GO TO 9600-WRITE-PROCESSING-EXIT` (caller's exit), jumping out of the PERFORM range; when data changed, abort `9600` (skip REWRITEs) with `DATA-WAS-CHANGED-BEFORE-UPDATE` set.
2. **COACTUPC-FB2** — `COACTUPC.cbl:746, 4174-4179` — DOB before-image ref-mod offsets: OLD copy is 8-char dashless (`ACUP-OLD-CUST-DOB-YYYY-MM-DD PIC X(08)`) compared `(5:2)/(7:2)` vs live 10-char dashed `(6:2)/(9:2)`; line up only by coincidence; reproduce the exact offsets.
3. **COACTUPC-FB3** — `COACTUPC.cbl:3993-4000` — Reissue-date double-write in `9600`: `MOVE ACCT-REISSUE-DATE TO ACCT-UPDATE-REISSUE-DATE` (dead) then overwritten by the `STRING`; keep both (STRING wins).
4. **COACTUPC-FB4** — `COACTUPC.cbl:493-528` — Duplicate/overshadowed 88 message values (`DID-NOT-FIND-ACCT-IN-CARDXREF` defined twice with different texts; `SEARCHED-ACCT-ZEROES`/`SEARCHED-ACCT-NOT-NUMERIC` share text); first definition wins; preserve as authored.
5. **COACTUPC-FB5** — `COACTUPC.cbl:483-484, 1791-1792, 1806-1810` — `1210-EDIT-ACCOUNT` blank branch sets `"Account number not provided"` while the non-numeric/zero branch STRINGs `"Account Number if supplied must be a 11 digit Non-Zero Number"`; keep both texts.
6. **COACTUPC-FB6** — `COACTUPC.cbl:3426-3435` — `CSSETATY` for EFT vs Primary-Holder are cross-labeled (comments swapped relative to the COPY fields used); reproduce the code as written.
7. **COACTUPC-FB7** — `COACTUPC.cbl:2234-2240` — `1260-EDIT-US-PHONE-NUM` optional-phone guard tests `WS-EDIT-US-PHONE-NUMA` twice where it meant `…-NUMC`; reproduce the buggy condition.
8. **COACTUPC-FB8** — `CSUTLDWY.cpy:9-10; CSUTLDPY.cpy:70-84` — Date year edit accepts only century 19 or 20; any 21xx+ date is rejected `": Century is not valid."`; keep the restriction.

## COUSR02C — User update (CICS)

1. **COUSR02C-FB1** — `COUSR02C.cbl:334-339, 166-172, 215-217` — `READ-USER-SEC-FILE` NORMAL branch sets "Press PF5..." and SENDs before the caller moves fetched fields and SENDs again (map sent twice per turn); on the PF5/PF3 update path the same extra SEND fires mid-update; reproduce the double-SEND.
2. **COUSR02C-FB2** — `COUSR02C.cbl:334-336` — Dead `CONTINUE` before the message MOVE (no-op); keep it.
3. **COUSR02C-FB3** — `COUSR02C.bms:130-134; COUSR02C.cbl:227-230` — PASSWD field is FSET + DRK: always returned on RECEIVE even when not retyped, but invisible; reproduce the attribute behavior.
4. **COUSR02C-FB4** — `COUSR02C.cbl:204-209,231-234` — No re-validation of USRTYPE; any single non-space char is accepted and written (e.g. 'Z'); do not add validation.
5. **COUSR02C-FB5** — `COUSR02C.cbl:347,384` — `DISPLAY 'RESP:'…'REAS:'…` on error branches writes to region SYSOUT/log; route to a diagnostic log sink, do not surface to user.

## COUSR03C — User delete (CICS)

1. **COUSR03C-FB1** — `COUSR03C.cbl:188-192, 287-292, 323-328` — PF5 `DELETE-USER-INFO` performs READ then DELETE unconditionally with no ERR-FLG re-check; on a missing user, READ sets ERR-FLG/SENDs and DELETE still executes (its own NOTFND SEND wins); do not insert an `IF NOT ERR-FLG-ON` around the DELETE.
2. **COUSR03C-FB2** — `COUSR03C.cbl:160-169, 281-286` — Double SEND on a successful ENTER fetch (READ's NORMAL branch SENDs "Press PF5...", then caller SENDs again with names populated; ERRMSGC color-byte carry-over on the second SEND); reproduce both SENDs and the color carry-over.
3. **COUSR03C-FB3** — `COUSR03C.cbl:45-47, 85` — `WS-USR-MODIFIED` flag is dead (never SET to YES, never tested); do not wire it.
4. **COUSR03C-FB4** — `COUSR03C.cbl:232-238` — RESP/RESP2 from RECEIVE never checked (no MAPFAIL handling); do not add.
5. **COUSR03C-FB5** — `COUSR03C.cbl:332` — DELETE error `WHEN OTHER` shows `'Unable to Update User...'` (wrong verb, copy-paste from COUSR02C); keep verbatim.
6. **COUSR03C-FB6** — `COUSR03C.cbl:298, 334; COUSR03.bms:103-107` — `MOVE -1 TO FNAMEL` on error paths puts the cursor on a protected (ASKIP) field (a no-op/odd placement); reproduce the -1 on FNAMEL exactly.
7. **COUSR03C-FB7** — `COUSR03C.cbl:38, 217; COUSR03.CPY:152` — `WS-MESSAGE` X(80) → `ERRMSGO` X(78) silent 2-char truncation; do not widen.
8. **COUSR03C-FB8** — `COUSR03C.cbl:188-191` — No re-validation that displayed user equals deleted user; PF5 deletes whatever is now in USRIDINI (operator could change it after the fetch); reproduce.
9. **COUSR03C-FB9** — `COUSR03C.cbl:99-105, 103-105` — Auto-fetch on first entry (when `CDEMO-CU03-USR-SELECTED` is pre-set) runs PROCESS-ENTER-KEY before the first SEND, then MAIN-PARA SENDs again; reproduce the extra SEND from the first-entry path.

## CORPT00C — Transaction report submit (CICS)

1. **CORPT00C-FB1** — `CORPT00C.cbl:556-591, 258-456` — `SEND-TRNRPT-SCREEN` ends with `GO TO RETURN-TO-CICS` (`EXEC CICS RETURN`), so the first `PERFORM SEND-TRNRPT-SCREEN` terminates the program (the rest of the validation chain does not run); model SEND as "SEND then return out of the transaction", not fall-through.
2. **CORPT00C-FB2** — `CORPT00C.cbl:305-371` — Date-part validations echo NUMVAL-C results back into INPUT fields then compare as text; month/day `'00'` pass (`NOT > '12'` and NUMERIC); only the CSUTLDTC call may reject `00`; do not add a `>= 1` lower-bound.
3. **CORPT00C-FB3** — `CORPT00C.cbl:498-508` — `SUBMIT-JOB-TO-INTRDR` sets `END-LOOP-YES` on the sentinel (`/*EOF` / SPACES / LOW-VALUES) but still WRITEs that line to the `JOBS` TDQ before ending; write the terminating line, then end.
4. **CORPT00C-FB4** — `CORPT00C.cbl:484-490` — Confirm error and success/confirm messages use `DELIMITED BY SPACE` on `CONFIRMI` (1 char) and `WS-REPORT-NAME`; keep the SPACE-delimited STRING semantics.
5. **CORPT00C-FB5** — `CORPT00C.cbl:39, 560; CORPT00.CPY:224` — `WS-MESSAGE` X(80) → `ERRMSGO` X(78) silent 2-char truncation; reproduce the 78-char clamp.
6. **CORPT00C-FB6** — `CORPT00C.cbl:44-46,56,77-78,146,562-578` — Dead working storage / dead branch (`WS-TRANSACT-EOF`, `WS-REC-COUNT`, `WS-TRAN-AMT`, `WS-TRAN-DATE`, copied `TRAN-RECORD`, the no-ERASE arm of `SEND-TRNRPT-SCREEN`); carry as inert, do not repurpose.
7. **CORPT00C-FB7** — `CORPT00C.cbl:210, 529` — `DISPLAY 'PROCESS ENTER KEY'` and the WRITEQ-failure `DISPLAY 'RESP:'…` are debug traces to the region log/SYSOUT; treat as logging side effects, document.
8. **CORPT00C-FB8** — `CORPT00C.cbl:223-234; CSDAT01Y.cpy:19-23` — Monthly end-of-month math mutates month/day in place (day=01, bump month/year), then `INTEGER-OF-DATE` of the now-next-month date minus 1 day = last day of the original month; reproduce exactly.

## CSUTLDTC — Date validation utility (CEEDAYS wrapper)

1. **CSUTLDTC-FB1** — `CSUTLDTC.cbl:122, 25-31, 108` — `MOVE WS-DATE-TO-TEST TO WS-DATE` copies the VSTRING length halfword (`X'000A'`, two low-value bytes) into the first 2 positions of the echoed date, shifting/garbling the "TstDate:" portion; reproduce the corruption, do not echo the clean date.
2. **CSUTLDTC-FB2** — `CSUTLDTC.cbl:105-106, 109-110` — VSTRING length hard-pinned to 10 (`LENGTH OF` the LINKAGE fields), ignoring trailing spaces; CEEDAYS always receives 10 chars (trailing spaces become part of the picture/date); do not trim.
3. **CSUTLDTC-FB3** — `CSUTLDTC.cbl:98, 123` — Severity returned via `MOVE WS-SEVERITY-N TO RETURN-CODE` (a `PIC 9(4)` REDEFINES of the 4-byte token-derived `WS-SEVERITY`); preserve the numeric path (0 on success, typically 3 on error).
4. **CSUTLDTC-FB4** — `CSUTLDTC.cbl:41, 114, 119` — `OUTPUT-LILLIAN` is computed by CEEDAYS but never used (dead output); keep the call signature, discard the value.
5. **CSUTLDTC-FB5** — `CSUTLDTC.cbl:62, 129-130` — Misnamed `FC-INVALID-DATE` 88-level actually means "valid" (all-zeros success token → `'Date is valid'`); do not rename in a way that changes the mapping.
6. **CSUTLDTC-FB6** — `CSUTLDTC.cbl:90, 45/51/54` — `INITIALIZE WS-MESSAGE` leaves FILLER literal labels (`'Mesg Code:'`, `'TstDate:'`, `'Mask used:'`) untouched (they hold load-time VALUE); the port must place those labels at the fixed byte offsets in every result.
7. **CSUTLDTC-FB7** — `CSUTLDTC.cbl:100-101` — `GOBACK` commented out; uses `EXIT PROGRAM`; note the deliberate use of `EXIT PROGRAM`.

## COBSWAIT — Batch wait step

1. **COBSWAIT-FB1** — `COBSWAIT.cbl:31, 36-37` — `PARM-VALUE` X(8) moved straight into a numeric `9(8) COMP` with no `IS NUMERIC` test; bad input yields garbage centiseconds; do not add validation, replicate raw de-edit-and-truncate.
2. **COBSWAIT-FB2** — `COBSWAIT.cbl:36; WAITSTEP.jcl:20-26` — Value is consumed via `ACCEPT … FROM SYSIN`, not PARM, despite the "PARM" banner; keep the SYSIN-stream source.
3. **COBSWAIT-FB3** — `COBSWAIT.cbl:30, 37` — `9(8)` holds at most 99,999,999 centiseconds with no overflow check; excess high-order digits silently dropped; preserve truncate-toward-zero, no rounding.
4. **COBSWAIT-FB4** — `COBSWAIT.cbl:38; WAITSTEP.jcl:23` — Hard dependency on external non-COBOL `MVSWAIT` (not in repo); its centisecond/blocking/return semantics are assumed, not defined here.

## CODATE01 — Date service (CICS-MQ)

1. **CODATE01-FB1** — `CODATE01.cbl:318,322,339-364` — `3000-GET-REQUEST` copies the request into `REQUEST-MESSAGE`/`REQUEST-MSG-COPY` but no field is inspected; the reply is always current date/time regardless of input.
2. **CODATE01-FB2** — `CODATE01.cbl:315,320,371-390` — Request `MQMD-REPLYTOQ` saved to `MQ-QUEUE-REPLY`/`SAVE-REPLY2Q` but the reply MQPUT targets the pre-opened literal `CARD.DEMO.REPLY.DATE` handle, never `SAVE-REPLY2Q`.
3. **CODATE01-FB3** — `CODATE01.cbl:182, 301-302, 383-384, 461` — `MQGET`/`MQPUT`/`MQCLOSE` pass `MQ-HCONN` (VALUE 0) while `MQOPEN` passes `QMGR-HANDLE-CONN` (VALUE 0); treat as a single ambient in-proc connection.
4. **CODATE01-FB4** — `CODATE01.cbl:194,228,444-449` — Flag/queue naming inversion: opening the input queue sets `REPLY-QUEUE-OPEN`, opening the reply/output queue sets `RESP-QUEUE-OPEN`; internally consistent but swapped names; preserve the mapping.
5. **CODATE01-FB5** — `CODATE01.cbl:355-360` — `STRING` concatenates with no separator, yielding `...MM-DD-YYYYSYSTEM TIME :...`; reproduce the exact byte layout.
6. **CODATE01-FB6** — `CODATE01.cbl:150-151, 152-154` — RESP2 display move is a self-move (`MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD`); the STRING emits the uninitialized `-D` field; preserve verbatim.
7. **CODATE01-FB7** — `CODATE01.cbl:496` — `5100-CLOSE-OUTPUT-QUEUE` error path moves `INPUT-QUEUE-NAME` (wrong queue name) into the diagnostic, not `REPLY-QUEUE-NAME`.
8. **CODATE01-FB8** — `CODATE01.cbl:444-452,476,498,521-522` — Close-error paths `PERFORM 8000-TERMINATION`, which re-PERFORMs the close paragraphs (potential recursive PERFORM / abend loop); model the close path as idempotent/guarded but keep call ordering observable.
9. **CODATE01-FB9** — `CODATE01.cbl:355-360; README:106-112` — README's structured `DATE-RESPONSE-MSG` is not what the code emits (free-text form); match the code.
10. **CODATE01-FB10** — `CODATE01.cbl:114-120,324,320` — Dead WORKING-STORAGE (`LIT-ACCTFILENAME`, `WS-RESP-CD`, `WS-REAS-CD`, `MQ-MSG-COUNT` incremented-never-read, `SAVE-REPLY2Q`, `MQ-QUEUE`/`MQ-QUEUE-REPLY`); do not introduce file access or external-effect counters.

## COACCT01 — Account inquiry service (CICS-MQ)

1. **COACCT01-FB1** — `COACCT01.cbl:245, 279, 540-545` — Open/close flag-name vs queue mismatch (input→`REPLY-QUEUE-OPEN`, output→`RESP-QUEUE-OPEN`); internally consistent but swapped names; preserve.
2. **COACCT01-FB2** — `COACCT01.cbl:233, 352, 479` — Wrong/duplicate HCONN: `MQGET`/`MQPUT` use `MQ-HCONN` while `MQOPEN`/`MQCLOSE` use `QMGR-HANDLE-CONN` (both VALUE 0); preserve as a single in-proc connection.
3. **COACCT01-FB3** — `COACCT01.cbl:201-203` — No-op self-move `MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD` (intended `-CD-D`); the STRING references the never-populated `-CD-D` (stays 0).
4. **COACCT01-FB4** — `COACCT01.cbl:375` — Dead message counter `ADD 1 TO MQ-MSG-COUNT` (never read/output).
5. **COACCT01-FB5** — `COACCT01.cbl:366,371,480` — ReplyToQ captured (`MQ-QUEUE-REPLY`/`SAVE-REPLY2Q`) then ignored; PUT always targets the literal `CARD.DEMO.REPLY.ACCT` handle.
6. **COACCT01-FB6** — `COACCT01.cbl:572,594,617-618,540-548` — Termination recursion on close error: `5000`/`5100`/`5200` close-error paths call `8000-TERMINATION`, which can re-enter the same close paragraph (flags not reset); model termination as idempotent, keep the call graph observable.
7. **COACCT01-FB7** — `COACCT01.cbl:200` — `'CICS RETREIVE'` misspelling in the error label.
8. **COACCT01-FB8** — `cpy/CVACT01Y.cpy:15; COACCT01.cbl:407-425` — `ACCT-ADDR-ZIP` is read but never moved to the reply (dropped).
9. **COACCT01-FB9** — `COACCT01.cbl:393; README:114-128` — README's structured `REQUEST-TYPE/RESPONSE-TYPE 'ACCT'` diverges from the code (`WS-FUNC = 'INQA'` + 11-digit key + free-text reply); honor the code.

## COTRTLIC — Transaction-type list (CICS-DB2)

1. **COTRTLIC-FB1** — `COTRTLIC.cbl:411-418, 817, 860` — `CA-DELETE-SUCCEEDED`/`CA-UPDATE-SUCCEEDED` are `VALUE LOW-VALUES`, colliding with the "not-requested" value; the program compensates via a `WS-DELETES-REQUESTED > 0` dispatch guard; reproduce the `VALUE LOW-VALUES` semantics.
2. **COTRTLIC-FB2** — `COTRTLIC.cbl:721-734` — Duplicate dead `WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE` clauses back-to-back (first empty, falls through to the second); keep both collapsing to the same forward-read.
3. **COTRTLIC-FB3** — `COTRTLIC.cbl:1841-1844` — `DCL-TR-DESCRIPTION-LEN = FUNCTION LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))` uses the fixed `PIC X(50)` length, so VARCHAR length is always 50 (trailing spaces stored); store trimmed text padded to 50, length 50.
4. **COTRTLIC-FB4** — `COTRTLIC.cbl:965-975, 991-994` — `1210-EDIT-ARRAY` reads `…FILTER-CHANGED` flags before `1220`/`1230` set them (stale read); reproduce the call order (1210,1230,1220,1290) and the stale read.
5. **COTRTLIC-FB5** — `COTRTLIC.cbl:1761-1790` — `8100-READ-BACKWARDS` has no `WHEN SQLCODE = +100`; end-of-data falls into `WHEN OTHER` → spurious 'Error on fetch ... BACKWARD' on a short backward page; keep this behavior.
6. **COTRTLIC-FB6** — `COTRTLIC.cbl:1448, 1453, 1458, 1464, 1469, 1471, 1479, 1484, 1495` — `2400-SETUP-SCREEN-ATTRS` writes attribute bytes into the input map (`…A OF CTRTLIAI`) which overlaps the output via REDEFINES; preserve the resulting attribute outcome.
7. **COTRTLIC-FB7** — `COTRTLIC.cbl:279-301` — Unused `WS-TYPE-CD-DELETE-FILTER` IN(...) delete-filter literal (never referenced); do not implement.
8. **COTRTLIC-FB8** — `COTRTLIC.cbl:253-254, 1263-1264` — Two distinct "no records" texts ("No records found for this search condition." vs "No Records found for these filter conditions"); keep both distinct strings.

## COTRTUPC — Transaction-type update (CICS-DB2)

1. **COTRTUPC-FB1** — `COTRTUPC.cbl:1539-1542, 335` — `9600-WRITE-PROCESSING` sets `DCL-TR-DESCRIPTION-LEN = LENGTH(TTUP-NEW-TTYP-TYPE-DESC)` (fixed X(50)) → length always 50 (asymmetric with the read which trims via fetched length); store trimmed text padded to 50, length 50.
2. **COTRTUPC-FB2** — `COTRTUPC.cbl:1645-1646` — `SQLERRM OF SQLCA` listed on two consecutive STRING lines in the DELETE -532 message, doubling it.
3. **COTRTUPC-FB3** — `COTRTUPC.cbl:1177-1178` — `3201-SHOW-INITIAL-VALUES` lists receiver `TRTYPCDO OF CTRTUPAO` twice in one MOVE (`LOW-VALUES`); harmless, verbatim.
4. **COTRTUPC-FB4** — `COTRTUPC.cbl:1113,1120` — `FUNCTION CURRENT-DATE` moved to `WS-CURDATE-DATA` twice in `3100-SCREEN-INIT`; keep both moves.
5. **COTRTUPC-FB5** — `COTRTUPC.cbl:145,169,173,183,195,306` — Dead/never-reached message 88s (`FOUND-TRANTYPE-DATA`, `WS-EXIT-MESSAGE`, `WS-NAME-MUST-BE-ALPHA`, `DATA-WAS-CHANGED-BEFORE-UPDATE`, `CODING-TO-BE-DONE`, `TTUP-REVIEW-NEW-RECORD`); keep declared, do not invent paths that set them.
6. **COTRTUPC-FB6** — `COTRTUPC.cbl:1471,1475-1482` — Comment in `9100` says "Read the Card file ... alternate index ACCTID" but the SQL reads `TRANSACTION_TYPE` by `TR_TYPE` (copy-paste comment); keep behavior, ignore comment.
7. **COTRTUPC-FB7** — `COTRTUPC.cbl:1555-1589` — `-911` (lock) on UPDATE sets INPUT-ERROR but the order-dependent two-stage flag→state EVALUATE still maps it to LOCK-ERROR; preserve the exact two-EVALUATE structure.

## COBTUPDT — Transaction-type batch update (DB2)

1. **COBTUPDT-FB1** — `COBTUPDT.cbl:230-233` — `9999-ABEND` is a misnomer: it only DISPLAYs and `MOVE 4 TO RETURN-CODE` then `EXIT`; processing continues to the next record (errors set RC=4 and keep going).
2. **COBTUPDT-FB2** — `COBTUPDT.cbl:132-226, 99` — No `COMMIT`/`ROLLBACK` anywhere; a unit of work with a failed statement plus later successes is committed together at STOP RUN; commit once at end-of-run, no per-record rollback.
3. **COBTUPDT-FB3** — `COBTUPDT.cbl:82-89, 91-92` — `0001-OPEN-FILES` checks `WS-INF-STATUS` only to choose a DISPLAY; on non-'00' it still falls into the read loop (no abend, no RC); open failure → 'OPEN FILE NOT OK' then proceed.
4. **COBTUPDT-FB4** — `COBTUPDT.cbl:40-46, 71-77, 101; dcl/DCLTRTYP.dcl:35-46` — Dead FD record `WS-INPUT-VARS` (READ is `INTO WS-INPUT-REC`) and dead DCLGEN `DCLTRANSACTION-TYPE`; binding uses file-image fields, so descriptions store as fixed 50-char space-padded, not trimmed VARCHARs.
5. **COBTUPDT-FB5** — `COBTUPDT.cbl:110-129` — Case-sensitive action codes: only uppercase `A`/`U`/`D`/`*` recognized; any other byte → `WHEN OTHER` → 'ERROR: TYPE NOT VALID' → "abend" (RC=4) and continue.
6. **COBTUPDT-FB6** — `jcl/MNTTRDB2.jcl:16; COBTUPDT.cbl:74, 145; dcl/DCLTRTYP.dcl:29` — JCL calls cols 2-3 "NUMERIC" but the host variable is `PIC X(2)`/column `CHAR(2)`; no numeric validation; treat `TR_TYPE` as opaque 2-char string.

## CBPAUP0C — Pending-authorization purge (IMS batch)

1. **CBPAUP0C-FB1** — `CBPAUP0C.cbl:287-292, 156-158, 310-313/335-338` — `4000-CHECK-IF-EXPIRED` decrements summary counts/amounts in the in-memory io-area but issues no `REPL` on `PAUTSUM0` (only `DLET`); persisted counts are NOT updated; mutate in-memory, use for the gate, do NOT UPDATE PAUT_SUMMARY.
2. **CBPAUP0C-FB2** — `CBPAUP0C.cbl:156` — Summary-delete gate `IF PA-APPROVED-AUTH-CNT <= 0 AND PA-APPROVED-AUTH-CNT <= 0` tests approved twice; declined count never tested; a summary is deleted whenever approved-count ≤ 0; reproduce the duplicated test.
3. **CBPAUP0C-FB3** — `CBPAUP0C.cbl:101,103,201-206,160,360` — `P-CHKP-FREQ`/`P-CHKP-DIS-FREQ` are `PIC X(05)` compared against numeric counters and defaulted via `MOVE 5`/`MOVE 10` (yielding `'5    '`/`'10   '`); reproduce the exact byte content and IBM comparison semantics.
4. **CBPAUP0C-FB4** — `CBPAUP0C.cbl:53,71-73,83-95,142` — Dead WS status definitions (`IMS-RETURN-CODE` 88s, `WS-IMS-PSB-SCHD-FLG`, `WS-INFILE-STATUS`, `WS-CUSTID-STATUS`/`END-OF-FILE`, `IDX`, `WS-TOT-REC-WRITTEN`, `WS-ERR-FLG`/`ERR-FLG-ON` unset in the loop condition); carry as inert.
5. **CBPAUP0C-FB5** — `CBPAUP0C.cbl:5` — Header typo "Delete Expired Pending Authoriation Messages" (missing 'z'); preserve verbatim.
6. **CBPAUP0C-FB6** — `CBPAUP0C.cbl:43,186` — `ACCEPT CURRENT-DATE FROM DATE` populates a `9(06)` field never referenced (only `CURRENT-YYDDD` used); keep the side-effect-free accept.
7. **CBPAUP0C-FB7** — `CBPAUP0C.cbl:75-77,355` — `WK-CHKPT-ID-CTR` never incremented; every `CHKP` uses the same id `'RMAD0000'`; reproduce a constant checkpoint id.

## COPAUA0C — Pending-authorization request handler (CICS-MQ-IMS)

1. **COPAUA0C-FB1** — `COPAUA0C.cbl:820-821, 885, 790-791` — `8400-UPDATE-SUMMARY` declined branch `ADD PA-TRANSACTION-AMT TO PA-DECLINED-AUTH-AMT` before `8500-INSERT-AUTH` sets `PA-TRANSACTION-AMT`, so it accumulates the prior/zero amount; approved totals correctly use `WS-APPROVED-AMT`.
2. **COPAUA0C-FB2** — `COPAUA0C.cbl:571-609, 665-717` — `5300-READ-CUST-RECORD` reads CUSTDAT but no customer field is used in the decision or writes; the read only sets a found/not-found flag (influences reason `'3100'`) and logs a warning.
3. **COPAUA0C-FB3** — `COPAUA0C.cbl:140-145, 647-650, 707-714` — Decline reasons 4200/4300/5100/5200 are dead (`CARD-NOT-ACTIVE`, `ACCOUNT-CLOSED`, `CARD-FRAUD`, `MERCHANT-FRAUD` never SET; `5600-READ-PROFILE-DATA` is a CONTINUE stub).
4. **COPAUA0C-FB4** — `COPAUA0C.cbl:419-421` — `M003` "FAILED TO READ REQUEST MQ" sets `ERR-CICS` ('C') instead of `ERR-MQ` ('M') though it is an MQ failure.
5. **COPAUA0C-FB5** — `COPAUA0C.cbl:544, 592` — `A002`='ACCT NOT FOUND IN XREF' and `A003`='CUST NOT FOUND IN XREF' both say "IN XREF" though they report the ACCT and CUST master reads.
6. **COPAUA0C-FB6** — `COPAUA0C.cbl:46, 722-731, 756` — `WS-RESP-LENGTH PIC S9(4) VALUE 1` used as the STRING `POINTER` and as `W02-BUFFLEN`, never reset to 1 per message; on 2nd+ messages the STRING begins at the leftover pointer and PUT length keeps growing; thread the same non-reset pointer.
7. **COPAUA0C-FB7** — `COPAUA0C.cbl:441-457, 490-492, 672-682` — Optimistic presets (`CARD-FOUND-XREF`, `FOUND-ACCT-IN-MSTR` TRUE before reads); on XREF not-found the acct/cust/summary reads are skipped and the flag is forced N, declining via bare `SET DECLINE-AUTH`; keep the exact preset ordering.
8. **COPAUA0C-FB8** — `COPAUA0C.cbl:818` — `MOVE 0 TO PA-CASH-BALANCE` only on approve; on decline it keeps its prior/INITIALIZEd value.
9. **COPAUA0C-FB9** — `COPAUA0C.cbl:810-811` — `8400` copies `ACCT-CREDIT-LIMIT`/`ACCT-CASH-CREDIT-LIMIT` into the summary even when the account read failed (stale ACCOUNT-RECORD); no guard.

## COPAUS0C — Pending-authorization list (CICS-IMS)

1. **COPAUS0C-FB1** — `COPAUS0C.cbl:235-238, 674-677` — PF3 branch performs `RETURN-TO-PREV-SCREEN` (unconditional `EXEC CICS XCTL`) then `PERFORM SEND-PAULST-SCREEN` which can never execute; keep the unreachable call.
2. **COPAUS0C-FB2** — `COPAUS0C.cbl:424-452, 488-519, 391-407` — PF8 forward-paging peeks one extra `GET-AUTHORIZATIONS` (sets `NEXT-PAGE-YES/NO`, discards the row), but reposition re-reads the saved last key so the peeked row is shown again; first-page entry consumes the look-ahead without an anchor (boundary handling differs); port the exact reposition+lookahead sequence.
3. **COPAUS0C-FB3** — `COPAUS0C.cbl:832-845, 882-895, 933-946, 349-356, 750-755` — XREF/ACCT/CUST `GET…` paragraphs set the error flag only on `WHEN OTHER`, NOT on NOTFND; on NOTFND they SEND but `ERR-FLG` stays OFF so the gather sequence keeps going on stale/garbage data (multiple SENDs); NOTFND must NOT abort the gather.
4. **COPAUS0C-FB4** — `COPAUS0C.cbl:681-709 + every inline PERFORM SEND-PAULST-SCREEN` — Multiple `SEND-PAULST-SCREEN` per turn (error paragraphs SEND inline plus MAIN's end SEND), each also doing SYNCPOINT/unschedule logic; preserve the repeated SEND calls.
5. **COPAUS0C-FB5** — `COPAUS0C.cbl:531-534, 690, 729-740` — `POPULATE-AUTH-LIST` reuses global `WS-CURDATE-YY/-MM/-DD` (CSDAT01Y) per row, clobbering `POPULATE-HEADER-INFO`'s values; the header date is recomputed inside `SEND-PAULST-SCREEN`; faithfully share one set of date work-fields.
6. **COPAUS0C-FB6** — `COPAUS0C.cbl:510, 477, 989, 1023` — IMS error strings have a leading space and the reposition error literally reads `repos.`; keep verbatim.

## COPAUS1C — Pending-authorization detail/fraud (CICS-IMS)

1. **COPAUS1C-FB1** — `COPAUS1C.cbl:54, 303-306` — `WS-AUTH-TIME VALUE '00:00:00'`; POPULATE overwrites only the digit pairs (1:2/4:2/7:2), leaving the `':'` at 3/6 from the initial VALUE (works only because the field is never reset); seed the buffer with `'00:00:00'` and overlay digit pairs.
2. **COPAUS1C-FB2** — `COPAUS1C.cbl:89-91, 218-221, 276-279, 590-591` — `WS-IMS-PSB-SCHD-FLG` has no VALUE clause (undefined init); set 'Y' only on successful `SCHEDULE-PSB`; reproduce the exact lifecycle, do not add defensive initialization.
3. **COPAUS1C-FB3** — `COPAUS1C.cbl:453-461, 177, 183, 294, 202` — On an unexpected `DIBSTAT`, read paragraphs set `WS-ERR-FLG`, STRING a message, and `PERFORM SEND-AUTHVIEW-SCREEN` inline, then MAIN SENDs again (screen sent twice on an error turn); do not collapse to a single SEND.
4. **COPAUS1C-FB4** — `COPAUS1C.cbl:336-338` — Card-expiry display slices the raw 4 bytes positionally (`(1:2)`/`'/'`/`(3:2)`) with no month/year validation; reproduce the byte-slice, not a parsed date.
5. **COPAUS1C-FB5** — `COPAUS1C.cbl:523` — Stray debug `DISPLAY 'RPT DT: ' PA-FRAUD-RPT-DATE` in `UPDATE-AUTH-DETAILS` fires on every fraud REPL (region log only); carry as a log line, do not remove.
6. **COPAUS1C-FB6** — `COPAUS1C.cbl:230-266` — `MARK-AUTH-FRAUD` unconditionally toggles `PA-AUTH-FRAUD` and LINKs COPAUS2C even on AUTHS-EOF/error (stale io-area), and lacks the `IMS-PSB-SCHD` syncpoint reset present in ENTER/PF8; reproduce as-is.
7. **COPAUS1C-FB7** — `COPAUS1C.cbl:47-48` — `WS-RESP-CD`/`WS-REAS-CD` declared but never used (dead); carry as unused.
8. **COPAUS1C-FB8** — `COPAUS1C.cbl:60, 63` — Truncated/misspelled reason descriptions `INSUFFICNT FUND` and `EXCED DAILY LMT` stored exactly so (to fit X(16)); preserve the spellings.

## COPAUS2C — Fraud-flag writer (CICS-DB2, LINKed)

1. **COPAUS2C-FB1** — `COPAUS2C.cbl:35-37, 107` — `COMPUTE WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C` into unsigned `9(09)`; out-of-range or negative results silently truncate high-order digits / drop the sign (no ON SIZE ERROR); reproduce truncate-toward-zero + silent overflow.
2. **COPAUS2C-FB2** — `COPAUS2C.cbl:56, 209, 237` — `MOVE SQLSTATE TO WS-SQLSTATE` (`PIC +9(09)`); SQLSTATE is a 5-char (possibly alphabetic) code; reproduce the edited-numeric conversion, even though it loses alphabetic content.
3. **COPAUS2C-FB3** — `COPAUS2C.cbl:101, 194, 224-225` — `MOVE WS-CUR-DATE TO PA-FRAUD-RPT-DATE` (COMMAREA mutation) is never used as a SQL host variable; the SQL writes `FRAUD_RPT_DATE = CURRENT DATE`; keep the COMMAREA mutation but the column reflects server date.
4. **COPAUS2C-FB4** — `COPAUS2C.cbl:80-82, 137, 165, 193` — `AUTH_FRAUD` sourced from `WS-FRD-ACTION` (the action code `F`/`R`), not the detail record's `PA-AUTH-FRAUD`; preserve the non-obvious mapping.
5. **COPAUS2C-FB5** — `COPAUS2C.cbl:125-126, 155, 183; CIPAUDTY.cpy:36; dcl/AUTHFRDS.dcl:37,68` — "CATAGORY" misspelling is part of the contract (`MERCHANT_CATAGORY_CODE`/`PA-MERCHANT-CATAGORY-CODE`); keep the misspelling in entity/column names.
6. **COPAUS2C-FB6** — `COPAUS2C.cbl:218-220; COPAUS1C.cbl:253-262` — No SYNCPOINT/COMMIT in COPAUS2C; the bare `EXEC CICS RETURN` leaves the DB2 change uncommitted; the caller (COPAUS1C) decides COMMIT vs ROLLBACK; do NOT auto-commit inside COPAUS2C.

---

## Summary

- **Total faithful bugs: 235**
- **Programs affected: 34** (every per-program PORT SPEC in `_design/specs/` carries a FAITHFUL BUGS section)

Per-program counts: CBACT01C 8, CBACT02C 6, CBACT03C 3, CBACT04C 7, CBCUS01C 3, CBEXPORT 8, CBIMPORT 5, CBTRN01C 7, CBTRN02C 7, CBTRN03C 9, COSGN00C 3, COMEN01C 7, COADM01C 7, COBIL00C 6, COCRDLIC 9, COCRDSLC 8, COCRDUPC 8, COACTVWC 7, COACTUPC 8, COUSR02C 5, COUSR03C 9, CORPT00C 8, CSUTLDTC 7, COBSWAIT 4, CODATE01 10, COACCT01 9, COTRTLIC 8, COTRTUPC 7, COBTUPDT 6, CBPAUP0C 7, COPAUA0C 9, COPAUS0C 6, COPAUS1C 8, COPAUS2C 6.

Each bug above MUST be locked by a pinning (characterization) test in the final verification pass.
