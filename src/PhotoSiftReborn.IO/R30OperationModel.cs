using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

// Candidate-only backend. No instance of these records is itself authority.
public sealed record R30OperationView(string Family, Guid Id, Guid Session, long Epoch,
    string State, string Source, string Destination, Guid? Consumes);
public sealed record R30HistoryView(bool Present, bool Complete, string Reason,
    IReadOnlyList<R30OperationView> Rows, IReadOnlyList<M6JDependencyEdge> Conflicts)
{
    public bool AuthorizesOrdinaryMixedFamily => false;
    public bool AuthorizesAutomaticReplay => false;
}

internal sealed record R30DirectoryPin(string Path, string Identity);
internal sealed record R30Seed(string Path, string Identity, string Hash, long Length, long Ticks);
internal sealed record R30Fixture(Guid Installation, string Root, R30DirectoryPin[] Directories,
    R30Seed[] Seeds);
internal sealed record R30Plan(Guid Id, string Family, Guid Installation, Guid Session,
    long Epoch, Guid Photo, Guid PlanId, long Revision, string Source, string Destination,
    string Temp, string OriginalArchive, string OutputArchive, string Identity, string Hash,
    long Length, long Ticks, Guid? Consumes, R30DirectoryPin[] Directories);
internal sealed record R30Operation(R30Plan Plan, string State, string? TempIdentity = null,
    string? OutputIdentity = null, long OutputTicks = 0)
{
    internal R30OperationView View => new(Plan.Family,Plan.Id,Plan.Session,Plan.Epoch,State,
        Plan.Source,Plan.Destination,Plan.Consumes);

    internal string NativeJson => Plan.Family switch
    {
        "Copy" => R30Native.Json(new DurableCopyItem(Plan.Id,Plan.Session,Plan.Photo,'F',
            Plan.PlanId,Plan.Revision,Plan.Epoch,Plan.Source,Plan.Destination,Plan.Temp,
            Plan.OutputArchive,Plan.Hash,Plan.Identity,Plan.Length,Plan.Ticks,
            TempIdentity,OutputIdentity,OutputTicks,State,null)),
        "Move" => R30Native.Json(new DurableMoveItem(Plan.Id,Plan.Installation,Plan.Session,
            Plan.Photo,Plan.PlanId,Plan.Revision,Plan.Epoch,Plan.Source,Plan.Destination,
            Plan.Temp,Plan.OriginalArchive,Plan.OutputArchive,
            Path.GetDirectoryName(Plan.Destination)!,Plan.Hash,Plan.Identity,Plan.Length,
            Plan.Ticks,TempIdentity,OutputIdentity,State,null,'F')),
        "Rename" => R30Native.Json(new DurableRenameItem(Plan.Id,Plan.Installation,Plan.Session,
            Plan.Photo,Plan.Revision,Plan.Epoch,Plan.Source,Plan.Destination,Plan.Hash,
            Plan.Identity,Plan.Length,Plan.Ticks,State,null,
            Plan.Directories.Single(x=>SortingWorkspace.PathComparer.Equals(x.Path,
                Path.GetDirectoryName(Plan.Source))).Identity)),
        "Reject" => R30Native.Json(new DurableRejectItem(Plan.Id,Plan.Installation,Plan.Session,
            Plan.Photo,Plan.Revision,Plan.Source,Plan.Destination,Plan.Hash,Plan.Identity,
            Plan.Length,Plan.Ticks,State)),
        _ => throw new IOException("R30 unsupported family.")
    };

    internal M6JOperationFootprint Footprint => new(Plan.Family,Plan.Id.ToString("N"),
        Plan.Session.ToString("N"),Plan.Photo,State,false,new[] {
            new M6JPathClaim("Source",Plan.Source,false,Plan.Identity),
            new M6JPathClaim("Destination",Plan.Destination,false,OutputIdentity),
            new M6JPathClaim("Temporary",Plan.Temp),
            new M6JPathClaim("OriginalArchive",Plan.OriginalArchive),
            new M6JPathClaim("OutputArchive",Plan.OutputArchive)
        });
}
internal sealed record R30Review(Guid Id, string Action, R30Operation Operation,
    string History, string Checkpoint, string NativeWitness);
internal sealed record R30Snapshot(R30Fixture Fixture, R30Operation[] Operations,
    string Digest, string Checkpoint, WorkspaceSessionSnapshot Saved);

internal static class R30Native
{
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);
    internal static T Decode<T>(string value) => value.Length<=1024*1024
        ? JsonSerializer.Deserialize<T>(value) ?? throw new IOException("R30 missing JSON.")
        : throw new IOException("R30 payload exceeds bound.");
    internal static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Hash(string path) => SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult();
    internal static string Canonical(string path)
    {
        string leaf=Path.GetFileName(path);
        string device=leaf.Split('.')[0].TrimEnd(' ','\t').ToUpperInvariant();
        if(!Path.IsPathFullyQualified(path) || Path.GetFullPath(path)!=path ||
            path.IndexOf('\0')>=0 || leaf.Any(c=>char.IsControl(c) || "<>:\"|?*".Contains(c)) ||
            device is "CON" or "PRN" or "AUX" or "NUL" ||
            (device.Length==4 && (device.StartsWith("COM",StringComparison.Ordinal) ||
                device.StartsWith("LPT",StringComparison.Ordinal)) && device[3] is >= '1' and <= '9') ||
            path.Split(Path.DirectorySeparatorChar).Any(x=>x.EndsWith('.') || x.EndsWith(' ')))
            throw new IOException("R30 noncanonical path refused.");
        return path.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
    internal static bool Inside(string path,string root) => Canonical(path).StartsWith(
        Canonical(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal);
    internal static void Directories(IEnumerable<R30DirectoryPin> pins)
    {
        foreach(var p in pins)
            if(p is null || string.IsNullOrWhiteSpace(p.Path) || string.IsNullOrWhiteSpace(p.Identity) ||
                !Directory.Exists(p.Path) || SortingWorkspace.HasLinkedExistingAncestor(p.Path) ||
                NativeFileCapability.DirectoryIdentity(p.Path)!=p.Identity)
                throw new IOException("R30 reviewed directory identity changed: "+(p?.Path ?? "missing pin"));
    }
    internal static void Vacant(string path)
    {
        Canonical(path);
        var parent=Path.GetDirectoryName(path)!;
        string[] entries=Directory.Exists(parent)?Directory.EnumerateFileSystemEntries(parent).Take(2049).ToArray():[];
        if(entries.Length>2048)throw new IOException("R30 directory entry bound exceeded.");
        if(SortingWorkspace.HasLinkedExistingAncestor(parent) ||
            File.Exists(path) || Directory.Exists(path) ||
            entries.Any(x=>Canonical(x)==Canonical(path)))
            throw new IOException("R30 exact path or portable alias occupied: "+path);
    }
    internal static void Owned(string path,string? identity,string hash,long length,long? ticks=null,
        bool publicationPair=false)
    {
        Canonical(path);
        if(identity is null || hash.Length!=64 || !File.Exists(path) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!) ||
            (!publicationPair && !NativeFileCapability.IsSingleLinkRegularFile(path)))
            throw new IOException("R30 regular single-link native ownership missing: "+path);
        using var pinned=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var info=new FileInfo(path);
        if(NativeFileCapability.Identity(path)!=identity || pinned.Length!=length ||
            (ticks.HasValue && info.LastWriteTimeUtc.Ticks!=ticks) ||
            Convert.ToHexString(SHA256.HashData(pinned))!=hash ||
            NativeFileCapability.Identity(path)!=identity || new FileInfo(path).Length!=length)
            throw new IOException("R30 native identity/bytes/metadata changed: "+path);
    }
    // Follow *recorded, exact consumed-source* edges, never lexical similarity.
    // A Move retains the consumed inode in its original archive; Rename and
    // Reject carry that inode forward. Undo restores the predecessor's name.
    // The graph is bounded and epoch-ordered before this resolver is used.
    internal static string OutputPath(R30Operation row,IReadOnlyList<R30Operation> history,int depth=0)
    {
        if(depth>512)throw new IOException("R30 native ownership chain bound exceeded.");
        if(row.State=="Undone")return row.Plan.Family is "Copy" or "Move"
            ? row.Plan.OutputArchive:SourcePath(row,history,depth+1);
        var consumers=history.Where(x=>x.Plan.Consumes==row.Plan.Id && x.Plan.Family!="Copy" &&
            x.State!="Undone").ToArray();
        if(consumers.Length>1)throw new IOException("R30 ambiguous active native owner.");
        if(consumers.Length==1)
        {
            var child=consumers[0];
            if(child.Plan.Family=="Move")
            {
                if(child.State is "SourceArchived" or "CleanupIntent" or "Committed" or "UndoIntent")
                    return child.Plan.OriginalArchive;
            }
            else if(child.State is "Committed" or "UndoIntent")return OutputPath(child,history,depth+1);
        }
        return row.Plan.Destination;
    }
    private static string SourcePath(R30Operation row,IReadOnlyList<R30Operation> history,int depth=0)
        =>row.Plan.Consumes is {} parent?OutputPath(history.Single(x=>x.Plan.Id==parent),history,depth+1):row.Plan.Source;
    internal static void Source(R30Operation row,IReadOnlyList<R30Operation> history)
    {
        var p=row.Plan;
        string source=SourcePath(row,history);
        Owned(source,p.Identity,p.Hash,p.Length,p.Ticks);
    }
    internal static void Terminal(R30Operation row,IReadOnlyList<R30Operation> history)
    {
        var p=row.Plan;Directories(p.Directories);
        if(row.State is not ("Committed" or "Undone"))
            throw new IOException("R30 outstanding "+row.State+" requires independent recovery review; no replay.");
        Vacant(p.Temp);
        if(p.Family=="Copy")
        {
            Source(row,history);
            Owned(OutputPath(row,history),row.OutputIdentity,p.Hash,p.Length,row.OutputTicks);
            Vacant(row.State=="Committed"?p.OutputArchive:p.Destination);Vacant(p.OriginalArchive);
        }
        else if(p.Family=="Move")
        {
            if(row.State=="Committed")Owned(p.OriginalArchive,p.Identity,p.Hash,p.Length,p.Ticks);
            else Source(row,history);
            Owned(OutputPath(row,history),row.OutputIdentity,p.Hash,p.Length,row.OutputTicks);
            Vacant(row.State=="Committed"?p.Source:p.OriginalArchive);
            Vacant(row.State=="Committed"?p.OutputArchive:p.Destination);
        }
        else
        {
            if(row.State=="Committed")Owned(OutputPath(row,history),p.Identity,p.Hash,p.Length,p.Ticks);
            else Source(row,history);
            Vacant(row.State=="Committed"?p.Source:p.Destination);
            Vacant(p.OriginalArchive);Vacant(p.OutputArchive);
        }
    }
    internal static IReadOnlyList<M6JDependencyEdge> Graph(IReadOnlyList<R30Operation> history)
    {
        var report=M6JDependencyInspector.Analyze(history.Select(x=>x.Footprint).ToArray());
        // This is a conflict veto, NEVER positive native proof. Keep actual
        // durable states and unverified diagnostic flags. Analyze still emits
        // its complete conflict set for well-formed intermediate footprints;
        // only the separate stage witnesses can prove current native ownership.
        if(report.ExaminedRows!=history.Count)
            throw new IOException("R30 incomplete dependency graph: "+report.Explanation);
        // Directed edges require an exact consumed installed identity. All of
        // the accepted inspector's other conflict kinds remain blocking.
        bool Ancestor(R30Operation ancestor,R30Operation child)
        {
            int depth=0;
            while(child.Plan.Consumes is {} id)
            {
                if(++depth>512)throw new IOException("R30 dependency chain bound exceeded.");
                var parent=history.SingleOrDefault(x=>x.Plan.Id==id)
                    ?? throw new IOException("R30 consumed predecessor missing.");
                if(parent.Plan.Epoch>=child.Plan.Epoch)throw new IOException("R30 dependency cycle/epoch inversion.");
                if(parent.Plan.Id==ancestor.Plan.Id)return true;
                child=parent;
            }
            return false;
        }
        foreach(var child in history.Where(x=>x.Plan.Consumes.HasValue))
        {
            var parent=history.SingleOrDefault(x=>x.Plan.Id==child.Plan.Consumes);
            if(parent is null || parent.Plan.Epoch>=child.Plan.Epoch || parent.Plan.Family=="Reject" ||
                child.Plan.Source!=parent.Plan.Destination || child.Plan.Identity!=parent.OutputIdentity ||
                child.Plan.Hash!=parent.Plan.Hash || child.Plan.Length!=parent.Plan.Length ||
                child.Plan.Ticks!=parent.OutputTicks)
                throw new IOException("R30 missing or altered directed native predecessor.");
        }
        foreach(var edge in report.Edges)
        {
            var a=history.Single(x=>x.Plan.Id.ToString("N")==edge.FirstJournalId);
            var b=history.Single(x=>x.Plan.Id.ToString("N")==edge.SecondJournalId);
            var child=Ancestor(a,b)?b:Ancestor(b,a)?a:null;
            var parent=child==a?b:a;
            if(child is null ||
                edge.Kind is not (M6JConflictKind.SamePhoto or M6JConflictKind.SameRecordedPath or
                    M6JConflictKind.SameRecordedNativeIdentity))
                throw new IOException("R30 ambiguous dependency: "+edge.Kind+" "+edge.Explanation);
            foreach(var x in child.Footprint.Claims)
            foreach(var y in parent.Footprint.Claims)
                if(Canonical(x.Path)==Canonical(y.Path) &&
                    !(child.Plan.Consumes==parent.Plan.Id && x.Role=="Source" && y.Role=="Destination"))
                    throw new IOException("R30 additional path correlation beyond consumed output.");
        }
        return report.Edges;
    }
    internal static void Fixture(R30Fixture fixture,string database)
    {
        if(string.IsNullOrWhiteSpace(fixture.Root) || !Path.IsPathFullyQualified(fixture.Root) ||
            fixture.Directories is null || fixture.Directories.Length!=5 ||
            fixture.Seeds is null || fixture.Seeds.Length is <4 or >256)
            throw new IOException("R30 malformed fixture provenance; preserve all files.");
        string root=fixture.Root;
        if(!NativeFileCapability.IsSupported || fixture.Installation==Guid.Empty ||
            !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
            !SortingWorkspace.PathComparer.Equals(Directory.GetParent(root)?.FullName,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(database),Path.Combine(root,"state")) ||
            File.ReadAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"))!="synthetic-interruption-only" ||
            File.ReadAllText(Path.Combine(root,"R30_GENERATED_ONLY.marker"))!=Json(fixture))
            throw new IOException("R30 generated fixture enrollment lost; ordinary authority locked.");
        Directories(fixture.Directories);
        foreach(var marker in new[]{"P3B2_TEST_ONLY.marker","R30_GENERATED_ONLY.marker"})
            if(!NativeFileCapability.IsSingleLinkRegularFile(Path.Combine(root,marker)))
                throw new IOException("R30 linked fixture marker.");
    }
}
