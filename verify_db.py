#!/usr/bin/env python3
"""
Database verification and test data setup script
Creates a test API key if one doesn't exist
"""

import sqlite3
import hashlib
from datetime import datetime

DB_PATH = "app.db"

def hash_api_key(key: str) -> str:
    """Hash an API key using SHA256"""
    return hashlib.sha256(key.encode()).hexdigest().upper()

def main():
    print("=" * 60)
    print("Database Verification and Setup")
    print("=" * 60)

    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()

    # Check for active API keys
    cursor.execute("SELECT COUNT(*) FROM ApiKeys WHERE IsActive = 1")
    active_count = cursor.fetchone()[0]

    print(f"\nActive API Keys: {active_count}")

    if active_count == 0:
        print("\nNo active API keys found. Creating test API key...")

        # Check if we have any users
        cursor.execute("SELECT Id FROM Users ORDER BY Id LIMIT 1")
        user_result = cursor.fetchone()

        if not user_result:
            print("ERROR: No users found in database. Cannot create API key.")
            conn.close()
            return

        user_id = user_result[0]

        # Check if we have any companies
        cursor.execute("SELECT Id FROM Companies ORDER BY Id LIMIT 1")
        company_result = cursor.fetchone()

        if not company_result:
            print("ERROR: No companies found in database. Cannot create API key.")
            conn.close()
            return

        company_id = company_result[0]

        # Create test API key
        test_key = "test-api-key-12345"
        key_hash = hash_api_key(test_key)

        cursor.execute("""
            INSERT INTO ApiKeys (Name, KeyHash, Scopes, IsActive, CompanyId, CreatedAt, CreatedBy)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """, (
            "Test API Key",
            key_hash,
            "swaps:read,swaps:write,swaps:approve,chores:read,chores:write,onduty:read,onduty:write,feedback:read,feedback:write",
            1,
            company_id,
            datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S"),
            user_id
        ))

        conn.commit()
        print(f"✓ Created test API key: {test_key}")
        print(f"  Company ID: {company_id}")
        print(f"  Created By User ID: {user_id}")
    else:
        # List existing API keys
        cursor.execute("""
            SELECT Id, Name, Scopes, CompanyId
            FROM ApiKeys
            WHERE IsActive = 1
            LIMIT 5
        """)

        print("\nExisting API Keys:")
        for row in cursor.fetchall():
            print(f"  ID: {row[0]}, Name: {row[1]}, Company: {row[3]}")
            print(f"    Scopes: {row[2][:80]}{'...' if len(row[2]) > 80 else ''}")

    # Check for test data in each table
    print("\nDatabase Statistics:")

    tables = [
        ("Users", "Users"),
        ("Companies", "Companies"),
        ("SwapRequests", "Swap Requests"),
        ("Chores", "Chores"),
        ("OnDuties", "On-Duty Assignments"),
        ("Feedbacks", "Feedback Items")
    ]

    for table_name, display_name in tables:
        try:
            cursor.execute(f"SELECT COUNT(*) FROM {table_name}")
            count = cursor.fetchone()[0]
            print(f"  {display_name}: {count}")
        except sqlite3.OperationalError:
            print(f"  {display_name}: Table not found")

    conn.close()

    print("\n" + "=" * 60)
    print("Database verification complete!")
    print("=" * 60)
    print("\nYou can now run the API tests using:")
    print("  powershell -ExecutionPolicy Bypass -File test-api-endpoints.ps1")
    print()

if __name__ == "__main__":
    main()
