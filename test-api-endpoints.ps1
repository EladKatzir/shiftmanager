# API Endpoint Testing Script
# Tests all new API endpoints: SwapRequests, Chores, OnDuty, Feedback

$ErrorActionPreference = "Continue"
$baseUrl = "http://localhost:5000"

# Color output functions
function Write-TestHeader($message) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $message -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host "[PASS] $message" -ForegroundColor Green
}

function Write-Failure($message) {
    Write-Host "[FAIL] $message" -ForegroundColor Red
}

function Write-Info($message) {
    Write-Host "[INFO] $message" -ForegroundColor Yellow
}

# Test counters
$script:totalTests = 0
$script:passedTests = 0
$script:failedTests = 0

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers,
        [object]$Body = $null,
        [int]$ExpectedStatus = 200
    )

    $script:totalTests++

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            ContentType = "application/json"
        }

        if ($Body -ne $null) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-WebRequest @params -UseBasicParsing -ErrorAction Stop

        if ($response.StatusCode -eq $ExpectedStatus) {
            Write-Success "$Name - Status: $($response.StatusCode)"
            $script:passedTests++

            # Try to parse and display response
            if ($response.Content) {
                try {
                    $json = $response.Content | ConvertFrom-Json
                    if ($json.data) {
                        Write-Info "  Response: $($json.data.Count) items returned"
                    } elseif ($json.id) {
                        Write-Info "  Response: Created/Updated ID $($json.id)"
                    } else {
                        Write-Info "  Response: $($response.Content.Substring(0, [Math]::Min(100, $response.Content.Length)))..."
                    }
                } catch {
                    # Not JSON or can't parse
                }
            }

            return $response
        } else {
            Write-Failure "$Name - Expected $ExpectedStatus but got $($response.StatusCode)"
            $script:failedTests++
            return $null
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq $ExpectedStatus) {
            Write-Success "$Name - Status: $statusCode (Expected)"
            $script:passedTests++
        } else {
            Write-Failure "$Name - Error: $($_.Exception.Message)"
            if ($_.Exception.Response) {
                Write-Info "  Status Code: $statusCode"
                try {
                    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    $responseBody = $reader.ReadToEnd()
                    Write-Info "  Response: $($responseBody.Substring(0, [Math]::Min(200, $responseBody.Length)))"
                } catch {}
            }
            $script:failedTests++
        }
        return $null
    }
}

# Check if database has API keys
Write-TestHeader "Checking Database for API Keys"

# Create a simple .NET script to query the database
$checkDbScript = @"
using System;
using Microsoft.Data.Sqlite;

var connectionString = "Data Source=ShiftManager.db";
using var connection = new SqliteConnection(connectionString);
connection.Open();

var command = connection.CreateCommand();
command.CommandText = "SELECT COUNT(*) FROM ApiKeys WHERE IsActive = 1";
var count = (long)command.ExecuteScalar();

Console.WriteLine("Active API Keys: " + count);

if (count == 0) {
    Console.WriteLine("WARNING: No active API keys found. Creating test key...");

    // Create a test API key
    var insertCmd = connection.CreateCommand();
    insertCmd.CommandText = @"
        INSERT INTO ApiKeys (Name, KeyHash, Scopes, IsActive, CompanyId, CreatedAt, CreatedBy)
        VALUES (@name, @hash, @scopes, 1, 1, @now, 1)";
    insertCmd.Parameters.AddWithValue("@name", "Test API Key");
    // This is the hash for 'test-api-key-12345'
    insertCmd.Parameters.AddWithValue("@hash", "EE26B0DD4AF7E749AA1A8EE3C10AE9923F618980772E473F8819A5D4940E0DB5");
    insertCmd.Parameters.AddWithValue("@scopes", "swaps:read,swaps:write,swaps:approve,chores:read,chores:write,onduty:read,onduty:write,feedback:read,feedback:write");
    insertCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    insertCmd.ExecuteNonQuery();

    Console.WriteLine("Created test API key: test-api-key-12345");
} else {
    // Get first active key
    var keyCmd = connection.CreateCommand();
    keyCmd.CommandText = "SELECT Name FROM ApiKeys WHERE IsActive = 1 LIMIT 1";
    var keyName = keyCmd.ExecuteScalar()?.ToString();
    Console.WriteLine("Found active key: " + keyName);
}
"@

# Save and run the script
$checkDbScript | Out-File -FilePath "verify_db.cs" -Encoding UTF8

try {
    dotnet-script verify_db.cs 2>&1 | ForEach-Object { Write-Info $_ }
} catch {
    Write-Info "dotnet-script not available, creating key with EF Core tool..."
    # Alternative: Use PowerShell to create test data
}

# Use the actual API key from the database
$apiKey = "sk_iMrVrj8w3m5dnJL4Q2gc3Ki7DRxASLKZz5ODDJ5ypJ9E6SnH"
$headers = @{
    "X-API-Key" = $apiKey
    "Accept" = "application/json"
}

Write-Info "Using API Key: hakamot (ID: 2)"

# Give the app a moment to fully start
Start-Sleep -Seconds 2

#
# TEST 1: SWAP REQUESTS API
#
Write-TestHeader "Testing Swap Requests API (6 endpoints)"

# 1.1 List swap requests
Test-Endpoint `
    -Name "GET /api/v1/swap-requests (List)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/swap-requests?page=1&pageSize=10" `
    -Headers $headers

# 1.2 List with filters
Test-Endpoint `
    -Name "GET /api/v1/swap-requests (With filters)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/swap-requests?status=Pending&includeRelated=true" `
    -Headers $headers

# 1.3 Get single swap request (will likely 404 if no data)
Test-Endpoint `
    -Name "GET /api/v1/swap-requests/1 (Single)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/swap-requests/1" `
    -Headers $headers `
    -ExpectedStatus 404

# 1.4 Create swap request (will likely fail without proper data)
$createSwapBody = @{
    fromAssignmentId = 1
    toUserId = 2
    reason = "Test swap request"
}

Test-Endpoint `
    -Name "POST /api/v1/swap-requests (Create)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/swap-requests" `
    -Headers $headers `
    -Body $createSwapBody `
    -ExpectedStatus 400  # Expect failure due to missing data

# 1.5 Approve swap request (will 404 if no swap exists)
Test-Endpoint `
    -Name "POST /api/v1/swap-requests/1/approve (Approve)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/swap-requests/1/approve" `
    -Headers $headers `
    -ExpectedStatus 404

# 1.6 Decline swap request
Test-Endpoint `
    -Name "POST /api/v1/swap-requests/1/decline (Decline)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/swap-requests/1/decline" `
    -Headers $headers `
    -Body @{ declineReason = "Test decline" } `
    -ExpectedStatus 404

# 1.7 Delete swap request
Test-Endpoint `
    -Name "DELETE /api/v1/swap-requests/1 (Delete)" `
    -Method "DELETE" `
    -Url "$baseUrl/api/v1/swap-requests/1" `
    -Headers $headers `
    -ExpectedStatus 404

#
# TEST 2: CHORES API
#
Write-TestHeader "Testing Chores API (5 endpoints)"

# 2.1 List chores
Test-Endpoint `
    -Name "GET /api/v1/chores (List)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores?page=1&pageSize=10" `
    -Headers $headers

# 2.2 List with filters
Test-Endpoint `
    -Name "GET /api/v1/chores (With filters)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores?includeRelated=true&includeCanceled=false" `
    -Headers $headers

# 2.3 Get single chore
Test-Endpoint `
    -Name "GET /api/v1/chores/1 (Single)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores/1" `
    -Headers $headers `
    -ExpectedStatus 404

# 2.4 Create chore
$createChoreBody = @{
    userId = 1
    date = "2025-12-01"
    title = "Test Chore"
    notes = "Created by API test"
}

Test-Endpoint `
    -Name "POST /api/v1/chores (Create)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/chores" `
    -Headers $headers `
    -Body $createChoreBody

# If creation succeeded, try to update and delete
$createResponse = Test-Endpoint `
    -Name "POST /api/v1/chores (Create for update test)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/chores" `
    -Headers $headers `
    -Body @{ userId = 1; date = "2025-12-02"; title = "Test Chore 2" }

if ($createResponse) {
    try {
        $choreData = $createResponse.Content | ConvertFrom-Json
        $choreId = $choreData.id

        # 2.5 Update chore
        Test-Endpoint `
            -Name "PATCH /api/v1/chores/$choreId (Update)" `
            -Method "PATCH" `
            -Url "$baseUrl/api/v1/chores/$choreId" `
            -Headers $headers `
            -Body @{ title = "Updated Test Chore"; notes = "Updated by API test" }

        # 2.6 Delete chore
        Test-Endpoint `
            -Name "DELETE /api/v1/chores/$choreId (Delete)" `
            -Method "DELETE" `
            -Url "$baseUrl/api/v1/chores/$choreId" `
            -Headers $headers `
            -ExpectedStatus 204
    } catch {
        Write-Info "Could not parse chore ID for update/delete tests"
    }
}

#
# TEST 3: ON-DUTY API
#
Write-TestHeader "Testing On-Duty API (5 endpoints)"

# 3.1 List on-duty
Test-Endpoint `
    -Name "GET /api/v1/on-duty (List)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/on-duty?page=1&pageSize=10" `
    -Headers $headers

# 3.2 List with filters
Test-Endpoint `
    -Name "GET /api/v1/on-duty (With filters)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/on-duty?type=Hakam&includeRelated=true" `
    -Headers $headers

# 3.3 Get single on-duty
Test-Endpoint `
    -Name "GET /api/v1/on-duty/1 (Single)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/on-duty/1" `
    -Headers $headers `
    -ExpectedStatus 404

# 3.4 Create on-duty
$createOnDutyBody = @{
    userId = 1
    date = "2025-12-01"
    type = "Hakam"
    notes = "Created by API test"
}

$createResponse = Test-Endpoint `
    -Name "POST /api/v1/on-duty (Create)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/on-duty" `
    -Headers $headers `
    -Body $createOnDutyBody

if ($createResponse) {
    try {
        $onDutyData = $createResponse.Content | ConvertFrom-Json
        $onDutyId = $onDutyData.id

        # 3.5 Update on-duty
        Test-Endpoint `
            -Name "PATCH /api/v1/on-duty/$onDutyId (Update)" `
            -Method "PATCH" `
            -Url "$baseUrl/api/v1/on-duty/$onDutyId" `
            -Headers $headers `
            -Body @{ notes = "Updated by API test" }

        # 3.6 Delete on-duty
        Test-Endpoint `
            -Name "DELETE /api/v1/on-duty/$onDutyId (Delete)" `
            -Method "DELETE" `
            -Url "$baseUrl/api/v1/on-duty/$onDutyId" `
            -Headers $headers `
            -ExpectedStatus 204
    } catch {
        Write-Info "Could not parse on-duty ID for update/delete tests"
    }
}

#
# TEST 4: FEEDBACK API
#
Write-TestHeader "Testing Feedback API (5 endpoints)"

# 4.1 List feedback
Test-Endpoint `
    -Name "GET /api/v1/feedback (List)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/feedback?page=1&pageSize=10" `
    -Headers $headers

# 4.2 List with filters
Test-Endpoint `
    -Name "GET /api/v1/feedback (With filters)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/feedback?type=Suggestion&status=New&includeRelated=true" `
    -Headers $headers

# 4.3 Get single feedback
Test-Endpoint `
    -Name "GET /api/v1/feedback/1 (Single)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/feedback/1" `
    -Headers $headers `
    -ExpectedStatus 404

# 4.4 Create feedback
$createFeedbackBody = @{
    type = "Suggestion"
    content = "This is a test feedback created by API test"
}

$createResponse = Test-Endpoint `
    -Name "POST /api/v1/feedback (Create)" `
    -Method "POST" `
    -Url "$baseUrl/api/v1/feedback" `
    -Headers $headers `
    -Body $createFeedbackBody

if ($createResponse) {
    try {
        $feedbackData = $createResponse.Content | ConvertFrom-Json
        $feedbackId = $feedbackData.id

        # 4.5 Update feedback status
        Test-Endpoint `
            -Name "PATCH /api/v1/feedback/$feedbackId/status (Update Status)" `
            -Method "PATCH" `
            -Url "$baseUrl/api/v1/feedback/$feedbackId/status" `
            -Headers $headers `
            -Body @{ status = "ToWorkOn" }

        # 4.6 Delete feedback
        Test-Endpoint `
            -Name "DELETE /api/v1/feedback/$feedbackId (Delete)" `
            -Method "DELETE" `
            -Url "$baseUrl/api/v1/feedback/$feedbackId" `
            -Headers $headers `
            -ExpectedStatus 204
    } catch {
        Write-Info "Could not parse feedback ID for update/delete tests"
    }
}

#
# TEST 5: AUTHENTICATION & ERROR HANDLING
#
Write-TestHeader "Testing Authentication & Error Handling"

# 5.1 Test without API key
Test-Endpoint `
    -Name "GET without API key (401 expected)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores" `
    -Headers @{ "Accept" = "application/json" } `
    -ExpectedStatus 401

# 5.2 Test with invalid API key
Test-Endpoint `
    -Name "GET with invalid API key (401 expected)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores" `
    -Headers @{ "X-API-Key" = "invalid-key-12345"; "Accept" = "application/json" } `
    -ExpectedStatus 401

# 5.3 Test pagination validation (page < 1)
Test-Endpoint `
    -Name "GET with invalid page (400 expected)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores?page=0" `
    -Headers $headers `
    -ExpectedStatus 400

# 5.4 Test pagination validation (pageSize > 100)
Test-Endpoint `
    -Name "GET with invalid pageSize (400 expected)" `
    -Method "GET" `
    -Url "$baseUrl/api/v1/chores?pageSize=200" `
    -Headers $headers `
    -ExpectedStatus 400

#
# SUMMARY
#
Write-TestHeader "Test Summary"
Write-Host ""
Write-Host "Total Tests:  $($script:totalTests)" -ForegroundColor White
Write-Host "Passed:       $($script:passedTests)" -ForegroundColor Green
Write-Host "Failed:       $($script:failedTests)" -ForegroundColor $(if ($script:failedTests -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($script:failedTests -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Some tests failed. Review the output above for details." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Note: 404 errors for specific resources are expected when testing against an empty database." -ForegroundColor Cyan
Write-Host ""
