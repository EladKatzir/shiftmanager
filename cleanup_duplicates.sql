-- Cleanup duplicate ShiftTypes
-- This script keeps only the first (oldest) record for each CompanyId+Key combination

-- For each company and key combination, keep only the oldest record
DELETE FROM ShiftTypes
WHERE Id NOT IN (
    SELECT MIN(Id)
    FROM ShiftTypes
    GROUP BY CompanyId, Key
);

-- Verify the cleanup
SELECT CompanyId, Key, COUNT(*) as Count
FROM ShiftTypes
GROUP BY CompanyId, Key
HAVING COUNT(*) > 1;

-- Show remaining ShiftTypes
SELECT Id, CompanyId, Key, Start, End
FROM ShiftTypes
ORDER BY CompanyId, Key;
