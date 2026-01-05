using System;
using System.Collections.Generic;
using System.Text;

namespace ReadyDOS.Example.Integration.Models {
    public sealed class RecommendationInput {
        public int CustomerId { get; set; }
        public int Sku { get; set; }
    }

    public sealed class RecommendationScore {
        public float Score { get; set; }
    }

    // Used for scoring rows to group by Email later
    public class RecInputWithEmail {
        public string Email { get; set; } = "";
        public int CustomerId { get; set; }
        public int Sku { get; set; }
    }

    public sealed class ScoredRow : RecInputWithEmail {
        public float Score { get; set; }
    }
}
