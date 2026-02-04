namespace ShiftManager.Models.Support;

public static class MilitaryRankExtensions
{
    public static bool IsOfficer(this MilitaryRank rank) => (int)rank >= 9;

    public static bool IsNCO(this MilitaryRank rank) => (int)rank >= 3 && (int)rank <= 8;

    public static bool IsEnlisted(this MilitaryRank rank) => (int)rank < 3;

    /// <summary>
    /// Returns CSS class name for rank badge styling.
    /// </summary>
    public static string GetBadgeClass(this MilitaryRank rank)
    {
        if (rank.IsOfficer()) return "officer";
        if (rank.IsNCO()) return "nco";
        return "enlisted";
    }

    public static string GetDisplayName(this MilitaryRank rank, string language = "he")
    {
        return language == "he" ? GetHebrewName(rank) : GetEnglishName(rank);
    }

    public static string GetAbbreviation(this MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "טר'",
            MilitaryRank.TuraiRishon => "טר\"ר",
            MilitaryRank.RavTurai => "רב\"ט",
            MilitaryRank.Samal => "סמל",
            MilitaryRank.SamalRishon => "סמ\"ר",
            MilitaryRank.RavSamal => "רס\"ל",
            MilitaryRank.RavSamalMitkadam => "רס\"מ",
            MilitaryRank.RavSamalBakhir => "רס\"ב",
            MilitaryRank.RavNagad => "רנ\"ג",
            MilitaryRank.SegenMishne => "סג\"מ",
            MilitaryRank.Segen => "סג'",
            MilitaryRank.Seren => "סרן",
            MilitaryRank.RavSeren => "רס\"ן",
            MilitaryRank.SganAluf => "סא\"ל",
            MilitaryRank.AlufMishne => "אל\"מ",
            MilitaryRank.TatAluf => "תא\"ל",
            MilitaryRank.Aluf => "אלוף",
            MilitaryRank.RavAluf => "רא\"ל",
            _ => rank.ToString()
        };
    }

    private static string GetHebrewName(MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "טוראי",
            MilitaryRank.TuraiRishon => "טוראי ראשון",
            MilitaryRank.RavTurai => "רב טוראי",
            MilitaryRank.Samal => "סמל",
            MilitaryRank.SamalRishon => "סמל ראשון",
            MilitaryRank.RavSamal => "רב סמל",
            MilitaryRank.RavSamalMitkadam => "רב סמל מתקדם",
            MilitaryRank.RavSamalBakhir => "רב סמל בכיר",
            MilitaryRank.RavNagad => "רב נגד",
            MilitaryRank.SegenMishne => "סגן משנה",
            MilitaryRank.Segen => "סגן",
            MilitaryRank.Seren => "סרן",
            MilitaryRank.RavSeren => "רב סרן",
            MilitaryRank.SganAluf => "סגן אלוף",
            MilitaryRank.AlufMishne => "אלוף משנה",
            MilitaryRank.TatAluf => "תת אלוף",
            MilitaryRank.Aluf => "אלוף",
            MilitaryRank.RavAluf => "רב אלוף",
            _ => rank.ToString()
        };
    }

    private static string GetEnglishName(MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "Private",
            MilitaryRank.TuraiRishon => "Private First Class",
            MilitaryRank.RavTurai => "Corporal",
            MilitaryRank.Samal => "Sergeant",
            MilitaryRank.SamalRishon => "Staff Sergeant",
            MilitaryRank.RavSamal => "Sergeant First Class",
            MilitaryRank.RavSamalMitkadam => "Master Sergeant",
            MilitaryRank.RavSamalBakhir => "Senior Master Sergeant",
            MilitaryRank.RavNagad => "Warrant Officer",
            MilitaryRank.SegenMishne => "Second Lieutenant",
            MilitaryRank.Segen => "Lieutenant",
            MilitaryRank.Seren => "Captain",
            MilitaryRank.RavSeren => "Major",
            MilitaryRank.SganAluf => "Lieutenant Colonel",
            MilitaryRank.AlufMishne => "Colonel",
            MilitaryRank.TatAluf => "Brigadier General",
            MilitaryRank.Aluf => "Major General",
            MilitaryRank.RavAluf => "Lieutenant General",
            _ => rank.ToString()
        };
    }
}
