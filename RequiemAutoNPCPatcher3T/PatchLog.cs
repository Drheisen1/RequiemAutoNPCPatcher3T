using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// "Catch everything" means nothing is dropped quietly. Every actor that is skipped, guessed at, or
/// found to have no comparable ends up in the summary, whether or not verbose logging is on.
/// </summary>
public sealed class PatchLog
{
    private readonly bool _verbose;
    private readonly List<string> _warnings = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _skips = new();
    private readonly List<string> _needsDecision = new();

    public int Patched { get; private set; }
    public int Untouched { get; private set; }

    public PatchLog(bool verbose) => _verbose = verbose;

    public void Actor(INpcGetter npc, string what)
    {
        Patched++;
        if (_verbose) Console.WriteLine($"  {Describe(npc)}  {what}");
    }

    public void Note(string message)
    {
        if (_verbose) Console.WriteLine($"  note: {message}");
    }

    public void Skip(string message)
    {
        Untouched++;
        _skips.Add(message);
        if (_verbose) Console.WriteLine($"  skip: {message}");
    }

    public void Warn(string message) => _warnings.Add(message);

    public void Error(string message) => _errors.Add(message);

    /// <summary>An actor the patcher will not guess at. Always surfaced.</summary>
    public void NeedsDecision(string message) => _needsDecision.Add(message);

    public void Summarise()
    {
        Console.WriteLine();
        Console.WriteLine($"Patched {Patched} actor(s). {Untouched} block(s) left alone because the target inherits them.");

        Section("Actors that need a decision", _needsDecision,
            "Each of these is on a race the donor plugins contain no comparable for. Add a 'Creature race " +
            "donor override' in the patcher settings pointing that race at a vanilla or stack race, then re-run.");

        Section("Warnings", _warnings, null);
        Section("Errors", _errors, null);

        if (!_verbose && _skips.Count > 0)
            Console.WriteLine($"\n{_skips.Count} templated block(s) were not written. Re-run with 'Log every patched actor' on to see them.");
    }

    private static void Section(string title, List<string> lines, string? footer)
    {
        if (lines.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine($"{title} ({lines.Count}):");
        foreach (var line in lines) Console.WriteLine($"  - {line}");
        if (footer is not null) Console.WriteLine($"  {footer}");
    }

    private static string Describe(INpcGetter npc) =>
        $"{npc.EditorID ?? npc.Name?.String ?? "<unnamed>"} ({npc.FormKey})";
}
