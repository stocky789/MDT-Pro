namespace MDTPro.Data {
    public class SearchHistoryEntry {
        public string ResultName;
        /// <summary>Date-of-birth string for the matched ped, used to disambiguate same-named peds.</summary>
        public string ResultDob;
        public string LastSearched;
        public int SearchCount;
    }
}
