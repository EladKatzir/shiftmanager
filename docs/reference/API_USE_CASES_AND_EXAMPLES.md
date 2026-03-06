# ShiftManager API - Use Cases & Examples

## Table of Contents
- [Getting Started](#getting-started)
- [User Management APIs](#user-management-apis)
- [Shift Management APIs](#shift-management-apis)
- [Time-Off Management APIs](#time-off-management-apis)
- [Notification APIs](#notification-apis)
- [Analytics APIs](#analytics-apis)
- [Audit & Compliance APIs](#audit--compliance-apis)
- [Webhook APIs](#webhook-apis) *(NOT IMPLEMENTED — design only)*
- [Common Patterns](#common-patterns)
- [Error Handling](#error-handling)

---

## Getting Started

### Authentication
All API requests require an API key in the `X-API-Key` header.

```bash
curl -H "X-API-Key: your-api-key-here" \
  https://api.shiftmanager.com/api/v1/users
```

### Base URL
- Production: `https://api.shiftmanager.com`
- Staging: `https://api-staging.shiftmanager.com`
- Development: `http://localhost:5000`

### Common Headers
```http
X-API-Key: your-api-key-here
Content-Type: application/json
Accept: application/json
X-Correlation-ID: optional-trace-id (auto-generated if not provided)
Idempotency-Key: unique-key-for-write-operations (optional but recommended)
```

### Rate Limiting
- Default: 100 requests/minute per API key
- Write operations: 50 requests/minute
- Analytics/Audit: 20-50 requests/minute
- Headers returned:
  - `X-RateLimit-Limit`: Total requests allowed per window
  - `X-RateLimit-Remaining`: Requests remaining in current window
  - `X-RateLimit-Reset`: Unix timestamp when limit resets
  - `Retry-After`: Seconds to wait when rate limited (429 response)

---

## User Management APIs

### 1. List Users (GET /api/v1/users)

#### Use Cases
1. **HR System Sync**: Sync employee roster with external HRIS (Workday, BambooHR)
2. **Mobile App**: Display employee directory in shift management mobile app
3. **Payroll Integration**: Export active employees for payroll processing
4. **Reporting**: Generate employee demographics and role distribution reports

#### Example 1: Get all active employees
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users?isActive=true&page=1&pageSize=50" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

**Response:**
```json
{
  "data": [
    {
      "id": 42,
      "email": "john.doe@example.com",
      "displayName": "John Doe",
      "role": "Employee",
      "isActive": true,
      "department": "Kitchen",
      "jobTitle": "Line Cook"
    },
    {
      "id": 43,
      "email": "jane.smith@example.com",
      "displayName": "Jane Smith",
      "role": "Manager",
      "isActive": true,
      "department": "Front of House",
      "jobTitle": "Shift Manager"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 87,
    "totalPages": 2
  }
}
```

#### Example 2: Search for specific user by email
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users?search=john.doe@example.com" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

#### Example 3: Filter by role (get all managers)
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users?role=Manager&pageSize=100" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

#### Integration Scenario: Daily HR Sync
```python
import requests
import json

API_KEY = "sk_live_1234567890abcdef"
BASE_URL = "https://api.shiftmanager.com/api/v1"

def sync_employees_to_hris():
    """Sync all active employees from ShiftManager to external HRIS"""
    page = 1
    all_employees = []

    while True:
        response = requests.get(
            f"{BASE_URL}/users",
            headers={"X-API-Key": API_KEY},
            params={
                "isActive": True,
                "page": page,
                "pageSize": 100
            }
        )

        if response.status_code != 200:
            print(f"Error: {response.status_code}")
            break

        data = response.json()
        all_employees.extend(data["data"])

        # Check if we've fetched all pages
        if page >= data["pagination"]["totalPages"]:
            break

        page += 1

    print(f"Synced {len(all_employees)} employees")
    return all_employees

# Run daily via cron or scheduled task
employees = sync_employees_to_hris()
```

---

### 2. Get User (GET /api/v1/users/{id})

#### Use Cases
1. **User Profile Display**: Show detailed employee profile in mobile app or dashboard
2. **Emergency Contact Lookup**: Access emergency contact info during incidents
3. **Certification Verification**: Check if employee has required certifications
4. **Integration Webhooks**: Fetch full user details when receiving user-related events

#### Example 1: Get full user profile
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users/42" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

**Response:**
```json
{
  "id": 42,
  "email": "john.doe@example.com",
  "displayName": "John Doe",
  "preferredName": "Johnny",
  "role": "Employee",
  "isActive": true,
  "phone": "+1-555-0123",
  "city": "New York",
  "dateOfBirth": "1990-05-15",
  "department": "Kitchen",
  "jobTitle": "Line Cook",
  "hireDate": "2023-01-15",
  "skills": ["Grill", "Prep", "Cleaning"],
  "certifications": ["Food Safety", "First Aid"],
  "emergencyContact": {
    "name": "Jane Doe",
    "phone": "+1-555-0199",
    "relation": "Spouse"
  },
  "avatarUrl": "/avatars/1/john-doe.jpg",
  "profileLastUpdated": "2025-01-10T14:30:00Z"
}
```

#### Example 2: Handle 404 Not Found
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users/99999" \
  -H "X-API-Key": sk_live_1234567890abcdef"
```

**Error Response:**
```json
{
  "type": "about:blank",
  "title": "Not Found",
  "status": 404,
  "detail": "User with ID 99999 not found or not accessible in your company",
  "instance": "/api/v1/users/99999"
}
```

#### Integration Scenario: Emergency Contact Lookup
```javascript
// React component for emergency contact display
async function getEmergencyContact(userId) {
  const response = await fetch(`${API_BASE}/users/${userId}`, {
    headers: {
      'X-API-Key': process.env.REACT_APP_API_KEY
    }
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch user: ${response.status}`);
  }

  const user = await response.json();

  if (user.emergencyContact) {
    return {
      name: user.emergencyContact.name,
      phone: user.emergencyContact.phone,
      relation: user.emergencyContact.relation,
      userDisplayName: user.displayName
    };
  }

  return null;
}

// Usage in emergency alert system
const contact = await getEmergencyContact(42);
if (contact) {
  console.log(`Emergency Contact: ${contact.name} (${contact.relation}) - ${contact.phone}`);
}
```

---

### 3. Create User (POST /api/v1/users)

#### Use Cases
1. **Automated Onboarding**: Create user accounts when new hires are added to HRIS
2. **Bulk Import**: Migrate employees from legacy systems
3. **Self-Service Signup**: Allow managers to provision accounts via portal
4. **Franchise Expansion**: Automatically create accounts for new location staff

#### Example 1: Create new employee
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/users" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: onboard-emp-12345" \
  -d '{
    "email": "alice.johnson@example.com",
    "displayName": "Alice Johnson",
    "role": "Employee",
    "department": "Front of House",
    "jobTitle": "Server",
    "hireDate": "2025-02-01",
    "sendWelcomeEmail": true
  }'
```

**Success Response (201 Created):**
```json
{
  "id": 88,
  "email": "alice.johnson@example.com",
  "displayName": "Alice Johnson",
  "role": "Employee",
  "isActive": true
}
```
*Note: temporaryPassword omitted because sendWelcomeEmail=true*

#### Example 2: Create without email (get temporary password)
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/users" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "bob.wilson@example.com",
    "displayName": "Bob Wilson",
    "role": "Trainee",
    "department": "Kitchen",
    "jobTitle": "Kitchen Assistant",
    "sendWelcomeEmail": false
  }'
```

**Response:**
```json
{
  "id": 89,
  "email": "bob.wilson@example.com",
  "displayName": "Bob Wilson",
  "role": "Trainee",
  "isActive": true,
  "temporaryPassword": "xK9mP2nQ7vL4wR3j"
}
```

#### Example 3: Validation Error
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/users" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "invalid-email",
    "displayName": "Test User",
    "role": "InvalidRole"
  }'
```

**Error Response (400 Bad Request):**
```json
{
  "type": "about:blank",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred",
  "instance": "/api/v1/users",
  "errors": {
    "email": ["Email must be a valid email address"],
    "role": ["Role must be one of: Manager, Employee, Trainee"]
  }
}
```

#### Integration Scenario: HRIS Webhook Handler
```python
# Flask webhook endpoint to create users when hired in external HRIS
from flask import Flask, request, jsonify
import requests
import hmac
import hashlib

app = Flask(__name__)
SHIFTMANAGER_API_KEY = "sk_live_1234567890abcdef"
SHIFTMANAGER_API_URL = "https://api.shiftmanager.com/api/v1"
WEBHOOK_SECRET = "your-webhook-secret"

@app.route('/webhooks/hris/employee-hired', methods=['POST'])
def handle_employee_hired():
    # Verify webhook signature
    signature = request.headers.get('X-Webhook-Signature')
    if not verify_signature(request.data, signature):
        return jsonify({"error": "Invalid signature"}), 401

    # Parse webhook payload
    data = request.json
    employee = data['employee']

    # Create user in ShiftManager
    response = requests.post(
        f"{SHIFTMANAGER_API_URL}/users",
        headers={
            "X-API-Key": SHIFTMANAGER_API_KEY,
            "Content-Type": "application/json",
            "Idempotency-Key": f"hris-import-{employee['id']}"
        },
        json={
            "email": employee['email'],
            "displayName": employee['full_name'],
            "role": map_role(employee['position']),
            "department": employee['department'],
            "jobTitle": employee['position'],
            "hireDate": employee['start_date'],
            "sendWelcomeEmail": True
        }
    )

    if response.status_code == 201:
        user = response.json()
        print(f"Created ShiftManager user: {user['id']}")
        return jsonify({"success": True, "userId": user['id']}), 200
    elif response.status_code == 409:
        # User already exists (idempotency worked)
        return jsonify({"success": True, "message": "User already exists"}), 200
    else:
        error = response.json()
        print(f"Error creating user: {error}")
        return jsonify({"error": error['detail']}), response.status_code

def verify_signature(payload, signature):
    expected = hmac.new(
        WEBHOOK_SECRET.encode(),
        payload,
        hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature)

def map_role(position):
    # Map HRIS positions to ShiftManager roles
    manager_titles = ['Manager', 'Supervisor', 'Lead']
    if any(title in position for title in manager_titles):
        return 'Manager'
    elif 'Trainee' in position or 'Intern' in position:
        return 'Trainee'
    else:
        return 'Employee'

if __name__ == '__main__':
    app.run(port=8080)
```

---

### 4. Update User (PATCH /api/v1/users/{id})

#### Use Cases
1. **Profile Sync**: Update employee details when changed in HRIS
2. **Promotion**: Update role when employee is promoted
3. **Department Transfer**: Update department and job title
4. **Contact Update**: Update phone numbers and emergency contacts

#### Example 1: Promote employee to manager
```bash
curl -X PATCH "https://api.shiftmanager.com/api/v1/users/42" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: promote-42-manager" \
  -d '[
    { "op": "replace", "path": "/role", "value": "Manager" },
    { "op": "replace", "path": "/jobTitle", "value": "Shift Manager" }
  ]'
```

**Response:**
```json
{
  "id": 42,
  "email": "john.doe@example.com",
  "displayName": "John Doe",
  "role": "Manager",
  "department": "Kitchen",
  "jobTitle": "Shift Manager",
  "profileLastUpdated": "2025-01-15T10:30:00Z"
}
```

#### Example 2: Update contact information
```bash
curl -X PATCH "https://api.shiftmanager.com/api/v1/users/42" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '[
    { "op": "replace", "path": "/phone", "value": "+1-555-9999" },
    { "op": "replace", "path": "/city", "value": "Brooklyn" }
  ]'
```

#### Example 3: Transfer to different department
```bash
curl -X PATCH "https://api.shiftmanager.com/api/v1/users/88" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '[
    { "op": "replace", "path": "/department", "value": "Back of House" },
    { "op": "replace", "path": "/jobTitle", "value": "Prep Cook" }
  ]'
```

#### Integration Scenario: Department Transfer Workflow
```typescript
// TypeScript function for automated department transfers
interface DepartmentTransfer {
  userId: number;
  newDepartment: string;
  newJobTitle: string;
  effectiveDate: string;
}

async function processDepartmentTransfer(transfer: DepartmentTransfer) {
  const apiKey = process.env.SHIFTMANAGER_API_KEY!;
  const baseUrl = 'https://api.shiftmanager.com/api/v1';

  // Step 1: Fetch current user details
  const userResponse = await fetch(`${baseUrl}/users/${transfer.userId}`, {
    headers: { 'X-API-Key': apiKey }
  });

  if (!userResponse.ok) {
    throw new Error(`User not found: ${transfer.userId}`);
  }

  const currentUser = await userResponse.json();
  console.log(`Transferring ${currentUser.displayName} from ${currentUser.department} to ${transfer.newDepartment}`);

  // Step 2: Update user profile
  const updateResponse = await fetch(`${baseUrl}/users/${transfer.userId}`, {
    method: 'PATCH',
    headers: {
      'X-API-Key': apiKey,
      'Content-Type': 'application/json',
      'Idempotency-Key': `transfer-${transfer.userId}-${transfer.effectiveDate}`
    },
    body: JSON.stringify([
      { op: 'replace', path: '/department', value: transfer.newDepartment },
      { op: 'replace', path: '/jobTitle', value: transfer.newJobTitle }
    ])
  });

  if (!updateResponse.ok) {
    const error = await updateResponse.json();
    throw new Error(`Update failed: ${error.detail}`);
  }

  const updatedUser = await updateResponse.json();
  console.log(`Transfer complete: ${updatedUser.displayName} is now ${updatedUser.jobTitle} in ${updatedUser.department}`);

  return updatedUser;
}

// Usage
const transfer: DepartmentTransfer = {
  userId: 42,
  newDepartment: 'Management',
  newJobTitle: 'Assistant Manager',
  effectiveDate: '2025-02-01'
};

processDepartmentTransfer(transfer)
  .then(user => console.log('Success:', user))
  .catch(error => console.error('Error:', error));
```

---

## Shift Management APIs

### 5. List Shifts (GET /api/v1/shifts)

#### Use Cases
1. **Mobile Schedule Display**: Show upcoming shifts in employee mobile app
2. **Workforce Analytics**: Analyze shift coverage and staffing patterns
3. **External Scheduling Tools**: Sync shifts with Shiftboard, When I Work, etc.
4. **Payroll Integration**: Export worked shifts for payroll processing

#### Example 1: Get next 7 days of shifts
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/shifts?startDate=2025-01-15&endDate=2025-01-22&page=1&pageSize=100" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

**Response:**
```json
{
  "data": [
    {
      "shiftInstanceId": 1234,
      "date": "2025-01-15",
      "shiftType": {
        "id": 5,
        "key": "MORNING",
        "name": "Morning Shift",
        "startTime": "08:00",
        "endTime": "16:00"
      },
      "staffingRequired": 3,
      "staffingAssigned": 3,
      "assignments": [
        {
          "assignmentId": 567,
          "userId": 42,
          "user": {
            "id": 42,
            "displayName": "John Doe",
            "role": "Employee"
          },
          "traineeUserId": null,
          "trainee": null
        },
        {
          "assignmentId": 568,
          "userId": 43,
          "user": {
            "id": 43,
            "displayName": "Jane Smith",
            "role": "Manager"
          },
          "traineeUserId": null,
          "trainee": null
        },
        {
          "assignmentId": 569,
          "userId": 88,
          "user": {
            "id": 88,
            "displayName": "Alice Johnson",
            "role": "Employee"
          },
          "traineeUserId": 89,
          "trainee": {
            "id": 89,
            "displayName": "Bob Wilson"
          }
        }
      ]
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 100,
    "totalCount": 42,
    "totalPages": 1
  }
}
```

#### Example 2: Get only shifts with open slots
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/shifts?startDate=2025-01-15&endDate=2025-01-22&includeUnassigned=true" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

#### Example 3: Large date range (90 days max)
```bash
curl -X GET "https://api.shiftmanager.com/api/v1/shifts?startDate=2025-01-01&endDate=2025-03-31&pageSize=200" \
  -H "X-API-Key: sk_live_1234567890abcdef"
```

**Error Response (400 Bad Request):**
```json
{
  "type": "about:blank",
  "title": "Bad Request",
  "status": 400,
  "detail": "Date range cannot exceed 90 days",
  "instance": "/api/v1/shifts"
}
```

#### Integration Scenario: Employee Mobile App Schedule View
```swift
// Swift iOS app to display employee's upcoming shifts
import Foundation

struct Shift: Codable {
    let shiftInstanceId: Int
    let date: String
    let shiftType: ShiftType
    let staffingRequired: Int
    let staffingAssigned: Int
    let assignments: [Assignment]
}

struct ShiftType: Codable {
    let id: Int
    let key: String
    let name: String
    let startTime: String
    let endTime: String
}

struct Assignment: Codable {
    let assignmentId: Int
    let userId: Int?
    let user: User?
}

struct User: Codable {
    let id: Int
    let displayName: String
    let role: String
}

struct ShiftResponse: Codable {
    let data: [Shift]
    let pagination: Pagination
}

struct Pagination: Codable {
    let page: Int
    let pageSize: Int
    let totalCount: Int
    let totalPages: Int
}

class ShiftManager {
    let apiKey = "sk_live_1234567890abcdef"
    let baseURL = "https://api.shiftmanager.com/api/v1"

    func fetchMyShifts(userId: Int, completion: @escaping ([Shift]?, Error?) -> Void) {
        // Calculate date range (today + next 14 days)
        let today = Date()
        let twoWeeksLater = Calendar.current.date(byAdding: .day, value: 14, to: today)!

        let dateFormatter = DateFormatter()
        dateFormatter.dateFormat = "yyyy-MM-dd"

        let startDate = dateFormatter.string(from: today)
        let endDate = dateFormatter.string(from: twoWeeksLater)

        // Build URL
        let urlString = "\(baseURL)/shifts?startDate=\(startDate)&endDate=\(endDate)&pageSize=100"
        guard let url = URL(string: urlString) else {
            completion(nil, NSError(domain: "Invalid URL", code: -1))
            return
        }

        // Create request
        var request = URLRequest(url: url)
        request.setValue(apiKey, forHTTPHeaderField: "X-API-Key")

        // Execute request
        URLSession.shared.dataTask(with: request) { data, response, error in
            if let error = error {
                completion(nil, error)
                return
            }

            guard let data = data else {
                completion(nil, NSError(domain: "No data", code: -1))
                return
            }

            do {
                let decoder = JSONDecoder()
                let shiftResponse = try decoder.decode(ShiftResponse.self, from: data)

                // Filter shifts where user is assigned
                let myShifts = shiftResponse.data.filter { shift in
                    shift.assignments.contains { assignment in
                        assignment.userId == userId
                    }
                }

                completion(myShifts, nil)
            } catch {
                completion(nil, error)
            }
        }.resume()
    }
}

// Usage in SwiftUI View
let shiftManager = ShiftManager()
shiftManager.fetchMyShifts(userId: 42) { shifts, error in
    if let error = error {
        print("Error fetching shifts: \(error)")
        return
    }

    if let shifts = shifts {
        print("You have \(shifts.count) upcoming shifts")
        for shift in shifts {
            print("- \(shift.date): \(shift.shiftType.name) (\(shift.shiftType.startTime)-\(shift.shiftType.endTime))")
        }
    }
}
```

---

## Time-Off Management APIs

### 6. Create Time-Off Request (POST /api/v1/time-off-requests)

#### Use Cases
1. **Mobile Self-Service**: Employees submit time-off requests via mobile app
2. **PTO Integration**: Create time-off records when approved in external PTO system
3. **Vacation Planner**: Bulk create time-off for scheduled vacations
4. **Conflict Prevention**: Check for shift conflicts before requesting time off

#### Example 1: Submit simple time-off request
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/time-off-requests" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: timeoff-42-2025-vacation" \
  -d '{
    "userId": 42,
    "startDate": "2025-07-01",
    "endDate": "2025-07-07",
    "reason": "Family vacation"
  }'
```

**Success Response (201 Created):**
```json
{
  "id": 123,
  "userId": 42,
  "startDate": "2025-07-01",
  "endDate": "2025-07-07",
  "reason": "Family vacation",
  "status": "Pending",
  "createdAt": "2025-01-15T10:00:00Z"
}
```

#### Example 2: Conflict detection error
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/time-off-requests" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 42,
    "startDate": "2025-01-20",
    "endDate": "2025-01-22",
    "reason": "Personal"
  }'
```

**Error Response (409 Conflict):**
```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "User has assigned shifts during this period. Conflicts: 2025-01-20 (Morning Shift), 2025-01-22 (Night Shift)",
  "instance": "/api/v1/time-off-requests",
  "conflicts": [
    {
      "date": "2025-01-20",
      "shiftType": "Morning Shift",
      "startTime": "08:00",
      "endTime": "16:00"
    },
    {
      "date": "2025-01-22",
      "shiftType": "Night Shift",
      "startTime": "22:00",
      "endTime": "06:00"
    }
  ]
}
```

#### Integration Scenario: Mobile Time-Off Request with Conflict Check
```javascript
// React Native mobile app time-off request flow
import React, { useState } from 'react';
import { View, TextInput, Button, Text, Alert } from 'react-native';
import DatePicker from 'react-native-date-picker';

const TimeOffRequestScreen = ({ userId, apiKey }) => {
  const [startDate, setStartDate] = useState(new Date());
  const [endDate, setEndDate] = useState(new Date());
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(false);
  const [conflicts, setConflicts] = useState([]);

  const checkConflicts = async () => {
    // First, fetch shifts for the date range
    const start = startDate.toISOString().split('T')[0];
    const end = endDate.toISOString().split('T')[0];

    try {
      const response = await fetch(
        `https://api.shiftmanager.com/api/v1/shifts?startDate=${start}&endDate=${end}`,
        {
          headers: { 'X-API-Key': apiKey }
        }
      );

      if (!response.ok) {
        throw new Error('Failed to fetch shifts');
      }

      const data = await response.json();

      // Check if user has any shifts in this period
      const userShifts = data.data.filter(shift =>
        shift.assignments.some(assignment => assignment.userId === userId)
      );

      if (userShifts.length > 0) {
        setConflicts(userShifts);
        Alert.alert(
          'Shift Conflicts Detected',
          `You have ${userShifts.length} assigned shift(s) during this period. Do you want to proceed anyway?`,
          [
            { text: 'Cancel', style: 'cancel' },
            { text: 'Submit Request', onPress: submitRequest }
          ]
        );
        return false;
      }

      return true;
    } catch (error) {
      console.error('Error checking conflicts:', error);
      Alert.alert('Error', 'Failed to check for shift conflicts');
      return false;
    }
  };

  const submitRequest = async () => {
    setLoading(true);

    try {
      const response = await fetch(
        'https://api.shiftmanager.com/api/v1/time-off-requests',
        {
          method: 'POST',
          headers: {
            'X-API-Key': apiKey,
            'Content-Type': 'application/json',
            'Idempotency-Key': `timeoff-${userId}-${Date.now()}`
          },
          body: JSON.stringify({
            userId,
            startDate: startDate.toISOString().split('T')[0],
            endDate: endDate.toISOString().split('T')[0],
            reason
          })
        }
      );

      if (response.status === 201) {
        const result = await response.json();
        Alert.alert(
          'Success',
          `Time-off request submitted! Status: ${result.status}`,
          [{ text: 'OK', onPress: () => navigation.goBack() }]
        );
      } else if (response.status === 409) {
        const error = await response.json();
        Alert.alert('Conflict', error.detail);
        if (error.conflicts) {
          setConflicts(error.conflicts);
        }
      } else {
        const error = await response.json();
        Alert.alert('Error', error.detail || 'Failed to submit request');
      }
    } catch (error) {
      console.error('Error submitting request:', error);
      Alert.alert('Error', 'Failed to submit time-off request');
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async () => {
    if (!reason.trim()) {
      Alert.alert('Validation Error', 'Please provide a reason for time off');
      return;
    }

    if (endDate < startDate) {
      Alert.alert('Validation Error', 'End date must be after start date');
      return;
    }

    // Check for conflicts first
    const noConflicts = await checkConflicts();
    if (noConflicts) {
      await submitRequest();
    }
  };

  return (
    <View style={{ padding: 20 }}>
      <Text>Request Time Off</Text>

      <Text>Start Date:</Text>
      <DatePicker
        date={startDate}
        onDateChange={setStartDate}
        mode="date"
      />

      <Text>End Date:</Text>
      <DatePicker
        date={endDate}
        onDateChange={setEndDate}
        mode="date"
      />

      <TextInput
        placeholder="Reason for time off"
        value={reason}
        onChangeText={setReason}
        multiline
        numberOfLines={4}
        style={{ borderWidth: 1, padding: 10, marginVertical: 10 }}
      />

      {conflicts.length > 0 && (
        <View style={{ backgroundColor: '#fff3cd', padding: 10, marginBottom: 10 }}>
          <Text style={{ fontWeight: 'bold' }}>⚠️ Shift Conflicts:</Text>
          {conflicts.map((shift, index) => (
            <Text key={index}>
              • {shift.date}: {shift.shiftType.name} ({shift.shiftType.startTime}-{shift.shiftType.endTime})
            </Text>
          ))}
        </View>
      )}

      <Button
        title={loading ? 'Submitting...' : 'Submit Request'}
        onPress={handleSubmit}
        disabled={loading}
      />
    </View>
  );
};

export default TimeOffRequestScreen;
```

---

## Webhook APIs

> **WARNING: NOT IMPLEMENTED** — The webhook API described below is a design document only.
> As of March 2026, ShiftManager does **not** have webhook endpoints. Do not rely on these examples.
> This section is retained for future reference.

### 7. Register Webhook (POST /api/v1/webhooks)

#### Use Cases
1. **Real-Time Notifications**: Get instant alerts when shifts are assigned/removed
2. **External System Sync**: Auto-sync data with external workforce management tools
3. **Custom Alerts**: Trigger custom workflows when specific events occur
4. **Audit Logging**: Send all events to external SIEM for compliance

#### Example 1: Register webhook for shift events
```bash
curl -X POST "https://api.shiftmanager.com/api/v1/webhooks" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://myapp.example.com/webhooks/shiftmanager",
    "events": [
      "shift.assigned",
      "shift.removed",
      "timeoff.approved",
      "timeoff.declined"
    ],
    "description": "Production workforce sync"
  }'
```

**Success Response (201 Created):**
```json
{
  "id": 1,
  "url": "https://myapp.example.com/webhooks/shiftmanager",
  "events": [
    "shift.assigned",
    "shift.removed",
    "timeoff.approved",
    "timeoff.declined"
  ],
  "secret": "whsec_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6",
  "isActive": true,
  "createdAt": "2025-01-15T10:00:00Z"
}
```

**⚠️ IMPORTANT:** Save the `secret` value securely - it's only returned once at creation!

#### Example 2: Webhook event payload (what you'll receive)
When a shift is assigned, ShiftManager will POST to your webhook URL:

```http
POST /webhooks/shiftmanager HTTP/1.1
Host: myapp.example.com
Content-Type: application/json
X-Webhook-Signature: sha256=8b03d8e1...
X-Webhook-Event: shift.assigned
X-Webhook-ID: evt_9f8e7d6c5b4a3210

{
  "id": "evt_9f8e7d6c5b4a3210",
  "event": "shift.assigned",
  "timestamp": "2025-01-15T14:30:00Z",
  "companyId": 1,
  "data": {
    "shiftInstanceId": 1234,
    "assignmentId": 567,
    "userId": 42,
    "user": {
      "id": 42,
      "displayName": "John Doe",
      "email": "john.doe@example.com"
    },
    "date": "2025-01-20",
    "shiftType": {
      "key": "MORNING",
      "name": "Morning Shift",
      "startTime": "08:00",
      "endTime": "16:00"
    }
  }
}
```

#### Integration Scenario: Webhook Receiver with Signature Verification
```python
# Flask webhook receiver with HMAC signature verification
from flask import Flask, request, jsonify
import hmac
import hashlib
import json

app = Flask(__name__)

# The secret you received when registering the webhook
WEBHOOK_SECRET = "whsec_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"

@app.route('/webhooks/shiftmanager', methods=['POST'])
def handle_webhook():
    # Step 1: Verify webhook signature (CRITICAL for security!)
    signature = request.headers.get('X-Webhook-Signature', '')
    if not verify_signature(request.data, signature):
        print("❌ Invalid webhook signature - possible spoofing attempt!")
        return jsonify({"error": "Invalid signature"}), 401

    # Step 2: Parse webhook data
    event_id = request.headers.get('X-Webhook-ID')
    event_type = request.headers.get('X-Webhook-Event')
    data = request.json

    print(f"✅ Received webhook: {event_type} (ID: {event_id})")

    # Step 3: Handle event based on type
    if event_type == 'shift.assigned':
        handle_shift_assigned(data['data'])
    elif event_type == 'shift.removed':
        handle_shift_removed(data['data'])
    elif event_type == 'timeoff.approved':
        handle_timeoff_approved(data['data'])
    elif event_type == 'timeoff.declined':
        handle_timeoff_declined(data['data'])
    else:
        print(f"⚠️  Unknown event type: {event_type}")

    # Step 4: Return 200 OK immediately (acknowledge receipt)
    # Don't do slow processing here - queue it for background job
    return jsonify({"received": True, "eventId": event_id}), 200

def verify_signature(payload, signature_header):
    """Verify HMAC-SHA256 signature"""
    if not signature_header.startswith('sha256='):
        return False

    provided_sig = signature_header.replace('sha256=', '')

    expected_sig = hmac.new(
        WEBHOOK_SECRET.encode('utf-8'),
        payload,
        hashlib.sha256
    ).hexdigest()

    return hmac.compare_digest(expected_sig, provided_sig)

def handle_shift_assigned(data):
    """Handle shift.assigned event"""
    user = data['user']
    shift_type = data['shiftType']
    date = data['date']

    print(f"📅 {user['displayName']} assigned to {shift_type['name']} on {date}")

    # Example: Send SMS notification
    send_sms(
        to=user['phone'],
        message=f"You've been assigned to {shift_type['name']} on {date} ({shift_type['startTime']}-{shift_type['endTime']})"
    )

    # Example: Update external calendar
    sync_to_google_calendar(user['id'], date, shift_type)

    # Example: Log to database
    log_shift_assignment(data)

def handle_shift_removed(data):
    """Handle shift.removed event"""
    print(f"🗑️  Shift removed: {data}")
    # Handle shift removal logic

def handle_timeoff_approved(data):
    """Handle timeoff.approved event"""
    print(f"✅ Time-off approved: {data}")
    # Send approval notification

def handle_timeoff_declined(data):
    """Handle timeoff.declined event"""
    print(f"❌ Time-off declined: {data}")
    # Send decline notification

def send_sms(to, message):
    # Implement SMS sending (Twilio, etc.)
    pass

def sync_to_google_calendar(user_id, date, shift_type):
    # Implement Google Calendar sync
    pass

def log_shift_assignment(data):
    # Log to database
    pass

if __name__ == '__main__':
    # Run with HTTPS in production!
    app.run(host='0.0.0.0', port=443, ssl_context='adhoc')
```

---

## Common Patterns

### Pagination
All list endpoints support pagination:

```bash
# Page through results
curl "https://api.shiftmanager.com/api/v1/users?page=1&pageSize=50"
curl "https://api.shiftmanager.com/api/v1/users?page=2&pageSize=50"

# Calculate total pages
# total_pages = ceil(total_count / page_size)
```

### Filtering
```bash
# Filter users by role
curl "https://api.shiftmanager.com/api/v1/users?role=Manager"

# Filter by active status
curl "https://api.shiftmanager.com/api/v1/users?isActive=true"

# Search by name/email
curl "https://api.shiftmanager.com/api/v1/users?search=john"

# Combine filters
curl "https://api.shiftmanager.com/api/v1/users?role=Employee&isActive=true&department=Kitchen"
```

### Idempotency
Use `Idempotency-Key` header for safe retries on write operations:

```bash
curl -X POST "https://api.shiftmanager.com/api/v1/users" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "Idempotency-Key: unique-operation-id-12345" \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","displayName":"Test User","role":"Employee"}'

# If request fails, retry with same Idempotency-Key
# Second request will return existing resource (not create duplicate)
```

### Request Tracing
Use `X-Correlation-ID` for end-to-end request tracing:

```bash
curl -X GET "https://api.shiftmanager.com/api/v1/users/42" \
  -H "X-API-Key: sk_live_1234567890abcdef" \
  -H "X-Correlation-ID: trace-123-abc-456"

# Same correlation ID will appear in:
# - Response headers
# - Server logs
# - Audit logs
# - Error messages
```

---

## Error Handling

All errors follow RFC-7807 Problem Details format:

### 400 Bad Request (Validation Error)
```json
{
  "type": "about:blank",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred",
  "instance": "/api/v1/users",
  "errors": {
    "email": ["Email is required", "Email must be valid"],
    "role": ["Role must be one of: Manager, Employee, Trainee"]
  }
}
```

### 401 Unauthorized (Invalid API Key)
```json
{
  "type": "about:blank",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Invalid or missing API key",
  "instance": "/api/v1/users"
}
```

### 403 Forbidden (Insufficient Permissions)
```json
{
  "type": "about:blank",
  "title": "Forbidden",
  "status": 403,
  "detail": "API key does not have 'user:write' scope",
  "instance": "/api/v1/users"
}
```

### 404 Not Found
```json
{
  "type": "about:blank",
  "title": "Not Found",
  "status": 404,
  "detail": "User with ID 99999 not found or not accessible in your company",
  "instance": "/api/v1/users/99999"
}
```

### 409 Conflict
```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "User with email 'john@example.com' already exists",
  "instance": "/api/v1/users"
}
```

### 429 Too Many Requests (Rate Limit)
```json
{
  "type": "about:blank",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Retry after 60 seconds.",
  "instance": "/api/v1/users",
  "retryAfter": 60
}
```

Response headers:
```http
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1705329600
Retry-After: 60
```

### 500 Internal Server Error
```json
{
  "type": "about:blank",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "An unexpected error occurred. Please contact support with correlation ID: trace-123-abc-456",
  "instance": "/api/v1/users"
}
```

---

## Best Practices

### 1. Always Verify Webhook Signatures
```python
def verify_webhook(payload, signature, secret):
    expected = hmac.new(secret.encode(), payload, hashlib.sha256).hexdigest()
    return hmac.compare_digest(f"sha256={expected}", signature)
```

### 2. Use Idempotency Keys for Write Operations
```bash
# Generate unique key per operation
IDEMPOTENCY_KEY="create-user-$(uuidgen)"

curl -X POST "https://api.shiftmanager.com/api/v1/users" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  ...
```

### 3. Handle Rate Limits Gracefully
```python
import time

def api_request_with_retry(url, headers, max_retries=3):
    for attempt in range(max_retries):
        response = requests.get(url, headers=headers)

        if response.status_code == 429:
            retry_after = int(response.headers.get('Retry-After', 60))
            print(f"Rate limited. Waiting {retry_after} seconds...")
            time.sleep(retry_after)
            continue

        return response

    raise Exception("Max retries exceeded")
```

### 4. Paginate Large Result Sets
```python
def fetch_all_users(api_key):
    all_users = []
    page = 1

    while True:
        response = requests.get(
            f"{BASE_URL}/users?page={page}&pageSize=100",
            headers={"X-API-Key": api_key}
        )

        data = response.json()
        all_users.extend(data["data"])

        if page >= data["pagination"]["totalPages"]:
            break

        page += 1

    return all_users
```

### 5. Log Correlation IDs for Debugging
```javascript
const correlationId = `trace-${Date.now()}-${Math.random().toString(36)}`;

fetch(`${API_URL}/users`, {
  headers: {
    'X-API-Key': apiKey,
    'X-Correlation-ID': correlationId
  }
})
.then(response => {
  console.log(`Request ${correlationId}: ${response.status}`);
  return response.json();
})
.catch(error => {
  console.error(`Request ${correlationId} failed:`, error);
});
```

---

## Support & Resources

- **API Status**: https://status.shiftmanager.com
- **Developer Portal**: https://developers.shiftmanager.com
- **Support Email**: api-support@shiftmanager.com
- **Rate Limit Issues**: Contact support to request limit increase
- **Bug Reports**: https://github.com/shiftmanager/api-issues

---

**Document Version:** 1.0
**Last Updated:** 2025-01-15
**API Version:** v1
