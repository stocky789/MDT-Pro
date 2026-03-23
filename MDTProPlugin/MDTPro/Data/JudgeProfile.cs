namespace MDTPro.Data {
    /// <summary>Judge personality profile for court sentencing. Leniency: -1 (very lenient) to 1 (very strict).</summary>
    public class JudgeProfile {
        public string name;
        public float leniency;
        public string notes;
    }

    /// <summary>Root object for judgeProfiles.json.</summary>
    public class JudgeProfilesData {
        public JudgeProfile[] judges;
    }
}
