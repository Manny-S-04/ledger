namespace FakeProvider.Scenarios;

// How a charge behaves. Picked at random by weight; a scenario named in the request (or Scenarios:Force)
// overrides the pick, but only in Development/Test.
public enum Scenario
{
    Normal,         // succeeded callback after ProcessingDelaySeconds
    LateSuccess,    // succeeded callback after LateSuccessDelaySeconds (can be past the ledger's expiry)
    Duplicate,      // succeeded callback sent twice: same event id and payload, DuplicateGapSeconds apart
    OutOfOrder,     // succeeded callback after a random 0..OutOfOrderMaxDelaySeconds, so charges overtake each other
    Never,          // charge succeeds, no callback is ever sent
    FailedCallback, // failed callback (InsufficientFunds) after ProcessingDelaySeconds
}

public static class ScenarioNames
{
    private static readonly Dictionary<string, Scenario> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"] = Scenario.Normal,
        ["late_success"] = Scenario.LateSuccess,
        ["duplicate"] = Scenario.Duplicate,
        ["out_of_order"] = Scenario.OutOfOrder,
        ["never"] = Scenario.Never,
        ["failed_callback"] = Scenario.FailedCallback,
    };

    public static Scenario? TryParse(string? name) =>
        name is not null && ByName.TryGetValue(name.Trim(), out Scenario scenario) ? scenario : null;

    public static string ToName(Scenario scenario) => ByName.First(pair => pair.Value == scenario).Key;
}

public sealed class ScenarioOptions
{
    public const string Section = "Scenarios";

    // Relative weights for the random pick, keyed by scenario name. Missing names have weight 0.
    public Dictionary<string, int> Weights { get; set; } = new() { ["normal"] = 100 };

    // Development/Test only: every charge uses this scenario (for deterministic end-to-end runs).
    public string? Force { get; set; }

    public int ProcessingDelaySeconds { get; set; } = 2;
    public int LateSuccessDelaySeconds { get; set; } = 120;
    public int DuplicateGapSeconds { get; set; } = 1;
    public int OutOfOrderMaxDelaySeconds { get; set; } = 30;
}

public static class ScenarioPicker
{
    // Rolls a number in [0, total weight) and walks the weights to find which scenario it lands in.
    public static Scenario PickRandom(IReadOnlyDictionary<string, int> weights, Random random)
    {
        List<(Scenario Scenario, int Weight)> entries = weights
            .Select(pair => (Scenario: ScenarioNames.TryParse(pair.Key), Weight: pair.Value))
            .Where(entry => entry.Scenario is not null && entry.Weight > 0)
            .Select(entry => (entry.Scenario!.Value, entry.Weight))
            .ToList();

        int total = entries.Sum(entry => entry.Weight);
        if (total <= 0)
        {
            return Scenario.Normal;
        }

        int roll = random.Next(total);
        foreach ((Scenario scenario, int weight) in entries)
        {
            if (roll < weight)
            {
                return scenario;
            }
            roll -= weight;
        }

        return Scenario.Normal;
    }
}
