-- =========================================================================
-- Ops Console Scheduler: Populate NameKey for Existing ShiftTypes
-- =========================================================================
-- This script populates the NameKey field for existing ShiftTypes.
-- Run this AFTER applying the OpsConsoleScheduler migration.
--
-- Purpose: ShiftTypes now use NameKey (resource keys) for bilingual names.
-- Existing ShiftTypes need to have this field populated to work with <loc>.
-- =========================================================================

BEGIN TRANSACTION;

-- 1. Predefined shift types: Map Key → NameKey
UPDATE ShiftTypes
SET NameKey = 'ShiftType_MORNING_Name'
WHERE [Key] = 'MORNING' AND (NameKey IS NULL OR NameKey = '');

UPDATE ShiftTypes
SET NameKey = 'ShiftType_MIDDLE_Name'
WHERE [Key] = 'MIDDLE' AND (NameKey IS NULL OR NameKey = '');

UPDATE ShiftTypes
SET NameKey = 'ShiftType_AFTERNOON_Name'
WHERE [Key] IN ('AFTERNOON', 'NOON') AND (NameKey IS NULL OR NameKey = '');

UPDATE ShiftTypes
SET NameKey = 'ShiftType_NIGHT_Name'
WHERE [Key] = 'NIGHT' AND (NameKey IS NULL OR NameKey = '');

UPDATE ShiftTypes
SET NameKey = 'ShiftType_EVENING_Name'
WHERE [Key] = 'EVENING' AND (NameKey IS NULL OR NameKey = '');

UPDATE ShiftTypes
SET NameKey = 'ShiftType_OFFLINE_Name'
WHERE [Key] = 'OFFLINE' AND (NameKey IS NULL OR NameKey = '');

-- 2. Custom shift types: Generate unique keys
-- Pattern: ShiftType_CUSTOM_<ID>_Name
UPDATE ShiftTypes
SET NameKey = 'ShiftType_CUSTOM_' || CAST(Id AS TEXT) || '_Name'
WHERE [Key] LIKE 'CUSTOM_%' AND (NameKey IS NULL OR NameKey = '');

-- 3. Verify all ShiftTypes now have NameKey
SELECT
    COUNT(*) as TotalShiftTypes,
    COUNT(NameKey) as WithNameKey,
    COUNT(*) - COUNT(NameKey) as MissingNameKey
FROM ShiftTypes;

-- If MissingNameKey > 0, investigate:
-- SELECT * FROM ShiftTypes WHERE NameKey IS NULL OR NameKey = '';

COMMIT;

-- =========================================================================
-- Post-Migration Notes:
-- =========================================================================
-- 1. Predefined shift types now reference global resource keys in
--    SharedResources.resx and SharedResources.he-IL.resx
--
-- 2. Custom shift types will need company-specific localization overrides
--    created via the Blueprints page or CompanyLocalizationService
--
-- 3. The <loc> tag helper will gracefully fall back to the Key value if
--    no resource or override exists
-- =========================================================================
