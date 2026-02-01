# Database Seeding Configuration Guide

## Overview

ShiftManager uses a configuration-driven seeding system that allows you to customize the initial database setup through `appsettings.json`. This is ideal for air-gapped production deployments on IIS where environment variables or command-line arguments are impractical.

## Table of Contents

1. [Quick Start](#quick-start)
2. [Configuration Reference](#configuration-reference)
3. [Seeding Flow](#seeding-flow)
4. [How to Customize](#how-to-customize)
5. [Production Deployment](#production-deployment)
6. [Troubleshooting](#troubleshooting)

---

## Quick Start

### Minimal Production Setup

Edit `appsettings.json`:

```json
{
  "Seeding": {
    "Owner": {
      "Email": "admin@yourdomain.mil",
      "Password": "YourSecurePassword123!",
      "DisplayName": "מנהל מערכת"
    }
  }
}
```

Start the application. The database will be automatically created and seeded.

---

## Configuration Reference

### Complete Seeding Section

```json
{
  "Seeding": {
    "Owner": {
      "Email": "admin@local",
      "Password": "admin123",
      "DisplayName": "מנהל מערכת"
    },
    "AdditionalMolecules": [],
    "AdditionalCompanies": [],
    "AdditionalDepartments": []
  }
}
```

### Owner Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Email` | string | `"admin@local"` | Login email for the owner account |
| `Password` | string | `"admin123"` | Password (change for production!) |
| `DisplayName` | string | `"Owner"` | Display name shown in UI |

### AdditionalMolecules Configuration

Add custom molecules beyond the default Shifty hierarchy.

```json
"AdditionalMolecules": [
  {
    "Name": "Delta",
    "DisplayName": "דלתא",
    "Type": "Workforce",
    "AreaName": "190"
  }
]
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | string | Required | Internal name (English, no spaces) |
| `DisplayName` | string | Same as Name | Hebrew display name |
| `Type` | string | `"Workforce"` | One of: `Workforce`, `Tech`, `Helper`, `System` |
| `AreaName` | string | `"190"` | Name of the parent area |

### AdditionalCompanies Configuration

Add custom companies to existing or new molecules.

```json
"AdditionalCompanies": [
  {
    "Name": "Alpha",
    "DisplayName": "אלפא",
    "MoleculeName": "Delta",
    "Slug": "alpha"
  }
]
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | string | Required | Internal name (English, no spaces) |
| `DisplayName` | string | Same as Name | Hebrew display name |
| `MoleculeName` | string | Required | Name of the parent molecule |
| `Slug` | string | Auto-generated | URL-friendly identifier |

### AdditionalDepartments Configuration

Add departments to Tech-type molecules.

```json
"AdditionalDepartments": [
  {
    "Name": "Networks",
    "DisplayName": "תקשורת",
    "MoleculeName": "Shikma"
  }
]
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | string | Required | Internal name (English, no spaces) |
| `DisplayName` | string | Same as Name | Hebrew display name |
| `MoleculeName` | string | Required | Name of the parent Tech molecule |

---

## Seeding Flow

### What Happens on Application Startup

```
1. LOAD CONFIGURATION
   └── Read Seeding section from appsettings.json
   └── Check for SEED_ADMIN_PASSWORD environment variable (optional override)

2. RUN DATABASE MIGRATIONS
   └── Create/update database schema

3. SEED BASE HIERARCHY (if not exists)
   ├── GrantTypes (107 permission types)
   ├── RoleTemplates (11 role definitions)
   └── Shifty Organization:
       ├── Project: Shifty (שיפטי)
       ├── Area: 190
       ├── JobTypes: Alhut, BR, Text, Hakam
       ├── Molecules (9):
       │   ├── Workforce: Oren, Ella, Harava, Shaked, Gefen
       │   ├── Tech: Shikma
       │   ├── Helper: NOC, Shiklut
       │   └── System: System (for admin users)
       ├── Companies (15):
       │   ├── Oren: Tzafona, Hir, Camps, City, Radio
       │   ├── Ella: Hitazmut, GAP, Yeadim
       │   ├── Harava: Element
       │   ├── Shaked: Inside, Out
       │   ├── Gefen: Hamasa, Kabah, Matot
       │   └── System: SystemAdmins
       ├── Departments (6 for Shikma):
       │   └── Pie, Tao, Yekeb, Snir, Arbel, Samapkam
       └── ShiftGroupings, AreaSettings

4. SEED ADDITIONAL ENTITIES FROM CONFIG
   ├── AdditionalMolecules (skips if exists)
   ├── AdditionalCompanies (skips if exists in same molecule)
   └── AdditionalDepartments (skips if exists in same molecule)

5. SEED OWNER USER (if no users exist)
   ├── Create user in SystemAdmins company
   ├── Use Email, Password, DisplayName from config
   └── Assign UserRole.Owner

6. SEED OWNER GRANTS
   └── Grant ALL 107 permissions with Project scope (godmode)
```

### Idempotency

The seeding system is designed to be **idempotent** - you can restart the application multiple times without duplicating data.

| Data Type | Check | Behavior |
|-----------|-------|----------|
| GrantTypes | If any exist | Skip all |
| RoleTemplates | If any exist | Skip all |
| Shifty Hierarchy | If Project "Shifty" exists | Skip all |
| Additional Molecules | By Name in Area | Skip if exists |
| Additional Companies | By Name in Molecule | Skip if exists |
| Additional Departments | By Name in Molecule | Skip if exists |
| Owner User | If any users exist | Skip |
| Owner Grants | Per grant type | Skip existing |

---

## How to Customize

### Change Owner Password (First Run Only)

```json
"Seeding": {
  "Owner": {
    "Password": "MyNewSecurePassword123!"
  }
}
```

> **Important:** This only works before the first user is created. After that, use the admin UI or direct database access.

### Add a New Molecule

```json
"AdditionalMolecules": [
  {
    "Name": "Echo",
    "DisplayName": "אקו",
    "Type": "Workforce",
    "AreaName": "190"
  }
]
```

Molecule Types:
- `Workforce` - Has companies, users have JobTypes (Alhut, BR, Text, Hakam)
- `Tech` - Has departments instead of companies
- `Helper` - Flat structure, no sub-units
- `System` - For administrative users

### Add Companies to a New Molecule

```json
"AdditionalMolecules": [
  {
    "Name": "Echo",
    "DisplayName": "אקו",
    "Type": "Workforce"
  }
],
"AdditionalCompanies": [
  {
    "Name": "Echo1",
    "DisplayName": "אקו 1",
    "MoleculeName": "Echo"
  },
  {
    "Name": "Echo2",
    "DisplayName": "אקו 2",
    "MoleculeName": "Echo"
  }
]
```

### Add Companies to Existing Molecules

```json
"AdditionalCompanies": [
  {
    "Name": "NewUnit",
    "DisplayName": "יחידה חדשה",
    "MoleculeName": "Oren"
  }
]
```

### Add Departments (Tech Molecules)

```json
"AdditionalDepartments": [
  {
    "Name": "CloudOps",
    "DisplayName": "תפעול ענן",
    "MoleculeName": "Shikma"
  }
]
```

---

## Production Deployment

### IIS Deployment Steps

1. **Prepare Configuration**

   Edit `appsettings.json` or create `appsettings.Production.json`:

   ```json
   {
     "Seeding": {
       "Owner": {
         "Email": "admin@yourdomain.mil",
         "Password": "YourSecureProductionPassword!",
         "DisplayName": "מנהל מערכת"
       }
     }
   }
   ```

2. **Deploy Application Files**

   Copy all files to the IIS application folder.

3. **Configure IIS**

   - Create Application Pool (No Managed Code or .NET CLR v4.0)
   - Create Website/Application pointing to the folder
   - Ensure the App Pool identity has write access for the database file

4. **Start Application**

   The first request will trigger:
   - Database creation (app.db)
   - Schema migrations
   - Data seeding

5. **Verify**

   Log in with the configured owner credentials.

### Adding Data After Initial Deployment

To add new molecules/companies to an existing database:

1. Edit `appsettings.json` with new entries in `AdditionalMolecules`/`AdditionalCompanies`
2. Restart the Application Pool
3. New entities are seeded on next request

### Environment Variable Override

For backward compatibility, you can override the owner password via environment variable:

```cmd
set SEED_ADMIN_PASSWORD=MyPassword
```

This takes precedence over the appsettings.json value.

---

## Troubleshooting

### Owner Password Not Working

**Cause:** The owner user was already created with a different password.

**Solution:**
- Use the UI to reset the password, or
- Delete the database and restart with the correct password

### Additional Entities Not Being Created

**Cause:** Entity with same name already exists.

**Check logs for:**
```
Molecule {Name} already exists, skipping
Company {Name} already exists in molecule {Molecule}, skipping
```

**Solution:**
- Use a different name, or
- Delete the existing entity via UI first

### "Area not found" Warning

**Cause:** Invalid `AreaName` in AdditionalMolecules config.

**Solution:** Use `"190"` (the default area name) or verify the area exists.

### "Molecule not found" Warning

**Cause:** Invalid `MoleculeName` in AdditionalCompanies/AdditionalDepartments config.

**Solution:**
- Ensure the molecule is defined in `AdditionalMolecules` AND appears before the company/department
- Or use an existing molecule name (Oren, Ella, Shikma, etc.)

### Security Warning in Logs

```
SECURITY WARNING: Using default owner password in Production environment
```

**Cause:** Password is still set to `admin123` in production.

**Solution:** Change `Seeding:Owner:Password` in appsettings.json.

---

## Default Hierarchy Reference

### Project Structure

```
Project: Shifty (שיפטי)
└── Area: 190
    ├── JobTypes:
    │   ├── Alhut (אלחוט) - #3b82f6
    │   ├── BR (ב"ר) - #10b981
    │   ├── Text (טקסט) - #8b5cf6
    │   └── Hakam (חק"ם) - #f59e0b
    │
    ├── Molecules [Workforce]:
    │   ├── Oren (אורן)
    │   │   └── Companies: Tzafona, Hir, Camps, City, Radio
    │   ├── Ella (אלה)
    │   │   └── Companies: Hitazmut, GAP, Yeadim
    │   ├── Harava (ערבה)
    │   │   └── Companies: Element
    │   ├── Shaked (שקד)
    │   │   └── Companies: Inside, Out
    │   └── Gefen (גפן)
    │       └── Companies: Hamasa, Kabah, Matot
    │
    ├── Molecules [Tech]:
    │   └── Shikma (שקמה)
    │       └── Departments: Pie, Tao, Yekeb, Snir, Arbel, Samapkam
    │
    ├── Molecules [Helper]:
    │   ├── NOC (נגדים)
    │   └── Shiklut (שקלוט)
    │
    └── Molecules [System]:
        └── System (מערכת)
            └── Companies: SystemAdmins (מנהלי מערכת)
                └── Owner user lives here
```

### Grant Types (107 total)

Permissions are organized by category:
- Scheduling grants
- User management grants
- Reporting grants
- System administration grants
- And more...

The owner user receives ALL grants with `CanOwn=true` and `CanGive=true` at Project scope.

---

## Configuration File Location

| Environment | File |
|-------------|------|
| Development | `appsettings.json` + `appsettings.Development.json` |
| Production | `appsettings.json` + `appsettings.Production.json` |

Production-specific settings in `appsettings.Production.json` override `appsettings.json`.

---

## Version History

- **v3.0** - Configuration-driven seeding with appsettings.json support
- **v2.x** - Environment variable only (SEED_ADMIN_PASSWORD)
