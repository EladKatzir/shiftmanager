import sqlite3, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
c = sqlite3.connect(r'C:\Users\katzi\Downloads\ShiftManager\app.db')
c.row_factory = sqlite3.Row

def show(title, q):
    print(f"\n=== {title} ===")
    rows = list(c.execute(q))
    for r in rows: print(dict(r))
    print(f"({len(rows)} rows)")

show("PROJECTS", "SELECT Id, Name FROM Projects ORDER BY Id")
show("AREAS", "SELECT Id, DisplayName, ProjectId FROM Areas ORDER BY ProjectId, Id")
show("MOLECULES", "SELECT Id, DisplayName, AreaId FROM Molecules ORDER BY AreaId, Id")
show("COMPANIES", "SELECT Id, DisplayName, MoleculeId FROM Companies ORDER BY MoleculeId, Id")
show("USERS (first 25)", """SELECT u.Id, u.Email, u.Role, u.CompanyId, c.DisplayName Co, c.MoleculeId
                            FROM Users u LEFT JOIN Companies c ON c.Id=u.CompanyId
                            ORDER BY u.Id LIMIT 25""")
show("ADMIN GRANTS (admin@local user.Id)", """
    SELECT g.Id, g.GrantTypeId, gt.[Key] AS GrantKey, g.ProjectId, g.AreaId, g.MoleculeId, g.CompanyId, g.DepartmentId, g.JobTypeId, g.CanOwn
    FROM Grants g LEFT JOIN GrantTypes gt ON gt.Id=g.GrantTypeId
    WHERE g.UserId=(SELECT Id FROM Users WHERE Email='admin@local')
      AND gt.[Key]='AssignChores'
""")
show("Counts of admin grants by scope shape", """
    SELECT
        SUM(CASE WHEN ProjectId IS NOT NULL THEN 1 ELSE 0 END) AS WithProject,
        SUM(CASE WHEN AreaId IS NOT NULL THEN 1 ELSE 0 END) AS WithArea,
        SUM(CASE WHEN MoleculeId IS NOT NULL THEN 1 ELSE 0 END) AS WithMol,
        SUM(CASE WHEN CompanyId IS NOT NULL THEN 1 ELSE 0 END) AS WithCo,
        SUM(CASE WHEN ProjectId IS NULL AND AreaId IS NULL AND MoleculeId IS NULL AND CompanyId IS NULL AND DepartmentId IS NULL AND JobTypeId IS NULL THEN 1 ELSE 0 END) AS AllNull,
        COUNT(*) AS Total
    FROM Grants WHERE UserId=(SELECT Id FROM Users WHERE Email='admin@local')
""")
