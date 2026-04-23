"""Comprehensive Playwright verification for cross-tenant chore flows.

Test cases A..G, executed against the running ShiftManager dev server.
DOES NOT MODIFY production code — only exercises pages/APIs.
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

import os
import time
import json
import sqlite3
import urllib.request
import urllib.error
from datetime import datetime
from playwright.sync_api import sync_playwright, TimeoutError as PWTimeout

BASE = "http://localhost:5000"
DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "app.db")

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def section(name):
    print()
    print("=" * 78)
    print(f"CASE {name}")
    print("=" * 78)


def wait_for_server(timeout_s=90):
    deadline = time.time() + timeout_s
    last_err = None
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"{BASE}/Auth/Login", timeout=3) as r:
                if r.status == 200:
                    return True
        except Exception as e:
            last_err = e
        time.sleep(2)
    print(f"Server never responded: {last_err}")
    return False


def db_query(sql, params=()):
    c = sqlite3.connect(DB_PATH)
    c.row_factory = sqlite3.Row
    try:
        cur = c.cursor()
        cur.execute(sql, params)
        return [dict(r) for r in cur.fetchall()]
    finally:
        c.close()


def login(page):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill('input[name="Email"]', "admin@local")
    page.fill('input[name="Password"]', "admin123")
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")


def post_quick_add_chore(page, assignee_id, date_str, title, force=False):
    """Use page.evaluate to do an authenticated fetch from the browser context."""
    js = """
    async ({assigneeId, date, title, force}) => {
      const res = await fetch('/Api/Calendar/QuickAddChore', {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          'X-Requested-With': 'XMLHttpRequest'
        },
        body: JSON.stringify({assigneeId, date, title, forceAssign: force})
      });
      const raw = await res.text();
      let body;
      try { body = JSON.parse(raw); } catch(e) { body = {raw: raw.slice(0, 500)}; }
      return {status: res.status, body};
    }
    """
    return page.evaluate(js, {"assigneeId": assignee_id, "date": date_str, "title": title, "force": force})


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

results = {}

print("Waiting for server...")
if not wait_for_server():
    print("ABORT: server not reachable.")
    sys.exit(1)
print("Server is up.")

# Reference data from DB
users_1_40 = db_query("""
    SELECT u.Id, u.DisplayName, u.Role, u.CompanyId, c.MoleculeId
    FROM Users u LEFT JOIN Companies c ON c.Id=u.CompanyId
    WHERE u.Id BETWEEN 1 AND 40
    ORDER BY u.Id
""")
director_users = [u for u in users_1_40 if u["Role"] == 3]
orphan_company_uids = {u["Id"] for u in users_1_40 if u["MoleculeId"] is None}
print(f"Reference: {len(director_users)} Director users in 1..40; orphan-company uids = {sorted(orphan_company_uids)}")

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(ignore_https_errors=True, viewport={"width": 1500, "height": 900})
    page = ctx.new_page()
    console_log = []
    page.on("console", lambda m: console_log.append(f"[{m.type}] {m.text[:300]}"))
    page.on("pageerror", lambda e: console_log.append(f"[PAGEERROR] {str(e)[:300]}"))

    login(page)

    # =====================================================================
    # CASE A — cross-tenant probe for assignees 1..40
    # =====================================================================
    section("A — Chore assignment cross-tenant probe (uids 1..40, 2026-05-15)")
    a_results = []
    created_chore_ids = []
    for uid in range(1, 41):
        try:
            r = post_quick_add_chore(page, uid, "2026-05-15", f"PROBE_{uid}")
        except Exception as e:
            r = {"status": -1, "body": {"error": str(e)[:200]}}
        msg = (r.get("body") or {}).get("message") if isinstance(r.get("body"), dict) else None
        chore_id = (r.get("body") or {}).get("choreId") if isinstance(r.get("body"), dict) else None
        if chore_id:
            created_chore_ids.append((chore_id, uid))
        a_results.append({"uid": uid, "status": r["status"], "message": msg})
        print(f"  uid={uid:>2} status={r['status']} message={msg!r} choreId={chore_id}")

    # Verify expectations
    expected_blocked = orphan_company_uids
    actually_blocked = {row["uid"] for row in a_results if row["status"] == 403 or (row["message"] and "annot assign" in row["message"])}
    successes = {row["uid"] for row in a_results if row["status"] == 200}
    print(f"  -> success uids ({len(successes)}): {sorted(successes)}")
    print(f"  -> blocked uids ({len(actually_blocked)}): {sorted(actually_blocked)}")
    print(f"  -> expected-blocked (orphan company): {sorted(expected_blocked)}")

    director_uids_in_range = {u["Id"] for u in director_users}
    # Exclude directors who are in orphan-company (those are correctly blocked by company,
    # not by role) — Role-removal verification only applies to molecule-rooted directors.
    director_uids_eligible = director_uids_in_range - expected_blocked
    director_blocked = director_uids_eligible & actually_blocked
    director_succeeded = director_uids_eligible & successes
    director_other = director_uids_eligible - director_blocked - director_succeeded
    print(f"  -> Director uids in range: {sorted(director_uids_in_range)}")
    print(f"  -> Director uids eligible (in molecule-rooted companies): {sorted(director_uids_eligible)}")
    print(f"     succeeded={sorted(director_succeeded)}, blocked_unexpectedly={sorted(director_blocked)}, other={sorted(director_other)}")

    a_pass = (actually_blocked == expected_blocked) and (director_succeeded == director_uids_eligible) and not director_blocked
    results["A"] = ("PASS" if a_pass else "FAIL",
                    f"blocked_match={actually_blocked == expected_blocked}; eligible_directors_all_succeed={director_succeeded == director_uids_eligible}; unexpected_director_blocks={sorted(director_blocked)}")

    # =====================================================================
    # CASE B — chore error toast UX (past date)
    # =====================================================================
    section("B — Chore error toast UX (past-date message persists 5+ s, dismiss button)")
    # Navigate to a chores page so window.quickAddChore is loaded.
    page.goto(f"{BASE}/Calendar/Chores?MoleculeId=2&Start=2026-04-26&ViewMode=2weeks", wait_until="networkidle")
    page.wait_for_timeout(500)

    has_fn = page.evaluate("() => typeof window.quickAddChore === 'function'")
    print(f"  window.quickAddChore exists: {has_fn}")

    # Trigger the past-date call.
    try:
        page.evaluate("async () => { try { await window.quickAddChore('2026-04-19', 14, 'PAST_TEST', false, null); } catch(e) { window.__pastErr = String(e); } }")
    except Exception as e:
        print(f"  evaluate raised: {e}")

    page.wait_for_timeout(800)
    modal_visible_initial = page.evaluate("""() => {
        const el = document.getElementById('js-feedback-modal');
        if (!el) return {present: false};
        const s = getComputedStyle(el);
        const r = el.getBoundingClientRect();
        const visible = s.display !== 'none' && s.visibility !== 'hidden' && parseFloat(s.opacity) > 0 && r.width > 0 && r.height > 0;
        return {present: true, visible, display: s.display, opacity: s.opacity, w: r.width, h: r.height, text: (el.innerText||'').slice(0,400)};
    }""")
    print(f"  modal initial: {modal_visible_initial}")

    # Wait 5+ seconds, recheck.
    page.wait_for_timeout(5500)
    modal_after_5s = page.evaluate("""() => {
        const el = document.getElementById('js-feedback-modal');
        if (!el) return {present: false};
        const s = getComputedStyle(el);
        const r = el.getBoundingClientRect();
        const visible = s.display !== 'none' && s.visibility !== 'hidden' && parseFloat(s.opacity) > 0 && r.width > 0 && r.height > 0;
        return {present: true, visible, display: s.display, opacity: s.opacity, w: r.width, h: r.height, text: (el.innerText||'').slice(0,400)};
    }""")
    print(f"  modal after 5.5s: {modal_after_5s}")

    # Try OK click.
    dismiss_clicked = page.evaluate("""() => {
        const el = document.getElementById('js-feedback-modal');
        if (!el) return {ok:false, reason:'no modal'};
        const btn = el.querySelector('button.ok, button[data-action="ok"], button.btn-primary, button');
        if (!btn) return {ok:false, reason:'no button'};
        btn.click();
        return {ok:true, btnText: (btn.innerText||'').trim()};
    }""")
    print(f"  dismiss click: {dismiss_clicked}")
    page.wait_for_timeout(500)
    modal_after_dismiss = page.evaluate("""() => {
        const el = document.getElementById('js-feedback-modal');
        if (!el) return {present: false};
        const s = getComputedStyle(el);
        const r = el.getBoundingClientRect();
        const visible = s.display !== 'none' && s.visibility !== 'hidden' && parseFloat(s.opacity) > 0 && r.width > 0 && r.height > 0;
        return {present: true, visible, display: s.display, opacity: s.opacity};
    }""")
    print(f"  modal after dismiss: {modal_after_dismiss}")

    msg_text = (modal_visible_initial.get("text") or "").lower()
    has_past_msg = ("past" in msg_text) or ("עבר" in (modal_visible_initial.get("text") or ""))
    b_pass = (modal_visible_initial.get("visible") and
              modal_after_5s.get("visible") and
              has_past_msg and
              not modal_after_dismiss.get("visible", True))
    results["B"] = ("PASS" if b_pass else "FAIL",
                    f"initial_visible={modal_visible_initial.get('visible')}, after5s={modal_after_5s.get('visible')}, past_msg={has_past_msg}, dismissed={not modal_after_dismiss.get('visible', True)}")

    # =====================================================================
    # CASE C — Cross-tenant assignment via UI bottom sheet
    # =====================================================================
    section("C — Cross-tenant assignment via UI bottom sheet (mol 2)")
    # Use mgr.hakam.ella uid=22 (CompanyId=6, MoleculeId=2) — different company than admin (15).
    network_c = []
    def on_resp_c(r):
        if "QuickAddChore" in r.url:
            # Do NOT read body — the page-side fetch already consumed it.
            network_c.append((r.status, r.url))
    page.on("response", on_resp_c)

    page.goto(f"{BASE}/Calendar/Chores?MoleculeId=2&Start=2026-05-10&ViewMode=2weeks", wait_until="networkidle")
    page.wait_for_timeout(800)

    # Pick a future date (2026-05-12) and uid=22.
    target_title = f"UI_CROSS_{int(time.time())}"
    target_date = "2026-05-12"
    target_uid = 22

    # Verify uid 22 is in the visible rows.
    visible_rows = page.evaluate("""() => Array.from(document.querySelectorAll('[data-row-id^=user-]'))
        .map(e => e.getAttribute('data-row-id'))
        .filter((v,i,a) => a.indexOf(v)===i)""")
    print(f"  visible user rows in mol 2 (count={len(visible_rows)}): {visible_rows[:15]}{'...' if len(visible_rows)>15 else ''}")

    # Use quickAddChore (the same path the bottom-sheet Assign uses) to drive the flow.
    ui_call = page.evaluate("""async ({date, uid, title}) => {
        try {
            await window.quickAddChore(date, uid, title, false, null);
            return {ok: true};
        } catch(e) { return {ok:false, err:String(e)}; }
    }""", {"date": target_date, "uid": target_uid, "title": target_title})
    print(f"  quickAddChore call result: {ui_call}")
    page.wait_for_timeout(1500)
    print(f"  network responses: {network_c}")

    # Reload and assert chore is present.
    page.goto(f"{BASE}/Calendar/Chores?MoleculeId=2&Start=2026-05-10&ViewMode=2weeks", wait_until="networkidle")
    page.wait_for_timeout(800)
    found = page.evaluate(f"""() => {{
        const cells = document.querySelectorAll('.excel-calendar__cell, [data-row-id]');
        for (const c of cells) {{
            if ((c.textContent || '').includes('{target_title}')) {{
                return {{
                    date: c.getAttribute('data-date'),
                    rowId: c.getAttribute('data-row-id'),
                }};
            }}
        }}
        return null;
    }}""")
    print(f"  chore visible after reload: {found}")
    # Confirm DB write too.
    chore_db = db_query("SELECT Id, UserId, Date, Title, CompanyId FROM Chores WHERE Title=? AND CanceledAt IS NULL", (target_title,))
    print(f"  chore in DB: {chore_db}")
    if chore_db:
        created_chore_ids.append((chore_db[0]["Id"], chore_db[0]["UserId"]))

    network_ok = any(s == 200 for s, _ in network_c)
    c_pass = network_ok and bool(chore_db)
    results["C"] = ("PASS" if c_pass else "FAIL",
                    f"network200={network_ok}, foundInDOM={bool(found)}, foundInDB={bool(chore_db)}")
    # Remove the response listener so it doesn't interfere with later cases.
    try: page.remove_listener("response", on_resp_c)
    except Exception: pass

    # =====================================================================
    # CASE D — Director-now-assignable (POST QuickAddChore to a Director)
    # =====================================================================
    section("D — Director-now-assignable (Role=3 user via QuickAddChore)")
    # Pick a Director whose company has a molecule (skip orphan-company directors).
    eligible_dirs = [u for u in director_users if u["MoleculeId"] is not None]
    if not eligible_dirs:
        results["D"] = ("SKIP", "no Director users in molecule-rooted companies")
    else:
        d_dir = eligible_dirs[0]
        # Use a future date that's unlikely to collide with case A.
        d_date = "2026-05-20"
        d_title = f"DIRECTOR_TEST_{int(time.time())}"
        r = post_quick_add_chore(page, d_dir["Id"], d_date, d_title)
        print(f"  Director uid={d_dir['Id']} ({d_dir['DisplayName']}, CompanyId={d_dir['CompanyId']}) -> status={r['status']} body={r['body']}")
        choreId = (r.get("body") or {}).get("choreId") if isinstance(r.get("body"), dict) else None
        if choreId:
            created_chore_ids.append((choreId, d_dir["Id"]))
        d_pass = r["status"] == 200 and choreId is not None
        results["D"] = ("PASS" if d_pass else "FAIL",
                        f"status={r['status']}, choreId={choreId}, dir_uid={d_dir['Id']}")

    # =====================================================================
    # CASE E — GriffinCallback page renders (no 500)
    # =====================================================================
    section("E — GriffinCallback renders (cshtml @inject fix)")
    try:
        resp = page.goto(f"{BASE}/Auth/GriffinCallback?error=test", wait_until="networkidle")
        status = resp.status if resp else None
        body_snippet = page.content()[:500]
        print(f"  status={status}; body starts: {body_snippet[:200]!r}")
        e_pass = status == 200
        results["E"] = ("PASS" if e_pass else "FAIL", f"status={status}")
    except Exception as ex:
        results["E"] = ("FAIL", f"exception={ex}")
        print(f"  exception: {ex}")

    # =====================================================================
    # CASE F — Audit log entries created for successful chore creates
    # =====================================================================
    section("F — AuditLogs ChoreCreatedQuick entries for our chores")
    print(f"  created chore ids this run: {created_chore_ids}")
    if not created_chore_ids:
        results["F"] = ("SKIP", "no chores created in this run")
    else:
        ids = [cid for cid, _ in created_chore_ids]
        placeholders = ",".join("?" * len(ids))
        rows = db_query(
            f"SELECT EntityId, Action, Description FROM AuditLogs WHERE Action='ChoreCreatedQuick' AND EntityType='Chore' AND EntityId IN ({placeholders})",
            tuple(ids)
        )
        found_ids = {r["EntityId"] for r in rows}
        missing = set(ids) - found_ids
        print(f"  audit rows found ({len(rows)}): {[(r['EntityId'], r['Action']) for r in rows][:10]}")
        print(f"  missing audit for choreIds: {sorted(missing)}")
        f_pass = not missing
        results["F"] = ("PASS" if f_pass else "FAIL",
                        f"found={len(found_ids)}/{len(ids)}, missing={sorted(missing)}")

    # =====================================================================
    # CASE G — avatar URL on /My/Profile
    # =====================================================================
    section("G — /My/Profile avatar URL pattern")
    try:
        page.goto(f"{BASE}/My/Profile", wait_until="networkidle")
        page.wait_for_timeout(500)
        avatar_info = page.evaluate("""() => {
            const imgs = Array.from(document.querySelectorAll('img'));
            const av = imgs.find(i => /avatars?\\//i.test(i.getAttribute('src')||''));
            if (av) return {src: av.getAttribute('src'), present: true};
            // initials fallback?
            const init = document.querySelector('.avatar, .avatar-initials, [class*="avatar"]');
            return {present: false, fallback: init ? (init.outerHTML||'').slice(0,200) : null};
        }""")
        print(f"  avatar info: {avatar_info}")
        if avatar_info.get("present"):
            src = avatar_info.get("src", "")
            # expected pattern /avatars/{companyId}/{userId}_thumb.jpg
            import re
            m = re.search(r"/avatars/(\d+)/(\d+)_thumb\.(jpg|png)", src)
            if m:
                results["G"] = ("PASS", f"src={src}, companyId={m.group(1)}, userId={m.group(2)}")
            else:
                results["G"] = ("FAIL", f"src present but pattern mismatch: {src}")
        else:
            results["G"] = ("SKIP", f"no avatar img rendered (admin@local likely has no avatar). fallback={avatar_info.get('fallback')!r}")
    except Exception as ex:
        results["G"] = ("FAIL", f"exception={ex}")

    # ---------------------------------------------------------------------
    # console summary (for debugging)
    # ---------------------------------------------------------------------
    print()
    print("--- last 12 console messages ---")
    for c in console_log[-12:]:
        print(c)

    browser.close()

# ---------------------------------------------------------------------------
# Final report
# ---------------------------------------------------------------------------
print()
print("=" * 78)
print("FINAL REPORT")
print("=" * 78)
total = len(results)
passed = sum(1 for v, _ in results.values() if v == "PASS")
failed = sum(1 for v, _ in results.values() if v == "FAIL")
skipped = sum(1 for v, _ in results.values() if v == "SKIP")
for case in sorted(results):
    status, evidence = results[case]
    print(f"  {case}: {status} — {evidence}")
print()
print(f"SUMMARY: {passed} pass, {failed} fail, {skipped} skip out of {total}")
sys.exit(0 if failed == 0 else 1)
