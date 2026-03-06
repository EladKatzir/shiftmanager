namespace ShiftManager.Models.Support;

public enum SetupTaskType
{
    AssignMoleculeAdmin = 0,
    AssignDirector = 1,
    // 2 = removed (was AssignTextDirector, now merged into AssignDirector)
    AssignBRDirector = 3,
    AssignLead = 4,
    // 5 = removed (was AssignTextLead, now merged into AssignLead)
    AssignAssigners = 6,
    SetupShiftGroupings = 7,
    SetupDutyPrograms = 8,
    SetupShiftBlueprints = 9
}
