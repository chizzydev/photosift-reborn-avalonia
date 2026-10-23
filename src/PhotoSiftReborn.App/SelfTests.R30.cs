using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PhotoSiftReborn;

internal static partial class SelfTests
{
    private sealed class R30TestFixture : IDisposable
    {
        internal readonly string Root;
        internal WorkspaceSessionStore Db;
        internal readonly SortingWorkspace Work;
        internal R30GeneratedOperationCoordinator Engine;
        internal R30TestFixture(int count=16)
        {
            Root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P1_DISPOSABLE_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);Db=new(Path.Combine(Root,"state"));
            Work=R30GeneratedOperationCoordinator.ProvisionGeneratedFixture(Db,count);
            Engine=new(Db);
        }
        internal R30Plan Plan(string family,int index=0,string? destination=null,Guid? consumes=null)
            =>Engine.Prepare(family,Work.Photos[index].Id,destination,consumes);
        internal R30Operation Apply(string family,int index=0) => Engine.Execute(Engine.Review(Plan(family,index),true));
        internal void Reopen()
        {
            Db.Dispose();Db=new(Path.Combine(Root,"state"));Engine=new(Db);
        }
        internal void Sql(string sql,params (string Key,object Value)[] parameters)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=Db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
            c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;
            foreach(var (key,value) in parameters)cmd.Parameters.AddWithValue(key,value);
            cmd.ExecuteNonQuery();
        }
        internal string Image()
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=Db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            c.Open();var rows=new List<string>();
            foreach(string table in new[]{"copy_items","move_items","rename_items","reject_items",
                "r30_operations","r30_reviews","r30_events","r30_meta"})
            {
                using var cmd=c.CreateCommand();cmd.CommandText="SELECT * FROM "+table+" ORDER BY rowid";
                using var reader=cmd.ExecuteReader();while(reader.Read())
                    rows.Add(table+JsonSerializer.Serialize(Enumerable.Range(0,reader.FieldCount)
                        .Select(i=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i))).ToArray()));
            }
            return JsonSerializer.Serialize(rows);
        }
        internal string Files() => JsonSerializer.Serialize(Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories)
            .Where(x=>!R30Native.Inside(x,Path.Combine(Root,"state"))).OrderBy(x=>x,StringComparer.Ordinal)
            .Select(x=>new {Path=x,Id=NativeFileCapability.Identity(x),Hash=R30Native.Hash(x)}).ToArray());
        internal void NoChange(Action action)
        {
            string sql=Image(),files=Files();Refuse<IOException>(action);
            Ensure(sql==Image(),"Refusal changed durable rows/reviews/audit");
            Ensure(files==Files(),"Refusal changed native files");
        }
        public void Dispose()=>Db.Dispose(); // keep generated interruption evidence
    }
    private static void RunR30Tests()
    {
        string[] families={"Copy","Move","Rename","Reject"};
        foreach(string family in families)
        {
            Check("R30_"+family.ToUpperInvariant()+"_THREE_REVIEWED_COMMITS_SELECT_FIRST_UNDO",()=>
            {
                using var f=new R30TestFixture();var rows=Enumerable.Range(0,3).Select(i=>f.Apply(family,i)).ToArray();
                var unrelated=rows.Skip(1).Select(x=>x.NativeJson).ToArray();
                var outputIds=rows.Skip(1).Select(x=>NativeFileCapability.Identity(x.Plan.Destination)).ToArray();
                f.Reopen();var undone=f.Engine.Undo(f.Engine.ReviewUndo(family,rows[0].Plan.Id,true));
                Ensure(undone.State=="Undone" && f.Db.InspectR30History().Complete,"Selective first-row Undo failed");
                var snapshot=f.Db.R30Load();
                Ensure(snapshot.Operations.Skip(1).Select(x=>x.NativeJson).SequenceEqual(unrelated),"Unrelated journal bytes changed");
                Ensure(rows.Skip(1).Select(x=>NativeFileCapability.Identity(x.Plan.Destination)).SequenceEqual(outputIds),"Unrelated outputs changed identity");
                f.NoChange(()=>f.Engine.ReviewUndo(family,rows[0].Plan.Id,true));
                var fourth=f.Apply(family,3);Ensure(fourth.Plan.Epoch==4,"Arbitrary per-family ceiling remains");
            });
            Check("R30_"+family.ToUpperInvariant()+"_REVIEW_SINGLE_USE_FORGED_AND_REPLAY_REFUSED",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                f.NoChange(()=>f.Engine.Execute(review with {Id=Guid.NewGuid()}));
                f.NoChange(()=>f.Engine.Review(review.Operation.Plan,true));
                var committed=f.Engine.Execute(review);Ensure(committed.State=="Committed","Commit failed");
                f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_ATOMIC_REVIEW_INTENT_ROLLBACK",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                string image=f.Image(),files=f.Files();
                Refuse<InvalidOperationException>(()=>f.Engine.Execute(review,()=>throw new InvalidOperationException("rollback-cut")));
                Ensure(image==f.Image() && files==f.Files(),"Consume/Intent did not roll back atomically");
                f.Reopen();Ensure(f.Engine.Execute(review).State=="Committed","Rolled-back authority was lost");
            });
            Check("R30_"+family.ToUpperInvariant()+"_SOURCE_REPLACED_IDENTICAL_BYTES_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                var replacement=plan.Source+".replacement";File.Copy(plan.Source,replacement);
                File.SetLastWriteTimeUtc(replacement,new DateTime(plan.Ticks,DateTimeKind.Utc));
                File.Move(plan.Source,plan.Source+".retained");File.Move(replacement,plan.Source);
                Ensure(NativeFileCapability.Identity(plan.Source)!=plan.Identity,"Replacement did not change identity");
                f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_SOURCE_HARDLINK_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                NativeFileCapability.PublishLinkNoReplace(plan.Source,plan.Source+".foreign-link");
                f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_SOURCE_BYTES_CHANGED_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                using(var fs=new FileStream(plan.Source,FileMode.Open,FileAccess.Write,FileShare.None))fs.WriteByte(0);
                f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_FOREIGN_DESTINATION_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                File.WriteAllText(plan.Destination,"foreign owner");f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_FOREIGN_RECOVERY_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                File.WriteAllText(plan.OutputArchive,"foreign recovery");f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_FOREIGN_TEMPORARY_OCCUPANT_REFUSED",()=>
            {
                using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                File.WriteAllText(plan.Temp,"foreign temporary");f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_UNDO_FOREIGN_OCCUPANT_REFUSED",()=>
            {
                using var f=new R30TestFixture();var committed=f.Apply(family);
                var review=f.Engine.ReviewUndo(family,committed.Plan.Id,true);
                string occupant=family=="Copy"?committed.Plan.OutputArchive:committed.Plan.Source;
                File.WriteAllText(occupant,"new owner");f.NoChange(()=>f.Engine.Undo(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_UNDO_REVIEW_REQUIRED_AND_SINGLE_USE",()=>
            {
                using var f=new R30TestFixture();var committed=f.Apply(family);
                f.NoChange(()=>f.Engine.ReviewUndo(family,committed.Plan.Id,false));
                var review=f.Engine.ReviewUndo(family,committed.Plan.Id,true);
                f.NoChange(()=>f.Engine.Undo(review with {Action="Apply"}));
                f.Engine.Undo(review);f.NoChange(()=>f.Engine.Undo(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_IDENTICAL_OUTPUT_REPLACEMENT_REFUSES_UNDO",()=>
            {
                using var f=new R30TestFixture();var row=f.Apply(family);string output=row.Plan.Destination;
                File.Copy(output,output+".replacement");
                File.SetLastWriteTimeUtc(output+".replacement",new DateTime(row.OutputTicks,DateTimeKind.Utc));
                File.Move(output,output+".retained");File.Move(output+".replacement",output);
                Ensure(NativeFileCapability.Identity(output)!=row.OutputIdentity,"Output replacement reused old inode");
                f.NoChange(()=>f.Engine.ReviewUndo(family,row.Plan.Id,true));
            });
            Check("R30_"+family.ToUpperInvariant()+"_STALE_CHECKPOINT_REFUSED",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                var saved=f.Work.CaptureSession();
                // Deliberate out-of-band checkpoint fault; legacy Save is fenced.
                f.Sql("UPDATE workspace_checkpoint SET json=$json WHERE id=1",("$json",JsonSerializer.Serialize(saved with {Revision=saved.Revision+1})));
                f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_EXACT_PLAN_SUBSTITUTION_REFUSED",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                f.NoChange(()=>f.Engine.Execute(review with {Operation=review.Operation with {
                    Plan=review.Operation.Plan with {Photo=Guid.NewGuid()}}}));
            });
            Check("R30_"+family.ToUpperInvariant()+"_CHECKPOINT_REVOKED_AFTER_INTENT_BLOCKS_PHYSICAL_EFFECT",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);string files=f.Files();
                var revoked=new R30GeneratedOperationCoordinator(f.Db,cut=>
                {if(cut=="INTENT_DURABLE")f.Sql("UPDATE workspace_checkpoint SET json=$json WHERE id=1",
                    ("$json",JsonSerializer.Serialize(f.Work.CaptureSession() with {Revision=f.Work.Revision+1})));});
                Refuse<IOException>(()=>revoked.Execute(review));
                Ensure(files==f.Files() && f.Db.R30Load().Operations.Single().State=="Intent",
                    "Checkpoint revocation did not stop the first physical effect");
            });
            var cuts=family is "Copy" or "Move"
                ? new[]{"INTENT_DURABLE","TEMP_CREATED_UNACKNOWLEDGED","TEMP_DURABLE","PUBLISH_INTENT_DURABLE",
                    "PUBLISHED_UNACKNOWLEDGED","PUBLISHED_DURABLE","CLEANUP_INTENT_DURABLE","TEMP_REMOVED_UNACKNOWLEDGED"}
                : new[]{"INTENT_DURABLE","RELOCATED_UNACKNOWLEDGED"};
            if(family=="Move")cuts=cuts.Concat(new[]{"ARCHIVE_INTENT_DURABLE","ORIGINAL_ARCHIVED_UNACKNOWLEDGED","ORIGINAL_ARCHIVED_DURABLE"}).ToArray();
            foreach(string cut in cuts)
            {
                Check("R30_"+family.ToUpperInvariant()+"_COLD_CUT_"+cut,()=>
                {
                    using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                    var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==cut)throw new SimulatedInterruptionException(x);});
                    Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));
                    string image=f.Image(),files=f.Files();f.Reopen();
                    Ensure(image==f.Image() && files==f.Files(),"Cold reopen replayed or rewrote interrupted work");
                    Ensure(!f.Db.InspectR30History().Complete,"Interrupted operation was promoted automatically");
                    f.NoChange(()=>f.Engine.Execute(review));f.NoChange(()=>f.Plan("Copy",1));
                });
                Check("R30_"+family.ToUpperInvariant()+"_REVIEWED_CONTINUATION_"+cut,()=>
                {
                    using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                    var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==cut)throw new SimulatedInterruptionException(x);});
                    Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));f.Reopen();
                    if(cut=="TEMP_CREATED_UNACKNOWLEDGED")
                    {
                        f.NoChange(()=>f.Engine.ReviewContinuation(family,review.Operation.Plan.Id,true));return;
                    }
                    f.NoChange(()=>f.Engine.ReviewContinuation(family,review.Operation.Plan.Id,false));
                    var continuation=f.Engine.ReviewContinuation(family,review.Operation.Plan.Id,true);
                    var complete=f.Engine.Continue(continuation);
                    Ensure(complete.State=="Committed" && f.Db.InspectR30History().Complete,
                        "Explicit native continuation failed to commit");
                    f.NoChange(()=>f.Engine.Continue(continuation));
                });
            }
            var undoCuts=family=="Move"?new[]{"UNDO_INTENT_DURABLE","UNDO_SOURCE_RESTORED_UNACKNOWLEDGED",
                "UNDO_SOURCE_RESTORED_DURABLE","UNDO_OUTPUT_INTENT_DURABLE","UNDO_RELOCATED_UNACKNOWLEDGED"}
                :new[]{"UNDO_INTENT_DURABLE","UNDO_RELOCATED_UNACKNOWLEDGED"};
            foreach(string cut in undoCuts)
            {
                Check("R30_"+family.ToUpperInvariant()+"_COLD_CUT_"+cut,()=>
                {
                    using var f=new R30TestFixture();var row=f.Apply(family);
                    var review=f.Engine.ReviewUndo(family,row.Plan.Id,true);
                    var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==cut)throw new SimulatedInterruptionException(x);});
                    Refuse<SimulatedInterruptionException>(()=>interrupted.Undo(review));
                    string image=f.Image(),files=f.Files();f.Reopen();
                    Ensure(image==f.Image() && files==f.Files(),"Cold Undo recovery replayed filesystem work");
                    f.NoChange(()=>f.Engine.Undo(review));f.NoChange(()=>f.Engine.ReviewUndo(family,row.Plan.Id,true));
                });
                Check("R30_"+family.ToUpperInvariant()+"_REVIEWED_CONTINUATION_"+cut,()=>
                {
                    using var f=new R30TestFixture();var row=f.Apply(family);var review=f.Engine.ReviewUndo(family,row.Plan.Id,true);
                    var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==cut)throw new SimulatedInterruptionException(x);});
                    Refuse<SimulatedInterruptionException>(()=>interrupted.Undo(review));f.Reopen();
                    var continuation=f.Engine.ReviewContinuation(family,row.Plan.Id,true);
                    Ensure(f.Engine.Continue(continuation).State=="Undone" && f.Db.InspectR30History().Complete,
                        "Explicit Undo continuation failed");f.NoChange(()=>f.Engine.Continue(continuation));
                });
            }
            foreach(string terminalCut in new[]{"COMMITTED_DURABLE","UNDONE_DURABLE"})
                Check("R30_"+family.ToUpperInvariant()+"_COLD_TERMINAL_CUT_"+terminalCut,()=>
                {
                    using var f=new R30TestFixture();var plan=f.Plan(family);var review=f.Engine.Review(plan,true);
                    var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==terminalCut)throw new SimulatedInterruptionException(x);});
                    if(terminalCut=="COMMITTED_DURABLE")Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));
                    else
                    {
                        f.Engine.Execute(review);var undo=f.Engine.ReviewUndo(family,plan.Id,true);
                        Refuse<SimulatedInterruptionException>(()=>interrupted.Undo(undo));
                    }
                    string image=f.Image(),files=f.Files();f.Reopen();
                    Ensure(f.Db.InspectR30History().Complete && image==f.Image() && files==f.Files(),"Terminal cut changed on cold open");
                });
            Check("R30_"+family.ToUpperInvariant()+"_EXPLICIT_TERMINAL_RECOVERY_ACKNOWLEDGEMENT",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                string cut=family is "Copy" or "Move"?"TEMP_REMOVED_UNACKNOWLEDGED":"RELOCATED_UNACKNOWLEDGED";
                var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x==cut)throw new SimulatedInterruptionException(x);});
                Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));f.Reopen();
                f.NoChange(()=>f.Engine.ReviewRecovery(family,review.Operation.Plan.Id,false));
                var recovery=f.Engine.ReviewRecovery(family,review.Operation.Plan.Id,true);string files=f.Files();
                var terminal=f.Engine.AcknowledgeRecovery(recovery);
                Ensure(terminal.State=="Committed" && f.Files()==files && f.Db.InspectR30History().Complete,
                    "Explicit acknowledgement failed or mutated files");
                f.NoChange(()=>f.Engine.AcknowledgeRecovery(recovery));
            });
        }
        foreach(var order in new[]{families,families.Reverse().ToArray(),new[]{"Move","Copy","Reject","Rename","Copy","Move","Reject","Rename"}})
            Check("R30_MIXED_"+string.Join("_",order).ToUpperInvariant()+"_SELECTIVE_UNDO",()=>
            {
                using var f=new R30TestFixture();var rows=order.Select((family,i)=>f.Apply(family,i)).ToArray();
                f.Reopen();foreach(int index in Enumerable.Range(0,rows.Length).Where(i=>i%2==0))
                {
                    var target=rows[index];var before=f.Db.R30Load().Operations.Where(x=>x.Plan.Id!=target.Plan.Id).Select(x=>x.NativeJson).ToArray();
                    f.Engine.Undo(f.Engine.ReviewUndo(target.Plan.Family,target.Plan.Id,true));
                    Ensure(before.SequenceEqual(f.Db.R30Load().Operations.Where(x=>x.Plan.Id!=target.Plan.Id).Select(x=>x.NativeJson)),"Mixed Undo touched independent rows");
                }
                Ensure(f.Db.InspectR30History().Complete,"Mixed history lost native verification");
            });
        Check("R30_DIRECTED_COPY_DEPENDENCY_BLOCKS_PARENT_UNTIL_CHILD_UNDONE",()=>
        {
            using var f=new R30TestFixture();var parent=f.Apply("Copy");var independent=f.Apply("Move",1);
            var childPlan=f.Plan("Copy",consumes:parent.Plan.Id);var child=f.Engine.Execute(f.Engine.Review(childPlan,true));
            f.NoChange(()=>f.Engine.ReviewUndo("Copy",parent.Plan.Id,true));
            f.Engine.Undo(f.Engine.ReviewUndo("Copy",child.Plan.Id,true));
            f.Engine.Undo(f.Engine.ReviewUndo("Copy",parent.Plan.Id,true));
            Ensure(f.Db.InspectR30History().Complete && R30Native.Json(f.Db.R30Load().Operations.Single(x=>x.Plan.Id==independent.Plan.Id))==R30Native.Json(independent),
                "Directed dependency Undo lost unrelated Move or terminal source provenance");
        });
        Check("R30_TRANSITIVE_COPY_DEPENDENCY_AND_INDEPENDENT_REJECT",()=>
        {
            using var f=new R30TestFixture();var parent=f.Apply("Copy");var independent=f.Apply("Reject",1);
            var child=f.Engine.Execute(f.Engine.Review(f.Plan("Copy",consumes:parent.Plan.Id),true));
            var grandchild=f.Engine.Execute(f.Engine.Review(f.Plan("Copy",consumes:child.Plan.Id),true));
            f.Reopen();f.NoChange(()=>f.Engine.ReviewUndo("Copy",parent.Plan.Id,true));
            f.NoChange(()=>f.Engine.ReviewUndo("Copy",child.Plan.Id,true));
            foreach(var row in new[]{grandchild,child,parent})
                f.Engine.Undo(f.Engine.ReviewUndo("Copy",row.Plan.Id,true));
            Ensure(f.Db.InspectR30History().Complete && R30Native.Json(f.Db.R30Load().Operations.Single(x=>x.Plan.Id==independent.Plan.Id))==R30Native.Json(independent),
                "Transitive Undo changed independent Reject or lost source delegation");
        });
        foreach(string family in families)
        {
            Check("R30_"+family.ToUpperInvariant()+"_FOREIGN_OCCUPANT_AFTER_INTENT_PRESERVED",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan(family),true);
                var intruder=new R30GeneratedOperationCoordinator(f.Db,cut=>
                {if(cut=="INTENT_DURABLE")File.WriteAllText(review.Operation.Plan.Destination,"foreign-after-intent");});
                Refuse<IOException>(()=>intruder.Execute(review));
                Ensure(File.ReadAllText(review.Operation.Plan.Destination)=="foreign-after-intent" &&
                    NativeFileCapability.Identity(review.Operation.Plan.Source)==review.Operation.Plan.Identity,
                    "Post-Intent foreign destination was overwritten or source moved");
                f.Reopen();f.NoChange(()=>f.Engine.Execute(review));
            });
            Check("R30_"+family.ToUpperInvariant()+"_EXPLICIT_UNDO_TERMINAL_RECOVERY",()=>
            {
                using var f=new R30TestFixture();var row=f.Apply(family);var review=f.Engine.ReviewUndo(family,row.Plan.Id,true);
                var interrupted=new R30GeneratedOperationCoordinator(f.Db,cut=>
                {if(cut=="UNDO_RELOCATED_UNACKNOWLEDGED")throw new SimulatedInterruptionException(cut);});
                Refuse<SimulatedInterruptionException>(()=>interrupted.Undo(review));f.Reopen();
                var recovery=f.Engine.ReviewRecovery(family,row.Plan.Id,true);string files=f.Files();
                f.Engine.AcknowledgeRecovery(recovery);
                Ensure(f.Db.InspectR30History().Complete && files==f.Files(),"Undo acknowledgement changed files or failed ownership proof");
            });
        }
        Check("R30_PUBLICATION_THIRD_HARDLINK_BLOCKS_OWNED_TEMP_CLEANUP",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Copy"),true);var p=review.Operation.Plan;
            var intruder=new R30GeneratedOperationCoordinator(f.Db,cut=>
            {if(cut=="PUBLISHED_UNACKNOWLEDGED")NativeFileCapability.PublishLinkNoReplace(p.Temp,p.Destination+".foreign-link");});
            Refuse<IOException>(()=>intruder.Execute(review));
            Ensure(File.Exists(p.Temp) && File.Exists(p.Destination) && NativeFileCapability.RegularFileLinkCount(p.Temp)==3,
                "Unowned publication hard link was removed or ignored");
        });
        Check("R30_PREDECESSOR_TAMPER_AFTER_INTENT_PRESERVES_NEW_SOURCE",()=>
        {
            using var f=new R30TestFixture();var prior=f.Apply("Copy");var review=f.Engine.Review(f.Plan("Move",1),true);
            var intruder=new R30GeneratedOperationCoordinator(f.Db,cut=>
            {if(cut=="INTENT_DURABLE")File.WriteAllText(prior.Plan.Destination,"changed-prior");});
            Refuse<IOException>(()=>intruder.Execute(review));
            Ensure(NativeFileCapability.Identity(review.Operation.Plan.Source)==review.Operation.Plan.Identity &&
                !File.Exists(review.Operation.Plan.Destination),"Stale predecessor allowed physical Move effect");
        });
        Check("R30_CROSS_SESSION_EXTRA_VALID_ROW_IS_NOT_IGNORED",()=>
        {
            using var f=new R30TestFixture();var prior=f.Apply("Copy");var review=f.Engine.Review(f.Plan("Move",1),true);
            var row=f.Db.CopyItems(prior.Plan.Session).Single() with {Id=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=99};
            f.Sql("INSERT INTO copy_items VALUES($id,$s,99,$json)",("$id",row.Id.ToString("N")),("$s",row.SessionId.ToString("N")),("$json",JsonSerializer.Serialize(row)));
            f.NoChange(()=>f.Engine.Execute(review));
        });
        Check("R30_REVIEWED_OPERATIONS_ACROSS_SESSIONS_KEEP_PRIOR_SESSION_UNDO_SELECTIVE",()=>
        {
            using var f=new R30TestFixture();var first=f.Apply("Move");
            var next=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            next.OpenSource(Path.Combine(f.Root,"source"));next.SetDestinationRoot(Path.Combine(f.Root,"output"));
            // Explicit generated cross-session fixture setup, not a production
            // checkpoint grant. Every operation still needs its own fresh review.
            f.Sql("UPDATE workspace_checkpoint SET json=$json WHERE id=1",("$json",JsonSerializer.Serialize(next.CaptureSession())));
            var photo=next.Photos.Single(x=>Path.GetFileName(x.Path)=="r30-001.png");
            var plan=f.Engine.Prepare("Copy",photo.Id);var second=f.Engine.Execute(f.Engine.Review(plan,true));
            Ensure(first.Plan.Session!=second.Plan.Session && second.Plan.Epoch==2,"Session was reused or epoch reset");
            f.Reopen();f.Engine.Undo(f.Engine.ReviewUndo("Move",first.Plan.Id,true));
            Ensure(R30Native.Json(f.Db.R30Load().Operations.Single(x=>x.Plan.Id==second.Plan.Id))==R30Native.Json(second) &&
                f.Db.InspectR30History().Complete,"Old-session Undo changed the new-session committed operation");
        });
        Check("R30_NATIVE_HISTORY_BOUND_REFUSES_WITHOUT_TRUNCATED_AUTHORITY",()=>
        {
            using var f=new R30TestFixture();var first=f.Apply("Copy");var review=f.Engine.Review(f.Plan("Rename",1),true);
            var old=f.Db.CopyItems(first.Plan.Session).Single();
            var extras=Enumerable.Range(1,512).Select(i=>old with {Id=Guid.NewGuid(),Sequence=i+1}).ToArray();
            f.Sql("INSERT INTO copy_items SELECT replace(json_extract(value,'$.Id'),'-',''),replace(json_extract(value,'$.SessionId'),'-',''),json_extract(value,'$.Sequence'),value FROM json_each($rows)",
                ("$rows",JsonSerializer.Serialize(extras)));
            string image=f.Image(),files=f.Files();var refusal=Refuse<IOException>(()=>f.Engine.Execute(review));
            Ensure(refusal.Message.Contains("over-limit native history") && image==f.Image() && files==f.Files(),
                "History bound truncated rows or performed a mutation");
        });
        Check("R30_POST_CONSUMPTION_NATIVE_CHANGE_ROLLS_BACK_REVIEW_AND_INTENT",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Move"),true);string image=f.Image();
            Refuse<IOException>(()=>f.Engine.Execute(review,()=>File.WriteAllText(review.Operation.Plan.Source,"foreign change inside transaction")));
            Ensure(image==f.Image() && !File.Exists(review.Operation.Plan.Temp) && !File.Exists(review.Operation.Plan.Destination),
                "Native change after consumption did not roll back both SQL changes before filesystem effects");
        });
        Check("R30_READINESS_PRESENTATION_HAS_EXACT_DEPENDENCY_AND_HOLD",()=>
        {
            var id=Guid.NewGuid();var parent=Guid.NewGuid();
            var view=new R30HistoryView(true,false,"HOLD: interrupted Copy",new[]{
                new R30OperationView("Copy",id,Guid.NewGuid(),2,"Intent","source","target",parent)},[]);
            var text=StudioR30ReadinessPresentation.Describe(view);
            Ensure(text.Contains(id.ToString("N")) && text.Contains(parent.ToString("N")) &&
                text.Contains("Ordinary mixed-family operations remain locked") && text.Contains("No automatic mutation replay") &&
                !view.AuthorizesOrdinaryMixedFamily,"Readiness UI concealed dependency, refusal or authority boundary");
        });
        Check("R30_REJECT_RECOVERY_ARCHIVE_CANNOT_BECOME_SUCCESSOR_SOURCE",()=>
        {
            using var f=new R30TestFixture();var parent=f.Apply("Reject");
            foreach(var family in families)f.NoChange(()=>f.Plan(family,consumes:parent.Plan.Id));
        });
        Check("R30_ORIGINAL_SEED_REUSE_WITHOUT_RECORDED_TRANSFER_REMAINS_HOLD",()=>
        {
            using var f=new R30TestFixture();var first=f.Apply("Copy");
            f.NoChange(()=>f.Plan("Copy"));
            f.Engine.Undo(f.Engine.ReviewUndo("Copy",first.Plan.Id,true));
            f.NoChange(()=>f.Plan("Move"));
            Ensure(f.Db.InspectR30History().Complete,"Refused source reuse changed terminal ownership evidence");
        });
        Check("R30_COPY_MOVE_RENAME_REJECT_DIRECTED_NATIVE_CHAIN_SELECTIVE_UNDO",()=>
        {
            using var f=new R30TestFixture();var copy=f.Apply("Copy");var independent=f.Apply("Move",1);
            var move=f.Engine.Execute(f.Engine.Review(f.Plan("Move",consumes:copy.Plan.Id),true));
            var rename=f.Engine.Execute(f.Engine.Review(f.Plan("Rename",consumes:move.Plan.Id),true));
            var reject=f.Engine.Execute(f.Engine.Review(f.Plan("Reject",consumes:rename.Plan.Id),true));
            f.Reopen();Ensure(f.Db.InspectR30History().Complete,"Directed four-family ownership not verified");
            foreach(var row in new[]{copy,move,rename})f.NoChange(()=>f.Engine.ReviewUndo(row.Plan.Family,row.Plan.Id,true));
            foreach(var row in new[]{reject,rename,move,copy})
                f.Engine.Undo(f.Engine.ReviewUndo(row.Plan.Family,row.Plan.Id,true));
            Ensure(f.Db.InspectR30History().Complete && R30Native.Json(f.Db.R30Load().Operations.Single(x=>x.Plan.Id==independent.Plan.Id))==R30Native.Json(independent),
                "Four-family reverse lost delegated inode evidence or independent committed Move");
        });
        Check("R30_REPEATED_MOVES_AND_RENAMES_CONSUME_EXACT_PREDECESSOR",()=>
        {
            using var f=new R30TestFixture();var rows=new List<R30Operation>{f.Apply("Move")};
            foreach(var family in new[]{"Move","Rename","Rename","Copy"})
                rows.Add(f.Engine.Execute(f.Engine.Review(f.Plan(family,consumes:rows[^1].Plan.Id),true)));
            f.Reopen();Ensure(f.Db.InspectR30History().Complete,"Repeated consumed-source ownership invalid");
            foreach(var row in rows.AsEnumerable().Reverse())f.Engine.Undo(f.Engine.ReviewUndo(row.Plan.Family,row.Plan.Id,true));
            Ensure(f.Db.InspectR30History().Complete,"Repeated family Undo failed");
        });
        Check("R30_TWO_OPEN_REVIEWS_SECOND_STALE_AFTER_FIRST_COMMIT",()=>
        {
            using var f=new R30TestFixture();var first=f.Engine.Review(f.Plan("Rename"),true);
            var second=f.Engine.Review(f.Plan("Copy",1),true);f.Engine.Execute(first);
            f.NoChange(()=>f.Engine.Execute(second));f.NoChange(()=>f.Engine.Review(second.Operation.Plan,true));
        });
        foreach(string corruption in new[]{"extra-valid-row","missing-row","payload","review","auxiliary","operation","audit"})
            Check("R30_HISTORY_CORRUPTION_"+corruption.Replace('-','_').ToUpperInvariant()+"_REFUSED",()=>
            {
                using var f=new R30TestFixture();var row=f.Apply("Copy");var next=f.Engine.Review(f.Plan("Rename",1),true);
                switch(corruption)
                {
                    case "extra-valid-row":
                        var old=f.Db.CopyItems(row.Plan.Session).Single();var extra=old with {Id=Guid.NewGuid(),Sequence=99};
                        f.Sql("INSERT INTO copy_items VALUES($id,$s,99,$json)",("$id",extra.Id.ToString("N")),("$s",extra.SessionId.ToString("N")),("$json",JsonSerializer.Serialize(extra)));break;
                    case "missing-row":f.Sql("DELETE FROM copy_items");break;
                    case "payload":f.Sql("UPDATE copy_items SET json='{}'");break;
                    case "review":f.Sql("UPDATE r30_reviews SET state='Reviewed' WHERE state='Consumed'");break;
                    case "auxiliary":f.Sql("INSERT INTO rename_closeout VALUES(1,$s,'unverified',1)",("$s",Guid.NewGuid().ToString("N")));break;
                    case "operation":f.Sql("DELETE FROM r30_operations");break;
                    case "audit":f.Sql("DELETE FROM r30_events WHERE sequence=1");break;
                }
                f.NoChange(()=>f.Engine.Execute(next));Ensure(!f.Db.InspectR30History().Complete,"Corrupt history advertised complete");
            });
        Check("R30_HARDLINKED_UNRELATED_OUTPUT_BLOCKS_NEW_OPERATION",()=>
        {
            using var f=new R30TestFixture();var row=f.Apply("Copy");var next=f.Engine.Review(f.Plan("Move",1),true);
            NativeFileCapability.PublishLinkNoReplace(row.Plan.Destination,row.Plan.Destination+".foreign");
            f.NoChange(()=>f.Engine.Execute(next));
        });
        Check("R30_PORTABLE_ALIAS_DESTINATION_REFUSED",()=>
        {
            using var f=new R30TestFixture();var target=Path.Combine(f.Root,"output","Case.png");
            var review=f.Engine.Review(f.Plan("Copy",destination:target),true);
            File.WriteAllText(Path.Combine(f.Root,"output","CASE.PNG"),"foreign");f.NoChange(()=>f.Engine.Execute(review));
        });
        Check("R30_OVERLAPPING_RECOVERY_DESTINATION_AND_OUTSIDE_ROOT_REFUSED",()=>
        {
            using var f=new R30TestFixture();
            f.NoChange(()=>f.Plan("Copy",destination:Path.Combine(f.Root,"r30-recovery","unapproved.png")));
            f.NoChange(()=>f.Plan("Move",destination:Path.Combine(f.Root,"state","foreign.png")));
        });
        Check("R30_PORTABLE_DEVICE_AND_ALTERNATE_DATA_STREAM_NAMES_REFUSED",()=>
        {
            using var f=new R30TestFixture();
            foreach(var leaf in new[]{"CON.png","con.extra.png","LPT1.png","image.png:foreign-stream","wild?.png"})
                f.NoChange(()=>f.Plan("Copy",destination:Path.Combine(f.Root,"output",leaf)));
        });
        Check("R30_NATIVE_DIRECTORY_REPLACEMENT_REFUSED",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Copy"),true);
            string path=Path.Combine(f.Root,"output");Directory.Move(path,path+"-retained");Directory.CreateDirectory(path);
            f.NoChange(()=>f.Engine.Execute(review));
        });
        Check("R30_UNCERTAIN_TEMP_CUT_RECOVERY_CONTINUATION_HOLD",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Move"),true);
            var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x=="TEMP_CREATED_UNACKNOWLEDGED")throw new SimulatedInterruptionException(x);});
            Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));f.Reopen();
            f.NoChange(()=>f.Engine.ReviewRecovery("Move",review.Operation.Plan.Id,true));
        });
        Check("R30_RECOVERY_CONTINUATION_RECHECKS_NATIVE_AND_SQL_AUTHORITY",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Move"),true);
            var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x=="TEMP_DURABLE")throw new SimulatedInterruptionException(x);});
            Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));f.Reopen();
            var continuation=f.Engine.ReviewContinuation("Move",review.Operation.Plan.Id,true);
            File.WriteAllText(review.Operation.Plan.Destination,"foreign after recovery review");
            f.NoChange(()=>f.Engine.Continue(continuation));
        });
        Check("R30_CONTINUATION_CONSUMPTION_CUT_CANNOT_REISSUE_SAME_STAGE",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Copy"),true);
            var interrupted=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x=="INTENT_DURABLE")throw new SimulatedInterruptionException(x);});
            Refuse<SimulatedInterruptionException>(()=>interrupted.Execute(review));f.Reopen();
            var continuation=f.Engine.ReviewContinuation("Copy",review.Operation.Plan.Id,true);
            var consumed=new R30GeneratedOperationCoordinator(f.Db,x=>{if(x=="CONTINUATION_AUTHORITY_CONSUMED")throw new SimulatedInterruptionException(x);});
            Refuse<SimulatedInterruptionException>(()=>consumed.Continue(continuation));f.Reopen();
            f.NoChange(()=>f.Engine.Continue(continuation));
            f.NoChange(()=>f.Engine.ReviewContinuation("Copy",review.Operation.Plan.Id,true));
        });
        Check("R30_UNMARKED_STORE_AND_REENROLLMENT_REFUSED",()=>
        {
            using var f=new R30TestFixture();f.NoChange(()=>R30GeneratedOperationCoordinator.ProvisionGeneratedFixture(f.Db));
            File.WriteAllText(Path.Combine(f.Root,"R30_GENERATED_ONLY.marker"),"foreign");
            f.NoChange(()=>new R30GeneratedOperationCoordinator(f.Db));
        });
        Check("R30_GENERATED_PROVISIONING_CANNOT_OVERWRITE_SAVED_WORKSPACE",()=>
        {
            using var f=new R30TestFixture();
            string other=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P1_DISPOSABLE_"+Guid.NewGuid().ToString("N"));
            using var db=new WorkspaceSessionStore(Path.Combine(other,"state"));
            db.Save(f.Work.CaptureSession());string before=JsonSerializer.Serialize(db.Load());
            Refuse<IOException>(()=>R30GeneratedOperationCoordinator.ProvisionGeneratedFixture(db));
            Ensure(before==JsonSerializer.Serialize(db.Load()) && !Directory.Exists(Path.Combine(other,"source")) &&
                !Directory.Exists(Path.Combine(other,"output")),"Provisioning replaced a saved checkpoint or created photo folders");
        });
        Check("R30_READ_ONLY_INVENTORY_AND_LEGACY_WRITER_LOCK",()=>
        {
            using var f=new R30TestFixture();var row=f.Apply("Copy");string image=f.Image(),files=f.Files();
            var view=f.Db.InspectR30History();
            Ensure(view.Present && view.Complete && !view.AuthorizesOrdinaryMixedFamily && !view.AuthorizesAutomaticReplay,
                "Read-only view acquired authority");
            Ensure(image==f.Image() && files==f.Files(),"Inspection wrote evidence");
            f.NoChange(()=>f.Db.PutCopyItem(f.Db.CopyItems(row.Plan.Session).Single()));
        });
        Check("R30_MISSING_ANCHOR_NEVER_LOOKS_LIKE_FRESH_LEGACY_STORE",()=>
        {
            using var f=new R30TestFixture();f.Sql("DROP TABLE r30_meta");
            var view=f.Db.InspectR30History();
            Ensure(view.Present && !view.Complete && view.Reason.Contains("missing its durable anchor"),"Missing anchor appeared fresh");
            Refuse<IOException>(()=>f.Db.RequireNoR30Writer());
            Refuse<IOException>(()=>new R30GeneratedOperationCoordinator(f.Db));
        });
        Check("R30_UNVERIFIED_SQL_TRIGGER_CANNOT_CHANGE_AUTHORITY_DURING_CONSUMPTION",()=>
        {
            using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Move"),true);
            f.Sql("CREATE TRIGGER r30_foreign_trigger BEFORE UPDATE OF state ON r30_reviews BEGIN UPDATE workspace_checkpoint SET json='{}'; END");
            string image=f.Image(),files=f.Files();var refusal=Refuse<IOException>(()=>f.Engine.Execute(review));
            Ensure(refusal.Message.Contains("trigger/view") && image==f.Image() && files==f.Files(),
                "Foreign SQL schema altered the exact checkpoint/review or performed a file action");
        });
        Check("R30_SCHEMA_DOWNGRADE_REFUSED_AND_ORDINARY_SCHEMA_UNCHANGED",()=>
        {
            using var f=new R30TestFixture();
            using(var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=f.Db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            {
                c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA user_version";
                Ensure(Convert.ToInt64(cmd.ExecuteScalar())==20,"Generated enrollment lacks old-binary schema fence");
            }
            f.Sql("PRAGMA user_version=19");
            f.NoChange(()=>f.Plan("Copy"));Refuse<IOException>(()=>f.Db.RequireNoR30Writer());
            Ensure(!f.Db.InspectR30History().Complete,"Downgraded schema acquired authority");
        });
        foreach(string corruption in new[]{"fixture","checkpoint","installation"})
            Check("R30_MALFORMED_"+corruption.ToUpperInvariant()+"_FAILS_CLOSED",()=>
            {
                using var f=new R30TestFixture();var review=f.Engine.Review(f.Plan("Copy"),true);
                f.Sql(corruption switch {
                    "fixture"=>"UPDATE r30_meta SET fixture='{}'",
                    "checkpoint"=>"UPDATE workspace_checkpoint SET json='{}'",
                    _=>"UPDATE installation_identity SET token='foreign'"});
                f.NoChange(()=>f.Engine.Execute(review));
                Ensure(!f.Db.InspectR30History().Complete,"Malformed provenance appeared verified");
            });
    }
}
