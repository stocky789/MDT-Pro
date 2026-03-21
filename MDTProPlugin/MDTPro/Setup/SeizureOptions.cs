using System.Collections.Generic;

namespace MDTPro.Setup {
    /// <summary>Seizure report dropdown options (drug types, drug quantities, firearm types) from seizureOptions.json. Quantities align with Policing Redefined Description/Value (e.g. Baggie, Bundle, 2g).</summary>
    public class SeizureOptions {
        public List<DrugTypeOption> drugTypes = new List<DrugTypeOption>();
        public List<QuantityOption> drugQuantities = new List<QuantityOption>();
        public List<FirearmTypeOption> firearmTypes = new List<FirearmTypeOption>();

        public class DrugTypeOption {
            public string id;
            public string name;
        }

        public class QuantityOption {
            public string id;
            public string name;
        }

        public class FirearmTypeOption {
            public string id;
            public string name;
        }
    }
}
