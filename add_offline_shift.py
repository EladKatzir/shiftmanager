#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script to add OFFLINE shift type to all companies in the database
"""
import sqlite3
import os
import sys

# Set UTF-8 encoding for Windows console
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

# Database path
db_path = r"C:\Users\katzi\Downloads\ShiftManager\app.db"

# Check if database exists
if not os.path.exists(db_path):
    print(f"[ERROR] Database not found at: {db_path}")
    exit(1)

print(f"[OK] Database found at: {db_path}\n")

try:
    # Connect to database
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    print("[OK] Connected to database successfully\n")

    # Get all companies
    cursor.execute("SELECT Id, Name FROM Companies")
    companies = cursor.fetchall()

    print(f"Found {len(companies)} company(ies) in database\n")

    companies_added = 0
    companies_skipped = 0

    for company_id, company_name in companies:
        # Check if OFFLINE shift type already exists
        cursor.execute(
            "SELECT COUNT(*) FROM ShiftTypes WHERE CompanyId = ? AND Key = 'OFFLINE'",
            (company_id,)
        )
        count = cursor.fetchone()[0]

        if count == 0:
            # Insert OFFLINE shift type
            cursor.execute(
                """
                INSERT INTO ShiftTypes (CompanyId, Key, Start, End, CustomName)
                VALUES (?, 'OFFLINE', '00:00:00', '00:00:00', NULL)
                """,
                (company_id,)
            )
            print(f"[+] Added OFFLINE shift type to company: {company_name} (ID={company_id})")
            companies_added += 1
        else:
            print(f"[-] OFFLINE shift type already exists for company: {company_name} (ID={company_id})")
            companies_skipped += 1

    # Commit changes
    conn.commit()
    conn.close()

    print(f"\n{'='*50}")
    print(f"Summary:")
    print(f"  Companies with OFFLINE added: {companies_added}")
    print(f"  Companies skipped (already had OFFLINE): {companies_skipped}")
    print(f"{'='*50}")
    print("\n[OK] Script completed successfully!")

except sqlite3.Error as e:
    print(f"[ERROR] SQLite Error: {e}")
    exit(1)
except Exception as e:
    print(f"[ERROR] Error: {e}")
    exit(1)
