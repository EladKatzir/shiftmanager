# Employee Profile Enhancements - Design Document

**Date**: 2025-10-19
**Status**: Design Phase
**Estimated Time**: 32-40 hours

---

## 1. Overview

### Purpose
Expand user profiles beyond basic authentication fields to include comprehensive employee information, enabling better workforce management and communication.

### Goals
1. **Rich Employee Profiles** - Contact info, skills, certifications, emergency contacts
2. **Avatar Upload** - Professional employee photos with image processing
3. **Field-Level Permissions** - Employees edit some fields, managers edit all
4. **Profile Audit Trail** - Track who changed what and when
5. **Quick Profile View** - Popup drawer for quick employee info access

### Access Control
- **Employees**: Can edit their own profile (limited fields)
- **Managers**: Can edit all employee profiles (all fields)
- **Directors**: Can edit profiles in assigned companies
- **Owners**: Can edit all profiles in all companies

---

## 2. Database Schema Changes

### 2.1 Extended AppUser Fields

**New Fields to Add to `AppUser` Table**:

```csharp
// Personal Information
public string? PreferredName { get; set; }  // What they prefer to be called
public string? Phone { get; set; }          // Mobile phone
public string? City { get; set; }           // City of residence
public DateOnly? DateOfBirth { get; set; }  // For age verification, birthday greetings

// Professional Information
public string? Department { get; set; }     // e.g., "Kitchen", "Front of House", "Management"
public string? JobTitle { get; set; }       // e.g., "Line Cook", "Server", "Shift Manager"
public DateOnly? HireDate { get; set; }     // When they started
public string? Skills { get; set; }         // JSON array, e.g., ["Grill", "Prep", "Cleaning"]
public string? Certifications { get; set; } // JSON array, e.g., ["Food Safety", "First Aid"]

// Emergency Contact
public string? EmergencyContactName { get; set; }
public string? EmergencyContactPhone { get; set; }
public string? EmergencyContactRelation { get; set; } // e.g., "Spouse", "Parent", "Friend"

// Profile Metadata
public string? AvatarFileName { get; set; }  // Filename in wwwroot/avatars/{companyId}/
public DateTime? ProfileLastUpdated { get; set; }
public int? ProfileLastUpdatedBy { get; set; } // User ID who made the change
```

**Field Permissions**:

| Field | Employee Can Edit | Manager Can Edit | Notes |
|-------|-------------------|------------------|-------|
| DisplayName | ✅ | ✅ | Full name |
| PreferredName | ✅ | ✅ | Nickname |
| Email | ❌ | ✅ | Login credential - manager only |
| Phone | ✅ | ✅ | Contact number |
| City | ✅ | ✅ | Location |
| DateOfBirth | ✅ | ✅ | Personal info |
| Department | ❌ | ✅ | Organizational - manager only |
| JobTitle | ❌ | ✅ | Organizational - manager only |
| HireDate | ❌ | ✅ | HR info - manager only |
| Skills | ✅ | ✅ | Self-reported skills |
| Certifications | ✅ | ✅ | Earned certifications |
| EmergencyContact* | ✅ | ✅ | All emergency fields |
| Avatar | ✅ | ✅ | Profile photo |
| Role | ❌ | ✅ | Authorization - manager only |
| IsActive | ❌ | ✅ | Status - manager only |

### 2.2 Profile Change Audit

**New Table: `ProfileChangeAudits`**

```csharp
public class ProfileChangeAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    public int TargetUserId { get; set; }  // Profile being changed
    public int ChangedBy { get; set; }      // Who made the change
    public DateTime Timestamp { get; set; }

    public string FieldName { get; set; }   // e.g., "Phone", "Department"
    public string? OldValue { get; set; }   // Previous value
    public string? NewValue { get; set; }   // New value

    // Navigation
    public AppUser TargetUser { get; set; }
    public AppUser ChangedByUser { get; set; }
    public Company Company { get; set; }
}
```

**Indexes**:
- `(CompanyId, TargetUserId, Timestamp)` - Profile history
- `(CompanyId, ChangedBy, Timestamp)` - Who changed what

---

## 3. Avatar Upload System

### 3.1 Storage Strategy

**Directory Structure**:
```
wwwroot/
  avatars/
    {companyId}/
      {userId}.jpg  (or .png)
      {userId}_thumb.jpg  (thumbnail)
```

**Multi-Tenant Isolation**:
- Each company has its own subdirectory
- Directory created automatically on first upload
- Company ID from `_tenantResolver.GetCurrentTenantId()`

### 3.2 Image Processing (SixLabors.ImageSharp)

**Requirements**:
- **Max File Size**: 5 MB
- **Allowed Formats**: JPEG, PNG
- **Output Format**: JPEG (converted, optimized)
- **Sizes**:
  - Full: 400x400px (square, cropped)
  - Thumbnail: 100x100px (for lists, navigation)

**Processing Pipeline**:
1. Validate file (size, type)
2. Load image with ImageSharp
3. Resize to 400x400 (crop to center if not square)
4. Save as JPEG with 85% quality
5. Create 100x100 thumbnail
6. Delete old avatar if exists
7. Update `AvatarFileName` in database

### 3.3 Service Interface

```csharp
public interface IAvatarService
{
    Task<(bool Success, string? FileName, string? Error)> UploadAvatarAsync(int userId, IFormFile file);
    Task<bool> DeleteAvatarAsync(int userId);
    string GetAvatarUrl(int userId, bool thumbnail = false);
    string GetDefaultAvatarUrl(string displayName); // Initials-based placeholder
}
```

**Default Avatar**:
- Generate initials from DisplayName (e.g., "John Doe" → "JD")
- Use CSS-based avatar with colored background
- No image file needed

---

## 4. Profile Service

### 4.1 Field-Level Permissions

**Service Interface**:

```csharp
public interface IProfileService
{
    // Permission checks
    Task<bool> CanEditFieldAsync(int currentUserId, int targetUserId, string fieldName);
    Task<bool> CanViewProfileAsync(int currentUserId, int targetUserId);

    // Profile operations
    Task<AppUser> GetProfileAsync(int userId);
    Task<(bool Success, string? Error)> UpdateProfileAsync(int userId, ProfileUpdateDto updates);
    Task<List<ProfileChangeAudit>> GetProfileHistoryAsync(int userId, int limit = 50);

    // Bulk operations
    Task<List<AppUser>> SearchProfilesAsync(string query, int limit = 20);
}
```

**Permission Logic**:

```csharp
public async Task<bool> CanEditFieldAsync(int currentUserId, int targetUserId, string fieldName)
{
    var currentUser = await _db.Users.FindAsync(currentUserId);
    var targetUser = await _db.Users.FindAsync(targetUserId);

    // Same company check
    if (currentUser.CompanyId != targetUser.CompanyId)
        return false;

    // Manager+ can edit all fields
    if (currentUser.Role == UserRole.Owner ||
        currentUser.Role == UserRole.Manager ||
        currentUser.Role == UserRole.Director)
        return true;

    // Employees can only edit their own profile
    if (currentUserId != targetUserId)
        return false;

    // Check if field is employee-editable
    var employeeEditableFields = new[]
    {
        "DisplayName", "PreferredName", "Phone", "City", "DateOfBirth",
        "Skills", "Certifications",
        "EmergencyContactName", "EmergencyContactPhone", "EmergencyContactRelation",
        "AvatarFileName"
    };

    return employeeEditableFields.Contains(fieldName);
}
```

### 4.2 Profile Update DTO

```csharp
public class ProfileUpdateDto
{
    // Personal
    public string? DisplayName { get; set; }
    public string? PreferredName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    // Professional (manager-only)
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public DateOnly? HireDate { get; set; }

    // Skills (JSON arrays)
    public List<string>? Skills { get; set; }
    public List<string>? Certifications { get; set; }

    // Emergency
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }
}
```

---

## 5. User Interface Design

### 5.1 Employee Profile Edit Page

**URL**: `/My/Profile`

**Authorization**: Authenticated users (self-edit only)

**Layout**:
```
┌─────────────────────────────────────────────┐
│  My Profile                                 │
├─────────────────────────────────────────────┤
│                                             │
│  [Avatar]    Display Name: John Doe         │
│  [Change]    Preferred Name: Johnny         │
│              Email: john@example.com (read-only) │
│                                             │
│  Personal Information                       │
│  ├─ Phone: [_____________]                  │
│  ├─ City: [_____________]                   │
│  └─ Date of Birth: [__/__/____]             │
│                                             │
│  Professional Information (read-only)       │
│  ├─ Department: Kitchen                     │
│  ├─ Job Title: Line Cook                    │
│  └─ Hire Date: 2024-01-15                   │
│                                             │
│  Skills & Certifications                    │
│  ├─ Skills: [Grill] [Prep] [+Add]          │
│  └─ Certifications: [Food Safety] [+Add]    │
│                                             │
│  Emergency Contact                          │
│  ├─ Name: [_____________]                   │
│  ├─ Phone: [_____________]                  │
│  └─ Relation: [Spouse ▼]                    │
│                                             │
│  [Save Changes] [Cancel]                    │
└─────────────────────────────────────────────┘
```

**Features**:
- Avatar upload with preview
- Editable fields only (manager fields read-only)
- Skills/certifications as tags (add/remove)
- Emergency contact dropdown
- Validation on save

### 5.2 Manager Profile Edit Page

**URL**: `/Admin/Users/Edit/{id}`

**Authorization**: Manager, Director, Owner

**Layout**:
```
┌─────────────────────────────────────────────┐
│  Edit Employee Profile - John Doe           │
├─────────────────────────────────────────────┤
│                                             │
│  [Avatar]    Display Name: [___________]    │
│  [Change]    Preferred Name: [___________]  │
│              Email: [___________]            │
│              Role: [Employee ▼]              │
│              Status: [✓ Active]              │
│                                             │
│  Personal Information                       │
│  ├─ Phone: [_____________]                  │
│  ├─ City: [_____________]                   │
│  └─ Date of Birth: [__/__/____]             │
│                                             │
│  Professional Information                   │
│  ├─ Department: [Kitchen ▼]                 │
│  ├─ Job Title: [_____________]              │
│  └─ Hire Date: [__/__/____]                 │
│                                             │
│  Skills & Certifications                    │
│  ├─ Skills: [Grill] [Prep] [+Add]          │
│  └─ Certifications: [Food Safety] [+Add]    │
│                                             │
│  Emergency Contact                          │
│  ├─ Name: [_____________]                   │
│  ├─ Phone: [_____________]                  │
│  └─ Relation: [Spouse ▼]                    │
│                                             │
│  Profile History                            │
│  └─ [View Change History] (50 recent)       │
│                                             │
│  [Save Changes] [Cancel] [Delete User]      │
└─────────────────────────────────────────────┘
```

**Features**:
- All fields editable
- Role and status management
- Profile change history link
- Delete user option

### 5.3 Profile Drawer Component

**Usage**: Click employee name anywhere in the app

**Layout** (Slide-in from right):
```
┌─────────────────────────┐
│  [X]  John Doe          │
├─────────────────────────┤
│                         │
│     [Avatar]            │
│   Johnny (Preferred)    │
│   john@example.com      │
│                         │
│  Kitchen - Line Cook    │
│  Employee               │
│                         │
│  📞 (555) 123-4567      │
│  📍 Los Angeles         │
│  🎂 Jan 15 (30 years)   │
│                         │
│  Skills:                │
│  • Grill               │
│  • Prep                │
│  • Cleaning            │
│                         │
│  Certifications:        │
│  • Food Safety         │
│  • First Aid           │
│                         │
│  Emergency Contact:     │
│  Jane Doe (Spouse)      │
│  📞 (555) 987-6543      │
│                         │
│  Hired: Jan 15, 2024    │
│                         │
│  [Edit Profile]         │
│  [View Schedule]        │
│                         │
└─────────────────────────┘
```

**Implementation**:
- JavaScript-based drawer (slide animation)
- Fetch profile data via AJAX
- Clickable employee names trigger drawer
- ESC key or outside click to close

---

## 6. Migration Design

### Migration Name: `AddEmployeeProfileEnhancements`

**Schema Changes**:

1. **Add Columns to `Users` Table**:
   - PreferredName (string, nullable)
   - Phone (string, nullable)
   - City (string, nullable)
   - DateOfBirth (string, nullable, stored as "yyyy-MM-dd")
   - Department (string, nullable)
   - JobTitle (string, nullable)
   - HireDate (string, nullable, stored as "yyyy-MM-dd")
   - Skills (string, nullable, JSON)
   - Certifications (string, nullable, JSON)
   - EmergencyContactName (string, nullable)
   - EmergencyContactPhone (string, nullable)
   - EmergencyContactRelation (string, nullable)
   - AvatarFileName (string, nullable)
   - ProfileLastUpdated (datetime, nullable)
   - ProfileLastUpdatedBy (int, nullable)

2. **Create `ProfileChangeAudits` Table**:
   - Id (int, primary key)
   - CompanyId (int, foreign key)
   - TargetUserId (int, foreign key)
   - ChangedBy (int, foreign key)
   - Timestamp (datetime)
   - FieldName (string)
   - OldValue (string, nullable)
   - NewValue (string, nullable)

3. **Create Indexes**:
   - `IX_ProfileChangeAudits_CompanyId_TargetUserId_Timestamp`
   - `IX_ProfileChangeAudits_CompanyId_ChangedBy_Timestamp`

**Rollback Script**: Generate SQL to drop columns and table

---

## 7. Localization Keys

### English (SharedResources.resx)

```xml
<data name="Profile" xml:space="preserve"><value>Profile</value></data>
<data name="MyProfile" xml:space="preserve"><value>My Profile</value></data>
<data name="EditProfile" xml:space="preserve"><value>Edit Profile</value></data>
<data name="PreferredName" xml:space="preserve"><value>Preferred Name</value></data>
<data name="Phone" xml:space="preserve"><value>Phone</value></data>
<data name="City" xml:space="preserve"><value>City</value></data>
<data name="DateOfBirth" xml:space="preserve"><value>Date of Birth</value></data>
<data name="Department" xml:space="preserve"><value>Department</value></data>
<data name="JobTitle" xml:space="preserve"><value>Job Title</value></data>
<data name="HireDate" xml:space="preserve"><value>Hire Date</value></data>
<data name="Skills" xml:space="preserve"><value>Skills</value></data>
<data name="Certifications" xml:space="preserve"><value>Certifications</value></data>
<data name="EmergencyContact" xml:space="preserve"><value>Emergency Contact</value></data>
<data name="EmergencyContactName" xml:space="preserve"><value>Contact Name</value></data>
<data name="EmergencyContactPhone" xml:space="preserve"><value>Contact Phone</value></data>
<data name="EmergencyContactRelation" xml:space="preserve"><value>Relationship</value></data>
<data name="Avatar" xml:space="preserve"><value>Avatar</value></data>
<data name="ChangeAvatar" xml:space="preserve"><value>Change Avatar</value></data>
<data name="UploadPhoto" xml:space="preserve"><value>Upload Photo</value></data>
<data name="PersonalInformation" xml:space="preserve"><value>Personal Information</value></data>
<data name="ProfessionalInformation" xml:space="preserve"><value>Professional Information</value></data>
<data name="ProfileHistory" xml:space="preserve"><value>Profile History</value></data>
```

### Hebrew (SharedResources.he-IL.resx)

```xml
<data name="Profile" xml:space="preserve"><value>פרופיל</value></data>
<data name="MyProfile" xml:space="preserve"><value>הפרופיל שלי</value></data>
<data name="EditProfile" xml:space="preserve"><value>עריכת פרופיל</value></data>
<data name="PreferredName" xml:space="preserve"><value>שם מועדף</value></data>
<data name="Phone" xml:space="preserve"><value>טלפון</value></data>
<data name="City" xml:space="preserve"><value>עיר</value></data>
<data name="DateOfBirth" xml:space="preserve"><value>תאריך לידה</value></data>
<data name="Department" xml:space="preserve"><value>מחלקה</value></data>
<data name="JobTitle" xml:space="preserve"><value>תפקיד</value></data>
<data name="HireDate" xml:space="preserve"><value>תאריך התחלה</value></data>
<data name="Skills" xml:space="preserve"><value>כישורים</value></data>
<data name="Certifications" xml:space="preserve"><value>אישורים</value></data>
<data name="EmergencyContact" xml:space="preserve"><value>איש קשר לחירום</value></data>
<data name="EmergencyContactName" xml:space="preserve"><value>שם איש קשר</value></data>
<data name="EmergencyContactPhone" xml:space="preserve"><value>טלפון איש קשר</value></data>
<data name="EmergencyContactRelation" xml:space="preserve"><value>קרבה</value></data>
<data name="Avatar" xml:space="preserve"><value>תמונה</value></data>
<data name="ChangeAvatar" xml:space="preserve"><value>שינוי תמונה</value></data>
<data name="UploadPhoto" xml:space="preserve"><value>העלאת תמונה</value></data>
<data name="PersonalInformation" xml:space="preserve"><value>מידע אישי</value></data>
<data name="ProfessionalInformation" xml:space="preserve"><value>מידע מקצועי</value></data>
<data name="ProfileHistory" xml:space="preserve"><value>היסטוריית פרופיל</value></data>
```

---

## 8. Implementation Phases

### Phase 1: Database & Models (4-6 hours)
1. Add new fields to AppUser model
2. Create ProfileChangeAudit model
3. Create migration
4. Update AppDbContext (indexes, relationships)
5. Apply migration

### Phase 2: Avatar Service (6-8 hours)
1. Install SixLabors.ImageSharp NuGet package
2. Implement AvatarService
3. Create wwwroot/avatars directory
4. Test image upload, resize, thumbnail generation
5. Test file deletion

### Phase 3: Profile Service (4-6 hours)
1. Create ProfileUpdateDto
2. Implement ProfileService
3. Add field-level permission logic
4. Add audit logging on profile changes
5. Test permissions

### Phase 4: Employee Profile Page (6-8 hours)
1. Create My/Profile.cshtml page
2. Implement profile form with validation
3. Add avatar upload UI
4. Add skills/certifications tag input
5. Test employee self-edit

### Phase 5: Manager Profile Page (4-6 hours)
1. Create Admin/Users/Edit.cshtml page
2. Implement full profile edit form
3. Add profile history view
4. Test manager edit all fields

### Phase 6: Profile Drawer Component (6-8 hours)
1. Create ProfileDrawer partial view
2. Implement JavaScript drawer logic
3. Add AJAX endpoint for profile data
4. Make employee names clickable across app
5. Test drawer open/close

### Phase 7: Localization & Navigation (2-3 hours)
1. Add all localization keys
2. Add "My Profile" link to employee navigation
3. Update Admin/Users page to link to Edit
4. Test Hebrew translations

### Phase 8: Testing & Documentation (2-3 hours)
1. Manual testing of all features
2. Test field-level permissions
3. Test audit logging
4. Create usage documentation
5. Build verification

**Total Estimated: 34-48 hours**

---

## 9. NuGet Package Required

**SixLabors.ImageSharp**:
```bash
dotnet add package SixLabors.ImageSharp --version 3.1.0
```

**License**: Apache 2.0 (commercial use allowed)

---

## 10. Success Criteria

**Profile Enhancements**:
- ✅ All new fields stored in database
- ✅ Employees can edit allowed fields
- ✅ Managers can edit all fields
- ✅ Avatar upload works correctly
- ✅ Images resized and optimized
- ✅ Profile changes audited
- ✅ Profile drawer works across app
- ✅ Hebrew localization complete
- ✅ Mobile-responsive design

---

## 11. Security Considerations

1. **Avatar Upload**:
   - Validate file type (JPEG/PNG only)
   - Limit file size (5 MB max)
   - Sanitize filename
   - Store in isolated company directories
   - No executable files allowed

2. **Field-Level Permissions**:
   - Server-side validation on every save
   - UI hides fields user can't edit
   - Audit log tracks unauthorized attempts

3. **Data Privacy**:
   - Emergency contact info visible to managers only
   - Date of birth not shown publicly (only age)
   - Phone numbers formatted/masked

---

**Ready to proceed with implementation?**
