using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

// This is a read-only, per-photograph operations eligibility report. It never
// stages a journal entry, creates a recovery directory, or grants a filesystem
// mutation. The appropriate coordinator must later revalidate independently.
public sealed record OrdinaryOperationPreflight(string Operation, string? Target,
    IReadOnlyList<string> Blockers)
{
    public bool PreliminaryChecksPass => Blockers.Count == 0;
    public bool AuthorizedForMutation => false;
}

public sealed record OrdinaryOperationsReport(Guid SessionId, Guid PhotoId,
    string Original, string? NativeIdentity, string? Sha256, long? Length,
    IReadOnlyList<OrdinaryOperationPreflight> Operations)
{
    public bool AuthorizedForMutation => false;
    public string HumanReadable
    {
        get
        {
            var lines = new List<string>
            {
                "ORDINARY OPERATION SAFETY CENTER — READ ONLY",
                "NO NATIVE FILE OPERATION IS AUTHORIZED BY THIS REPORT.",
                "",
                "ORIGINAL: " + Original,
                "IMPORTED NATIVE ID: " + (NativeIdentity ?? "not proved"),
                "CURRENT SHA-256: " + (Sha256 ?? "not proved"),
                "CURRENT BYTES: " + (Length?.ToString() ?? "not proved"),
                ""
            };
            foreach (var op in Operations)
            {
                lines.Add(op.Operation.ToUpperInvariant() + " — " +
                    (op.PreliminaryChecksPass ? "PRELIMINARY CHECKS PASS" : "BLOCKED"));
                lines.Add("PROPOSED TARGET: " + (op.Target ?? "Not yet reviewed / not applicable"));
                foreach (var blocker in op.Blockers) lines.Add("  • " + blocker);
                lines.Add("EXECUTION AUTHORITY: NONE. The native coordinator is NOT enabled by this inspection.");
                lines.Add("");
            }
            lines.Add("Native Move still needs a separately journalled, owner-reviewed operation and Undo.");
            lines.Add("Normal cross-platform Rename execution remains restricted pending native acceptance.");
            lines.Add("Reject marking is reversible and non-destructive. Native Reject requires separate approval and recovery evidence.");
            lines.Add("Never use this preflight to bypass the existing GUI/native execution locks.");
            return string.Join(Environment.NewLine,lines);
        }
    }
}

public static class OrdinaryOperationsSafetyAudit
{
    private static bool SameOrInside(string child, string parent)
    {
        string rel=Path.GetRelativePath(parent,child);
        return rel=="." || (!Path.IsPathRooted(rel) && rel!=".." &&
            !rel.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }
    private static bool Occupied(string path)=>File.Exists(path) || Directory.Exists(path);
    private static void Add(List<string> blockers,string message)
    {
        if (!blockers.Contains(message,StringComparer.Ordinal))blockers.Add(message);
    }
    private static string? CandidateTarget(string source,string candidate,List<string> blockers)
    {
        try
        {
            string name=DurableRenameCoordinator.ValidateFilename(source,candidate);
            string parent=Path.GetDirectoryName(source)!;
            string target=Path.Combine(parent,name);
            if (Occupied(target)) Add(blockers,"Requested Rename target is occupied; replacement is forbidden.");
            // Prevent the cross-platform case/Unicode collision that Windows might
            // otherwise resolve differently from Linux.
            var normalized=name.Normalize(NormalizationForm.FormC);
            if(Directory.EnumerateFileSystemEntries(parent).Any(entry=>
                string.Equals(Path.GetFileName(entry).Normalize(NormalizationForm.FormC),
                    normalized,StringComparison.OrdinalIgnoreCase)))
                Add(blockers,"Requested target aliases an existing filename (case/Unicode).");
            return target;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Add(blockers,"Invalid proposed Rename filename or unreadable directory: "+ex.Message);
            return null;
        }
    }

    public static OrdinaryOperationsReport Inspect(WorkspaceSessionStore db,
        SortingWorkspace work, Photo selected, string proposedRename)
    {
        if(db is null || work is null || selected is null)throw new ArgumentNullException(nameof(work));
        var common = new List<string>();
        var move = new List<string>();
        var rename = new List<string>();
        var reject = new List<string>();
        string? identity=null,hash=null;
        long? length=null;
        // No SQLite write and no workspace ReviewPlan() here: that method
        // changes the current review authorization. Inspect only snapshot data.
        var saved=db.Load();
        if(saved is null) Add(common,"No saved workspace checkpoint exists.");
        else
        {
            var live=work.CaptureSession();
            if(saved.SessionId!=work.SessionId || selected.Id==Guid.Empty ||
               work.Current?.Id!=selected.Id ||
               saved.Photos.SingleOrDefault(p=>p.Id==selected.Id) is not { } checkpoint ||
               !SortingWorkspace.PathComparer.Equals(checkpoint.Path,selected.Path) ||
               (live.Revision!=saved.Revision &&
                  !(saved.Revision<long.MaxValue-1 && live.Revision==saved.Revision+1)) ||
               JsonSerializer.Serialize(live with {Revision=saved.Revision})!=JsonSerializer.Serialize(saved))
                Add(common,"The current selected photograph or live session differs from the durable checkpoint.");
        }
        if(!NativeFileCapability.IsSupported || selected.NativeIdentity is null)
            Add(common,"A recorded Windows/Linux native photo identity is required.");
        if(db.HasUnreconciledCopy || db.AllCopyHistoryCount()!=0 ||
           db.AllMoveHistoryCount()!=0 || db.AllRenameHistoryCount()!=0 ||
           db.AllRejectHistoryCount()!=0)
            Add(common,"Existing native Copy/Move/Rename/Reject history requires a separate terminal recovery contract.");
        if(work.RemovedCount!=0 || work.HasAppliedCopies)
            Add(common,"This collection contains removed or applied photographs.");
        string source=Path.GetFullPath(selected.Path);
        string directory=Path.GetDirectoryName(source)!;
        string? output=work.DestinationRoot;
        if(!Directory.Exists(directory) ||
           SortingWorkspace.HasLinkedExistingAncestor(directory) ||
           SortingWorkspace.HasSymlinkOrReparsePoint(source))
            Add(common,"Original path or its parent is missing or linked/reparse.");
        if(work.SourceRoot is null || output is null || work.SourceRoots.Count!=1 ||
           !SortingWorkspace.PathComparer.Equals(work.SourceRoot,work.SourceRoots[0]) ||
           !SortingWorkspace.PathComparer.Equals(work.SourceRoot,directory) ||
           work.Photos.Any(p=>!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),directory)) ||
           !Directory.Exists(output) || SortingWorkspace.HasLinkedExistingAncestor(output) ||
           SameOrInside(output,directory) || SameOrInside(directory,output))
            Add(common,"Requires one flat source and a distinct existing, nonlinked output folder.");
        var state=Path.GetDirectoryName(db.DatabasePath)!;
        if(SameOrInside(state,directory) || SameOrInside(directory,state) ||
           (output is not null && (SameOrInside(state,output) || SameOrInside(output,state))))
            Add(common,"Application state, source and output directories must not overlap.");
        try
        {
            if(!File.Exists(source))Add(common,"Original is absent.");
            else if(!SortingWorkspace.HasSymlinkOrReparsePoint(source) &&
                    !SortingWorkspace.HasLinkedExistingAncestor(directory) && NativeFileCapability.IsSupported)
            {
                var f=new FileInfo(source);
                identity=NativeFileCapability.Identity(source);length=f.Length;
                if(f.Length!=selected.Length || f.LastWriteTimeUtc.Ticks!=selected.LastWriteUtcTicks ||
                   identity!=selected.NativeIdentity)
                    Add(common,"Original native identity, size or modification time changed after import.");
                else
                {
                    using var read=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
                    hash=Convert.ToHexString(SHA256.HashData(read));
                    if(NativeFileCapability.Identity(source)!=identity ||
                       new FileInfo(source).LastWriteTimeUtc.Ticks!=selected.LastWriteUtcTicks)
                        Add(common,"Original changed during inspection; discard this report.");
                }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {Add(common,"Cannot prove the native original safely: "+ex.GetType().Name);}
        foreach(var reason in common){Add(move,reason);Add(rename,reason);Add(reject,reason);}
        if(work.AssignmentCount!=1 || work.PendingRejectCount!=0)
            Add(move,"Move preview requires exactly one assigned photograph and no Reject marks.");
        if(work.AssignmentCount!=0 || work.PendingRejectCount!=0)
            Add(rename,"Rename preview requires no staged routes or Reject marks.");
        if(work.AssignmentCount!=0 || work.PendingRejectCount!=1 || !work.IsPendingReject(selected.Id))
            Add(reject,"Reject preview requires exactly the selected photo marked, no assignments.");
        string? renameTarget=CandidateTarget(source,proposedRename,rename);
        if(renameTarget is not null && !SortingWorkspace.PathComparer.Equals(
              Path.GetDirectoryName(renameTarget),directory))
            Add(rename,"Rename cannot leave the original directory.");
        if(Directory.Exists(Path.Combine(directory,".photosift-reborn-reject-recovery")))
            Add(reject,"Existing Reject recovery ownership must be verified by its coordinator.");
        if(File.Exists(Path.Combine(directory,".photosift-reborn-reject-recovery")))
            Add(reject,"Reject recovery root is occupied by a file.");
        if(Directory.Exists(Path.Combine(directory,".photosift-reborn-recovery")) ||
           File.Exists(Path.Combine(directory,".photosift-reborn-recovery")))
            Add(move,"Existing Move recovery directory requires separate ownership validation.");
        return new OrdinaryOperationsReport(work.SessionId,selected.Id,source,identity,hash,length,
          new OrdinaryOperationPreflight[] {
             new("Move", null,move.ToArray()),
             new("Rename",renameTarget,rename.ToArray()),
             new("Reject to owned recovery",null,reject.ToArray())
          });
    }
}
