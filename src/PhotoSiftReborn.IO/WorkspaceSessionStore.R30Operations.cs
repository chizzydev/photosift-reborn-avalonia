using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PhotoSiftReborn;

public sealed partial class WorkspaceSessionStore
{
    internal void R30Exclusive(Action action) { lock(_dbGate) action(); }
    private SqliteCommand R30Command(SqliteTransaction tx,string sql,
        params (string Name,object Value)[] values)
    {
        var cmd=_connection.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (name,value) in values)cmd.Parameters.AddWithValue(name,value);
        return cmd;
    }
    private bool R30Exists(SqliteTransaction tx)
    {
        using var c=R30Command(tx,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='r30_meta'");
        return Convert.ToInt64(c.ExecuteScalar())==1;
    }
    // A surviving envelope/review/event table also identifies damaged enrollment.
    // Bind inspection reads to its transaction; legacy callers check before one.
    private bool R30EnrollmentDetected(SqliteTransaction? transaction=null)
    {
        using var c=_connection.CreateCommand();
        c.Transaction=transaction;
        c.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name GLOB 'r30_*'";
        string? parent=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)?.FullName;
        long tables=Convert.ToInt64(c.ExecuteScalar());
        c.CommandText="PRAGMA user_version";
        return tables!=0 || Convert.ToInt64(c.ExecuteScalar())>=20 || (parent is not null &&
            (File.Exists(Path.Combine(parent,"R30_GENERATED_ONLY.marker")) ||
             Directory.Exists(Path.Combine(parent,"R30_GENERATED_ONLY.marker"))));
    }
    // All legacy state writers, not just typed rows, share this fail-closed gate.
    internal void RequireNoR30Writer()
    {
        lock(_dbGate)
        {
            if(R30EnrollmentDetected())
                throw new IOException("R30 enrolled history requires exact operation-scoped authority.");
        }
    }
    internal void R30Enroll(R30Fixture fixture)
    {
        lock(_dbGate)
        {
            R30Native.Fixture(fixture,DatabasePath);
            using var tx=_connection.BeginTransaction();
            if(R30Exists(tx))throw new IOException("R30 fixture cannot be reenrolled.");
            // All legacy rows, across ALL sessions/families/auxiliaries, must
            // be empty. Existing provenance is never upgraded by enrollment.
            R30Raw(tx,out _,out _,allowNew:true);
            using var c=R30Command(tx,"""
                CREATE TABLE r30_meta(id INTEGER PRIMARY KEY CHECK(id=1),fixture TEXT NOT NULL,seal TEXT NOT NULL);
                CREATE TABLE r30_operations(id TEXT PRIMARY KEY,family TEXT NOT NULL,session TEXT NOT NULL,epoch INTEGER NOT NULL UNIQUE,json TEXT NOT NULL);
                CREATE TABLE r30_reviews(id TEXT PRIMARY KEY,operation TEXT NOT NULL,action TEXT NOT NULL,json TEXT NOT NULL,state TEXT NOT NULL CHECK(state IN ('Reviewed','Consumed')),UNIQUE(operation,action));
                CREATE TABLE r30_events(sequence INTEGER PRIMARY KEY,json TEXT NOT NULL);
                """
            );c.ExecuteNonQuery();
            using var insert=R30Command(tx,"INSERT INTO r30_meta VALUES(1,$fixture,'')",
                ("$fixture",R30Native.Json(fixture)));insert.ExecuteNonQuery();
            // Optional generated enrollment only. Ordinary databases remain
            // v19; older binaries must refuse to write a new v20 ledger.
            using var version=R30Command(tx,"PRAGMA user_version=20");version.ExecuteNonQuery();
            R30Reseal(tx);tx.Commit();
        }
    }
    internal void R30RequirePristineGeneratedStore()
    {
        lock(_dbGate)
        {
            using var tx=_connection.BeginTransaction();
            using var checkpoint=R30Command(tx,"SELECT COUNT(*) FROM workspace_checkpoint");
            if(R30Exists(tx) || Convert.ToInt64(checkpoint.ExecuteScalar())!=0)
                throw new IOException("R30 provisioning cannot replace an existing checkpoint or enrollment.");
            R30Raw(tx,out _,out _,allowNew:true);tx.Commit();
        }
    }
    // Enumerate even unknown auxiliary tables. Empty unknown schema is not
    // authority; populated unknown provenance is always HOLD. Identifiers are
    // SQLite metadata, safely quoted; all values use bound parameters.
    private string R30Raw(SqliteTransaction tx,out string history,out Dictionary<string,string> journals,
        bool allowNew=false)
    {
        using(var schema=R30Command(tx,"SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view')"))
            if(Convert.ToInt64(schema.ExecuteScalar())!=0)
                throw new IOException("R30 unverified trigger/view schema; no native authority.");
        var tables=new List<string>();
        using(var c=R30Command(tx,"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
        using(var r=c.ExecuteReader())while(r.Read())
        {
            if(tables.Count>=64)throw new IOException("R30 table bound exceeded.");
            tables.Add(r.GetString(0));
        }
        var all=new List<string>();var predecessors=new List<string>();
        journals=new(StringComparer.Ordinal);int rows=0;long characters=0;
        foreach(var table in tables)
        {
            if(table is "workspace_checkpoint" or "installation_identity" or "r30_meta")continue;
            var values=new List<string>();
            using var c=R30Command(tx,"SELECT * FROM \""+table.Replace("\"","\"\"")+"\" LIMIT 8194");
            using var r=c.ExecuteReader();
            while(r.Read())
            {
                if(++rows>16384 || values.Count>=8192)throw new IOException("R30 durable history bound exceeded.");
                var cells=new string?[r.FieldCount];
                for(int i=0;i<cells.Length;i++)
                {
                    cells[i]=r.IsDBNull(i)?null:Convert.ToString(r.GetValue(i),System.Globalization.CultureInfo.InvariantCulture);
                    if(cells[i]?.Length>1024*1024)throw new IOException("R30 oversized history cell.");
                    characters+=cells[i]?.Length ?? 0;
                    if(characters>64L*1024*1024)throw new IOException("R30 aggregate history size bound exceeded.");
                }
                values.Add(R30Native.Json(cells));
                if(table is "copy_items" or "move_items" or "rename_items" or "reject_items")
                {
                    if(journals.Count>=512 || cells.Length!=(table=="reject_items"?3:4) ||
                        !journals.TryAdd(table+":"+cells[0],R30Native.Json(cells)))
                        throw new IOException("R30 malformed/duplicate/over-limit native history.");
                }
                else if(table is not ("r30_operations" or "r30_reviews" or "r30_events"))
                    throw new IOException("R30 unverified auxiliary provenance: "+table);
                if(allowNew)throw new IOException("R30 enrollment refuses existing native or auxiliary history.");
            }
            values.Sort(StringComparer.Ordinal);
            string image=R30Native.Json(new {Table=table,Rows=values});all.Add(image);
            if(table!="r30_reviews")predecessors.Add(image);
        }
        history=R30Native.Digest(R30Native.Json(predecessors));
        return R30Native.Digest(R30Native.Json(all));
    }
    private void R30Reseal(SqliteTransaction tx)
    {
        string seal=R30Raw(tx,out _,out _);
        using var c=R30Command(tx,"UPDATE r30_meta SET seal=$seal WHERE id=1",("$seal",seal));
        if(c.ExecuteNonQuery()!=1)throw new IOException("R30 anchor missing.");
    }
    private void R30VerifyCommit(SqliteTransaction tx,string checkpoint,IReadOnlyList<R30Operation> expected)
    {
        // Re-read actual SQL after every write, not the pre-write snapshot.
        // This closes checkpoint revocation and unrelated-row changes at the
        // atomic boundary, including effects from unexpected SQLite schema.
        var final=R30Read(tx);
        if(final.Checkpoint!=checkpoint || R30Native.Json(final.Operations)!=R30Native.Json(expected))
            throw new IOException("R30 checkpoint or unrelated journal changed inside the durable transaction.");
    }
    private R30Snapshot R30Read(SqliteTransaction tx)
    {
        if(!R30Exists(tx))throw new IOException("R30 fixture is not enrolled.");
        using(var version=R30Command(tx,"PRAGMA user_version"))
            if(Convert.ToInt64(version.ExecuteScalar())!=20)throw new IOException("R30 schema provenance changed.");
        string rawFixture,seal;
        using(var c=R30Command(tx,"SELECT fixture,seal FROM r30_meta WHERE id=1"))
        using(var r=c.ExecuteReader())
        {
            if(!r.Read())throw new IOException("R30 anchor missing.");
            rawFixture=r.GetString(0);seal=r.GetString(1);
        }
        var fixture=R30Native.Decode<R30Fixture>(rawFixture);
        R30Native.Fixture(fixture,DatabasePath);
        using(var install=R30Command(tx,"SELECT token FROM installation_identity WHERE id=1"))
            if(install.ExecuteScalar() as string!=fixture.Installation.ToString("N"))
                throw new IOException("R30 installation changed.");
        if(R30Raw(tx,out string history,out var journals)!=seal)
            throw new IOException("R30 history/review seal changed or rows missing; preserve all evidence.");
        string checkpoint;WorkspaceSessionSnapshot saved;
        using(var c=R30Command(tx,"SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1"))
        using(var r=c.ExecuteReader())
        {
            if(!r.Read() || r.GetInt64(1)!=0)throw new IOException("R30 missing/unsafe checkpoint.");
            string json=r.GetString(0);checkpoint=R30Native.Digest(json);
            saved=R30Native.Decode<WorkspaceSessionSnapshot>(json);
            if(saved.SessionId==Guid.Empty || saved.Revision<0 || saved.Photos is null || saved.Photos.Length>512 ||
                saved.Photos.Any(x=>x is null || x.Id==Guid.Empty || string.IsNullOrWhiteSpace(x.Path)) ||
                saved.Photos.Select(x=>x.Id).Distinct().Count()!=saved.Photos.Length ||
                !SortingWorkspace.PathComparer.Equals(saved.SourceRoot,Path.Combine(fixture.Root,"source")) ||
                !SortingWorkspace.PathComparer.Equals(saved.DestinationRoot,Path.Combine(fixture.Root,"output")))
                throw new IOException("R30 malformed or outside-root saved checkpoint.");
        }
        var operations=new List<R30Operation>();
        using(var c=R30Command(tx,"SELECT id,family,session,epoch,json FROM r30_operations ORDER BY epoch"))
        using(var r=c.ExecuteReader())while(r.Read())
        {
            if(operations.Count>=512)throw new IOException("R30 operation limit exceeded.");
            var row=R30Native.Decode<R30Operation>(r.GetString(4));
            var p=row.Plan ?? throw new IOException("R30 operation plan missing.");
            if(p.Id==Guid.Empty || p.Session==Guid.Empty || p.Photo==Guid.Empty ||
                p.PlanId==Guid.Empty || p.Epoch!=operations.Count+1 || p.Length<1 || p.Ticks<1 ||
                p.Installation!=fixture.Installation || p.Id.ToString("N")!=r.GetString(0) ||
                p.Family!=r.GetString(1) || p.Session.ToString("N")!=r.GetString(2) || p.Epoch!=r.GetInt64(3) ||
                p.Family is not ("Copy" or "Move" or "Rename" or "Reject"))
                throw new IOException("R30 envelope identity/epoch inconsistent.");
            R30ValidatePlan(fixture,p);
            string table=R30Table(p.Family);
            string image=R30Native.Json(p.Family=="Reject"
                ? new[]{p.Id.ToString("N"),p.Session.ToString("N"),row.NativeJson}
                : new[]{p.Id.ToString("N"),p.Session.ToString("N"),
                    p.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),row.NativeJson});
            if(!journals.Remove(table+":"+p.Id.ToString("N"),out var actual) || actual!=image)
                throw new IOException("R30 typed journal/envelope mismatch or missing provenance.");
            operations.Add(row);
        }
        if(journals.Count!=0)throw new IOException("R30 legacy/foreign journal rows have no reviewed R30 provenance.");
        return new(fixture,operations.ToArray(),history,checkpoint,saved);
    }
    internal static string R30Table(string family) => family switch {
        "Copy"=>"copy_items","Move"=>"move_items","Rename"=>"rename_items",
        "Reject"=>"reject_items",_=>throw new IOException("R30 unknown family.")};
    internal static void R30ValidatePlan(R30Fixture fixture,R30Plan p)
    {
        string recovery=Path.Combine(fixture.Root,"r30-recovery");
        string output=Path.Combine(fixture.Root,"output");
        string source=Path.Combine(fixture.Root,"source");
        string token=p.Id.ToString("N");
        if(p.Temp!=Path.Combine(Path.GetDirectoryName(p.Destination)!,".r30-"+token+".part") ||
            p.OriginalArchive!=Path.Combine(recovery,token+"-original.png") ||
            p.OutputArchive!=Path.Combine(recovery,token+"-output.png") ||
            !(SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Source),source) ||
                (p.Consumes.HasValue &&
                 R30Native.Inside(p.Source,output))) ||
            (p.Family is "Copy" or "Move" &&
                !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Destination),output)) ||
            (p.Family=="Reject" && p.Destination!=Path.Combine(recovery,token+"-reject.png")) ||
            (p.Family=="Rename" && !SortingWorkspace.PathComparer.Equals(
                Path.GetDirectoryName(p.Source),Path.GetDirectoryName(p.Destination))) ||
            p.Directories is null || p.Directories.Length!=fixture.Directories.Length ||
            R30Native.Json(p.Directories)!=R30Native.Json(fixture.Directories))
            throw new IOException("R30 source/destination/recovery path scope changed.");
        var paths=new[]{p.Source,p.Destination,p.Temp,p.OriginalArchive,p.OutputArchive};
        if(paths.Select(R30Native.Canonical).Distinct().Count()!=paths.Length)
            throw new IOException("R30 operation aliases its own paths.");
        if(p.Family=="Rename")DurableRenameCoordinator.ValidateFilename(p.Source,Path.GetFileName(p.Destination));
    }
    internal R30Snapshot R30Load()
    {
        lock(_dbGate){using var tx=_connection.BeginTransaction();var s=R30Read(tx);tx.Commit();return s;}
    }
    internal R30Review R30Approve(R30Operation operation,string action,bool acknowledge,
        Action<R30Snapshot> verify,string witness)
    {
        lock(_dbGate)
        {
            if(!acknowledge || (action is not ("Apply" or "Undo") &&
                action!="Acknowledge/"+operation.State && action!="Continue/"+operation.State))
                throw new IOException("R30 independent exact review acknowledgement required.");
            using var tx=_connection.BeginTransaction();var s=R30Read(tx);verify(s);
            var review=new R30Review(Guid.NewGuid(),action,operation,s.Digest,s.Checkpoint,witness);
            using var c=R30Command(tx,"INSERT INTO r30_reviews VALUES($id,$op,$action,$json,'Reviewed')",
                ("$id",review.Id.ToString("N")),("$op",operation.Plan.Id.ToString("N")),
                ("$action",action),("$json",R30Native.Json(review)));
            try{c.ExecuteNonQuery();}catch(SqliteException ex){throw new IOException("R30 review already exists; cannot silently reissue.",ex);}
            R30Reseal(tx);R30VerifyCommit(tx,s.Checkpoint,s.Operations);tx.Commit();return review;
        }
    }
    internal R30Operation R30Consume(R30Review review,string nextState,
        Action<R30Snapshot> verify,Action? afterConsume=null,R30Operation? terminal=null)
    {
        lock(_dbGate)
        {
            using var tx=_connection.BeginTransaction();var s=R30Read(tx);
            if(s.Digest!=review.History || s.Checkpoint!=review.Checkpoint)
                throw new IOException("R30 stale predecessor/checkpoint approval.");
            verify(s);
            using(var c=R30Command(tx,"UPDATE r30_reviews SET state='Consumed' WHERE id=$id AND json=$json AND state='Reviewed'",
                ("$id",review.Id.ToString("N")),("$json",R30Native.Json(review))))
                if(c.ExecuteNonQuery()!=1)throw new IOException("R30 forged or consumed review; no replay.");
            afterConsume?.Invoke();
            if(terminal is not null && ((review.Action!="Acknowledge/"+review.Operation.State &&
                review.Action!="Continue/"+review.Operation.State) || terminal.State!=nextState ||
                R30Native.Json(terminal.Plan)!=R30Native.Json(review.Operation.Plan) ||
                (R30Native.Json(terminal)!=R30Native.Json(review.Operation) && !R30Transition(review.Operation,terminal))))
                throw new IOException("R30 invalid recovery acknowledgement image.");
            var next=terminal ?? review.Operation with {State=nextState};
            R30Write(tx,review.Action=="Apply"?null:review.Operation,next);
            verify(s); // native witness AGAIN, after consumption and Intent insert
            R30Reseal(tx);
            var expected=review.Action=="Apply"?s.Operations.Append(next).ToArray():
                s.Operations.Select(x=>x.Plan.Id==next.Plan.Id?next:x).ToArray();
            R30VerifyCommit(tx,s.Checkpoint,expected);tx.Commit();return next;
        }
    }
    internal void R30Advance(R30Operation previous,R30Operation next,Action<R30Snapshot> verify)
    {
        lock(_dbGate)
        {
            using var tx=_connection.BeginTransaction();var s=R30Read(tx);
            if(!s.Operations.Any(x=>R30Native.Json(x)==R30Native.Json(previous)))
                throw new IOException("R30 exact transition predecessor missing.");
            if(R30Native.Json(previous.Plan)!=R30Native.Json(next.Plan) ||
                !R30Transition(previous,next))throw new IOException("R30 illegal immutable journal transition.");
            verify(s);R30Write(tx,previous,next);R30Reseal(tx);
            R30VerifyCommit(tx,s.Checkpoint,s.Operations.Select(x=>x.Plan.Id==next.Plan.Id?next:x).ToArray());tx.Commit();
        }
    }
    private static bool R30Transition(R30Operation a,R30Operation b)
    {
        string? expected=a.State switch {
            "Intent"=>a.Plan.Family is "Copy" or "Move"?"TempReady":"Committed",
            "TempReady"=>"PublishIntent","PublishIntent"=>"Published",
            "Published"=>a.Plan.Family=="Move"?"ArchiveIntent":"CleanupIntent",
            "ArchiveIntent"=>"SourceArchived","SourceArchived"=>"CleanupIntent",
            "CleanupIntent"=>"Committed",
            "UndoIntent"=>a.Plan.Family=="Move"?"SourceRestored":"Undone",
            "SourceRestored"=>"UndoOutputIntent","UndoOutputIntent"=>"Undone",_=>null};
        return b.State==expected &&
            (a.TempIdentity==b.TempIdentity || (a.State=="Intent" && b.State=="TempReady" && a.TempIdentity is null && b.TempIdentity is not null)) &&
            (a.OutputIdentity==b.OutputIdentity ||
                (a.State is "Intent" or "PublishIntent" && a.OutputIdentity is null && b.OutputIdentity is not null)) &&
            (a.OutputTicks==b.OutputTicks || (a.OutputTicks==0 && b.OutputTicks>0));
    }
    private void R30Write(SqliteTransaction tx,R30Operation? previous,R30Operation next)
    {
        var p=next.Plan;string table=R30Table(p.Family);
        if(previous is null)
        {
            if(next.State!="Intent")throw new IOException("R30 first write requires Intent.");
            using var c=R30Command(tx,"INSERT INTO r30_operations VALUES($id,$family,$session,$epoch,$json)",
                ("$id",p.Id.ToString("N")),("$family",p.Family),("$session",p.Session.ToString("N")),
                ("$epoch",p.Epoch),("$json",R30Native.Json(next)));c.ExecuteNonQuery();
            using var journal=R30Command(tx,"INSERT INTO "+table+(p.Family=="Reject"
                ? " VALUES($id,$session,$json)" : " VALUES($id,$session,$epoch,$json)"),
                ("$id",p.Id.ToString("N")),("$session",p.Session.ToString("N")),
                ("$epoch",p.Epoch),("$json",next.NativeJson));journal.ExecuteNonQuery();
        }
        else
        {
            using var c=R30Command(tx,"UPDATE r30_operations SET json=$next WHERE id=$id AND json=$old",
                ("$id",p.Id.ToString("N")),("$old",R30Native.Json(previous)),("$next",R30Native.Json(next)));
            if(c.ExecuteNonQuery()!=1)throw new IOException("R30 envelope CAS failed.");
            using var j=R30Command(tx,"UPDATE "+table+" SET json=$next WHERE id=$id AND json=$old",
                ("$id",p.Id.ToString("N")),("$old",previous.NativeJson),("$next",next.NativeJson));
            if(j.ExecuteNonQuery()!=1)throw new IOException("R30 typed journal CAS failed.");
        }
        using var audit=R30Command(tx,"INSERT INTO r30_events(json) VALUES($json)",
            ("$json",R30Native.Json(new {p.Id,p.Family,p.Session,p.Epoch,Before=previous?.State,
                After=next.State,Image=R30Native.Digest(R30Native.Json(next))})));
        audit.ExecuteNonQuery();
    }
    public R30HistoryView InspectR30History()
    {
        lock(_dbGate)
        {
            try
            {
                using var tx=_connection.BeginTransaction();
                if(!R30Exists(tx))
                {
                    if(R30EnrollmentDetected(tx))
                        throw new IOException("R30 enrolled fixture is missing its durable anchor; preserve all journals.");
                    return new(false,true,"Ordinary mixed-family authority remains locked.",[],[]);
                }
                var s=R30Read(tx);tx.Commit();
                var edges=R30Native.Graph(s.Operations);
                foreach(var row in s.Operations)R30Native.Terminal(row,s.Operations);
                return new(true,true,"Generated-only history verifies now. Ordinary authority locked; no automatic recovery replay.",
                    s.Operations.Select(x=>x.View).ToArray(),edges);
            }
            catch(Exception ex) when(ex is IOException or SqliteException or JsonException or
                InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                // Preserve a bounded recorded inventory even when native proof
                // fails. Each displayed state is explicitly unverified.
                var rows=new List<R30OperationView>();
                IReadOnlyList<M6JDependencyEdge> edges=[];
                try
                {
                    using var tx=_connection.BeginTransaction();
                    var known=new Dictionary<string,R30Operation>();
                    using(var c=R30Command(tx,"SELECT json FROM r30_operations ORDER BY epoch LIMIT 512"))
                    using(var r=c.ExecuteReader())while(r.Read())
                    {
                        try
                        {
                            var row=R30Native.Decode<R30Operation>(r.GetString(0));
                            known.TryAdd(row.Plan.Family+":"+row.Plan.Id.ToString("N"),row);
                        }catch(Exception invalid) when(invalid is IOException or JsonException or NullReferenceException) { }
                    }
                    edges=M6JDependencyInspector.Analyze(known.Values.Select(x=>x.Footprint).ToArray(),false).Edges;
                    foreach(var family in new[]{"Copy","Move","Rename","Reject"})
                    {
                        using var c=R30Command(tx,"SELECT id,session,json FROM "+R30Table(family)+" ORDER BY rowid LIMIT 513");
                        using var r=c.ExecuteReader();
                        while(r.Read() && rows.Count<512)
                        {
                            string id=r.GetString(0),session=r.GetString(1),json=r.GetString(2);
                            var key=family+":"+id;
                            if(known.Remove(key,out var recorded))
                            {rows.Add(recorded.View with {State=recorded.State+" (native evidence unverified)"});continue;}
                            Guid.TryParseExact(id,"N",out var operationId);Guid.TryParseExact(session,"N",out var sessionId);
                            string state="Invalid or foreign row",source="",destination="";long epoch=0;
                            if(json.Length<=32768)
                            {
                                try
                                {
                                    using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
                                    if(root.ValueKind!=JsonValueKind.Object)throw new JsonException("Native row is not an object.");
                                    string Read(string name)=>root.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String?value.GetString() ?? "":"";
                                    source=Read("Source");destination=Read(family=="Rename"?"Target":family=="Reject"?"Archive":"Destination");
                                    state=Read("State")+" (unverified provenance)";
                                    if(root.TryGetProperty("Sequence",out var sequence) && sequence.ValueKind==JsonValueKind.Number)sequence.TryGetInt64(out epoch);
                                }catch(JsonException) { }
                            }
                            rows.Add(new(family,operationId,sessionId,epoch,state,source,destination,null));
                        }
                    }
                    foreach(var orphan in known.Values.Take(512-rows.Count))
                        rows.Add(orphan.View with {State=orphan.State+" (native row missing)"});
                }catch(Exception inner) when(inner is IOException or SqliteException or JsonException or ArgumentException) { }
                return new(true,false,"HOLD: "+ex.Message,rows,edges);
            }
        }
    }
}
