namespace ShiftManager.Models.Support;

public enum RoleScopeLevel
{
    Implicit = 0,        // Employee - no assignment needed
    Company = 1,         // BR Director
    CompanyJobType = 2,  // Alhut Lead, Text Lead
    Department = 3,      // Department Lead (tech)
    Molecule = 4,        // Molecule Admin, Assigner
    MoleculeJobType = 5, // Alhut Director, Text Director
    Area = 6,            // Area Admin
    Project = 7          // Owner
}
