using Game.Info;
using Language;
using ScriptableObjectScripts;

namespace LogisticsMod.Logic;

/// <summary>
/// Central place for user-visible strings. Each entry goes through
/// <see cref="LEManager.Get(string, string)"/> with a stable key plus an English
/// fallback so translation packs can override without code changes.
/// </summary>
internal static class LogisticsStrings
{
    private const string Prefix = "logisticsmod.";

    private static string Loc(string key, string fallback)
    {
        return LEManager.Get(Prefix + key, fallback);
    }

    private static string Name(ObjectInfo oi) => oi?.ObjectName ?? "?";
    private static string Name(ResourceDefinition rd) => rd != null ? LEManager.Get(rd.ID, rd.ID) : "?";

    // --- status words shown in the UI ---
    public static string StatusPending() => Loc("status.pending", "pending");
    public static string StatusInTransit() => Loc("status.in_transit", "in transit");
    public static string StatusSatisfied() => Loc("status.satisfied", "satisfied");
    public static string StatusFailed() => Loc("status.failed", "failed");

    public static string NoProviderInNetwork() => Loc("note.no_provider", "No provider in network");

    // --- planner/blocker reasons (returned by TryCreateDeliveries) ---
    public static string NoSurplusAt(ResourceDefinition rd, ObjectInfo provider) => string.Format(Loc("blocker.no_surplus", "No surplus {0} available at {1}"), Name(rd), Name(provider));

    // --- transit suffix shown in the UI ---
    public static string TransitArrivesOnly(string arrival) => string.Format(Loc("transit.arrives_only", " (arrives {0})"), arrival ?? "?");
}
