# Project: Shifty (שיפטי)

## Area: 190 — Organizational Hierarchy

### Molecule: Oren (אורן)

#### JobType/Department: Alhut (אלחוט)
- Company: Tzafona (צפונה)
- Company: Hir (חיר)
- Company: Camps (מחנות)
- Company: City (העיר)
- Company: Radio (טקטי)

#### JobType/Department: BR (ב"ר)
- Company: Tzafona (צפונה)
- Company: Hir (חיר)
- Company: Camps (מחנות)
- Company: City (העיר)
- Company: Radio (טקטי)

#### JobType/Department: Text (טקסט)
- Company: Tzafona (צפונה)
- Company: Hir (חיר)
- Company: Camps (מחנות)
- Company: City (העיר)
- Company: Radio (טקטי)

#### JobType/Department: Hakam (חק"ם)
- Joint assignment: Tzafona + City (צפונה + העיר)
- Company: Hir (חיר)
- Company: Camps (מחנות)
- Company: Radio (טקטי)

---

### Molecule: Ella (אלה)

#### JobType/Department: Alhut (אלחוט)
- Company: Hitazmut (התעצמות)
- Company: GAP (גא"פ)
- Company: Yeadim (יעדים)

#### JobType/Department: BR (ב"ר)
- Company: Hitazmut (התעצמות)
- Company: GAP (גא"פ)
- Company: Yeadim (יעדים)

#### JobType/Department: Text (טקסט)
- Company: Hitazmut (התעצמות)
- Company: GAP (גא"פ)

#### JobType/Department: Hakam (חק"ם)
- Joint assignment: Hitazmut + Yeadim (התעצמות + יעדים)
- Company: GAP (גא"פ)

---

### Molecule: Harava (ערבה)

> Works the same way as **Ella**; has a single department/company: **Element (אלמנט)**.

#### JobType/Department: Alhut (אלחוט)
- Department/Company: Element (אלמנט)

#### JobType/Department: BR (ב"ר)
- Department/Company: Element (אלמנט)

#### JobType/Department: Text (טקסט)
- Department/Company: Element (אלמנט)

#### JobType/Department: Hakam (חק"ם)
- Department/Company: Element (אלמנט)

---

### Molecule: Shaked (שקד)

#### JobType/Department: Alhut (אלחוט)
- Inside (פנים)
- Out (חוץ)

#### JobType/Department: BR (ב"ר)
- Inside (פנים)
- Out (חוץ)

#### JobType/Department: Text (טקסט)
- Inside (פנים)
- Out (חוץ)

#### JobType/Department: Hakam (חק"ם)
- Inside (פנים)
- Out (חוץ)

---

### Molecule: Shikma (שקמה) — Tech
- Department: Pie (פאי)
- Department: Tao (טאו)
- Department: Yekeb (יקב)
- Department: Snir (שניר)
- Department: Arbel (ארבל)
- Department: Samapkam (סמפקמה)

---

### Molecule: NOC (נגדים)

---

### Molecule: Shiklut (שקלוט)
- (No children)

---

### Molecule: Gefen (גפן)

#### JobType/Department: Alhut (אלחוט)
- Company: Hamasa (חמסה)
- Company: Kabah (קבה"ח)
- Company: Matot (מטות)

#### JobType/Department: BR (ב"ר)
- Company: Hamasa (חמסה)
- Company: Kabah (קבה"ח)
- Company: Matot (מטות)

#### JobType/Department: Text (טקסט)
- Company: Hamasa (חמסה)
- Company: Kabah (קבה"ח)
- Company: Matot (מטות)

#### JobType/Department: Hakam (חק"ם)
- Note: Two Hakams cover all companies

---

## Operating Model (Real World)

### Naming & normalization rules
- Entity names are **case-insensitive** (e.g., `GAP`, `Gap`, `gap` are the same entity).
- Hebrew/English spellings are treated as aliases for the same entity (when context matches).
- Canonical spellings used in this document:
  - **Shiklut** (שקלוט)
  - **NOC** (נגדים)

### Roles & authority
- **Area Admin** manages: Gefen, Oren, Ella, Harava, Shiklut, NOC, Shikma, Shaked.
- **Molecule Admin** is a formal role (per molecule) and has all BR Director permissions (and more).
- **Per molecule:** exactly **one Alhut Director** and **one Text Director**.
- **Workforce companies:** the **BR Director (קב"ר)** is the **jobtype lead by definition**, and is usually also the **overall company boss**.
  - Rare exception: an **Alhut Director** may temporarily lead a company (extremely rare).
  - If this occurs, there is **no process change** (same scheduling/chores flows); it's only a leadership override.
- **Dispute resolution:** the **Molecule Admin** is the final decider.

### Role mapping (for clarity)
- **Alhut Lead** = Alhut “jobtype company lead”
- **Text Lead** = Text “jobtype company lead”
- **BR Director** = BR “jobtype company lead” (by definition)

### Molecule types (conceptual)
- **Non-tech**
  - **Workforce molecules:** Gefen, Oren, Ella, Harava, Shaked (companies + jobtypes BR/Alhut/Text/Hakam)
  - **Help molecules:** Shiklut and NOC (separate entities; support all workforce molecules)
- **Tech**
  - **Shikma** is the only tech molecule (departments, no companies)

---

## Shifts

### Workforce molecules (Gefen/Oren/Ella/Harava/Shaked)
- Default shifts:
  - **Morning:** 08:00–16:00
  - **Afternoon:** 16:00–00:00
  - **Night:** 00:00–08:00
- Some companies may define additional/custom shift windows.

#### Participation rules
- People (including leads/directors) may take shifts **only in their own jobtype**.
- **BR uses the same shift blocks** as other jobtypes and is scoped by company/shift grouping.
  - Example: BR in **Hitazmut** does the **Hitazmut shift**.

#### Who can add/modify shift windows
- The **jobtype company lead** may add new shift windows for their jobtype in that company.
  - Example: Tzafona Alhut Lead can add a “middle” shift (e.g., 12:00–20:00).

#### Company-specific scheduling notes
- **Oren (Alhut/Text) grouping example:**
  - **Tzafon shift:** Tzafona + City
  - **Darom shift:** Camps + Hir
  - **Tacti shift:** Radio
- **Gefen:** Hamasa and Kabah share a combined shift structure (still separated by jobtype); each jobtype has its own drive.
- **Ella:** each company has its own shifts split by jobtype; each jobtype has its own drive.
- **Harava:** behaves like **Ella**, but with a single department/company (**Element**) under each jobtype.

### Hakam (special rule)
- **Area-wide on-call**, not per-company shifts:
  - **1 primary hakam + 1 backup** across the entire area.
  - **Snir department executes scheduling** and chooses who covers on-call from **all hakams in the entire area**.

### Helper molecules (Shiklut + NOC)
- On-call helpers; people come to them with questions.
- **No routing:** ask whoever is available.

### Tech molecule shifts (Shikma)
- Managed in one Excel and divided into:
  - **Hanava (מאייש הנבה)**
  - **Delta (דלתא)**
  - **Yekev (יקב)**
  - **Moviltech (מובילט)** — department heads only
- Eligibility:
  - Pie + Tao → Hanava or Delta
  - Samapkam → Delta only
  - Yekev → Yekev shift

---

## Chores

### Where chores live
- **Workforce molecules:** one Excel per molecule, organized by jobtype.
- **Help molecules:** only **Shiklut** does chores.
- **Tech (Shikma):** one Excel for the whole molecule, organized by departments.

### Who can assign chores
- Two specific people per molecule can assign chores to anyone in that molecule (regardless of role).
- Lead level and above can assign chores.

---

## Current Excel system (no URLs available)

### Shifts — where to look
- **Workforce molecules (Gefen/Oren/Ella/Harava/Shaked):**
  - Shifts are tracked in Excel.
  - Structure varies by molecule:
    - **Oren:** shift groupings may combine companies (e.g., Tzafona+City) while remaining separated by jobtype.
    - **Gefen:** Hamasa+Kabah share a combined shift structure; still separated by jobtype; each jobtype has its own drive.
    - **Ella:** each company has its own shifts split by jobtype; each jobtype has its own drive.
    - **Harava:** same as Ella, but only Element exists under each jobtype.
- **Hakam:** scheduled centrally (Snir) as an area-wide on-call (primary + backup).
- **Shikma:** all tech shifts are in one Excel (Hanava/Delta/Yekev/Moviltech).
- **Helpers (Shiklut/NOC):** on-call style; operationally handled as availability-based help.

### Chores — where to look
- **Workforce:** one Excel per molecule, organized by jobtype.
- **Shiklut:** has its own chores Excel.
- **Shikma:** one Excel for all departments.

> Note: If/when you add file names or folder paths later, they can be appended here without changing the operating rules above.
