using FreitasBoost.Core.Models;

namespace FreitasBoost.App.Models;

public sealed class ProcessCandidateView : ProcessCandidate
{
    public bool IsSelected { get; set; }
    public bool IsLocked { get; set; }
    public string DisplayName => Count > 1 ? $"{Name} ({Count})" : Name;
    public string MemoryText => $"{MemMB:0} MB";
    public string TagText { get; set; } = "";
    public string ReasonText { get; set; } = "";
    public bool CanSelect => !IsLocked;
}

public sealed class StateSnapshotView
{
    public StateSnapshotView(StateSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public StateSnapshot Snapshot { get; }
    public string Id => Snapshot.Id;
    public string Label => Snapshot.Label;
    public string Meta => $"{Snapshot.CreatedAt:dd/MM HH:mm} - {SourceLabel} - {Snapshot.PowerPlanName}";
    public string SourceLabel => Snapshot.Source == "automatico" ? "auto" : Snapshot.Source == "manual" ? "manual" : Snapshot.Source;
}

public static class ProcessTags
{
    private static readonly string[] SuggestedKill =
    [
        "googleupdate", "googlecrashhandler", "onedrive", "spotify",
        "epicgameslauncher", "yourphone", "phoneexperiencehost", "msteams",
        "ms-teams", "teams", "skype", "ccleaner", "officeclicktorun",
        "adobe", "acrotray", "msedge", "widgets"
    ];

    private static readonly string[] ManualReview =
    [
        "discord", "obs", "gamebar", "nvcontainer", "rtkauduservice"
    ];

    public static bool IsSuggested(string name, IEnumerable<string>? extraSuggested = null)
    {
        return SuggestedKill.Concat(extraSuggested ?? Enumerable.Empty<string>())
            .Any(tag => name.Contains(tag, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPreserved(string name, IEnumerable<string>? neverKill = null)
    {
        return (neverKill ?? Enumerable.Empty<string>()).Any(tag => name.Contains(tag, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetTag(string name, IEnumerable<string>? neverKill = null, IEnumerable<string>? extraSuggested = null)
    {
        if (IsPreserved(name, neverKill)) return "preservado";
        if (IsSuggested(name, extraSuggested)) return "sugerido";
        if (ManualReview.Any(tag => name.Contains(tag, StringComparison.OrdinalIgnoreCase))) return "manual";
        return "";
    }

    public static string GetReason(string name, IEnumerable<string>? neverKill = null, IEnumerable<string>? extraSuggested = null)
    {
        if (IsPreserved(name, neverKill)) return "Preferencia local: nunca encerrar.";
        if (IsSuggested(name, extraSuggested)) return "Baixo risco ou marcado como sugestao nas configuracoes.";
        if (ManualReview.Any(tag => name.Contains(tag, StringComparison.OrdinalIgnoreCase))) return "Revisao manual: pode afetar voz, captura, overlay ou driver.";
        return "Processo pesado detectado por uso de memoria.";
    }
}
