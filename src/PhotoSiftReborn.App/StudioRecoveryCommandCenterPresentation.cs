using System.Text;

namespace PhotoSiftReborn;

// Presentation of independently read native evidence for ALL recorded operation
// families, including foreign/retired sessions. Absolutely no operation grant,
// plan approval, journal repair, filesystem mutation or automatic recovery.
internal sealed record StudioRecoveryCase(string Key, string Family, string State,
    string SessionId, UnifiedRowProof Proof, string Explanation)
{
    internal string Headline => Family + "  ·  " + State + "  ·  " + Proof;
    internal string ExactDetails => "Family: "+Family+"\nOperation: "+Key+
        "\nSession: "+SessionId+"\nSaved state: "+State+
        "\nNative inspection: "+Proof+"\nObserved evidence: "+Explanation+
        "\n\nThis observation is not permission to execute, replay, Undo or repair a journal.";
}

internal sealed record StudioRecoveryDossier(string Heading, string Risk,
    string NextStep, string DependencySummary,
    IReadOnlyList<StudioRecoveryCase> Cases,
    IReadOnlyList<M6JDependencyEdge> Conflicts,
    string Report)
{
    internal bool AuthorizesNativeOperation => false;
    internal bool AuthorizesRepair => false;
    internal bool AuthorizesSelectiveUndo => false;
}

internal static class StudioRecoveryCommandCenterPresentation
{
    internal const string EvidenceLimit = "Read-only snapshot, not a transaction or a live file lock. " +
        "Filesystem identities can change after inspection. No automatic replay, recovery or Undo is authorized.";

    internal static StudioRecoveryDossier Create(
        UnifiedHistoryInventory inventory, M6JDependencyAssessment dependency)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(dependency);
        if (inventory.Rows is null || inventory.Installation is null ||
            inventory.Rows.Count>512 || dependency.Edges is null ||
            dependency.Edges.Count>2048)
            throw new InvalidDataException("Recovery dossier has invalid/oversized evidence.");
        var cases = new List<StudioRecoveryCase>(inventory.Rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in inventory.Rows)
        {
            if (row is null || !new[]{"Copy","Move","Rename","Reject"}.Contains(row.Family) ||
                !Guid.TryParseExact(row.JournalId,"N",out var id) || id==Guid.Empty ||
                !Guid.TryParseExact(row.SessionId,"N",out var session) || session==Guid.Empty ||
                string.IsNullOrWhiteSpace(row.State) || row.State.Length>64 ||
                string.IsNullOrWhiteSpace(row.Explanation) || row.Explanation.Length>8192 ||
                !seen.Add(row.Family+":"+row.JournalId))
                throw new InvalidDataException("Recovery dossier contains malformed or duplicate journal evidence.");
            cases.Add(new(row.JournalId,row.Family,row.State,row.SessionId,row.Proof,row.Explanation));
        }
        var unverified=cases.Count(x=>x.Proof==UnifiedRowProof.NotVerified);
        var inFlight=cases.Count(x=>x.State is not ("Committed" or "Undone"));
        bool incomplete = !inventory.ScanComplete ||
            inventory.Installation.TotalRows!=cases.Count ||
            dependency.Kind==M6JDependencyKind.IncompleteOrUnverified ||
            dependency.ExaminedRows>512 ||
            (dependency.ExaminedRows!=cases.Count && dependency.Kind!=M6JDependencyKind.IncompleteOrUnverified);
        bool needsAttention=incomplete || inventory.IsRecoveryReviewRequired ||
            unverified!=0 || inFlight!=0 || dependency.Edges.Count>0;
        string risk=incomplete ? "INSPECTION INCOMPLETE · HISTORY LOCKS PRESERVED" :
            needsAttention ? "REVIEW REQUIRED · NO NEW FILE ACTION" :
            "OBSERVED ONLY · NEVER AN OPERATION GRANT";
        string heading="Installation-wide recovery · "+cases.Count+" journal"+
            (cases.Count==1?"":"s")+" · "+unverified+" unverified · "+
            inFlight+" unfinished · "+dependency.Edges.Count+" conflict"+
            (dependency.Edges.Count==1?"":"s");
        string next=incomplete
            ? "One or more journals cannot be fully examined. Preserve all source/output/archive files and the SQLite database; do not retry or clear history."
            : needsAttention
                ? "Inspect the flagged records and retained recovery archives. Only independent, existing operation-specific safety controls may be considered; this panel offers none."
                : "No immediate issue detected in this observation. Existing journals and future changes still require independent approval and fresh native checks.";
        string dep="Dependency analysis: "+dependency.Kind+". "+dependency.Explanation;
        var builder=new StringBuilder();
        builder.AppendLine("PHOTOSIFT REBORN — RECOVERY & DEPENDENCY DOSSIER")
            .AppendLine("READ-ONLY · NO OPERATION AUTHORITY")
            .AppendLine(heading).AppendLine(risk).AppendLine(next)
            .AppendLine("Installation history: "+inventory.Installation.Kind)
            .AppendLine("Full scan: "+inventory.ScanComplete)
            .AppendLine("Inventory: "+inventory.Explanation)
            .AppendLine(dep).AppendLine(EvidenceLimit)
            .AppendLine("Recorded native-identity correlations: " +
                dependency.Edges.Count(x => x.Kind == M6JConflictKind.SameRecordedNativeIdentity) +
                " (historical journal values; not proof of current identity)")
            .AppendLine("Recorded path hierarchy correlations: " +
                dependency.Edges.Count(x => x.Kind == M6JConflictKind.PathAncestorOverlap) +
                " (lexical paths; not proof of native independence)")
            .AppendLine().AppendLine("OPERATION EVIDENCE");
        foreach(var item in cases)
            builder.AppendLine(item.ExactDetails).AppendLine("---");
        builder.AppendLine("RECORDED DEPENDENCY CONFLICTS");
        if(dependency.Edges.Count==0)
            builder.AppendLine("No recorded edges in this observation; NOT proof of independence.");
        foreach(var edge in dependency.Edges)
        {
            if(edge is null || string.IsNullOrWhiteSpace(edge.Explanation))
                throw new InvalidDataException("Malformed dependency evidence.");
            builder.AppendLine(edge.Kind+": "+edge.FirstFamily+"/"+edge.FirstJournalId+
                " ↔ "+edge.SecondFamily+"/"+edge.SecondJournalId+
                " — "+edge.Explanation);
        }
        return new(heading,risk,next,dep,cases.AsReadOnly(),
            dependency.Edges.ToArray(),builder.ToString());
    }

    internal static StudioRecoveryCase[] Filter(IReadOnlyList<StudioRecoveryCase> cases,
        int familyFilter, int evidenceFilter, string? search)
    {
        ArgumentNullException.ThrowIfNull(cases);
        if(familyFilter<0 || familyFilter>4 || evidenceFilter<0 || evidenceFilter>3)
            throw new ArgumentOutOfRangeException(nameof(familyFilter));
        string family=familyFilter switch {
            1=>"Copy",2=>"Move",3=>"Rename",4=>"Reject",_=>""};
        return cases.Where(x=>
            (family.Length==0 || x.Family==family) &&
            (evidenceFilter switch {
                1=>x.Proof==UnifiedRowProof.NotVerified,
                2=>x.State is not ("Committed" or "Undone"),
                3=>x.Proof!=UnifiedRowProof.NotVerified && (x.State is "Committed" or "Undone"),
                _=>true
            }) &&
            (string.IsNullOrWhiteSpace(search) ||
             x.ExactDetails.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
