# APIs for the Non-Tech: Complete Beginner's Guide

This guide explains how to use the ShiftManager APIs from scratch, even if you've never worked with APIs or written code before.

---

## Table of Contents

1. [What is an API?](#what-is-an-api)
2. [What You Need to Get Started](#what-you-need-to-get-started)
3. [Understanding the Basics](#understanding-the-basics)
4. [Step-by-Step: Your First API Request](#step-by-step-your-first-api-request)
5. [Available APIs](#available-apis)
6. [Common Scenarios with Examples](#common-scenarios-with-examples)
7. [Troubleshooting](#troubleshooting)

---

## What is an API?

### Non-Technical Explanation
Think of an API as a waiter in a restaurant:
- **You (the customer)** tell the waiter what you want
- **The waiter** takes your order to the kitchen
- **The kitchen** prepares your food
- **The waiter** brings your food back to you

In the digital world:
- **You (your program/app)** ask the API for something
- **The API** processes your request
- **The API** sends back the information you asked for

### Technical Explanation
An API (Application Programming Interface) is a set of rules and endpoints that allow different software applications to communicate with each other. You send HTTP requests to specific URLs (endpoints), and the API responds with data (usually in JSON format).

---

## What You Need to Get Started

### 1. A Tool to Send API Requests

**Non-Technical:** You need a special program that can "talk" to the API. Think of it like a phone to make calls.

**Technical:** You need an HTTP client. We recommend:
- **Postman** (easiest, with a visual interface) - Download from https://www.postman.com/downloads/
- **cURL** (command-line tool, pre-installed on Mac/Linux)
- **PowerShell** (Windows built-in)

For this guide, we'll use **Postman** because it's the most beginner-friendly.

### 2. The ShiftManager Application Running

**Non-Technical:** The ShiftManager program needs to be turned on and running on a computer, like turning on a light before you can use it.

**Technical:** You need the ShiftManager application running locally or on a server. By default, it runs at:
```
http://localhost:5000
```

To start it:
```bash
cd /path/to/ShiftManager
dotnet run
```

### 3. An API Key

**Non-Technical:** An API key is like a password that proves you're allowed to use the API. It keeps unauthorized people out.

**Technical:** The ShiftManager API uses API key authentication. You'll need to:
1. Ask your system administrator for an API key
2. Include this key in the `X-API-Key` header of every request

---

## Understanding the Basics

### HTTP Methods (Verbs)

**Non-Technical:** These are different types of actions you can perform:
- **GET** = "Show me something" (like reading a book)
- **POST** = "Create something new" or "Do an action" (like writing a new page)
- **PUT** = "Update something completely" (like rewriting a whole page)
- **DELETE** = "Remove something" (like tearing out a page)

**Technical:** HTTP methods define the operation:
- **GET** - Retrieve data (safe, idempotent)
- **POST** - Create resources or trigger actions (not idempotent)
- **PUT** - Update resources (idempotent)
- **DELETE** - Remove resources (idempotent)

### Endpoints (URLs)

**Non-Technical:** An endpoint is the specific "address" where you send your request. Like different phone numbers for different departments.

**Technical:** Endpoints are URL paths that map to specific API operations:
```
http://localhost:5000/api/v1/users
http://localhost:5000/api/v1/shifts
```

### Request Headers

**Non-Technical:** Headers are extra pieces of information you send along with your request, like putting a return address on an envelope.

**Technical:** Headers provide metadata about the request:
```
X-API-Key: your-api-key-here
Content-Type: application/json
```

### Request Body (for POST/PUT)

**Non-Technical:** The body is the actual information you're sending, like the letter inside an envelope.

**Technical:** The request body contains the data payload, typically in JSON format:
```json
{
  "email": "user@example.com",
  "displayName": "John Doe"
}
```

### Response Status Codes

**Non-Technical:** Status codes tell you if your request worked or what went wrong:
- **200** = Success! Everything worked
- **400** = You made a mistake in your request
- **401** = Your API key is wrong or missing
- **403** = You're not allowed to do this
- **404** = What you're looking for doesn't exist
- **429** = You're sending too many requests too fast
- **500** = The server had an error (not your fault)

**Technical:** HTTP status codes indicate the result:
- **2xx** - Success (200 OK, 201 Created)
- **4xx** - Client errors (400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found)
- **5xx** - Server errors (500 Internal Server Error)

---

## Step-by-Step: Your First API Request

### Step 1: Install Postman

**Non-Technical:**
1. Go to https://www.postman.com/downloads/
2. Download Postman for your operating system
3. Install it like any other program
4. Open Postman

**Technical:**
```bash
# macOS with Homebrew
brew install --cask postman

# Or download directly from https://www.postman.com/downloads/
```

### Step 2: Start ShiftManager

**Non-Technical:**
1. Open a command prompt or terminal
2. Navigate to the ShiftManager folder
3. Type the command to start it
4. Wait until you see "Now listening on: http://localhost:5000"

**Technical:**
```bash
cd C:\Users\katzi\Downloads\ShiftManager
dotnet run
```

You should see output like:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

### Step 3: Get Your API Key

**Non-Technical:**
Contact your system administrator and ask: "Can I have an API key for ShiftManager?"

They'll give you something that looks like a long random string:
```
sk_live_abc123def456ghi789jkl012mno345pqr678stu901vwx234yz
```

**Technical:**
API keys are generated by the system administrator. For development/testing, you can use a test key if configured in the application. Store your API key securely (never commit to version control).

### Step 4: Make Your First Request in Postman

**Non-Technical Step-by-Step:**

1. **Open Postman** - You'll see a blank workspace

2. **Create a new request:**
   - Click the "+" button or "New" → "HTTP Request"

3. **Set the method to GET:**
   - Look for a dropdown that says "GET" (it might already be selected)

4. **Enter the URL:**
   ```
   http://localhost:5000/api/v1/users
   ```

5. **Add your API key:**
   - Click on the "Headers" tab (below the URL bar)
   - In the "Key" column, type: `X-API-Key`
   - In the "Value" column, paste your API key

6. **Send the request:**
   - Click the big blue "Send" button on the right

7. **Look at the response:**
   - Below your request, you'll see the response appear
   - A green "200 OK" means it worked!
   - You'll see JSON data with a list of users

**Technical:**
```http
GET http://localhost:5000/api/v1/users
Headers:
  X-API-Key: your-api-key-here
  Content-Type: application/json
```

Expected response:
```json
{
  "data": [
    {
      "id": 1,
      "email": "user@example.com",
      "displayName": "John Doe",
      "role": "Employee",
      "isActive": true
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

---

## Available APIs

All APIs are disabled by default and must be enabled in `appsettings.json`. Contact your administrator to enable specific endpoints.

### 1. Users API

**What it does:** Manage employee/user information

#### List All Users
**Non-Technical:** Get a list of all employees in the system

**Endpoint:** `GET /api/v1/users`

**Query Parameters (optional filters):**
- `page` - Which page of results (default: 1)
- `pageSize` - How many results per page (default: 50, max: 100)
- `role` - Filter by role (Employee, Manager, Owner, Director)
- `isActive` - Filter by active status (true/false)

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/users?page=1&pageSize=10&isActive=true`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "data": [
    {
      "id": 1,
      "email": "john@example.com",
      "displayName": "John Doe",
      "role": "Employee",
      "isActive": true,
      "hireDate": "2024-01-15"
    },
    {
      "id": 2,
      "email": "jane@example.com",
      "displayName": "Jane Smith",
      "role": "Manager",
      "isActive": true,
      "hireDate": "2023-06-01"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 10,
    "totalCount": 2,
    "totalPages": 1
  }
}
```

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/users?page=1&pageSize=10" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Get Single User
**Non-Technical:** Get detailed information about one specific employee

**Endpoint:** `GET /api/v1/users/{id}`

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/users/1` (replace 1 with the user's ID)
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "id": 1,
  "email": "john@example.com",
  "displayName": "John Doe",
  "role": "Employee",
  "isActive": true,
  "hireDate": "2024-01-15"
}
```

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/users/1" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Create New User
**Non-Technical:** Add a new employee to the system

**Endpoint:** `POST /api/v1/users`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/users`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body (click "Body" tab, select "raw" and "JSON"):
```json
{
  "email": "newuser@example.com",
  "displayName": "New User",
  "password": "SecurePassword123!",
  "role": "Employee"
}
```
5. Click Send

**Example Response:**
```json
{
  "id": 3,
  "email": "newuser@example.com",
  "displayName": "New User",
  "role": "Employee",
  "isActive": true,
  "hireDate": "2024-10-28"
}
```

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/users" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "newuser@example.com",
    "displayName": "New User",
    "password": "SecurePassword123!",
    "role": "Employee"
  }'
```

---

#### Update User
**Non-Technical:** Change an employee's information

**Endpoint:** `PUT /api/v1/users/{id}`

**Example in Postman:**
1. Method: PUT
2. URL: `http://localhost:5000/api/v1/users/3`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body:
```json
{
  "displayName": "Updated Name",
  "isActive": true,
  "role": "Manager"
}
```
5. Click Send

**Technical:**
```bash
curl -X PUT "http://localhost:5000/api/v1/users/3" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{
    "displayName": "Updated Name",
    "isActive": true,
    "role": "Manager"
  }'
```

---

### 2. Shifts API

**What it does:** View scheduled work shifts

#### List Shifts
**Non-Technical:** Get a list of work shifts for a specific time period

**Endpoint:** `GET /api/v1/shifts`

**Query Parameters:**
- `startDate` - Beginning of date range (format: yyyy-MM-dd, e.g., 2024-10-01)
- `endDate` - End of date range (format: yyyy-MM-dd)
- `userId` - Filter by specific employee (optional)
- `page` - Page number (default: 1)
- `pageSize` - Results per page (default: 50)

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/shifts?startDate=2024-10-01&endDate=2024-10-31`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "data": [
    {
      "id": 1,
      "workDate": "2024-10-28",
      "shiftKey": "MORNING",
      "startTime": "08:00:00",
      "endTime": "16:00:00",
      "userId": 1,
      "userName": "John Doe",
      "userEmail": "john@example.com"
    },
    {
      "id": 2,
      "workDate": "2024-10-28",
      "shiftKey": "NOON",
      "startTime": "16:00:00",
      "endTime": "00:00:00",
      "userId": 2,
      "userName": "Jane Smith",
      "userEmail": "jane@example.com"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 2,
    "totalPages": 1
  }
}
```

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/shifts?startDate=2024-10-01&endDate=2024-10-31&userId=1" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Get Single Shift
**Non-Technical:** Get details about one specific shift

**Endpoint:** `GET /api/v1/shifts/{id}`

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/shifts/1`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/shifts/1" \
  -H "X-API-Key: your-api-key-here"
```

---

### 3. Time-Off Requests API

**What it does:** Manage employee vacation/time-off requests

#### List Time-Off Requests
**Non-Technical:** See all vacation requests

**Endpoint:** `GET /api/v1/time-off-requests`

**Query Parameters:**
- `page` - Page number
- `pageSize` - Results per page
- `userId` - Filter by employee
- `status` - Filter by status (Pending, Approved, Declined)
- `startDate` - Filter requests starting from this date
- `endDate` - Filter requests ending before this date

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/time-off-requests?status=Pending`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "data": [
    {
      "id": 1,
      "userId": 1,
      "userName": "John Doe",
      "userEmail": "john@example.com",
      "startDate": "2024-11-15",
      "endDate": "2024-11-17",
      "reason": "Family vacation",
      "status": "Pending",
      "createdAt": "2024-10-28T10:30:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/time-off-requests?status=Pending" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Create Time-Off Request
**Non-Technical:** Submit a new vacation request

**Endpoint:** `POST /api/v1/time-off-requests`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/time-off-requests`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body:
```json
{
  "userId": 1,
  "startDate": "2024-12-20",
  "endDate": "2024-12-27",
  "reason": "Holiday vacation"
}
```
5. Click Send

**Example Response:**
```json
{
  "id": 2,
  "userId": 1,
  "userName": "John Doe",
  "userEmail": "john@example.com",
  "startDate": "2024-12-20",
  "endDate": "2024-12-27",
  "reason": "Holiday vacation",
  "status": "Pending",
  "createdAt": "2024-10-28T14:45:00Z"
}
```

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/time-off-requests" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "startDate": "2024-12-20",
    "endDate": "2024-12-27",
    "reason": "Holiday vacation"
  }'
```

---

#### Approve Time-Off Request
**Non-Technical:** Approve an employee's vacation request

**Endpoint:** `POST /api/v1/time-off-requests/{id}/approve`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/time-off-requests/1/approve`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "id": 1,
  "userId": 1,
  "userName": "John Doe",
  "userEmail": "john@example.com",
  "startDate": "2024-11-15",
  "endDate": "2024-11-17",
  "reason": "Family vacation",
  "status": "Approved",
  "createdAt": "2024-10-28T10:30:00Z"
}
```

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/time-off-requests/1/approve" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Decline Time-Off Request
**Non-Technical:** Reject an employee's vacation request

**Endpoint:** `POST /api/v1/time-off-requests/{id}/decline`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/time-off-requests/1/decline`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/time-off-requests/1/decline" \
  -H "X-API-Key: your-api-key-here"
```

---

### 4. Notifications API

**What it does:** Manage system notifications and alerts

#### List Notifications
**Non-Technical:** See all notifications/alerts in the system

**Endpoint:** `GET /api/v1/notifications`

**Query Parameters:**
- `page` - Page number
- `pageSize` - Results per page
- `userId` - Filter by user
- `isRead` - Filter by read status (true/false)
- `type` - Filter by notification type

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/notifications?isRead=false&userId=1`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "data": [
    {
      "id": 1,
      "userId": 1,
      "type": "TimeOffApproved",
      "title": "Time Off Approved",
      "message": "Your time off request for Nov 15-17 has been approved",
      "isRead": false,
      "createdAt": "2024-10-28T15:30:00Z",
      "readAt": null,
      "relatedEntityId": 1
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/notifications?isRead=false&userId=1" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Get Single Notification
**Non-Technical:** Get details about one specific notification

**Endpoint:** `GET /api/v1/notifications/{id}`

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/notifications/1`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/notifications/1" \
  -H "X-API-Key: your-api-key-here"
```

---

#### Mark Notification as Read
**Non-Technical:** Mark a notification as read (like opening an email)

**Endpoint:** `POST /api/v1/notifications/{id}/mark-read`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/notifications/1/mark-read`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body (optional):
```json
{
  "userId": 1
}
```
5. Click Send

**Example Response:**
```json
{
  "id": 1,
  "userId": 1,
  "type": "TimeOffApproved",
  "title": "Time Off Approved",
  "message": "Your time off request for Nov 15-17 has been approved",
  "isRead": true,
  "createdAt": "2024-10-28T15:30:00Z",
  "readAt": "2024-10-28T16:00:00Z",
  "relatedEntityId": 1
}
```

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/notifications/1/mark-read" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{"userId": 1}'
```

---

#### Mark All Notifications as Read
**Non-Technical:** Mark all notifications for a user as read at once

**Endpoint:** `POST /api/v1/notifications/mark-all-read`

**Example in Postman:**
1. Method: POST
2. URL: `http://localhost:5000/api/v1/notifications/mark-all-read`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body:
```json
{
  "userId": 1
}
```
5. Click Send

**Example Response:**
```json
{
  "markedCount": 5
}
```

**Technical:**
```bash
curl -X POST "http://localhost:5000/api/v1/notifications/mark-all-read" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{"userId": 1}'
```

---

### 5. Analytics API

**What it does:** Get summary statistics and metrics about your organization

#### Get Analytics Summary
**Non-Technical:** See overall statistics like total employees, pending requests, etc.

**Endpoint:** `GET /api/v1/analytics/summary`

**Query Parameters:**
- `startDate` - Beginning of date range (format: yyyy-MM-dd)
- `endDate` - End of date range (format: yyyy-MM-dd)

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/analytics/summary?startDate=2024-10-01&endDate=2024-10-31`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "companyId": 1,
  "periodStart": "2024-10-01",
  "periodEnd": "2024-10-31",
  "totalUsers": 25,
  "activeUsers": 23,
  "shiftsScheduled": 450,
  "pendingTimeOffRequests": 3,
  "unreadNotifications": 12,
  "generatedAt": "2024-10-28T16:30:00Z"
}
```

**What each number means:**
- `totalUsers` - Total number of employees in the system
- `activeUsers` - Employees currently active (not deactivated)
- `shiftsScheduled` - Number of shifts in the date range
- `pendingTimeOffRequests` - Vacation requests waiting for approval
- `unreadNotifications` - Total unread notifications across all users

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/analytics/summary?startDate=2024-10-01&endDate=2024-10-31" \
  -H "X-API-Key: your-api-key-here"
```

---

### 6. Audit Logs API

**What it does:** View a history of actions taken in the system (for compliance and security)

#### List Audit Logs
**Non-Technical:** See a record of who did what and when

**Endpoint:** `GET /api/v1/audit-logs`

**Query Parameters:**
- `page` - Page number
- `pageSize` - Results per page
- `userId` - Filter by user who performed the action
- `action` - Filter by action type (e.g., "User.Created", "Shift.Modified")
- `startDate` - Filter logs from this date
- `endDate` - Filter logs until this date

**Example in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/audit-logs?page=1&pageSize=20&action=User.Created`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Example Response:**
```json
{
  "data": [
    {
      "id": 1,
      "userId": 2,
      "userEmail": "admin@example.com",
      "userDisplayName": "Admin User",
      "action": "User.Created",
      "entityType": "AppUser",
      "entityId": 3,
      "description": "Created new user: John Doe",
      "details": "{\"email\":\"john@example.com\",\"role\":\"Employee\"}",
      "ipAddress": "192.168.1.100",
      "userAgent": "Mozilla/5.0...",
      "timestamp": "2024-10-28T10:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

**What each field means:**
- `userId` - Who performed the action
- `action` - What type of action was performed
- `entityType` - What kind of thing was affected (User, Shift, etc.)
- `entityId` - The ID of the specific thing affected
- `description` - Human-readable description
- `details` - Technical details in JSON format
- `ipAddress` - Where the action came from
- `timestamp` - When it happened

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/audit-logs?page=1&action=User.Created" \
  -H "X-API-Key: your-api-key-here"
```

---

## Common Scenarios with Examples

### Scenario 1: Check if an Employee is Scheduled Tomorrow

**Non-Technical:** You want to see if John Doe (user ID 1) has a shift tomorrow (Oct 29, 2024).

**Steps in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/shifts?userId=1&startDate=2024-10-29&endDate=2024-10-29`
3. Headers: `X-API-Key: your-key`
4. Click Send
5. Look at the response:
   - If `totalCount` is 0, they're not scheduled
   - If `totalCount` is 1 or more, they have shift(s)

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/shifts?userId=1&startDate=2024-10-29&endDate=2024-10-29" \
  -H "X-API-Key: your-api-key-here"
```

---

### Scenario 2: Approve All Pending Time-Off Requests

**Non-Technical:** You want to approve all pending vacation requests at once.

**Steps:**

**Step 1:** Get list of pending requests
1. Method: GET
2. URL: `http://localhost:5000/api/v1/time-off-requests?status=Pending`
3. Headers: `X-API-Key: your-key`
4. Click Send
5. Write down all the IDs (e.g., 1, 3, 5)

**Step 2:** Approve each one
For each ID from Step 1:
1. Method: POST
2. URL: `http://localhost:5000/api/v1/time-off-requests/{id}/approve` (replace {id} with actual ID)
3. Headers: `X-API-Key: your-key`
4. Click Send

Repeat for IDs 3 and 5.

**Technical:**
```bash
# Step 1: Get pending requests
curl -X GET "http://localhost:5000/api/v1/time-off-requests?status=Pending" \
  -H "X-API-Key: your-api-key-here"

# Step 2: Approve each one (assuming IDs 1, 3, 5)
curl -X POST "http://localhost:5000/api/v1/time-off-requests/1/approve" \
  -H "X-API-Key: your-api-key-here"

curl -X POST "http://localhost:5000/api/v1/time-off-requests/3/approve" \
  -H "X-API-Key: your-api-key-here"

curl -X POST "http://localhost:5000/api/v1/time-off-requests/5/approve" \
  -H "X-API-Key: your-api-key-here"
```

---

### Scenario 3: Get Monthly Report

**Non-Technical:** You want a summary of everything that happened in October 2024.

**Steps in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/analytics/summary?startDate=2024-10-01&endDate=2024-10-31`
3. Headers: `X-API-Key: your-key`
4. Click Send
5. You'll get a summary with:
   - Total employees
   - How many shifts were scheduled
   - How many vacation requests are pending
   - How many unread notifications

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/analytics/summary?startDate=2024-10-01&endDate=2024-10-31" \
  -H "X-API-Key: your-api-key-here"
```

---

### Scenario 4: Find All Unread Notifications for an Employee

**Non-Technical:** You want to see all unread notifications for John Doe (user ID 1).

**Steps in Postman:**
1. Method: GET
2. URL: `http://localhost:5000/api/v1/notifications?userId=1&isRead=false`
3. Headers: `X-API-Key: your-key`
4. Click Send
5. You'll see all unread notifications

**Technical:**
```bash
curl -X GET "http://localhost:5000/api/v1/notifications?userId=1&isRead=false" \
  -H "X-API-Key: your-api-key-here"
```

---

### Scenario 5: Create a New Employee and Assign a Shift

**Non-Technical:** You're hiring a new employee and want to add them to the system.

**Steps:**

**Step 1:** Create the user
1. Method: POST
2. URL: `http://localhost:5000/api/v1/users`
3. Headers:
   - `X-API-Key: your-key`
   - `Content-Type: application/json`
4. Body:
```json
{
  "email": "sarah@example.com",
  "displayName": "Sarah Johnson",
  "password": "Welcome123!",
  "role": "Employee"
}
```
5. Click Send
6. Write down the new user's ID from the response (e.g., 10)

**Step 2:** You would typically use the web interface to assign shifts, but you can view their shifts using:
1. Method: GET
2. URL: `http://localhost:5000/api/v1/shifts?userId=10`
3. Headers: `X-API-Key: your-key`
4. Click Send

**Technical:**
```bash
# Step 1: Create user
curl -X POST "http://localhost:5000/api/v1/users" \
  -H "X-API-Key: your-api-key-here" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "sarah@example.com",
    "displayName": "Sarah Johnson",
    "password": "Welcome123!",
    "role": "Employee"
  }'

# Step 2: View their shifts
curl -X GET "http://localhost:5000/api/v1/shifts?userId=10" \
  -H "X-API-Key: your-api-key-here"
```

---

## Troubleshooting

### Error: "404 Not Found - This API endpoint is not enabled"

**Problem:** The endpoint you're trying to use is turned off.

**Non-Technical Solution:** Contact your system administrator and ask them to enable the specific API endpoint you need.

**Technical Solution:**
Edit `appsettings.json` and set the endpoint's flag to `true`:
```json
"Features": {
  "Api": {
    "Users": {
      "ListEnabled": true  // Change false to true
    }
  }
}
```
Then restart the application.

---

### Error: "401 Unauthorized - Invalid authentication"

**Problem:** Your API key is missing or incorrect.

**Non-Technical Solution:**
1. Double-check you added the header in Postman:
   - Header name: `X-API-Key` (exact spelling, case-sensitive)
   - Header value: Your actual API key
2. Make sure there are no extra spaces before or after the key
3. Try getting a new API key from your administrator

**Technical Solution:**
```bash
# Ensure header is properly formatted
curl -X GET "http://localhost:5000/api/v1/users" \
  -H "X-API-Key: sk_live_abc123..." \  # No extra spaces
  -v  # Verbose mode to see what headers are sent
```

---

### Error: "403 Forbidden - Insufficient permissions"

**Problem:** Your API key doesn't have permission to access this endpoint.

**Non-Technical Solution:** Contact your administrator and ask for the necessary permissions (scopes) for your API key.

**Technical Solution:** Your API key needs the appropriate scope claim. For example, to list users, you need the `user:read` scope.

---

### Error: "429 Too Many Requests - Rate limit exceeded"

**Problem:** You're sending too many requests too quickly.

**Non-Technical Solution:**
1. Wait 1 minute before trying again
2. Slow down your requests (add delays between them)

**Technical Solution:**
The API has rate limiting enabled. Default limits:
- Authenticated: 1000 requests per minute
- Per endpoint: 100 requests per minute

Implement exponential backoff:
```javascript
async function apiCallWithRetry(url, options, maxRetries = 3) {
  for (let i = 0; i < maxRetries; i++) {
    const response = await fetch(url, options);
    if (response.status !== 429) return response;

    // Wait before retrying (exponential backoff)
    const delay = Math.pow(2, i) * 1000; // 1s, 2s, 4s
    await new Promise(resolve => setTimeout(resolve, delay));
  }
  throw new Error('Rate limit exceeded after retries');
}
```

---

### Error: "400 Bad Request - Validation error"

**Problem:** The data you sent is invalid or missing required fields.

**Non-Technical Solution:**
1. Read the error message carefully - it tells you what's wrong
2. Check your request body for typos
3. Make sure all required fields are included
4. Check date formats (should be yyyy-MM-dd like 2024-10-28)

**Technical Solution:**
Example error response:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "Email is required",
  "instance": "/api/v1/users"
}
```

Common validation rules:
- Email must be valid format (user@domain.com)
- Dates must be yyyy-MM-dd format
- Required fields cannot be empty
- Password must meet complexity requirements

---

### Error: "500 Internal Server Error"

**Problem:** Something went wrong on the server (not your fault).

**Non-Technical Solution:**
1. Try again in a few minutes
2. If it keeps happening, contact your administrator with:
   - The exact URL you tried
   - The exact time the error occurred
   - What you were trying to do

**Technical Solution:**
Check the application logs:
```bash
# View recent logs
tail -f /path/to/logs/application.log

# Or check console output where dotnet run is running
```

Common causes:
- Database connection issues
- Missing database migrations
- Unhandled exceptions (check logs for stack trace)

---

### Error: "Cannot connect to http://localhost:5000"

**Problem:** The ShiftManager application isn't running.

**Non-Technical Solution:**
1. Open a command prompt/terminal
2. Navigate to the ShiftManager folder
3. Run: `dotnet run`
4. Wait for "Now listening on: http://localhost:5000"
5. Try your API request again

**Technical Solution:**
```bash
# Check if application is running
netstat -an | grep 5000  # Linux/Mac
netstat -an | findstr 5000  # Windows

# If not running, start it
cd /path/to/ShiftManager
dotnet run

# Or run in background
dotnet run &
```

---

### Problem: "I get HTML instead of JSON"

**Problem:** You're accessing the wrong URL or the endpoint isn't an API endpoint.

**Non-Technical Solution:**
Make sure your URL starts with `/api/v1/`. For example:
- ✅ Correct: `http://localhost:5000/api/v1/users`
- ❌ Wrong: `http://localhost:5000/users`

**Technical Solution:**
API endpoints are under the `/api/v1/` prefix. Regular page endpoints return HTML. Ensure you're hitting the API routes.

---

### Problem: "Dates in response look weird"

**Problem:** Dates are in ISO 8601 format, which looks like: `2024-10-28T16:30:00Z`

**Non-Technical Explanation:**
This is an international standard format. Breaking it down:
- `2024-10-28` = October 28, 2024
- `T` = separator between date and time
- `16:30:00` = 4:30 PM and 0 seconds
- `Z` = UTC timezone (Greenwich Mean Time)

**Technical Solution:**
Parse ISO 8601 dates in your programming language:
```javascript
// JavaScript
const date = new Date("2024-10-28T16:30:00Z");
console.log(date.toLocaleString()); // "10/28/2024, 4:30:00 PM"

// C#
DateTime date = DateTime.Parse("2024-10-28T16:30:00Z");
Console.WriteLine(date.ToLocalTime()); // Converts to local timezone

// Python
from datetime import datetime
date = datetime.fromisoformat("2024-10-28T16:30:00Z".replace('Z', '+00:00'))
print(date.strftime("%Y-%m-%d %I:%M %p"))  # "2024-10-28 04:30 PM"
```

---

## Additional Resources

### Learning More About APIs
- **Postman Learning Center:** https://learning.postman.com/
- **REST API Tutorial:** https://restfulapi.net/
- **HTTP Status Codes:** https://httpstatuses.com/

### JSON Format
JSON (JavaScript Object Notation) is the format used for sending and receiving data. It looks like:
```json
{
  "key": "value",
  "number": 123,
  "list": ["item1", "item2"],
  "nested": {
    "innerKey": "innerValue"
  }
}
```

### Getting Help
- Check application logs if you have access
- Contact your system administrator
- Save the error messages and responses to share when asking for help

---

## Security Best Practices

### Non-Technical
1. **Keep your API key secret** - Never share it publicly or put it in emails
2. **Use HTTPS in production** - The URL should start with `https://` not `http://`
3. **Don't save API keys in your browser** - Use environment variables or secure storage
4. **Log out when done** - Close Postman when you're finished

### Technical
1. **Store API keys securely:**
   ```bash
   # Use environment variables
   export SHIFTMANAGER_API_KEY="your-key-here"

   # Reference in code
   const apiKey = process.env.SHIFTMANAGER_API_KEY;
   ```

2. **Never commit API keys to version control:**
   ```bash
   # Add to .gitignore
   echo ".env" >> .gitignore
   echo "appsettings.*.json" >> .gitignore
   ```

3. **Use HTTPS in production:**
   ```json
   // appsettings.Production.json
   {
     "EnableHttpsRedirection": true
   }
   ```

4. **Implement proper error handling:**
   ```javascript
   try {
     const response = await fetch(url, options);
     if (!response.ok) {
       throw new Error(`HTTP ${response.status}: ${response.statusText}`);
     }
     const data = await response.json();
     return data;
   } catch (error) {
     console.error('API call failed:', error);
     // Handle error appropriately
   }
   ```

5. **Validate and sanitize input:**
   ```javascript
   // Always validate before sending
   function validateDateFormat(dateString) {
     const regex = /^\d{4}-\d{2}-\d{2}$/;
     return regex.test(dateString);
   }
   ```

---

## Conclusion

You now have everything you need to start using the ShiftManager APIs! Remember:

1. **Start with GET requests** - They're the safest and easiest
2. **Use Postman** - It's the most beginner-friendly tool
3. **Check the response** - Always read what comes back
4. **Don't be afraid to experiment** - GET requests won't break anything
5. **Save your work** - Postman lets you save requests for reuse
6. **Ask for help** - If you get stuck, reach out to your administrator

Good luck, and happy API exploring!
