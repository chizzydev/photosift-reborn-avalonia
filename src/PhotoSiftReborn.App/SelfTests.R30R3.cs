using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

internal static partial class SelfTests
{
    internal static int RunR30R3Development()
    {
        RunR30R3Tests();
        Console.WriteLine($"P2_R30_R3_TARGETED_PASSED={_passed}");
        Console.WriteLine($"P2_R30_R3_TARGETED_FAILED={_failed}");
        return _failed==0?0:1;
    }

    private sealed class R30R3Pins
    {
        internal string Root { get; }=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_R30_R3_PINS_"+Guid.NewGuid().ToString("N"));
        internal M6JP4PinnedFileExpectation[] Items { get; }
        internal R30R3Pins()
        {
            Directory.CreateDirectory(Root);
            Items=Enumerable.Range(0,4).Select(i=>
            {
                string dir=Path.Combine(Root,"parent-"+i);Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"owned.dat");File.WriteAllText(path,"generated-r30-r3-bytes-"+i);
                return R30R3Expectation(path);
            }).ToArray();
        }
        internal string Image()=>JsonSerializer.Serialize(Directory.GetFiles(Root,"*",SearchOption.AllDirectories)
            .OrderBy(x=>x,StringComparer.Ordinal).Select(x=>new {path=x,id=NativeFileCapability.Identity(x),
                hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))),ticks=File.GetLastWriteTimeUtc(x).Ticks}));
        internal M6JP4FreshPredecessorObservation Inspect(bool four=true)=>four
            ? M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(Items[0],Items[1],Items[2],Items[3])
            : M6JP4FreshPinnedPredecessor.Inspect(Items[0],Items[1],Items[2]);
    }

    private static M6JP4PinnedFileExpectation R30R3Expectation(string path)=>new(path,NativeFileCapability.Identity(path),
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),new FileInfo(path).Length,File.GetLastWriteTimeUtc(path).Ticks);

    private static void R30R3NoAuthority(M6JP4FreshPredecessorObservation proof)
        =>Ensure(!proof.AuthorizesNativeOperation && !proof.CanWriteMoveIntent && !proof.AuthorizesSelectiveUndo &&
            !proof.GrantsSuccessorEpoch && !proof.IsDurableProof,"Ephemeral inspection minted authority.");

    // The actual upstream reviewed v19 lane, deliberately separate from R30 v20.
    private sealed class R30R3Reviewed : IDisposable
    {
        internal string Root { get; }=SelfTests.Root();
        internal SortingWorkspace Work { get; }
        internal WorkspaceSessionStore Db;
        internal DurableCopyItem Prior { get; }
        internal DurableMoveItem Intent { get; }
        internal PreparedCopy Prepared { get; }
        internal R30R3Reviewed()
        {
            Work=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            Work.OpenSource(Path.Combine(Root,"source"));Work.SetDestinationRoot(Path.Combine(Root,"output"));
            Db=new WorkspaceSessionStore(Path.Combine(Root,"state"));
            Guid first=Work.Current!.Id;Work.Assign('F');var plan=Work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(Work,plan,Work.CurrentPlanId!.Value,Work.Revision).GetAwaiter().GetResult();
            Work.ApprovePlan(Work.CurrentPlanId!.Value,Work.Revision);Db.Save(Work.CaptureSession());
            Ensure(new DurableCopyCoordinator(Db).ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"R3 Copy fixture failed.");
            Prior=Db.CopyItems(Work.SessionId).Single();
            Work.RetireCommitted(new[]{first});Db.Save(Work.CaptureSession());
            Work=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};Work.RestoreSession(Db.Load()!);Work.JumpToPhoto(2);
            Ensure(Work.Current!.Id!=first,"R3 Move fixture was not independent.");
            Work.Assign('T');plan=Work.ReviewPlan();
            Prepared=DurableCopyCoordinator.PrepareAsync(Work,plan,Work.CurrentPlanId!.Value,Work.Revision).GetAwaiter().GetResult().Single();
            Work.ApprovePlan(Work.CurrentPlanId!.Value,Work.Revision);Db.Save(Work.CaptureSession());
            Directory.CreateDirectory(Path.GetDirectoryName(Prepared.Destination)!);
            Guid id=Guid.NewGuid();var p=Prepared;
            var candidate=new M6JProspectiveCandidate(id,"Move",p.SessionId,p.PhotoId,p.Source,p.Destination,p.Identity,p.Hash,p.Length,p.WriteTicks);
            Db.StageM6JProspectiveReview(candidate);Db.ReviewM6JProspectiveCandidate(id,candidate,true);
            string archive=Path.Combine(Path.GetDirectoryName(p.Source)!,".photosift-reborn-recovery",Db.MoveInstallationId().ToString("N"),p.SessionId.ToString("N"),id.ToString("N"));
            Intent=new(id,Db.MoveInstallationId(),p.SessionId,p.PhotoId,p.PlanId,p.Revision,DateTime.UtcNow.Ticks,p.Source,p.Destination,
                Path.Combine(Path.GetDirectoryName(p.Destination)!,".photosift-move-"+id.ToString("N")+".part"),
                Path.Combine(archive,Path.GetFileName(p.Source)),Path.Combine(archive,"installed-"+Path.GetFileName(p.Destination)),
                p.OutputRoot,p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,"Intent",null,p.Key);
        }
        internal DurableMoveItem Move(Action<string>? cut=null)=>new DurableMoveCoordinator(Db,cut)
            .ExecuteM6JReviewedGeneratedCopyMoveAsync(Work,Prepared,Intent).GetAwaiter().GetResult();
        internal string Files()=>JsonSerializer.Serialize(Directory.GetFiles(Root,"*",SearchOption.AllDirectories)
            .Where(x=>!x.StartsWith(Path.Combine(Root,"state")+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            .OrderBy(x=>x,StringComparer.Ordinal).Select(x=>new {path=x,id=NativeFileCapability.Identity(x),
                hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))}));
        internal void Sql(string sql)
        {
            using var con=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Db.DatabasePath,Pooling=false}.ToString());
            con.Open();using var cmd=con.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();
        }
        internal void Reopen(){Db.Dispose();Db=new WorkspaceSessionStore(Path.Combine(Root,"state"));}
        public void Dispose()=>Db.Dispose();
    }

    private static void RunR30R3Tests()
    {
        foreach(bool four in new[]{false,true})
        Check("R30_R3_NATIVE_"+(four?"FOUR":"THREE")+"_DESCRIPTORS_POSITIVE_NONAUTHORITY",()=>
        {
            var f=new R30R3Pins();string before=f.Image();var report=f.Inspect(four);R30R3NoAuthority(report);
            Ensure(report.IsFreshWitness && report.ObservationFingerprint?.Length==64 &&
                report.ObservationFingerprint==f.Inspect(four).ObservationFingerprint && before==f.Image(),report.Explanation);
            if(OperatingSystem.IsLinux()) Ensure(report.Explanation.StartsWith("R30-R3-LINUX-STATX-",StringComparison.Ordinal),"Missing Linux witness domain.");
            else
            {
                string tag=four?"P4-R7-GENERATED-SELECTIVE-UNDO-FOUR-PINNED-v1":"P4-R2-PINNED-WINDOWS-THREE-FILES-v1";
                string statement=tag+"\n"+string.Join("\n",f.Items.Take(four?4:3).Select(x=>x.Identity+":"+x.UpperSha256+":"+x.Length));
                Ensure(report.ObservationFingerprint==Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(statement))),"Windows fingerprint changed.");
            }
        });
        foreach(int index in Enumerable.Range(0,4))
        foreach(string defect in new[]{"HASH","LENGTH","MTIME","IDENTITY","FOREIGN_PLATFORM","MALFORMED_ID"})
        Check($"R30_R3_PIN_{index}_{defect}_REFUSES_UNCHANGED",()=>
        {
            var f=new R30R3Pins();string before=f.Image();var old=f.Items[index];
            f.Items[index]=defect switch {
                "HASH"=>old with {UpperSha256=new string('0',64)},
                "LENGTH"=>old with {Length=old.Length+1},
                "MTIME"=>old with {ExpectedWriteUtcTicks=old.ExpectedWriteUtcTicks-TimeSpan.TicksPerSecond},
                "IDENTITY"=>old with {Identity=f.Items[(index+1)%4].Identity},
                "FOREIGN_PLATFORM"=>old with {Identity=OperatingSystem.IsWindows()?"linux:00000000:00000001:0000000000000001":"win:00000001:0000000000000001"},
                _=>old with {Identity=old.Identity.ToLowerInvariant()+":"}};
            var report=f.Inspect();R30R3NoAuthority(report);
            Ensure(!report.IsFreshWitness && before==f.Image(),"Bad expectation altered native files or passed.");
        });
        foreach(int index in Enumerable.Range(0,4))
        Check($"R30_R3_PIN_{index}_FOREIGN_HARDLINK_REFUSES_UNCHANGED",()=>
        {
            var f=new R30R3Pins();NativeFileCapability.PublishLinkNoReplace(f.Items[index].Path,Path.Combine(f.Root,"foreign-link"));
            string before=f.Image();var report=f.Inspect();R30R3NoAuthority(report);
            Ensure(!report.IsFreshWitness && before==f.Image(),"Foreign inode link accepted or changed.");
        });
        Check("R30_R3_PARTICIPANT_PATH_ALIAS_REFUSES_UNCHANGED",()=>
        {
            var f=new R30R3Pins();f.Items[3]=f.Items[0];string before=f.Image();
            Ensure(!f.Inspect().IsFreshWitness && before==f.Image(),"Duplicate native participant passed.");
        });
        Check("R30_R3_LIVE_LINK_ADDED_WITH_ALL_HANDLES_HELD_REFUSES",()=>
        {
            var f=new R30R3Pins();string? after=null;
            var report=M6JP4FreshPinnedPredecessor.InspectHeldForTest(f.Items,()=>
            {
                NativeFileCapability.PublishLinkNoReplace(f.Items[3].Path,Path.Combine(f.Root,"foreign-live-link"));after=f.Image();
            });
            Ensure(after is not null && !report.IsFreshWitness && after==f.Image(),"Live fourth-inode link not detected with all pins held.");
        });
        Check("R30_R3_NATIVE_NO_REPLACE_PRESERVES_BOTH_OCCUPANTS",()=>
        {
            var f=new R30R3Pins();string before=f.Image();
            Refuse<IOException>(()=>NativeFileCapability.PublishLinkNoReplace(f.Items[0].Path,f.Items[1].Path));
            Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplace(f.Items[2].Path,f.Items[3].Path));
            Ensure(before==f.Image(),"Native no-replace modified an owned or foreign occupant.");
        });
        Check("R30_R3_REVIEWED_P1_P2A_COMMIT_REPLAY_COLD",()=>
        {
            using var f=new R30R3Reviewed();string files=f.Files();
            var proof=f.Db.InspectM6JMoveAfterCopyIntent(f.Intent.Id,f.Intent);
            Ensure(proof.IsExactPreparedShape && !proof.CanWriteMoveIntent && !proof.AuthorizesNativeOperation,proof.Explanation);
            Ensure(f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent).JournalCommitted,"Real reviewed Intent refused.");
            f.Reopen();string sql=R30R2DatabaseImage(f.Db);
            Refuse<IOException>(()=>f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent));
            Ensure(sql==R30R2DatabaseImage(f.Db) && files==f.Files() && f.Db.AllMoveHistoryCount()==1 &&
                f.Db.InspectM6JProspectiveReview(f.Intent.Id).State=="Consumed","Cold review replay changed SQL/native state.");
        });
        foreach(string tamper in new[]{"REVIEW","PREDECESSOR","CHECKPOINT"})
        Check("R30_R3_REVIEWED_"+tamper+"_TAMPER_REFUSES_UNCHANGED",()=>
        {
            using var f=new R30R3Reviewed();
            f.Sql(tamper switch {"REVIEW"=>"UPDATE m6j_review_proposals SET review_digest='BAD'",
                "PREDECESSOR"=>"UPDATE copy_items SET json=json || ' '",_=>"UPDATE workspace_checkpoint SET json=json || ' '"});
            string sql=R30R2DatabaseImage(f.Db),files=f.Files();
            Refuse<IOException>(()=>f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent));
            Ensure(sql==R30R2DatabaseImage(f.Db) && files==f.Files() && f.Db.AllMoveHistoryCount()==0,"Tamper refusal changed SQL/native state.");
        });
        Check("R30_R3_REVIEW_CONSUMPTION_CUT_ROLLBACK_COLD_RETRY",()=>
        {
            using var f=new R30R3Reviewed();string files=f.Files();
            Refuse<SimulatedInterruptionException>(()=>f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent,
                ()=>throw new SimulatedInterruptionException("R3 transaction cut")));
            f.Reopen();Ensure(f.Db.AllMoveHistoryCount()==0 && files==f.Files() &&
                f.Db.InspectM6JProspectiveReview(f.Intent.Id).State=="Reviewed","Cut persisted partial consumption or changed native files.");
            Ensure(f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent).JournalCommitted,"Independent retry after rollback failed.");
        });
        Check("R30_R3_NATIVE_TAMPER_AFTER_CONSUMPTION_ROLLS_BACK",()=>
        {
            using var f=new R30R3Reviewed();string? after=null;
            Refuse<IOException>(()=>f.Db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Intent.Id,f.Intent,()=>
            {File.AppendAllText(f.Prior.Destination,"foreign alteration");after=f.Files();}));
            f.Reopen();Ensure(after==f.Files() && f.Db.AllMoveHistoryCount()==0,"Native alteration not preserved or Intent committed.");
            using var con=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=f.Db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            con.Open();using var cmd=con.CreateCommand();cmd.CommandText="SELECT state FROM m6j_review_proposals";
            Ensure(cmd.ExecuteScalar() as string=="Reviewed","Native refusal did not roll back consumption.");
        });
        foreach(string cut in new[]{"MOVE_TEMP_RECORDED","MOVE_SOURCE_RELOCATED_UNJOURNALLED","MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED","MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED"})
        Check("R30_R3_"+cut+"_COLD_NO_AUTOREPLAY",()=>
        {
            using var f=new R30R3Reviewed();
            void Interrupt(string step){if(step==cut)throw new SimulatedInterruptionException(step);}
            if(cut.StartsWith("MOVE_UNDO",StringComparison.Ordinal))
            {
                f.Move();Refuse<SimulatedInterruptionException>(()=>new DurableMoveCoordinator(f.Db,Interrupt)
                    .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId).GetAwaiter().GetResult());
            }
            else Refuse<SimulatedInterruptionException>(()=>f.Move(Interrupt));
            string files=f.Files(),sql=R30R2DatabaseImage(f.Db);f.Reopen();
            var row=f.Db.MoveItems(f.Intent.SessionId).Single();
            var report=f.Db.InspectM6JInterruptedCopyMoveReadOnly(f.Intent.SessionId);
            Refuse<IOException>(()=>new DurableMoveCoordinator(f.Db).UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId).GetAwaiter().GetResult());
            Ensure(!report.AuthorizesAutomaticReplay && !report.AuthorizesAutomaticUndo &&
                sql==R30R2DatabaseImage(f.Db) && files==f.Files() && row==f.Db.MoveItems(f.Intent.SessionId).Single() &&
                f.Db.CopyItems(f.Prior.SessionId).Single()==f.Prior,"Cold observation/retry changed interrupted evidence.");
        });
        Check("R30_R3_GENERATED_GUI_FACTORY_OPT_IN_AND_COLD_NATIVE_PAIR",()=>
        {
            string? old=Environment.GetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable);
            try
            {
                Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,null);
                Refuse<InvalidOperationException>(()=>GeneratedNativeRecoveryHistoryLab.Create());
                Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,GeneratedNativeRecoveryHistoryLab.ExactOptIn);
                string state=GeneratedNativeRecoveryHistoryLab.Create();
                using var cold=new WorkspaceSessionStore(state);string sql=R30R2DatabaseImage(cold);
                var report=cold.InspectM6JP3PairedRecoveryObservation();
                Ensure(report.Inventory.Rows.Count==2 && report.ConsistentDatabaseObservation &&
                    report.Inventory.Rows.Count(x=>x.Family=="Copy" && x.State=="Committed" && x.Proof==UnifiedRowProof.NativeActiveVerified)==1 &&
                    report.Inventory.Rows.Count(x=>x.Family=="Move" && x.State=="Undone" && x.Proof==UnifiedRowProof.NativeTerminalVerified)==1 &&
                    !report.AuthorizesNativeOperation && sql==R30R2DatabaseImage(cold),"Generated GUI factory/cold native pair not verified.");
            }
            finally {Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,old);}
        });
        // These are additional Linux-native cases, registered only on Linux.
        // Windows logs do not emit their names or pretend their bodies passed.
        if(OperatingSystem.IsLinux()) RunR30R3LinuxTests();
    }

    private static void RunR30R3LinuxTests()
    {
        foreach(int count in new[]{3,4})
        Check($"R30_R3_LINUX_{count}_DESCRIPTORS_SIMULTANEOUSLY_LIVE",()=>
        {
            var f=new R30R3Pins();bool observed=false;
            var proof=M6JP4FreshPinnedPredecessor.InspectHeldForTest(f.Items.Take(count).ToArray(),()=>
            {
                var targets=Directory.GetFiles("/proc/self/fd").Select(x=>new FileInfo(x).LinkTarget).ToArray();
                Ensure(f.Items.Take(count).All(x=>targets.Contains(x.Path)),"A participant descriptor was closed before the final set comparison.");
                observed=true;
            });
            Ensure(observed && proof.IsFreshWitness,proof.Explanation);
        });
        foreach(string attack in new[]{"INODE_SWAP","PARENT_SWAP","LEAF_SYMLINK","ANCESTOR_SYMLINK","CASE_ALIAS","UNICODE_ALIAS","WRITE_RESTORE_MTIME","WRITE_RESTORE_BYTES_AND_MTIME"})
        Check("R30_R3_LINUX_LIVE_"+attack+"_HOLD",()=>
        {
            var f=new R30R3Pins();var victim=f.Items[3];string parent=Path.GetDirectoryName(victim.Path)!;
            if(attack=="UNICODE_ALIAS")
            {
                string composed=Path.Combine(parent,"\u00f3wned.dat");File.Move(victim.Path,composed);
                victim=f.Items[3]=R30R3Expectation(composed);
            }
            string? after=null;
            var report=M6JP4FreshPinnedPredecessor.InspectHeldForTest(f.Items,()=>
            {
                switch(attack)
                {
                    case "INODE_SWAP": File.Move(victim.Path,victim.Path+".original");File.WriteAllBytes(victim.Path,File.ReadAllBytes(victim.Path+".original"));File.SetLastWriteTimeUtc(victim.Path,new DateTime(victim.ExpectedWriteUtcTicks,DateTimeKind.Utc));break;
                    case "PARENT_SWAP": Directory.Move(parent,parent+".original");Directory.CreateDirectory(parent);File.Move(Path.Combine(parent+".original","owned.dat"),victim.Path);break;
                    case "LEAF_SYMLINK": File.Move(victim.Path,victim.Path+".original");File.CreateSymbolicLink(victim.Path,victim.Path+".original");break;
                    case "ANCESTOR_SYMLINK": Directory.Move(parent,parent+".original");Directory.CreateSymbolicLink(parent,parent+".original");break;
                    case "CASE_ALIAS": File.WriteAllText(Path.Combine(parent,"OWNED.DAT"),"foreign alias");break;
                    case "UNICODE_ALIAS":
                        // Decomposed name collides with the composed participant.
                        File.WriteAllText(Path.Combine(parent,"o\u0301wned.dat"),"foreign alias");break;
                    case "WRITE_RESTORE_MTIME":
                    case "WRITE_RESTORE_BYTES_AND_MTIME":
                        byte original=File.ReadAllBytes(victim.Path)[0];
                        using(var writer=new FileStream(victim.Path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))
                        {
                            writer.WriteByte(0x21);writer.Flush(true);
                            if(attack=="WRITE_RESTORE_BYTES_AND_MTIME")
                            {writer.Position=0;writer.WriteByte(original);writer.Flush(true);}
                        }
                        File.SetLastWriteTimeUtc(victim.Path,new DateTime(victim.ExpectedWriteUtcTicks,DateTimeKind.Utc));break;
                }
                // Includes links as entries without following them into recursive
                // trees; verifier must not remove or replace an attacker's names.
                after=R30R3LinuxImage(f.Root);
            });
            R30R3NoAuthority(report);
            Ensure(after is not null && !report.IsFreshWitness && after==R30R3LinuxImage(f.Root),"Live Linux attack was not refused/preserved: "+attack);
        });
        Check("R30_R3_LINUX_EXCLUSIVE_ADVISORY_OWNER_HOLD",()=>
        {
            var f=new R30R3Pins();string before=f.Image();
            using var writer=new FileStream(f.Items[2].Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var report=f.Inspect();R30R3NoAuthority(report);
            Ensure(!report.IsFreshWitness,"Exclusive cooperative owner was not respected.");
            writer.Dispose();Ensure(before==f.Image() && f.Inspect().IsFreshWitness,"Lock refusal changed files or leaked descriptors.");
        });
        Check("R30_R3_LINUX_SHARED_PINS_DO_NOT_CLAIM_MANDATORY_WRITER_LOCK",()=>
        {
            var f=new R30R3Pins();bool wrote=false;string? after=null;
            var report=M6JP4FreshPinnedPredecessor.InspectHeldForTest(f.Items,()=>
            {
                using var writer=new FileStream(f.Items[0].Path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
                writer.WriteByte(0x33);writer.Flush(true);wrote=true;
                after=R30R3LinuxImage(f.Root);
            });
            Ensure(wrote && !report.IsFreshWitness && after==R30R3LinuxImage(f.Root),"Linux writer semantics were falsely treated as mandatory exclusion.");
        });
    }

    private static string R30R3LinuxImage(string root)
    {
        var rows=new List<string>();
        void Walk(string dir)
        {
            foreach(string path in Directory.EnumerateFileSystemEntries(dir).OrderBy(x=>x,StringComparer.Ordinal))
            {
                var info=new FileInfo(path);
                if(info.LinkTarget is string link) rows.Add(path+"->"+link);
                else if(Directory.Exists(path)){rows.Add(path+":"+NativeFileCapability.DirectoryIdentity(path));Walk(path);}
                else rows.Add(path+":"+NativeFileCapability.Identity(path)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }
        }
        Walk(root);return JsonSerializer.Serialize(rows);
    }
}
