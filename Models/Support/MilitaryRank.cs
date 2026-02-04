namespace ShiftManager.Models.Support;

/// <summary>
/// IDF military rank system for eligibility checks.
/// Enlisted ranks are 0-8, Officers are 9+.
/// </summary>
public enum MilitaryRank
{
    // Enlisted (0-8)
    Turai = 0,              // טוראי - Private
    TuraiRishon = 1,        // טוראי ראשון - Private First Class
    RavTurai = 2,           // רב טוראי - Corporal
    Samal = 3,              // סמל - Sergeant
    SamalRishon = 4,        // סמל ראשון - Staff Sergeant
    RavSamal = 5,           // רב סמל - Sergeant First Class
    RavSamalMitkadam = 6,   // רב סמל מתקדם - Master Sergeant
    RavSamalBakhir = 7,     // רב סמל בכיר - Senior Master Sergeant
    RavNagad = 8,           // רב נגד - Warrant Officer

    // Officers (9+)
    SegenMishne = 9,        // סגן משנה - Second Lieutenant
    Segen = 10,             // סגן - Lieutenant
    Seren = 11,             // סרן - Captain
    RavSeren = 12,          // רב סרן - Major
    SganAluf = 13,          // סגן אלוף - Lieutenant Colonel
    AlufMishne = 14,        // אלוף משנה - Colonel
    TatAluf = 15,           // תת אלוף - Brigadier General
    Aluf = 16,              // אלוף - Major General
    RavAluf = 17            // רב אלוף - Lieutenant General
}
