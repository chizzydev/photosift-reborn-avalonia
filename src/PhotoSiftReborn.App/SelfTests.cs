using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

internal static partial class SelfTests
{
    private static int _passed;
    private static int _failed;
    private static void Check(string name, Action run)
    {
        try { run(); Console.WriteLine($"P2_{name}=PASS"); _passed++; }
        catch (Exception ex) { Console.WriteLine($"P2_{name}=FAIL {ex.GetType().Name}: {ex.Message}"); _failed++; }
    }
    private static void Ensure(bool value, string reason)
    { if (!value) throw new Exception(reason); }
    private static T Refuse<T>(Action action) where T : Exception
    {
        try { action(); throw new Exception("Expected exception was not thrown"); }
        catch (T ex) { return ex; }
    }
    // P6G1: deterministic, in-memory JPEG/EXIF sample. No personal-photo fixture.
    private static byte[] MinimalExifJpeg()
    {
        var tiff=new byte[120];
        tiff[0]=(byte)'I';tiff[1]=(byte)'I';tiff[2]=42;tiff[4]=8;
        tiff[8]=4; // Four 0th-IFD entries.
        void Entry(int at,ushort tag,ushort type,uint count,uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at,2),tag);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at+2,2),type);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at+4,4),count);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at+8,4),value);
        }
        Entry(10,0x0112,3,1,6); // 90-degree oriented image.
        Entry(22,0x010F,2,6,62);
        Entry(34,0x0110,2,3,0x00395A); // "Z9\0" inline ASCII.
        Entry(46,0x8769,4,1,72);
        System.Text.Encoding.ASCII.GetBytes("Nikon\0").CopyTo(tiff,62);
        tiff[72]=1;
        Entry(74,0x9003,2,20,90);
        System.Text.Encoding.ASCII.GetBytes("2026:10:15 08:24:30\0").CopyTo(tiff,90);
        var frame=new List<byte> {0xff,0xd8,0xff,0xe1,0x00,0x80};
        frame.AddRange(System.Text.Encoding.ASCII.GetBytes("Exif\0\0"));
        frame.AddRange(tiff);
        frame.AddRange(new byte[] {0xff,0xc0,0,17,8,0,10,0,20,3,1,0x11,0,2,0x11,0,3,0x11,0,
                                  0xff,0xd9});
        return frame.ToArray();
    }

    // P4-R13: scoped, explicit second key for disposable FOUR-family fixtures.
    // The ordinary A3 fixture deliberately does NOT carry this authority.
    // Never leave the process environment pointed at the previous fixture.
    private sealed class P4R13FourthFamilyFixtureScope : IDisposable
    {
        private readonly string? _previous;
        internal P4R13FourthFamilyFixtureScope(string root,bool authorize=true)
        {
            _previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            if(authorize)
            {
                if(!Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
                    !File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
                    !Directory.Exists(Path.Combine(root,"source")) ||
                    !Directory.Exists(Path.Combine(root,"output")) ||
                    File.Exists(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker")))
                    throw new IOException("P4-R13 marker setup must be a fresh disposable A3 fixture.");
                File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),
                    P5CGuiFixturePolicy.MarkerContents);
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
            }
            else Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,null);
        }
        public void Dispose() => Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,_previous);
    }

    private static string Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "PHOTOSIFT_REBORN_P1_DISPOSABLE_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(root,"output"));
        File.WriteAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"),"synthetic-interruption-only");
        var assets=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        if (!Directory.Exists(assets)) throw new FileNotFoundException("Packaged synthetic fixtures missing: "+assets);
        foreach (var src in Directory.GetFiles(assets,"*.png"))
            File.Copy(src, Path.Combine(source, Path.GetFileName(src)), overwrite:false);
        return root;
    }
    private static string OrdinaryRejectRoot()
    {
        var root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P6F2B_ORDINARY_SYNTHETIC_"+
            Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"source");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(root,"output"));
        // This is a synthetic test harness provenance marker, deliberately
        // NOT an authorization marker recognized by the actual coordinator.
        File.WriteAllText(Path.Combine(root,"P6F2B_SYNTHETIC_ONLY.marker"),"synthetic test assets only");
        var assets=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        if(!Directory.Exists(assets))throw new FileNotFoundException("Test fixture assets missing.");
        foreach(var src in Directory.GetFiles(assets,"*.png"))
            File.Copy(src,Path.Combine(source,Path.GetFileName(src)),overwrite:false);
        return root;
    }
    private static string OrdinaryGuiProbeRoot()
    {
        var root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_P6F2C_ORDINARY_GUI_PROBE_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"source"));
        Directory.CreateDirectory(Path.Combine(root,"output"));
        Directory.CreateDirectory(Path.Combine(root,"state"));
        var fixture=Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png");
        File.Copy(fixture,Path.Combine(root,"source","photo_01.png"),overwrite:false);
        const string sentinel="P6F2C_PROTECTED_OUTPUT_SENTINEL\n";
        File.WriteAllText(Path.Combine(root,"output","PROTECTED_SENTINEL.txt"),sentinel);
        File.WriteAllText(Path.Combine(root,"P6F2C_PROBE_SCOPE.json"),System.Text.Json.JsonSerializer.Serialize(new {
            schema=1,origin="P6F2C_SYNTHETIC_ONLY",
            sha256=SortingWorkspace.ComputeSha256Async(Path.Combine(root,"source","photo_01.png")).GetAwaiter().GetResult(),
            sentinel
        }));
        return root;
    }
    // P6F2D2: D2-created only; never reopen an existing owner-accepted C3 root.
    private static string TwoOrdinaryGuiRoot()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "PHOTOSIFT_P6F2D2_ORDINARY_GUI_PROBE_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"source"));
        Directory.CreateDirectory(Path.Combine(root,"output"));
        Directory.CreateDirectory(Path.Combine(root,"state"));
        var assets=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        foreach(string name in new[]{"photo_01.png","photo_02.png"})
            File.Copy(Path.Combine(assets,name),Path.Combine(root,"source",name),false);
        const string sentinel="P6F2D2_PROTECTED_OUTPUT_SENTINEL\n";
        File.WriteAllText(Path.Combine(root,"output","PROTECTED_SENTINEL.txt"),sentinel);
        File.WriteAllText(Path.Combine(root,"P6F2D2_PROBE_SCOPE.json"),
            System.Text.Json.JsonSerializer.Serialize(new {
                schema=2,origin="P6F2D2_TWO_GENERATED_ONLY",sentinel,
                sha256_01=SortingWorkspace.ComputeSha256Async(Path.Combine(root,"source","photo_01.png")).GetAwaiter().GetResult(),
                sha256_02=SortingWorkspace.ComputeSha256Async(Path.Combine(root,"source","photo_02.png")).GetAwaiter().GetResult()
            }));
        return root;
    }
    private static void WithTwoOrdinaryGui(Action<WorkspaceSessionStore,
        SortingWorkspace,DurableRejectCoordinator,string> run)
    {
        string root=TwoOrdinaryGuiRoot();
        string? oldRoot=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT");
        string? oldHome=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
        string? oldFixture=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
        Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",root);
        Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",Path.Combine(root,"state"));
        Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,null);
        try
        {
            var work=RenameFixture(root);
            Ensure(work.Photos.Count==2 && Path.GetFileName(work.Photos[0].Path)=="photo_01.png",
                "Two-generated-photo import did not preserve exact order");
            work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            OrdinaryRejectGuiProbePolicy.Require(db,work);
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            run(db,work,engine,root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",oldRoot);
            Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",oldHome);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,oldFixture);
        }
    }
    private static void CompleteFirstOrdinaryGuiReject(WorkspaceSessionStore db,
        SortingWorkspace work,DurableRejectCoordinator engine)
    {
        var photo=work.Photos[0];
        var first=engine.Stage(work,photo.Id);
        engine.Execute(engine.Approve(first,true));
        engine.Undo(work.SessionId);
        Ensure(engine.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED" &&
            db.RejectItems().Count==1 && File.Exists(photo.Path),
            "First ordinary GUI native Undo did not retain restored source");
    }
    private static SortingWorkspace Create(string root)
    {
        var work = new SortingWorkspace();
        work.OpenSource(Path.Combine(root, "source"));
        work.SetDestinationRoot(Path.Combine(root, "output"));
        return work;
    }
    // P6F3C1R1: continuation evidence deliberately compares the journal's native
    // source identity with the saved photo's import-time native identity.
    // Earlier generic Create(root) does not install an identity resolver and
    // therefore records NativeIdentity=null; those older tests must keep their
    // original fixture semantics. Only the new continuation cases opt into this.
    private static SortingWorkspace NativeCopyUndoFixture(string root)
    {
        var work = new SortingWorkspace { IdentityResolver = NativeFileCapability.Identity };
        work.OpenSource(Path.Combine(root,"source"));
        work.SetDestinationRoot(Path.Combine(root,"output"));
        if (!NativeFileCapability.IsSupported ||
            work.Photos.Count == 0 ||
            work.Photos.Any(p => string.IsNullOrWhiteSpace(p.NativeIdentity) ||
                p.NativeIdentity != NativeFileCapability.Identity(p.Path)))
            throw new InvalidOperationException("Synthetic native Copy continuation fixture has no verified import-time identity.");
        return work;
    }
    private static SortingWorkspace RenameFixture(string root)
    {
        // Generated test photographs need a native identity saved at import;
        // do not alter legacy test fixtures or reuse external user photographs.
        var work=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
        work.OpenSource(Path.Combine(root,"source"));
        work.SetDestinationRoot(Path.Combine(root,"output"));
        return work;
    }
    // P6C2-B2: all sources remain bounded disposable native fixtures. The
    // successor is a read-only proposal; NO second Move mutation is performed.
    private static (WorkspaceSessionStore Db, string Root, DurableMoveItem First,
        DurableMoveItem Next, SortingWorkspace NextWork, PreparedCopy Prepared,
        IReadOnlyList<SortPlanItem> NextPlan) B2TicketFixture()
    {
        var root=Root();
        var firstWork=Create(root);firstWork.Assign('F');
        var firstPlan=firstWork.ReviewPlan();
        firstWork.ApprovePlan(firstWork.CurrentPlanId!.Value,firstWork.Revision);
        var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
        try
        {
            var mover=new DurableMoveCoordinator(db);
            var prepared=mover.PrepareApprovedAsync(firstWork,firstPlan).GetAwaiter().GetResult();
            var first=mover.ExecuteApprovedAsync(firstWork,prepared).GetAwaiter().GetResult();
            mover.UndoAsync(firstWork.SessionId).GetAwaiter().GetResult();
            Ensure(db.SealTerminalMoveHistory().Verified,"B2 native historical closeout missing");
            var nextWork=Create(root);
            nextWork.JumpToPhoto(2);nextWork.Assign('T');
            var nextPlan=nextWork.ReviewPlan();
            nextWork.ApprovePlan(nextWork.CurrentPlanId!.Value,nextWork.Revision);
            var successor=DurableCopyCoordinator.PrepareAsync(nextWork,nextPlan,
                nextWork.CurrentPlanId!.Value,nextWork.Revision).GetAwaiter().GetResult().Single();
            var id=Guid.NewGuid();var install=db.MoveInstallationId();
            var folder=Path.GetDirectoryName(successor.Destination)!;
            var archive=Path.Combine(Path.GetDirectoryName(successor.Source)!,
                ".photosift-reborn-recovery",install.ToString("N"),
                successor.SessionId.ToString("N"),id.ToString("N"));
            var row=new DurableMoveItem(id,install,successor.SessionId,successor.PhotoId,
                successor.PlanId,successor.Revision,DateTime.UtcNow.Ticks,
                successor.Source,successor.Destination,
                Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
                Path.Combine(archive,Path.GetFileName(successor.Source)),
                Path.Combine(archive,"installed-"+Path.GetFileName(successor.Destination)),
                successor.OutputRoot,successor.Hash,successor.Identity,successor.Length,
                successor.WriteTicks,null,null,"Intent",null,successor.Key);
            Ensure(first.SessionId!=row.SessionId &&
                !SortingWorkspace.PathComparer.Equals(first.Source,row.Source),
                "B2 fixture incorrectly reused an earlier source/session");
            return (db,root,first,row,nextWork,successor,nextPlan);
        }
        catch {db.Dispose();throw;}
    }

    // B4-A: real generated source and installation marker; tests intentionally
    // use the same fixture gate as the owner-operated Windows GUI launcher.
    private static void WithB4GuiFixture(Action<WorkspaceSessionStore,SortingWorkspace,
        WorkspaceSessionSnapshot,string> test)
    {
        string root=Root();
        File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),
            P5CGuiFixturePolicy.MarkerContents);
        File.WriteAllText(Path.Combine(root,"P5C_OWNER_MANIFEST.json"),
            System.Text.Json.JsonSerializer.Serialize(new {
                schema=1,
                source_sha256=new Dictionary<string,string> {
                    ["photo_02.png"]=SortingWorkspace.ComputeSha256Async(
                        Path.Combine(root,"source","photo_02.png")).GetAwaiter().GetResult(),
                    ["photo_03.png"]=SortingWorkspace.ComputeSha256Async(
                        Path.Combine(root,"source","photo_03.png")).GetAwaiter().GetResult()
                }
            }));
        var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
        Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
        try
        {
            var initial=RenameFixture(root);
            initial.Assign('F');
            var plan=initial.ReviewPlan();
            initial.ApprovePlan(initial.CurrentPlanId!.Value,initial.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(initial.CaptureSession());
            var mover=new DurableMoveCoordinator(db);
            var prepared=mover.PrepareApprovedAsync(initial,plan).GetAwaiter().GetResult();
            var first=mover.ExecuteApprovedAsync(initial,prepared).GetAwaiter().GetResult();
            initial.RetireCommitted(new[]{first.PhotoId});
            db.Save(initial.CaptureSession());
            mover.UndoAsync(initial.SessionId).GetAwaiter().GetResult();
            initial.RestoreUndone(first.PhotoId,'F');
            db.Save(initial.CaptureSession());
            Ensure(db.MoveItems(initial.SessionId).Single().State=="Undone" &&
                db.InspectTerminalMoveHistory().CanConsiderLaterAuthorization,
                "First GUI Move Undo setup is not terminal");
            test(db,initial,initial.CaptureSession(),root);
        }
        finally { Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous); }
    }

    private static (DurableMoveItem Ticket,PreparedCopy Prepared,IReadOnlyList<SortPlanItem> Plan)
        PrepareB4Candidate(WorkspaceSessionStore db,SortingWorkspace next)
    {
        next.Assign('F');
        var plan=next.ReviewPlan();
        Ensure(plan.Count==1 && Path.GetFileName(plan[0].Source)=="photo_02.png",
            "B4 candidate must be the untouched second photo");
        next.ApprovePlan(next.CurrentPlanId!.Value,next.Revision);
        var p=DurableCopyCoordinator.PrepareAsync(next,plan,next.CurrentPlanId!.Value,next.Revision)
            .GetAwaiter().GetResult().Single();
        var id=Guid.NewGuid();var install=db.MoveInstallationId();
        string folder=Path.GetDirectoryName(p.Destination)!;
        string archive=Path.Combine(Path.GetDirectoryName(p.Source)!,".photosift-reborn-recovery",
            install.ToString("N"),p.SessionId.ToString("N"),id.ToString("N"));
        var row=new DurableMoveItem(id,install,p.SessionId,p.PhotoId,p.PlanId,p.Revision,
            DateTime.UtcNow.Ticks,p.Source,p.Destination,
            Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
            Path.Combine(archive,Path.GetFileName(p.Source)),
            Path.Combine(archive,"installed-"+Path.GetFileName(p.Destination)),
            p.OutputRoot,p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,"Intent",null,p.Key);
        return (row,p,plan);
    }

    // B4B1A: distinct generated photo03 after two natively undone Moves.
    // The two-Move B4A GUI integration remains unchanged and owner-accepted.
    private static void WithThirdEpochFixture(Action<WorkspaceSessionStore,
        DurableMoveCoordinator,SortingWorkspace,DurableMoveItem,string> test)
    {
        WithB4GuiFixture((db,first,saved,root)=>
        {
            Ensure(db.SealMoveEpochLedger().SealedRows==1,
                "First terminal epoch receipt did not seal");
            Ensure(db.SealTerminalMoveHistory().Verified,
                "Prior B4A Move closeout missing");
            var next=db.BeginFirstGuiSuccessor(saved);
            var secondWork=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            secondWork.RestoreSession(next);
            var second=PrepareB4Candidate(db,secondWork);
            Ensure(db.StageNextMoveEpochProposal(second.Ticket).Verified,
                "Prior singleton B4A successor ticket did not stage");
            var mover=new DurableMoveCoordinator(db);
            var prep=mover.PrepareApprovedStagedSuccessorAsync(secondWork,second.Plan,
                second.Ticket).GetAwaiter().GetResult();
            mover.ApproveStagedSuccessor(secondWork,prep,second.Ticket,true);
            var committed=mover.ExecuteApprovedStagedSuccessorAsync(secondWork,prep,
                second.Ticket).GetAwaiter().GetResult();
            secondWork.RetireCommitted(new[]{committed.PhotoId});
            db.Save(secondWork.CaptureSession());
            var undone=mover.UndoAsync(next.SessionId).GetAwaiter().GetResult();
            secondWork.RestoreUndone(undone.PhotoId,'F');
            db.Save(secondWork.CaptureSession());
            Ensure(db.SealMoveEpochLedger() is {Verified:true,SealedRows:2,MoveRows:2},
                "Two terminal native Move receipts required for third epoch");
            // B4B1B: produce the THIRD fixture through the same atomic,
            // immutable checkpoint transition used by the actual GUI.
            var thirdSaved=db.BeginThirdGuiSession(secondWork.CaptureSession());
            var third=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            third.RestoreSession(thirdSaved);
            string thirdSource=Path.Combine(root,"source","photo_03.png");
            Ensure(third.Current is not null &&
                SortingWorkspace.PathComparer.Equals(third.Current.Path,thirdSource) &&
                db.InspectThirdGuiSession(third.CaptureSession()).Verified,
                "Atomic third-GUI session must select the exact generated photo03");
            third.Assign('F');
            var plan=third.ReviewPlan();
            Ensure(plan.Count==1 && Path.GetFileName(plan[0].Source)=="photo_03.png",
                "Third review must contain only untouched generated photo03");
            third.ApprovePlan(third.CurrentPlanId!.Value,third.Revision);
            var prepared=DurableCopyCoordinator.PrepareAsync(third,plan,
                third.CurrentPlanId!.Value,third.Revision).GetAwaiter().GetResult().Single();
            var id=Guid.NewGuid();var install=db.MoveInstallationId();
            var folder=Path.GetDirectoryName(prepared.Destination)!;
            var archive=Path.Combine(Path.GetDirectoryName(prepared.Source)!,
                ".photosift-reborn-recovery",install.ToString("N"),
                prepared.SessionId.ToString("N"),id.ToString("N"));
            var row=new DurableMoveItem(id,install,prepared.SessionId,prepared.PhotoId,
                prepared.PlanId,prepared.Revision,DateTime.UtcNow.Ticks,
                prepared.Source,prepared.Destination,
                Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
                Path.Combine(archive,Path.GetFileName(prepared.Source)),
                Path.Combine(archive,"installed-"+Path.GetFileName(prepared.Destination)),
                prepared.OutputRoot,prepared.Hash,prepared.Identity,prepared.Length,
                prepared.WriteTicks,null,null,"Intent",null,prepared.Key);
            // This test-only save does NOT pretend to be a completed third-GUI
            // continuation; B4B2 must add a native-proved atomic GUI checkpoint.
            db.Save(third.CaptureSession());
            test(db,mover,third,row,root);
        });
    }

    // P6D2A: same two-key owner-generated fixture gate as Windows GUI.
    // Every case receives a DIFFERENT synthetic photo/data/home. No personal files.
    private static void WithRejectFixture(Action<WorkspaceSessionStore,SortingWorkspace,Photo,string> test)
    {
        string root=Root();
        File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),
            P5CGuiFixturePolicy.MarkerContents);
        var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
        Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
        try
        {
            var work=RenameFixture(root);
            var target=work.Current ?? throw new IOException("No disposable photograph for Reject test.");
            work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            test(db,work,target,root);
        }
        finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous);}
    }

    private static void WriteSecondRejectGuiManifest(string root)
    {
        var hashes=new Dictionary<string,string>();
        foreach(var name in new[]{"photo_01.png","photo_02.png","photo_03.png"})
            hashes[name]=SortingWorkspace.ComputeSha256Async(
                Path.Combine(root,"source",name)).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(root,"P5C_OWNER_MANIFEST.json"),
            System.Text.Json.JsonSerializer.Serialize(new {schema=1,source_sha256=hashes,
                output_sentinel="P5C_SENTINEL_DO_NOT_MODIFY\n"}));
    }

    public static int Run()
    {
        _passed=0; _failed=0;
        Console.WriteLine("P3B2_SOURCE_CANDIDATE=SYNTHETIC_INTERRUPTION_CUTS_READ_ONLY_RECOVERY_UI_NOT_PRODUCTION_CERTIFIED");
        Console.WriteLine("P2_RUNTIME_UI=NOT_TESTED_BY_SELF_TEST");
        var root=Root();
        Console.WriteLine("P2_DISPOSABLE_TEST_ROOT="+root);
        var work=Create(root);
        Check("IMPORT_REAL_FOLDER",()=>Ensure(work.Photos.Count==3,"Expected 3 synthetic real PNGs"));
        Check("ROUTE_CONFIGURATION",()=>{
            work.SetRoute('P',"Projects");
            Ensure(work.Routes.Any(x=>x.Key=='P' && x.Label=="Projects"),"Cannot configure P route");
            Refuse<ArgumentException>(()=>work.SetRoute('L',"Projects"));
            Refuse<ArgumentException>(()=>work.SetRoute('L',"../elsewhere"));
        });
        Check("DESTINATION_PARENT_REFUSED",()=>{
            Refuse<IOException>(()=>work.SetDestinationRoot(root));
            Ensure(work.DestinationRoot==Path.Combine(root,"output"),"Wrong root after refusal");
        });
        Check("SOURCE_AFTER_DESTINATION_OVERLAP_REFUSED",()=>{
            var t=Root(); var w=new SortingWorkspace();
            w.SetDestinationRoot(Path.Combine(t,"output"));
            Refuse<IOException>(()=>w.OpenSource(t));
            Ensure(w.SourceRoot is null,"Source set despite overlap refusal");
        });
        Check("KEY_ASSIGN_AND_STAGED_UNDO",()=>{
            work.Assign('f'); work.Assign('t');
            Ensure(work.AssignmentCount==2 && work.Index==2,"Wrong assignment progress");
            work.UndoDecision();
            Ensure(work.AssignmentCount==1 && work.Index==1,"Undo did not return to current photo");
            work.Assign('p');
            Ensure(work.AssignmentCount==2,"Undo then reassign missing");
        });
        IReadOnlyList<SortPlanItem> plan = [];
        Check("PLAN_SHOWS_EXACT_DESTINATIONS",()=>{
            plan=work.ReviewPlan();
            Ensure(plan.Count==2 && plan.Any(x=>x.Destination.Contains("Family")) && plan.Any(x=>x.Destination.Contains("Projects")),"Plan contents wrong");
            Ensure(plan.All(x=>x.Destination.StartsWith(work.DestinationRoot!,SortingWorkspace.PathComparison)),"Plan path escaped root");
            Ensure(!Directory.EnumerateFiles(work.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),"Review caused disk writes");
        });
        Check("REVISION_REQUIRES_REVIEW",()=>{
            work.Assign('t');
            try {work.ApplyReviewedCopiesAsync().GetAwaiter().GetResult();throw new Exception("Stale review accepted");}
            catch(IOException){}
            work.UndoDecision();
            plan=work.ReviewPlan();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
        });
        Check("VERIFIED_COPIES_AND_SOURCE_PRESERVED",()=>{
            var before=plan.ToDictionary(p=>p.Source,p=>SortingWorkspace.ComputeSha256Async(p.Source).GetAwaiter().GetResult());
            var result=work.ApplyReviewedCopiesAsync().GetAwaiter().GetResult();
            Ensure(result.Error is null && result.Copied==2,"Expected 2 complete copies");
            foreach(var p in plan){
                Ensure(File.Exists(p.Destination),"Output missing: "+p.Destination);
                Ensure(SortingWorkspace.ComputeSha256Async(p.Source).GetAwaiter().GetResult()==before[p.Source],"Source modified");
                Ensure(SortingWorkspace.ComputeSha256Async(p.Destination).GetAwaiter().GetResult()==before[p.Source],"Output data mismatch");
            }
        });
        Check("BLOCK_SWITCH_WITH_LIVE_APPLIED_UNDO",()=>{
            Refuse<InvalidOperationException>(()=>work.OpenSource(Path.Combine(root,"source")));
            Refuse<InvalidOperationException>(()=>work.SetDestinationRoot(Path.Combine(root,"output")));
        });
        Check("UNDO_REFUSES_CHANGED_OUTPUT",()=>{
            var target=work.Applied[^1].Destination;
            File.AppendAllText(target,"externally modified");
            var result=work.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(result.Error is not null,"Should report conflict");
            Ensure(File.Exists(target),"Externally modified file deleted");
            // This test mutates only disposable bytes and then restores the output.
            File.WriteAllBytes(target,File.ReadAllBytes(work.Applied[^1].Source));
            // File write time will differ; retry must still refuse since identity is uncertain.
        });
        Check("UNDO_AFTER_CONFLICT_REFUSES_UNCERTAIN_FILE",()=>{
            var result=work.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(result.Error is not null,"An externally rewritten file must remain unresolved");
            Ensure(work.Applied.Count>0,"Lost pending recovery record");
        });
        var cleanRoot=Root();
        var clean=Create(cleanRoot);
        Check("FRESH_BATCH_COPY_AND_UNDO",()=>{
            clean.Assign('f'); clean.Assign('t');
            clean.ReviewPlan();
            clean.ApprovePlan(clean.CurrentPlanId!.Value,clean.Revision);
            Ensure(clean.ApplyReviewedCopiesAsync().GetAwaiter().GetResult().Copied==2,"Copy failed");
            var outcome=clean.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(outcome.Error is null && outcome.Copied==2,"Undo did not remove own outputs");
            Ensure(!Directory.EnumerateFiles(clean.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),"Output bytes remained after undo");
            Ensure(Directory.EnumerateFiles(clean.SourceRoot!,"*.png").Count()==3,"Undo harmed source");
        });
        Check("PREEXISTING_COLLISION_REFUSED",()=>{
            var collisionRoot=Root();
            var w=Create(collisionRoot);
            w.Assign('f');
            var target=Path.Combine(w.DestinationRoot!,"Family",Path.GetFileName(w.Photos[0].Path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,"do-not-clobber");
            Refuse<IOException>(()=>w.ReviewPlan());
            Ensure(File.ReadAllText(target)=="do-not-clobber","Existing file altered");
        });
        Check("STALE_SOURCE_REFUSED",()=>{
            var staleRoot=Root(); var w=Create(staleRoot);
            w.Assign('f'); w.ReviewPlan(); w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            File.AppendAllText(w.Photos[0].Path,"externally changed source");
            Refuse<IOException>(()=>w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult());
            Ensure(!Directory.EnumerateFiles(w.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),"Wrote despite stale source");
        });
        Check("APPLY_CANCELLATION_BEFORE_MUTATION",()=>{
            var cancelRoot=Root(); var w=Create(cancelRoot); w.Assign('f'); w.ReviewPlan(); w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var cts=new CancellationTokenSource();cts.Cancel();
            try{ w.ApplyReviewedCopiesAsync(cts.Token).GetAwaiter().GetResult();throw new Exception("Precancel unexpectedly accepted"); }
            catch(OperationCanceledException){}
            Ensure(!Directory.EnumerateFiles(w.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),"Cancellation wrote data");
        });
        Check("P2_EXPLICIT_APPROVAL_NOT_PREVIEW",()=>{
            var w = Create(Root()); w.Assign('f');
            w.ReviewPlan();
            Refuse<IOException>(() => w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult());
            var id = w.CurrentPlanId!.Value;
            w.ApprovePlan(id, w.Revision);
            Ensure(w.IsCurrentPlanApproved,"Core approval failed");
            w.SetRoute('P',"Projects");
            Ensure(!w.IsCurrentPlanApproved,"Route mutation left approval in force");
            Refuse<IOException>(() => w.ApprovePlan(id,w.Revision));
        });
        Check("P2_FINAL_PHOTO_NOT_REASSIGNED",()=>{
            var w=Create(Root());
            w.Assign('f'); w.Assign('t'); w.Assign('w');
            Ensure(w.AtEnd && w.Current is null,"Expected real end-of-pool state");
            var before=w.AssignmentCount;
            w.Assign('f');
            Ensure(w.AtEnd && w.AssignmentCount==before,"End-of-pool key mutated photo");
            w.Navigate(-1);
            Ensure(w.Current is not null && w.Index==2,"Cannot navigate back from end");
        });
        Check("P2_DIGIT_AND_NUMPAD_ROUTING",()=>{
            for (int i=0;i<26;i++)
                Ensure(KeyRouting.RouteFor((Avalonia.Input.Key)((int)Avalonia.Input.Key.A + i)) == (char)('A'+i),"Letter key mapping failed");
            for (int i=0;i<10;i++)
            {
                Ensure(KeyRouting.RouteFor((Avalonia.Input.Key)((int)Avalonia.Input.Key.D0 + i)) == (char)('0'+i),"Top-row digit mapping failed");
                Ensure(KeyRouting.RouteFor((Avalonia.Input.Key)((int)Avalonia.Input.Key.NumPad0 + i)) == (char)('0'+i),"Numpad digit mapping failed");
            }
            Ensure(KeyRouting.RouteFor(Avalonia.Input.Key.Left) is null,"Navigation mapped to a route");
        });
        Check("P2C_CODEC_REAL_JPEG_PNG_AND_DIMENSIONS",()=>{
            var assets = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            foreach (var name in new[]{"demo_camera.jpg", "photo_01.png"})
            {
                var bytes = MagickPreviewDecoder.DecodeToPng(Path.Combine(assets, name));
                Ensure(bytes.Length > 24 && bytes.Length <= MagickPreviewDecoder.MaximumPngBytes, "Unexpected encoded preview size");
                Ensure(bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}), "Expected PNG-encoded preview");
                int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
                int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
                Ensure(width > 0 && height > 0 && Math.Max(width,height) <= MagickPreviewDecoder.PreviewLongEdge, "Out of bounds preview geometry");
            }
        });
        Check("P2C_CODEC_REJECTS_DISGUISED_FORMAT_AND_EMPTY",()=>{
            var fake = Path.Combine(root, "source", "disguised.png");
            File.WriteAllText(fake, "%PDF-1.7 disguised as PNG");
            Refuse<InvalidDataException>(()=>MagickPreviewDecoder.DecodeToPng(fake));
            File.WriteAllBytes(fake, []);
            Refuse<InvalidDataException>(()=>MagickPreviewDecoder.DecodeToPng(fake));
        });
        Check("P2C_EXIF_ORIENTATION_6_PORTRAIT_PREVIEW",()=>{
            var file=Path.Combine(AppContext.BaseDirectory,"Fixtures","orientation_6.jpg");
            var bytes=MagickPreviewDecoder.DecodeToPng(file);
            int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
            int height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            Ensure(width == 90 && height == 160,$"EXIF auto-orient expected 90x160, got {width}x{height}");
        });
        Check("P2C_CACHE_LRU_LIMIT_AND_KEY_VERSION",()=>{
            var cache = new PreviewCache(maxBytes: 1024, maxEntries:2);
            var a=new PreviewCache.Key(Guid.NewGuid(), "same.png",100,1);
            var b=new PreviewCache.Key(Guid.NewGuid(), "other.png",100,1);
            var c=new PreviewCache.Key(Guid.NewGuid(), "third.png",100,1);
            cache.Put(a,new byte[350]);cache.Put(b,new byte[350]);
            Ensure(cache.TryGet(a,out var hit) && hit!.Length==350, "Cache hit broken");
            cache.Put(c,new byte[350]);
            Ensure(!cache.TryGet(b,out _),"Oldest cache entry not evicted");
            Ensure(cache.Count==2 && cache.Bytes==700 && cache.Evictions==1,"LRU accounting incorrect");
            Ensure(!cache.TryGet(a with { WriteTicks=2 },out _),"Version changes must miss cache");
            cache.Put(new PreviewCache.Key(Guid.NewGuid(),"huge.png",100,1),new byte[1500]);
            Ensure(cache.Count==2,"Oversize cache item was admitted");
            cache.Clear();Ensure(cache.Count==0 && cache.Bytes==0,"Cache clear failed");
        });
        Check("P3A_SQLITE_SESSION_ROUNDTRIP_AND_UNDO",()=>{
            var trial = Root(); var w = Create(trial);
            w.SetRoute('1',"One");
            w.Assign('f'); w.Assign('1');
            Guid photoId = w.Photos[0].Id;
            Guid sessionId = w.SessionId;
            string storeRoot=Path.Combine(trial,"state");
            using (var db=new WorkspaceSessionStore(storeRoot))
            {
                Ensure(db.Load() is null,"Fresh DB unexpectedly has a checkpoint");
                db.Save(w.CaptureSession());
            }
            var restored=new SortingWorkspace();
            using (var db=new WorkspaceSessionStore(storeRoot))
            {
                restored.RestoreSession(db.Load()!);
                Ensure(!db.HasUnreconciledCopy,"Fresh checkpoint was marked as active copy");
            }
            Ensure(restored.SessionId==sessionId && restored.Photos[0].Id==photoId,"Logical photo/session ID changed after restart");
            Ensure(restored.AssignmentCount==2 && restored.Index==2 &&
                   restored.CategoryFor(restored.Photos[1].Path)=='1',"Choices/cursor did not resume");
            Ensure(!restored.IsCurrentPlanApproved,"Plan approval illegally survived restart");
            restored.UndoDecision();
            Ensure(restored.AssignmentCount==1 && restored.Index==1,"Persisted choice history could not be undone");
        });
        Check("P3A_SQLITE_COPY_RISK_BARRIER_SURVIVES_RESTART",()=>{
            var trial=Root();var w=Create(trial);
            var home=Path.Combine(trial,"state");
            using(var db=new WorkspaceSessionStore(home))
            {
                db.Save(w.CaptureSession());
                db.SetCopyBarrier(true,w.CaptureSession());
                Ensure(db.HasUnreconciledCopy,"Copy barrier missing before close");
            }
            using(var db=new WorkspaceSessionStore(home))
            {
                Ensure(db.HasUnreconciledCopy,"Risk barrier lost on restart");
                Ensure(db.Load()!.SessionId == w.SessionId,"Risk checkpoint lost session");
                db.SetCopyBarrier(false,w.CaptureSession());
            }
            using(var db=new WorkspaceSessionStore(home))
                Ensure(!db.HasUnreconciledCopy,"Clean Undo not recorded");
        });
        Check("P3A_SQLITE_NEWER_SCHEMA_REJECTED",()=>{
            var trial=Root();var home=Path.Combine(trial,"state");
            using(var db=new WorkspaceSessionStore(home)) { db.Save(Create(trial).CaptureSession()); }
            var file=Path.Combine(home,"workspaces-v1.sqlite");
            using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+file))
            { db.Open();using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA user_version=200;";cmd.ExecuteNonQuery(); }
            Refuse<InvalidDataException>(()=>{using var db=new WorkspaceSessionStore(home);});
            Ensure(File.Exists(file),"Newer DB was overwritten/deleted");
        });
        Check("P3B_NATIVE_IDENTITY_DIFFERENT_FOR_EQUAL_BYTES",()=>{
            var root=Root();var f=Path.Combine(root,"source","photo_01.png");
            var other=Path.Combine(root,"source","independent.png");
            File.Copy(f,other);
            Ensure(NativeFileCapability.Identity(f)!=NativeFileCapability.Identity(other),
                "Two independently-created byte-identical files must have different native IDs");
        });
        Check("P3B_DURABLE_COPY_RESTART_UNDO_ARCHIVES_OUTPUT",()=>{
            var root=Root();var work=Create(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            string destination=plan[0].Destination;
            string expected=SortingWorkspace.ComputeSha256Async(plan[0].Source).GetAwaiter().GetResult();
            string store=Path.Combine(root,"state");
            using(var db=new WorkspaceSessionStore(store))
            {
                db.Save(work.CaptureSession());
                var journal=new DurableCopyCoordinator(db);
                var applied=journal.ApplyAsync(prepared).GetAwaiter().GetResult();
                Ensure(applied.Completed==1 && applied.Detail is null,"Journalled copy did not finish");
                Ensure(File.Exists(destination),"Expected published output");
                Ensure(SortingWorkspace.ComputeSha256Async(destination).GetAwaiter().GetResult()==expected,"Output mismatch");
            }
            using(var db=new WorkspaceSessionStore(store))
            {
                var journal=new DurableCopyCoordinator(db);
                Ensure(journal.Reconcile(work.SessionId).Contains("1 completed"),"Restart reconciliation missed the committed output");
                Ensure(journal.Committed(work.SessionId)==1,"Committed journal record not restored");
                var done=journal.UndoAsync(work.SessionId).GetAwaiter().GetResult();
                Ensure(done.Completed==1 && done.Detail is null,"Restart Undo refused a verified owned output");
                Ensure(!File.Exists(destination),"Undo left a copy at the sorted destination");
                Ensure(journal.Committed(work.SessionId)==0,"Undo retained an active copy");
                var history=db.CopyItems(work.SessionId).Single();
                Ensure(history.State=="Undone" && File.Exists(history.Recovery),"Undo lost the archived recovery copy");
                Ensure(SortingWorkspace.ComputeSha256Async(history.Recovery).GetAwaiter().GetResult()==expected,"Archive bytes mismatched");
                Ensure(File.Exists(plan[0].Source),"Undo deleted the original");
            }
        });
        Check("P3B_REFUSE_SAME_BYTES_REPLACED_OUTPUT",()=>{
            var root=Root();var work=Create(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var journal=new DurableCopyCoordinator(db);
            Ensure(journal.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup copy failed");
            string output=plan[0].Destination;
            var originalBytes=File.ReadAllBytes(output);
            File.Move(output,output+".untouched");
            File.WriteAllBytes(output,originalBytes); // same bytes, DIFFERENT native identity
            var before=NativeFileCapability.Identity(output);
            var result=journal.UndoAsync(work.SessionId).GetAwaiter().GetResult();
            Ensure(result.Completed==0 && result.Detail is not null,"Byte-identical replacement was wrongly undone");
            Ensure(File.Exists(output) && NativeFileCapability.Identity(output)==before,"Replacement was modified by Undo");
            Ensure(File.Exists(output+".untouched"),"Original app-owned output was deleted");
        });
        Check("P3B_STRANDED_INTENT_REFUSES_AUTO_CLEANUP",()=>{
            var root=Root();var work=Create(root);
            var op=Guid.NewGuid();var src=work.Photos[0].Path;
            string temp=Path.Combine(work.DestinationRoot!,".photosift-"+op.ToString("N")+".part");
            File.WriteAllText(temp,"unverified stranger text");
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.PutCopyItem(new DurableCopyItem(op,work.SessionId,work.Photos[0].Id,'F',Guid.NewGuid(),0,1,
                src,Path.Combine(work.DestinationRoot!,"Family","would-be.png"),temp,
                Path.Combine(root,"recovery.png"),"DEADBEEF","unknown",0,0,null,null,0,"Intent",null));
            var journal=new DurableCopyCoordinator(db);
            journal.Reconcile(work.SessionId);
            Ensure(journal.NeedsAttention(work.SessionId)==1,"Interrupted intent was automatically authorized");
            Ensure(File.Exists(temp) && File.ReadAllText(temp)=="unverified stranger text","Unknown temp destroyed");
        });
        // P3B2: Deterministic abrupt-stop injection. This observes actual local
        // filesystem and SQLite state after a simulated no-catch process exit;
        // it does NOT substitute for OS kill/power-failure validation.
        foreach (string cutpoint in new[] {
            "INTENT_RECORDED", "OUTPUT_DIRECTORY_READY", "TEMP_FLUSHED_UNRECORDED",
            "TEMP_RECORDED", "TEMP_VERIFIED", "OUTPUT_PUBLISHED_UNJOURNALLED",
            "OUTPUT_PUBLISHED_RECORDED", "TEMP_REMOVED_BEFORE_COMMIT", "COPY_COMMITTED" })
        {
            string boundary = cutpoint;
            Check("P3B2_COPY_CUT_"+boundary,()=>
            {
                string root=Root(); var w=Create(root); w.Assign('F');
                var plan=w.ReviewPlan();
                var prepared=DurableCopyCoordinator.PrepareAsync(w,plan,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
                w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
                var source=plan[0].Source; var original=File.ReadAllBytes(source);
                string store=Path.Combine(root,"state");
                using(var db=new WorkspaceSessionStore(store))
                {
                    var journal=new DurableCopyCoordinator(db, cut => {
                        if (cut==boundary) throw new SimulatedInterruptionException(cut);
                    });
                    Refuse<SimulatedInterruptionException>(()=> journal.ApplyAsync(prepared).GetAwaiter().GetResult());
                }
                using(var db=new WorkspaceSessionStore(store))
                {
                    var journal=new DurableCopyCoordinator(db);
                    journal.Reconcile(w.SessionId);
                    var entry=db.CopyItems(w.SessionId).Single();
                    bool installed = boundary is "OUTPUT_PUBLISHED_RECORDED" or "TEMP_REMOVED_BEFORE_COMMIT" or "COPY_COMMITTED";
                    Ensure(entry.State==(installed ? "Committed" : "NeedsAttention"),
                        "Unexpected recovered state "+entry.State+" after "+boundary);
                    Ensure(journal.Inspect(w.SessionId).Single().OperationId==entry.Id,"Inspection lost journal identity");
                    if (boundary=="OUTPUT_PUBLISHED_UNJOURNALLED")
                        Ensure(File.Exists(plan[0].Destination),"Ambiguous published copy must be preserved");
                    if (installed) Ensure(File.Exists(plan[0].Destination),"Committed output missing after simulated stop");
                    Ensure(File.ReadAllBytes(source).SequenceEqual(original),"Simulated interruption changed original bytes");
                    if (!installed) Ensure(journal.NeedsAttention(w.SessionId)==1,"Unresolved state incorrectly cleared");
                }
            });
        }
        foreach(string cutpoint in new[] {"UNDO_INTENT_RECORDED", "UNDO_RELOCATED_UNJOURNALLED", "UNDO_ARCHIVED_RECORDED"})
        {
            string boundary=cutpoint;
            Check("P3B2_UNDO_CUT_"+boundary,()=>
            {
                string root=Root(); var w=Create(root); w.Assign('F');
                var plan=w.ReviewPlan();
                var prepared=DurableCopyCoordinator.PrepareAsync(w,plan,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
                w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
                string store=Path.Combine(root,"state");
                using(var db=new WorkspaceSessionStore(store))
                {
                    var setup=new DurableCopyCoordinator(db);
                    Ensure(setup.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup did not publish");
                    var journal=new DurableCopyCoordinator(db, cut => {
                        if (cut==boundary) throw new SimulatedInterruptionException(cut);
                    });
                    Refuse<SimulatedInterruptionException>(()=>journal.UndoAsync(w.SessionId).GetAwaiter().GetResult());
                }
                using(var db=new WorkspaceSessionStore(store))
                {
                    var journal=new DurableCopyCoordinator(db);
                    journal.Reconcile(w.SessionId);
                    var entry=db.CopyItems(w.SessionId).Single();
                    if (boundary=="UNDO_INTENT_RECORDED")
                    {
                        Ensure(entry.State=="NeedsAttention","Unmoved UndoIntent unexpectedly authorized");
                        Ensure(File.Exists(plan[0].Destination),"Ambiguous Undo removed destination");
                    }
                    else
                    {
                        Ensure(entry.State=="Undone","Archived verified Undo did not reconcile");
                        Ensure(!File.Exists(plan[0].Destination) && File.Exists(entry.Recovery),"Undo archive not preserved");
                        Ensure(SortingWorkspace.ComputeSha256Async(entry.Recovery).GetAwaiter().GetResult()==prepared[0].Hash,
                            "Recovery bytes differ after simulated interruption");
                    }
                }
            });
        }
        Check("P3B2_RECOVERY_INSPECTION_READ_ONLY",()=>
        {
            string root=Root();var w=Create(root);string store=Path.Combine(root,"state");
            using var db=new WorkspaceSessionStore(store);
            var src=w.Photos[0].Path; var id=Guid.NewGuid();
            var temp=Path.Combine(w.DestinationRoot!,".photosift-"+id.ToString("N")+".part");
            File.WriteAllText(temp,"never trust this unrecorded file");
            db.PutCopyItem(new DurableCopyItem(id,w.SessionId,w.Photos[0].Id,'F',Guid.NewGuid(),0,1,
                src,Path.Combine(w.DestinationRoot!,"Family","photo.png"),temp,
                Path.Combine(root,"recover.png"),"unknown","unknown",0,0,null,null,0,"Intent",null));
            var journal=new DurableCopyCoordinator(db);var before=db.CopyItems(w.SessionId).Single();
            var inspected=journal.Inspect(w.SessionId).Single();
            Ensure(inspected.State=="Intent" && inspected.Temporary==temp,"Inspector mislabelled intent");
            Ensure(db.CopyItems(w.SessionId).Single()==before,"Inspection modified SQLite");
            Ensure(File.Exists(temp),"Inspection deleted ambiguous temp");
            var marker=Path.Combine(root,"P3B2_TEST_ONLY.marker");
            File.Move(marker, marker+".temporarily-off");
            try { Refuse<InvalidOperationException>(()=> new DurableCopyCoordinator(db, _=>{})); }
            finally { File.Move(marker+".temporarily-off",marker); }
        });
        // P3B3: archive-audit evidence is read-only and NEVER promotes historical
        // Undone into a fresh permission to mutate/clean up anything.
        Check("P3B3_ARCHIVED_VERIFIED_AFTER_RESTART_AUDIT",()=>
        {
            string root=Root();var w=Create(root);w.Assign('F');
            var p=w.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(w,p,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            string home=Path.Combine(root,"state");
            using(var db=new WorkspaceSessionStore(home))
            {
                var j=new DurableCopyCoordinator(db);
                Ensure(j.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Copy failed");
                Ensure(j.UndoAsync(w.SessionId).GetAwaiter().GetResult().Completed==1,"Archive Undo failed");
            }
            using(var db=new WorkspaceSessionStore(home))
            {
                var j=new DurableCopyCoordinator(db);
                var before=db.CopyItems(w.SessionId).Single();
                Ensure(j.Inspect(w.SessionId).Single().Evidence=="ARCHIVE_VERIFIED","Audit could not verify owned archive");
                Ensure(db.CopyItems(w.SessionId).Single()==before,"Read-only audit changed SQLite");
            }
        });
        Check("P3B3_ARCHIVE_MISSING_ALERT_PRESERVES_HISTORY",()=>
        {
            string root=Root();var w=Create(root);w.Assign('F');
            var p=w.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(w,p,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var j=new DurableCopyCoordinator(db);
            Ensure(j.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Copy setup failed");
            Ensure(j.UndoAsync(w.SessionId).GetAwaiter().GetResult().Completed==1,"Undo setup failed");
            var prior=db.CopyItems(w.SessionId).Single();
            var held=prior.Recovery+".owner-retained";
            File.Move(prior.Recovery,held); // fixture-owned file moved aside, NEVER deleted
            Ensure(j.Inspect(w.SessionId).Single().Evidence=="ARCHIVE_MISSING","Missing archive not flagged");
            Ensure(db.CopyItems(w.SessionId).Single()==prior,"Audit rewrote historical state");
            Ensure(File.Exists(held),"Audit destroyed the moved archive");
        });
        Check("P3B3_ARCHIVE_SAME_BYTES_DIFFERENT_ID_REJECTED",()=>
        {
            string root=Root();var w=Create(root);w.Assign('F');
            var p=w.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(w,p,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var j=new DurableCopyCoordinator(db);
            Ensure(j.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Copy setup failed");
            Ensure(j.UndoAsync(w.SessionId).GetAwaiter().GetResult().Completed==1,"Undo setup failed");
            var prior=db.CopyItems(w.SessionId).Single();
            var retained=prior.Recovery+".original-retained";
            File.Move(prior.Recovery,retained);
            File.Copy(retained,prior.Recovery); // identical digest, distinct inode
            Ensure(j.Inspect(w.SessionId).Single().Evidence=="ARCHIVE_CHANGED_OR_IDENTITY_MISMATCH",
                "Byte-identical replacement was trusted without original native identity");
            Ensure(File.Exists(retained) && File.Exists(prior.Recovery),"Audit deleted an archive");
            Ensure(db.CopyItems(w.SessionId).Single()==prior,"Audit modified historical record");
        });
        Check("P3B3_DESTINATION_REAPPEARED_AFTER_UNDO_ALERT",()=>
        {
            string root=Root();var w=Create(root);w.Assign('F');
            var p=w.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(w,p,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var j=new DurableCopyCoordinator(db);
            Ensure(j.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Copy setup failed");
            Ensure(j.UndoAsync(w.SessionId).GetAwaiter().GetResult().Completed==1,"Undo setup failed");
            var prior=db.CopyItems(w.SessionId).Single();
            File.WriteAllText(prior.Destination,"third-party data");
            Ensure(j.Inspect(w.SessionId).Single().Evidence=="DESTINATION_PRESENT_AFTER_UNDO",
                "Third-party file at undone destination was not flagged");
            Ensure(File.ReadAllText(prior.Destination)=="third-party data","Audit overwrote a foreign destination");
            Ensure(db.CopyItems(w.SessionId).Single()==prior,"Audit rewrote journal");
        });
        // P4A: original default destinations work on an existing v1 SQLite
        // snapshot, without changing the database schema or historical routes.
        Check("P4A_IMPLICIT_LETTER_AND_DIGIT_ROUTES",()=>
        {
            var w=Create(Root());
            Ensure(!w.Routes.Any(r=>r.Key=='B' || r.Key=='7'),"Fresh workspace unexpectedly materialized defaults");
            w.Assign('b'); w.Assign('7'); w.Assign('9');
            Ensure(w.AtEnd && w.AssignmentCount==3,"Implicit routes did not stage 3 decisions");
            var entries=w.ReviewPlan();
            Ensure(entries.Count==3 && entries.Any(e=>e.Key=='B' &&
                e.Destination.Contains(Path.DirectorySeparatorChar+"B"+Path.DirectorySeparatorChar)) &&
                entries.Any(e=>e.Key=='7' &&
                e.Destination.Contains(Path.DirectorySeparatorChar+"7"+Path.DirectorySeparatorChar)),
                "Implicit destination path was not exactly key-named");
            Ensure(!Directory.EnumerateFiles(w.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),
                "Default-routing review unexpectedly changed files");
            var snapshot=w.CaptureSession();
            var restored=new SortingWorkspace(); restored.RestoreSession(snapshot);
            Ensure(restored.Routes.Any(r=>r.Key=='B' && r.Label=="B") &&
                restored.Routes.Any(r=>r.Key=='7' && r.Label=="7") &&
                restored.AssignmentCount==3 && restored.AtEnd,
                "Implicit routes did not persist/restore in same v1 session format");
            Ensure(!restored.IsCurrentPlanApproved,"Restore accidentally carried review approval");
        });
        Check("P4A_OLD_SESSION_UNCONFIGURED_KEY_FALLBACK",()=>
        {
            var old=Create(Root()).CaptureSession();
            Ensure(old.Routes.Length==3,"Legacy initial routing fixture unexpectedly changed");
            var w=new SortingWorkspace(); w.RestoreSession(old);
            w.Assign('0');
            Ensure(w.Routes.Any(r=>r.Key=='0' && r.Label=="0") &&
                w.AssignmentCount==1 && w.CaptureSession().Routes.Length==4,
                "An older saved session could not add a default numeric route");
        });
        Check("P4A_DEFAULT_ROUTE_NAME_CONFLICT_FAILS_CLOSED",()=>
        {
            var w=Create(Root());
            w.SetRoute('B',"9");
            Refuse<ArgumentException>(()=>w.Assign('9'));
            Ensure(w.Index==0 && w.AssignmentCount==0,"Ambiguous implicit route advanced the selection");
            w.SetRoute('B',"Other"); w.Assign('9');
            Ensure(w.AssignmentCount==1 && w.Routes.Any(r=>r.Key=='9'),"Corrected fallback did not work");
        });
        Check("P4D_CROSS_KIND_DEFAULT_RELATIVE_ROUTE_ALIAS_REFUSED",() =>
        {
            var w=Create(Root());
            w.SetRelativeRoute('B',"9");
            long revision=w.Revision;
            Refuse<ArgumentException>(()=>w.SetDefaultRoute('9'));
            Ensure(w.Revision==revision && !w.Routes.Any(r=>r.Key=='9'),
                "Rejected implicit-key alias changed the saved route state");
            w.SetRelativeRoute('B',"Other");
            w.SetDefaultRoute('9');
            revision=w.Revision;
            Refuse<ArgumentException>(()=>w.SetRelativeRoute('C',"9"));
            Ensure(w.Revision==revision && !w.Routes.Any(r=>r.Key=='C'),
                "Rejected cross-kind alias changed the default route");
            var bad=w.CaptureSession() with
            {
                Routes=w.CaptureSession().Routes.Append(new Route('C',"9",RouteKind.Relative,"9")).ToArray()
            };
            Refuse<ArgumentException>(()=>new SortingWorkspace().RestoreSession(bad));
        });
        Check("P4A_INSPECTION_QUARTER_TURN_GEOMETRY",()=>
        {
            var original=InspectionGeometry.Calculate(1600,900,0,1);
            Ensure(original.CanvasWidth==1600 && original.CanvasHeight==900 && original.OffsetX==0,
                "0 degree preview dimensions wrong");
            var clockwise=InspectionGeometry.Calculate(1600,900,1,2);
            Ensure(clockwise.CanvasWidth==1800 && clockwise.CanvasHeight==3200 &&
                clockwise.ImageWidth==3200 && clockwise.ImageHeight==1800 &&
                clockwise.OffsetX==1800 && clockwise.OffsetY==0 && clockwise.RotationDegrees==90,
                "90 degree zoom+translation wrong");
            var down=InspectionGeometry.Calculate(1600,900,2,1);
            Ensure(down.OffsetX==1600 && down.OffsetY==900 && down.RotationDegrees==180,
                "180 degree preview translation wrong");
            var inverse=InspectionGeometry.Calculate(1600,900,-1,0.5);
            Ensure(inverse.CanvasWidth==450 && inverse.CanvasHeight==800 &&
                inverse.OffsetY==800 && inverse.RotationDegrees==270,
                "Negative 90 degree preview translation wrong");
            Refuse<ArgumentOutOfRangeException>(()=>InspectionGeometry.Calculate(1600,900,0,8));
        });
        Check("P4B_ADDITIVE_IMPORT_STABLE_IDS_AND_CHOICES",()=>
        {
            var r=Root(); var w=Create(r); var first=w.Photos[0];
            w.Assign('F');
            var extra=Path.Combine(r,"extra");Directory.CreateDirectory(extra);
            File.Copy(first.Path,Path.Combine(extra,"second_folder.png"));
            var added=w.AddSourceFolder(extra);
            Ensure(added==1 && w.Photos.Count==4 && w.AssignmentCount==1 &&
                w.Photos[0].Id==first.Id && w.SourceRoots.Count==2,
                "Additive folder lost existing logical photos/assignments");
            Ensure(w.AddSourceFolder(extra)==0 && w.Photos.Count==4,"Duplicate folder import duplicated a photo");
            var snap=w.CaptureSession();
            Ensure(snap.Schema==4,"P4E saved snapshot schema must be 4 for reversible removal history");
            var resumed=new SortingWorkspace();resumed.RestoreSession(snap);
            Ensure(resumed.SourceRoots.Count==2 && resumed.Photos[0].Id==first.Id &&
                resumed.AssignmentCount==1,"Resuming additive saved session lost work");
        });
        Check("P4B_ADDITIVE_IMPORT_CANCEL_ATOMIC",()=>
        {
            var r=Root(); var w=Create(r); var before=w.CaptureSession();
            using var cts=new CancellationTokenSource(); cts.Cancel();
            Refuse<OperationCanceledException>(()=>w.AddSourceFolder(Path.Combine(r,"source"),cts.Token));
            Ensure(w.CaptureSession().Revision==before.Revision && w.Photos.Count==before.Photos.Length,
                "Cancelled add mutated saved collection");
        });
        Check("P4B_ADDITIVE_INPUT_ROOT_OVERLAP_REFUSED",()=>
        {
            var r=Root();var w=Create(r);
            Refuse<IOException>(()=>w.AddSourceFolder(r));
            Refuse<IOException>(()=>w.AddSourceFolder(Path.Combine(r,"output")));
            Ensure(w.SourceRoots.Count==1,"Overlap refusal added source root");
        });
        Check("P4B_COLLISION_NUMBER_EXACT_PLAN_SENTINEL_PROTECTED",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var original=Path.Combine(w.DestinationRoot!,"Family",Path.GetFileName(w.Photos[0].Path));
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            File.WriteAllText(original,"original-sentinel");
            Refuse<IOException>(()=>w.ReviewPlan());
            w.SetCollisionChoice(CollisionChoice.Number);
            var plan=w.ReviewPlan();
            Ensure(plan.Count==1 && plan[0].Destination==Path.Combine(w.DestinationRoot!,"Family",
                Path.GetFileNameWithoutExtension(w.Photos[0].Path)+" (2)"+Path.GetExtension(w.Photos[0].Path)),
                "Reviewed collision numbering not exact/stable");
            Ensure(File.ReadAllText(original)=="original-sentinel" && !File.Exists(plan[0].Destination),
                "Collision Review changed output bytes");
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            w.SetCollisionChoice(CollisionChoice.Skip);
            Ensure(!w.IsCurrentPlanApproved,"Changing collision choice retained approval");
        });
        Check("P4B_COLLISION_SKIP_RETAINS_UNCOPIED_CHOICE",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');w.Assign('T');
            var taken=Path.Combine(w.DestinationRoot!,"Family",Path.GetFileName(w.Photos[0].Path));
            Directory.CreateDirectory(Path.GetDirectoryName(taken)!);File.WriteAllText(taken,"sentinel");
            w.SetCollisionChoice(CollisionChoice.Skip);
            var plan=w.ReviewPlan();
            Ensure(plan.Count==1 && w.SkippedReview.Count==1 && w.AssignmentCount==2 &&
                plan[0].Destination.Contains("Travel"),"Skip lost uncommitted decision or valid plan");
            Ensure(File.ReadAllText(taken)=="sentinel","Skip removed sentinel");
        });
        Check("P4B_OLDER_SNAPSHOT_READ_COMPATIBILITY",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var old=w.CaptureSession() with { Schema=1, SourceRoots=null, CollisionMode=CollisionChoice.Refuse };
            var restored=new SortingWorkspace(); restored.RestoreSession(old);
            Ensure(restored.AssignmentCount==1 && restored.SourceRoots.Count==1 &&
                restored.CollisionMode==CollisionChoice.Refuse,"Previous v1 snapshot cannot migrate in memory");
        });
        Check("P4B_BOUNDED_TRUE_PIXEL_DECODE_VS_PREVIEW",()=>
        {
            string fixture=Path.Combine(AppContext.BaseDirectory,"Fixtures","inspection_original_4096x2304.jpg");
            Ensure(File.Exists(fixture),"Large synthetic JPEG missing");
            var preview=MagickPreviewDecoder.DecodeToPng(fixture);
            var original=MagickPreviewDecoder.DecodeToPng(fixture,originalPixels:true);
            int previewWidth=checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(preview.AsSpan(16,4)));
            int originalWidth=checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(16,4)));
            Ensure(previewWidth==2048 && originalWidth==4096,
                "1:1 mode merely upscaled the bounded preview or changed original dimensions");
            Ensure(new FileInfo(fixture).Length>0,"Missing original bytes");
        });
        Check("P4B_SAME_NAME_CROSS_SOURCE_NUMBERING",()=>
        {
            var r=Root(); var w=Create(r); var name=Path.GetFileName(w.Photos[0].Path);
            var other=Path.Combine(r,"more");Directory.CreateDirectory(other);
            File.Copy(w.Photos[0].Path,Path.Combine(other,name));
            Ensure(w.AddSourceFolder(other)==1,"Second file from distinct folder ignored");
            w.Assign('F'); w.Navigate(2); w.Assign('F');
            Ensure(w.AssignmentCount==2,"Cross-root assignment lost choice");
            w.SetCollisionChoice(CollisionChoice.Number);
            var plan=w.ReviewPlan();
            Ensure(plan.Count==2 && plan.Select(p=>p.Destination).Distinct(SortingWorkspace.PathComparer).Count()==2,
                "Two identical basenames shared reviewed output");
            Ensure(plan.Any(x=>x.Destination.EndsWith(" (2)"+Path.GetExtension(name),StringComparison.OrdinalIgnoreCase)),
                "Second same-name source not deterministically numbered");
        });
        Check("P4B_FULL_RESOLUTION_THROUGH_SERIAL_WORKER",()=>
        {
            var asset=Path.Combine(AppContext.BaseDirectory,"Fixtures","inspection_original_4096x2304.jpg");
            var f=new FileInfo(asset);
            using var worker=new PreviewWorker();
            var task=worker.DecodeOriginalAsync(new PreviewWorker.Request(Guid.NewGuid(),asset,f.Length,f.LastWriteTimeUtc.Ticks,1));
            var png=task.GetAwaiter().GetResult();
            Ensure(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16,4))==4096,
                "Original decode worker produced bounded preview instead of original pixels");
        });
        Check("P4C_ADD_SINGLE_FILE_PRESERVES_CHOICE_AND_ID",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');var id=w.Photos[0].Id;
            var dir=Path.Combine(r,"individual");Directory.CreateDirectory(dir);
            var f=Path.Combine(dir,"an individual photo.png");File.Copy(w.Photos[0].Path,f);
            var report=w.ImportPaths([f]);
            Ensure(report.Added.Count==1 && w.Photos.Count==4 && w.AssignmentCount==1 &&
                w.Photos[0].Id==id && w.SourceRoots.Count==2,"Individual Add replaced previous choices");
            Ensure(w.ImportPaths([f]).DuplicateCount==1 && w.Photos.Count==4,
                "Reimport of exact same file added duplicate");
        });
        Check("P4C_RECURSION_IS_EXPLICIT_AND_SAFE",()=>
        {
            var r=Root();var w=Create(r);
            var outer=Path.Combine(r,"recursion");var inner=Path.Combine(outer,"nested","deep");
            Directory.CreateDirectory(inner);
            File.Copy(w.Photos[0].Path,Path.Combine(inner,"deep.png"));
            Ensure(w.ImportPaths([outer],recursive:false).Added.Count==0,"Top-level scan recursed unexpectedly");
            Ensure(w.ImportPaths([outer],recursive:true).Added.Count==1 && w.Photos.Count==4,
                "Explicit recursive import missed nested photograph");
        });
        Check("P4C_RECURSION_EXCLUDES_RECOVERY_TREES",()=>
        {
            var r=Root();var w=Create(r);
            var outer=Path.Combine(r,"tree"); var owned=Path.Combine(outer,".photosift-reborn-recovery","item");
            Directory.CreateDirectory(owned);
            File.Copy(w.Photos[0].Path,Path.Combine(owned,"owned.png"));
            var result=w.ImportPaths([outer],recursive:true);
            Ensure(result.Added.Count==0 && result.UnsafeCount>=1 && w.Photos.Count==3,
                "Recovery contents entered sorting pool");
            Refuse<IOException>(()=>w.ImportPaths([owned]));
        });
        Check("P4C_SELECTED_OUTPUT_ANCESTRY_REFUSED",()=>
        {
            var r=Root();var w=Create(r);var old=w.CaptureSession();
            Refuse<IOException>(()=>w.ImportPaths([r],recursive:true));
            Refuse<IOException>(()=>w.ImportPaths([Path.Combine(r,"output")],recursive:true));
            Ensure(w.CaptureSession().Revision==old.Revision && w.Photos.Count==3,
                "Unsafe root refusal mutated existing collection");
        });
        Check("P4C_CANCEL_MULTI_INPUT_IS_ATOMIC",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');var before=w.CaptureSession();
            using var cts=new CancellationTokenSource();cts.Cancel();
            Refuse<OperationCanceledException>(()=>w.ImportPaths([Path.Combine(r,"source"),Path.Combine(r,"output")],cancel:cts.Token));
            Ensure(w.Revision==before.Revision && w.AssignmentCount==1,
                "Cancelled mixed import modified assigned state");
        });
        Check("P4C_MISSING_AND_CHANGED_INPUT_REFUSED",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');var before=w.CaptureSession();
            Refuse<FileNotFoundException>(()=>w.ImportPaths([Path.Combine(r,"missing.png")]));
            var photo=w.Photos[0];File.AppendAllText(photo.Path,"file-changed");
            Refuse<InvalidOperationException>(()=>w.ImportPaths([photo.Path]));
            Ensure(w.Revision==before.Revision && w.AssignmentCount==1,"Changed source stole saved decision");
        });
        Check("P4C_MULTIPLE_ENTRY_DUPLICATE_SUPPRESSED",()=>
        {
            var r=Root();var w=Create(r);
            var extra=Path.Combine(r,"newfiles");Directory.CreateDirectory(extra);
            var one=Path.Combine(extra,"single.png");File.Copy(w.Photos[0].Path,one);
            var report=w.ImportPaths([one,extra,one]);
            Ensure(report.Added.Count==1 && report.DuplicateCount==2 && w.Photos.Count==4,
                "Picker+drop same physical path created duplicate entries");
        });
        Check("P4C_UNICODE_SPACE_PATH_SNAPSHOT_RESTART",()=>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var sub=Path.Combine(r,"日本語 and photos");Directory.CreateDirectory(sub);
            var photo=Path.Combine(sub,"café picture.png");File.Copy(w.Photos[0].Path,photo);
            var loaded=w.ImportPaths([photo]);
            Ensure(loaded.Added.Count==1,"Unicode and spaces import failed");
            var json=System.Text.Json.JsonSerializer.Serialize(w.CaptureSession());
            var saved=System.Text.Json.JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(json)!;
            var replay=new SortingWorkspace();replay.RestoreSession(saved);
            Ensure(replay.Photos.Count==4 && replay.AssignmentCount==1 &&
                replay.Photos.Any(p=>p.Path==photo),"Saved file import not restored with Unicode filename");
        });
        Check("P4C_NESTED_FILE_ROOT_REPLAY",()=>
        {
            var r=Root();var w=Create(r);var nested=Path.Combine(r,"source","subfolder");
            Directory.CreateDirectory(nested);var one=Path.Combine(nested,"from_nested.png");
            File.Copy(w.Photos[0].Path,one);
            Ensure(w.ImportPaths([one]).Added.Count==1,"Nested file not added");
            var copy=new SortingWorkspace();copy.RestoreSession(w.CaptureSession());
            Ensure(copy.SourceRoots.Count==2 && copy.Photos.Count==4,
                "Nested individually selected file not restored on restart");
        });
        Check("P4C_NATIVE_IDENTITY_ALIAS_SUPPRESSED",()=>
        {
            var r=Root();var w=Create(r);
            var dir=Path.Combine(r,"alias");Directory.CreateDirectory(dir);
            var photo=w.Photos[0];var a=Path.Combine(dir,"A.png");var b=Path.Combine(dir,"B.png");
            File.Copy(photo.Path,a);File.Copy(photo.Path,b);
            // Deterministic simulated native identity probe: production provides
            // actual OS volume+file IDs through NativeFileCapability.
            w.IdentityResolver = path=>path==a || path==b ? "same-physical-file" : "other:"+path;
            var scan=w.ImportPaths([a,b]);
            Ensure(scan.Added.Count==1 && scan.DuplicateCount==1 && w.Photos.Count==4,
                "Hardlink-alias identity suppression logic failed");
        });
        Check("P4C_CLI_LITERAL_PATHS_NO_SHELL_SPLITTING",()=>
        {
            var parsed=CliImportPaths.Parse(["--recursive","日本語 photo 01.png","directory with spaces"]);
            Ensure(parsed.Recursive && parsed.Paths.Length==2 && parsed.Paths[0]=="日本語 photo 01.png",
                "Argv re-tokenized Unicode/space-containing paths");
            var end=CliImportPaths.Parse(["--","--literal-file.png"]);
            Ensure(!end.Recursive && end.Paths.Single()=="--literal-file.png","CLI -- escaped path incorrectly");
            Refuse<ArgumentException>(()=>CliImportPaths.Parse(["--bad-option","photo.png"]));
            Refuse<ArgumentException>(()=>CliImportPaths.Parse(["--recursive"]));
        });
        Check("P4C_IMPORT_DOES_NOT_WRITE_SOURCE_OR_OUTPUT",()=>
        {
            var r=Root();var w=Create(r);var bytes=File.ReadAllBytes(w.Photos[0].Path);
            var extra=Path.Combine(r,"fresh");Directory.CreateDirectory(extra);
            var pic=Path.Combine(extra,"image.png");File.WriteAllBytes(pic,bytes);
            var before=SortingWorkspace.ComputeSha256Async(pic).GetAwaiter().GetResult();
            w.ImportPaths([extra]);
            Ensure(SortingWorkspace.ComputeSha256Async(pic).GetAwaiter().GetResult()==before &&
                !Directory.EnumerateFiles(Path.Combine(r,"output"),"*",SearchOption.AllDirectories).Any(),
                "Metadata-only import unexpectedly wrote image/output data");
        });
        Check("P4C_LINKED_DIRECTORY_RECURSION_GUARD_WHEN_AVAILABLE",()=>
        {
            var r=Root();var w=Create(r);var real=Path.Combine(r,"real");Directory.CreateDirectory(real);
            var link=Path.Combine(real,"linked");
            bool created=false;
            try { Directory.CreateSymbolicLink(link,Path.Combine(r,"source"));created=true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.Security.SecurityException or NotSupportedException)
            { Console.WriteLine("P4C_SYMLINK_FIXTURE=NOT_RUN_NO_SYMLINK_PERMISSION"); }
            if (created)
            {
                var report=w.ImportPaths([real],recursive:true);
                Ensure(report.UnsafeCount>=1 && report.Added.Count==0,
                    "Linked directory recursion followed alias");
                Console.WriteLine("P4C_SYMLINK_FIXTURE=AUTOMATED_PASS");
            }
        });
        // P4D typed routes: tests use marked disposable sources/targets only.
        Check("P4D_NESTED_RELATIVE_EXACT_REVIEW_NO_MUTATION",() =>
        {
            var r=Root();var w=Create(r);
            w.SetRelativeRoute('H',"Family/Holidays 2026");w.Assign('H');
            var original=w.Photos[0].Path;var before=File.ReadAllBytes(original);
            var expected=Path.Combine(r,"output","Family","Holidays 2026",Path.GetFileName(original));
            var result=w.ReviewPlan();
            Ensure(result.Count==1 && result[0].Destination==expected,"Nested relative destination mismatch");
            Ensure(!Directory.Exists(Path.GetDirectoryName(expected)!),"Review created directory without approval");
            Ensure(File.ReadAllBytes(original).SequenceEqual(before),"Review touched original source bytes");
        });
        Check("P4D_RELATIVE_VALIDATION_AND_RECOVERY_GUARD",() =>
        {
            var w=Create(Root());long rev=w.Revision;
            foreach(var value in new[]{"../escape","Family/../escape","/rooted","\\rooted", "C:\\Windows", "CON", "bad//empty","A/..","A/COM1", "A/", "A/.photosift-reborn-recovery"})
                try { w.SetRelativeRoute('H',value); if(value.Contains(".photosift-reborn-recovery")) {w.Assign('H');Refuse<IOException>(()=>w.ReviewPlan());w.UndoDecision();} else throw new Exception("Unsafe route accepted: "+value); }
                catch(ArgumentException) { /* expected input validation */ }
            Ensure(w.Revision>=rev,"Invalid input unexpectedly rewrote workspace");
        });
        Check("P4D_ABSOLUTE_ROUTE_REVIEW_SOURCE_REFUSAL",() =>
        {
            var r=Root();var w=Create(r);var destination=Path.Combine(r,"custom outside output");
            Directory.CreateDirectory(destination);
            w.SetAbsoluteRoute('Q',destination);w.Assign('Q');
            var plan=w.ReviewPlan();
            Ensure(plan.Single().Destination==Path.Combine(destination,Path.GetFileName(w.Photos[0].Path)),
                "Absolute folder not honored");
            Ensure(!File.Exists(plan[0].Destination),"Review wrote the absolute target");
            Refuse<IOException>(()=>w.SetAbsoluteRoute('Q',Path.Combine(r,"source")));
            Ensure(w.Routes.Single(route=>route.Key=='Q').Location==destination,"Rejected source route changed the saved route");
        });
        Check("P4D_TYPED_ROUTES_RESTART_AND_OLD_SCHEMA_COMPAT",() =>
        {
            var r=Root();var w=Create(r);w.SetRelativeRoute('Q',"Family/School");
            var outside=Path.Combine(r,"external");Directory.CreateDirectory(outside);
            w.SetAbsoluteRoute('X',outside);w.Assign('Q');
            var snap=w.CaptureSession();
            var json=System.Text.Json.JsonSerializer.Serialize(snap);
            var resumed=new SortingWorkspace();
            resumed.RestoreSession(System.Text.Json.JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(json)!);
            Ensure(resumed.AssignmentCount==1 && resumed.Routes.Single(x=>x.Key=='X').Kind==RouteKind.Absolute &&
                resumed.Routes.Single(x=>x.Key=='Q').Location!.Contains("School"),"Typed routes lost on restart");
            var old=w.CaptureSession() with { Schema=2,
                Routes=[new Route('F',"Family"),new Route('T',"Travel"),new Route('W',"Work")] ,
                Assignments=[], Decisions=[] };
            var legacy=new SortingWorkspace();legacy.RestoreSession(old);
            Ensure(legacy.Routes.Single(x=>x.Key=='F').Kind==RouteKind.Relative &&
                legacy.ExpandRouteDirectory('F')==Path.Combine(r,"output","Family"),"Legacy route changed destination");
        });
        Check("P4D_ROUTE_MUTATION_REVOKES_APPROVAL",() =>
        {
            var w=Create(Root());w.Assign('F');w.ReviewPlan();
            var id=w.CurrentPlanId!.Value;w.ApprovePlan(id,w.Revision);
            w.SetRelativeRoute('F',"Family/Holidays");
            Ensure(!w.IsCurrentPlanApproved,"Route update retained stale approval");
            Refuse<IOException>(()=>w.ApprovePlan(id,w.Revision));
        });
        Check("P4D_NESTED_COLLISION_NUMBER_SKIP_AND_REFUSE",() =>
        {
            var r=Root();var w=Create(r);w.SetRelativeRoute('H',"Family/Holidays");w.Assign('H');
            var folder=Path.Combine(r,"output","Family","Holidays");Directory.CreateDirectory(folder);
            var existing=Path.Combine(folder,Path.GetFileName(w.Photos[0].Path));
            File.WriteAllText(existing,"DO_NOT_REPLACE");
            Refuse<IOException>(()=>w.ReviewPlan());
            w.SetCollisionChoice(CollisionChoice.Number);var numbered=w.ReviewPlan().Single();
            Ensure(numbered.Destination==Path.Combine(folder,Path.GetFileNameWithoutExtension(existing)+" (2)"+Path.GetExtension(existing)),"Numbered destination not frozen");
            w.SetCollisionChoice(CollisionChoice.Skip);Refuse<IOException>(()=>w.ReviewPlan());
            Ensure(File.ReadAllText(existing)=="DO_NOT_REPLACE","Existing target damaged during review");
        });
        Check("P4D_MISSING_ABSOLUTE_DESTINATION_REFUSES",() =>
        {
            var r=Root();var w=Create(r);var dir=Path.Combine(r,"missing-later");
            Directory.CreateDirectory(dir);w.SetAbsoluteRoute('Q',dir);w.Assign('Q');
            Directory.Delete(dir);
            Refuse<IOException>(()=>w.ReviewPlan());
            Ensure(w.AssignmentCount==1,"Refusal lost staged decision");
        });
        Check("P4D_NESTED_JOURNAL_COPY_AND_ARCHIVE_UNDO",() =>
        {
            var r=Root();var w=Create(r);w.SetRelativeRoute('H',"Family/Holidays");w.Assign('H');
            var plan=w.ReviewPlan();var originalHash=SortingWorkspace.ComputeSha256Async(plan[0].Source).GetAwaiter().GetResult();
            var prepared=DurableCopyCoordinator.PrepareAsync(w,plan,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var store=new WorkspaceSessionStore(Path.Combine(r,"state"));var coordinator=new DurableCopyCoordinator(store);
            var copied=coordinator.ApplyAsync(prepared).GetAwaiter().GetResult();
            Ensure(copied.Completed==1 && copied.Detail is null && File.Exists(plan[0].Destination),"Nested copy publication failed");
            var undone=coordinator.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            var journal=store.CopyItems(w.SessionId).Single();
            Ensure(undone.Completed==1 && journal.State=="Undone" &&
                journal.Recovery.Contains(".photosift-reborn-recovery") && File.Exists(journal.Recovery) &&
                !File.Exists(plan[0].Destination) && File.Exists(plan[0].Source),"Nested Undo did not preserve known files");
            Ensure(SortingWorkspace.ComputeSha256Async(journal.Recovery).GetAwaiter().GetResult()==originalHash,"Archive bytes differ");
        });
        Check("P4D_ABSOLUTE_JOURNAL_APPLY_AND_UNDO",() =>
        {
            var r=Root();var w=Create(r);var absolute=Path.Combine(r,"absolute album");Directory.CreateDirectory(absolute);
            w.SetAbsoluteRoute('Q',absolute);w.Assign('Q');var items=w.ReviewPlan();
            var p=DurableCopyCoordinator.PrepareAsync(w,items,w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));var coord=new DurableCopyCoordinator(db);
            Ensure(coord.ApplyAsync(p).GetAwaiter().GetResult().Completed==1,"Absolute Apply failed");
            Ensure(File.Exists(items[0].Destination),"Absolute target not created");
            Ensure(coord.UndoAsync(w.SessionId).GetAwaiter().GetResult().Completed==1,"Absolute Undo failed");
            Ensure(!File.Exists(items[0].Destination) && db.CopyItems(w.SessionId).Single().State=="Undone",
                "Absolute output not safely archived");
        });
        // Migration tests begin with a new current-schema disposable database,
        // then simulate an older user_version. Their fixture SQL must also
        // remove newer tables: a v11 move_epoch_ledger cannot be present in a
        // faithful legacy v2-v9 fixture. Never change production migration to
        // CREATE IF NOT EXISTS to hide that inconsistent database shape.
        Check("P4D_SQLITE_V2_IN_PLACE_UPGRADE_AND_BACKUP",() =>
        {
            var root=Root();var folder=Path.Combine(root,"state");var w=Create(root);
            using(var db=new WorkspaceSessionStore(folder)) db.Save(w.CaptureSession() with {Schema=2});
            string file=Path.Combine(folder,"workspaces-v1.sqlite");
            using(var sqlite=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+file))
            {
                sqlite.Open();using var cmd=sqlite.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE m6j_review_proposals; PRAGMA user_version=2;";cmd.ExecuteNonQuery();
            }
            using(var upgraded=new WorkspaceSessionStore(folder))
                Ensure(upgraded.Load()?.SessionId==w.SessionId,"Old session disappeared in typed-route schema upgrade");
            Ensure(Directory.GetFiles(folder,"*.before-v3-*.sqlite").Length==1,
                "SQLite v2 was upgraded without a consistent backup");
        });
        // P4E: no file mutations. Removed-photo history is part of the SQLite
        // workspace checkpoint; legacy schemas remain readable.
        Check("P4E_JUMP_BOUNDS_AND_VIEW_ONLY_APPROVAL",() =>
        {
            var w=Create(Root());var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            long before=w.Revision;
            w.JumpToPhoto(3);
            Ensure(w.Current?.Id==ids[2] && w.Revision==before && w.IsCurrentPlanApproved,
                "Cursor jump unexpectedly changed approved plan or photo identity");
            Refuse<ArgumentOutOfRangeException>(()=>w.JumpToPhoto(0));
            Refuse<ArgumentOutOfRangeException>(()=>w.JumpToPhoto(4));
            Ensure(w.Current?.Id==ids[2],"Invalid jump changed current photo");
        });
        Check("P4E_NEXT_UNASSIGNED_WRAP_AND_ALL_ASSIGNED",() =>
        {
            var w=Create(Root());var first=w.Photos[0].Id;
            w.Assign('F');w.JumpToPhoto(3);
            Ensure(w.JumpToNextUnassigned() && w.Current?.Id==w.Photos[1].Id,
                "Next unassigned did not wrap over the assigned first photo");
            w.Assign('T');w.Assign('W');
            Ensure(!w.JumpToNextUnassigned() && w.AssignmentCount==3,"Already assigned photo offered as unassigned");
            Ensure(first==w.Photos[0].Id,"Navigation changed photo ID");
        });
        Check("P4E_SHUFFLE_STABLE_IDS_ASSIGNMENTS_APPROVAL",() =>
        {
            var w=Create(Root());var original=w.Photos.Select(x=>x.Id).ToHashSet();
            var assigned=w.Current!.Path;w.Assign('F');
            w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            w.JumpToPhoto(2);var selected=w.Current!.Id;
            w.ShufflePhotos(48);
            Ensure(w.Photos.Select(x=>x.Id).ToHashSet().SetEquals(original) &&
                w.Current?.Id==selected && w.CategoryFor(assigned)=='F',
                "Shuffle lost identities, current selection, or saved assignment");
            Ensure(!w.IsCurrentPlanApproved,"Shuffle retained stale plan authorization");
        });
        Check("P4E_REMOVE_NO_DISK_WRITES_UNDO_STABLE_ID",() =>
        {
            var r=Root();var w=Create(r);var item=w.Current!;
            var hash=SortingWorkspace.ComputeSha256Async(item.Path).GetAwaiter().GetResult();
            w.RemoveCurrentFromSession();
            Ensure(w.RemovedCount==1 && w.Photos.Count==2 && File.Exists(item.Path) &&
                SortingWorkspace.ComputeSha256Async(item.Path).GetAwaiter().GetResult()==hash,
                "Remove-from-session modified original or collection incorrectly");
            w.UndoLastSessionRemoval();
            Ensure(w.Photos.Count==3 && w.Current?.Id==item.Id && w.RemovedCount==0 &&
                !Directory.EnumerateFiles(w.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),
                "Undo remove lost identity or wrote output files");
        });
        Check("P4E_REMOVE_REFUSES_ASSIGNED_OR_HISTORY",() =>
        {
            var w=Create(Root());var first=w.Current!;w.Assign('F');w.JumpToPhoto(1);
            Refuse<InvalidOperationException>(()=>w.RemoveCurrentFromSession());
            w.UndoDecision();w.RemoveCurrentFromSession();
            Ensure(!w.Photos.Any(p=>p.Id==first.Id) && w.RemovedCount==1,"Undo then remove failed");
            Refuse<InvalidOperationException>(()=>w.UndoDecision());
        });
        Check("P4E_REMOVAL_STACK_RESTART_SQLITE_REVERSE_ORDER",() =>
        {
            var r=Root();var w=Create(r);var original=w.Photos.Select(p=>p.Id).ToArray();
            w.JumpToPhoto(2);w.RemoveCurrentFromSession();
            w.JumpToPhoto(2);w.RemoveCurrentFromSession();
            Ensure(w.Photos.Count==1 && w.RemovedCount==2,"Multiple removals not stacked");
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state"))) db.Save(w.CaptureSession());
            WorkspaceSessionSnapshot checkpoint;
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state"))) checkpoint=db.Load()!;
            var recovered=new SortingWorkspace();recovered.RestoreSession(checkpoint);
            Ensure(recovered.RemovedCount==2 && recovered.Photos.Count==1 && !recovered.IsCurrentPlanApproved,
                "SQLite checkpoint did not restore removal stack");
            recovered.UndoLastSessionRemoval();recovered.UndoLastSessionRemoval();
            Ensure(recovered.Photos.Select(p=>p.Id).SequenceEqual(original),"LIFO restoration changed original order");
            Refuse<InvalidOperationException>(()=>recovered.UndoLastSessionRemoval());
        });
        Check("P4E_REMOVED_DUPLICATE_SUPPRESSED_AND_REPLACE_RESETS",() =>
        {
            var r=Root();var w=Create(r);var file=w.Current!.Path;
            w.RemoveCurrentFromSession();
            var scan=w.ImportPaths([file]);
            Ensure(scan.Added.Count==0 && scan.DuplicateCount==1 && w.RemovedCount==1,
                "Re-import duplicated a hidden session photo");
            w.OpenSource(Path.Combine(r,"source"));
            Ensure(w.RemovedCount==0 && w.Photos.Count==3,"Explicit replacement did not clear removal history");
        });
        Check("P4E_TAMPERED_REMOVAL_REJECTED_AT_RESTORE",() =>
        {
            var w=Create(Root());w.RemoveCurrentFromSession();var valid=w.CaptureSession();
            var removed=valid.RemovedPhotos![0];
            var bad=valid with {RemovedPhotos=[removed with {Photo=valid.Photos[0]}]};
            var restored=new SortingWorkspace();
            Refuse<InvalidDataException>(()=>restored.RestoreSession(bad));
            Ensure(restored.Photos.Count==0 && restored.RemovedCount==0,"Invalid checkpoint partially restored");
            var legacy=valid with { Schema=3, RemovedPhotos=null, Photos=valid.Photos };
            var old=new SortingWorkspace();old.RestoreSession(legacy);
            Ensure(old.Photos.Count==2 && old.RemovedCount==0,"Old P4D session with no removal history is incompatible");
        });
        Check("P4F_ORIGINAL_SHAPED_PREVIEW_IS_READ_ONLY",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var before=w.CaptureSession();
            var path=Path.Combine(r,"Settings.xml");
            File.WriteAllText(path,"<AppSettings><KeyFolder_P>Family/Holidays</KeyFolder_P>"+
                "<KeyFolder_X>C:\\OldPhotos</KeyFolder_X><FileMode>Move</FileMode>"+
                "<DeleteMode>RecycleBin</DeleteMode><ExistingFiles>AppendNumber</ExistingFiles>"+
                "<TargetFolder>C:\\Pictures</TargetFolder><CustomMenuColors>true</CustomMenuColors></AppSettings>");
            var hash=SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult();
            var p=LegacySettingsImporter.Prepare(path,w);
            Ensure(p.RoutesChanged==1 && p.CollisionChanged &&
                p.Report.Contains("absolute/foreign path") && p.Report.Contains("remains Copy-only") &&
                p.Candidate.Routes.Any(x=>x.Key=='P' && x.Kind==RouteKind.Relative) &&
                p.Candidate.CollisionMode==CollisionChoice.Number,
                "Legacy preview failed safe mapping or warning");
            Ensure(w.CaptureSession().Equals(before) ||
                (w.Revision==before.Revision && w.CollisionMode==before.CollisionMode &&
                !w.Routes.Any(x=>x.Key=='P') && w.AssignmentCount==1),
                "Read-only preview mutated existing session");
            Ensure(hash==SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult(),
                "Legacy XML was changed during preview");
        });
        Check("P4F_APPROVED_SNAPSHOT_PRESERVES_DECISIONS_REVOKES_APPROVAL",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var session=w.SessionId;var photoIds=w.Photos.Select(p=>p.Id).ToArray();
            w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var path=Path.Combine(r,"Settings.xml");
            File.WriteAllText(path,"<AppSettings><KeyFolder_P>Holidays/2026</KeyFolder_P>"+
                "<ExistingFiles>AppendNumber</ExistingFiles></AppSettings>");
            var proposal=LegacySettingsImporter.Prepare(path,w);
            Ensure(w.IsCurrentPlanApproved,"Preview revoked live approval before consent");
            w.RestoreSession(proposal.Candidate);
            Ensure(w.SessionId==session && w.Photos.Select(p=>p.Id).SequenceEqual(photoIds) &&
                w.AssignmentCount==1 && !w.IsCurrentPlanApproved &&
                w.Routes.Any(p=>p.Key=='P' && p.Kind==RouteKind.Relative),
                "Explicitly imported candidate lost identity/assignment or restored approval");
            Ensure(!Directory.EnumerateFiles(w.DestinationRoot!,"*",SearchOption.AllDirectories).Any(),
                "Preview/application made file outputs");
        });
        Check("P4F_REJECTS_DTD_DUPLICATE_AND_WRONG_ROOT",() =>
        {
            var r=Root();var w=Create(r);var path=Path.Combine(r,"Settings.xml");
            File.WriteAllText(path,"<!DOCTYPE AppSettings [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><AppSettings><KeyFolder_P>&x;</KeyFolder_P></AppSettings>");
            Refuse<System.Xml.XmlException>(()=>LegacySettingsImporter.Prepare(path,w));
            File.WriteAllText(path,"<AppSettings><KeyFolder_P>One</KeyFolder_P><KeyFolder_P>Two</KeyFolder_P></AppSettings>");
            Refuse<InvalidDataException>(()=>LegacySettingsImporter.Prepare(path,w));
            File.WriteAllText(path,"<DifferentSettings><KeyFolder_P>One</KeyFolder_P></DifferentSettings>");
            Refuse<InvalidDataException>(()=>LegacySettingsImporter.Prepare(path,w));
            Ensure(w.Routes.All(x=>x.Key!='P'),"Invalid import modified live routes");
        });
        Check("P4F_UNSAFE_ROUTES_AND_OVERWRITE_FAIL_CLOSED",() =>
        {
            var r=Root();var w=Create(r);w.SetCollisionChoice(CollisionChoice.Number);
            var path=Path.Combine(r,"Settings.xml");
            File.WriteAllText(path,"<AppSettings><KeyFolder_P>../escape</KeyFolder_P>"+
                "<KeyFolder_X>/etc/secrets</KeyFolder_X>"+
                "<KeyFolder_Q>Family</KeyFolder_Q>"+
                "<ExistingFiles>Overwrite</ExistingFiles><FileMode>Move</FileMode></AppSettings>");
            var proposal=LegacySettingsImporter.Prepare(path,w);
            Ensure(proposal.RoutesChanged==0 && proposal.CollisionChanged &&
                proposal.Candidate.CollisionMode==CollisionChoice.Refuse &&
                proposal.Report.Contains("Unsafe/conflicting routes skipped: 3") &&
                w.CollisionMode==CollisionChoice.Number && !w.Routes.Any(x=>x.Key=='Q'),
                "Unsafe/aliased legacy routes or legacy Overwrite mapped incorrectly");
        });
        Check("P5A_XML_PICKER_REJECTS_ZIP_AND_WRONG_EXTENSION",() =>
        {
            var root=Root();var w=Create(root);
            var before=w.CaptureSession();
            var zip=Path.Combine(root,"settings_archive.zip");
            File.WriteAllBytes(zip,[0x50,0x4B,0x03,0x04,0x05]);
            var error=Refuse<InvalidDataException>(() => LegacySettingsImporter.Prepare(zip,w));
            Ensure(error.Message.Contains(".xml"),"ZIP rejection must explain the required file type");
            var disguised=Path.Combine(root,"Settings.xml");
            File.Copy(zip,disguised);
            var disguisedError=Refuse<InvalidDataException>(() => LegacySettingsImporter.Prepare(disguised,w));
            Ensure(disguisedError.Message.Contains("ZIP"),"Renamed ZIP must be rejected with helpful error");
            Ensure(w.Revision==before.Revision && w.Photos.Count==before.Photos.Length,
                "Unsupported file changed session");
            Ensure(File.ReadAllBytes(zip).SequenceEqual(File.ReadAllBytes(disguised)),"XML validation changed a selected file");
        });
        Check("P5A_MOVE_PREFLIGHT_ONE_PHOTO_NO_DISK_WRITES",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var source=w.Photos[0].Path;
            var original=SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var readiness=MoveReadinessProbe.InspectAsync(w,db).GetAwaiter().GetResult();
            Ensure(readiness.Source==source && readiness.SortedDestination.Contains("Family") &&
                readiness.ProposedSourceArchive.Contains(".photosift-reborn-recovery") &&
                readiness.SourceHash==original && readiness.SourceNativeIdentity==NativeFileCapability.Identity(source),
                "Read-only Move preflight did not freeze exact identity/destination/archive");
            Ensure(readiness.Report.Contains("NOT EXECUTION AUTHORIZED") == false &&
                readiness.Report.Contains("NO EXECUTION AUTHORIZED"),"Preflight must disclose Move disabled");
            Ensure(!Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")),
                "Preflight created a recovery directory");
            Ensure(!Directory.EnumerateFileSystemEntries(Path.Combine(r,"output")).Any(),
                "Preflight wrote to output");
            Ensure(SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult()==original &&
                !w.IsCurrentPlanApproved,"Preflight changed bytes or approved a plan");
        });
        Check("P5A_MOVE_PREFLIGHT_REFUSES_MULTI_AND_UNOWNED_RECOVERY",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');w.Assign('T');
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            Refuse<IOException>(() => MoveReadinessProbe.InspectAsync(w,db).GetAwaiter().GetResult());
            w.UndoDecision();
            Directory.CreateDirectory(Path.Combine(r,"source",".photosift-reborn-recovery"));
            Refuse<IOException>(() => MoveReadinessProbe.InspectAsync(w,db).GetAwaiter().GetResult());
            Ensure(Directory.EnumerateFileSystemEntries(Path.Combine(r,"output")).Count()==0,
                "Refused Move wrote output");
        });
        // P5B: file-changing integration tests are restricted to freshly generated
        // PHOTOSIFT_REBORN_P1_DISPOSABLE_ roots and are never exposed by the GUI.
        Check("P5B_DATABASE_V4_PRESERVES_OLD_SESSION",() =>
        {
            var r=Root();var w=Create(r);
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state"))) db.Save(w.CaptureSession());
            var path=Path.Combine(r,"state","workspaces-v1.sqlite");
            using (var con=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            { con.Open();using var cmd=con.CreateCommand();cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE m6j_review_proposals; PRAGMA user_version=3";cmd.ExecuteNonQuery(); }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                Ensure(db.Load()?.SessionId==w.SessionId,"Old saved workspace lost in v4 migration");
                Ensure(db.MoveInstallationId()!=Guid.Empty,"Missing persistent installation identity");
                Ensure(db.MoveItems(w.SessionId).Count==0,"Unexpected Move history in old workspace");
            }
            Ensure(Directory.GetFiles(Path.Combine(r,"state"),"*.before-v4-*.sqlite").Length>=1,
                "Version 3 workspace upgrade did not write a consistent backup");
        });
        Check("P5B_NATIVE_DEVICE_ID_ACCEPTS_REGULAR_FILES",() =>
        {
            var r=Root(); var w=Create(r); var source=w.Photos[0].Path;
            var sourceFolder=Path.GetDirectoryName(source)!;
            Ensure(NativeFileCapability.DeviceId(source)==NativeFileCapability.DeviceId(sourceFolder),
                "A regular file and its parent must be on the same native volume");
            var blockingFile=Path.Combine(r,"not_a_directory");
            File.WriteAllText(blockingFile,"BLOCKED_PARENT");
            Refuse<IOException>(() => NativeFileCapability.DeviceId(Path.Combine(blockingFile,"child.png")));
        });
        Check("P5B_NATIVE_MOVE_UNDO_RESTART_EXACT_IDS",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var src=plan.Single().Source;var sourceId=NativeFileCapability.Identity(src);
            var originalHash=SortingWorkspace.ComputeSha256Async(src).GetAwaiter().GetResult();
            Guid session=w.SessionId; Guid rowId; string target; string archive;
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                db.Save(w.CaptureSession());
                var mover=new DurableMoveCoordinator(db);
                var prepared=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                var moved=mover.ExecuteApprovedAsync(w,prepared).GetAwaiter().GetResult();
                rowId=moved.Id;target=moved.Destination;archive=moved.OriginalArchive;
                Ensure(moved.State=="Committed" && !File.Exists(src) && File.Exists(archive) && File.Exists(target),
                    "Verified target + retained original not committed");
                Ensure(NativeFileCapability.Identity(archive)==sourceId &&
                    SortingWorkspace.ComputeSha256Async(target).GetAwaiter().GetResult()==originalHash,
                    "Move altered original identity or image contents");
            }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                Ensure(db.MoveItems(session).Single().Id==rowId,"Restart lost Move identity");
                var mover=new DurableMoveCoordinator(db);
                Ensure(mover.Inspect(session).Single().Evidence.Contains("TARGET=OWNED"),"No owned output observed after restart");
                var undone=mover.UndoAsync(session).GetAwaiter().GetResult();
                Ensure(undone.State=="Undone" && !File.Exists(target) && File.Exists(src),"Undo did not restore exact original");
                Ensure(NativeFileCapability.Identity(src)==sourceId && File.Exists(undone.OutputArchive) &&
                    SortingWorkspace.ComputeSha256Async(src).GetAwaiter().GetResult()==originalHash,
                    "Undo lost original file identity or retained output archive");
            }
        });
        Check("P5B_UNAPPROVED_AND_OCCUPIED_TARGET_REFUSED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            Refuse<IOException>(()=>mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult());
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var prep=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(prep.Destination)!);
            File.WriteAllText(prep.Destination,"DO_NOT_REPLACE");
            Refuse<IOException>(()=>mover.ExecuteApprovedAsync(w,prep).GetAwaiter().GetResult());
            Ensure(File.ReadAllText(prep.Destination)=="DO_NOT_REPLACE" && File.Exists(prep.Source) &&
                db.MoveItems(w.SessionId).Count==0,"Occupied destination was changed or journal improperly started");
        });
        Check("P5B_SAME_BYTES_REPLACED_ORIGINAL_REFUSED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var bytes=File.ReadAllBytes(p.Source);File.Delete(p.Source);File.WriteAllBytes(p.Source,bytes);
            Refuse<IOException>(()=>mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult());
            Ensure(File.Exists(p.Source) && db.MoveItems(w.SessionId).Count==0,
                "Replaced same-byte source was accepted");
        });
        Check("P5B_UNOWNED_RECOVERY_ROOT_REFUSED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.Combine(r,"source",".photosift-reborn-recovery"));
            Refuse<IOException>(()=>mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult());
            Ensure(File.Exists(p.Source) && !File.Exists(p.Destination),
                "Unowned recovery directory triggered a file change");
        });
        Check("P5B_INTERRUPTED_PUBLICATION_OBSERVE_NO_REPLAY",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);Guid session=w.SessionId;
            string original=plan.Single().Source;
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db, cut=>
                {if(cut=="MOVE_OUTPUT_PUBLISHED_UNJOURNALLED") throw new SimulatedInterruptionException(cut);});
                var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                Refuse<SimulatedInterruptionException>(()=>mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult());
                Ensure(File.Exists(original) && File.Exists(p.Destination),"Interrupted publish lost original or copy");
            }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db);var obs=mover.Inspect(session).Single();
                Ensure(obs.State=="TempReady" && obs.Evidence.Contains("SOURCE=OWNED") &&
                    obs.Evidence.Contains("TARGET=OWNED"),"Interrupted publish was not faithfully observed");
                Ensure(File.Exists(original),"Rescan performed an unauthorized mutation");
                Refuse<IOException>(()=>mover.UndoAsync(session).GetAwaiter().GetResult());
            }
        });
        Check("P5B_INTERRUPTED_SOURCE_ARCHIVE_PRESERVED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);Guid session=w.SessionId;
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db, cut=>
                {if(cut=="MOVE_SOURCE_RELOCATED_UNJOURNALLED") throw new SimulatedInterruptionException(cut);});
                var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                Refuse<SimulatedInterruptionException>(()=>mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult());
            }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db);var obs=mover.Inspect(session).Single();
                Ensure(obs.State=="ArchiveIntent" && obs.Evidence.Contains("ORIGINAL_ARCHIVE=OWNED") &&
                    obs.Evidence.Contains("TARGET=OWNED") && obs.Evidence.Contains("SOURCE=ABSENT"),
                    "Interrupted original archive not fully observable");
                Refuse<IOException>(()=>mover.UndoAsync(session).GetAwaiter().GetResult());
            }
        });
        Check("P5B_INTERRUPTED_UNDO_OBSERVABLE",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);Guid session=w.SessionId;
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db, cut=>
                {if(cut=="MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED") throw new SimulatedInterruptionException(cut);});
                var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
                Refuse<SimulatedInterruptionException>(()=>mover.UndoAsync(session).GetAwaiter().GetResult());
            }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db);var obs=mover.Inspect(session).Single();
                Ensure(obs.State=="UndoRestoreIntent" && obs.Evidence.Contains("SOURCE=OWNED") &&
                    obs.Evidence.Contains("TARGET=OWNED") && obs.Evidence.Contains("ORIGINAL_ARCHIVE=ABSENT"),
                    "Interrupted Undo would have no auditable recovery state");
                Refuse<IOException>(()=>mover.UndoAsync(session).GetAwaiter().GetResult());
            }
        });
        Check("P5B_TAMPERED_TARGET_UNDO_REFUSES",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var item=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            File.AppendAllText(item.Destination,"EXTERNAL_TAMPER_DETECTED");
            Refuse<IOException>(()=>mover.UndoAsync(w.SessionId).GetAwaiter().GetResult());
            Ensure(File.Exists(item.OriginalArchive) && !File.Exists(item.Source) &&
                File.Exists(item.Destination) && db.MoveItems(w.SessionId).Single().State=="NeedsAttention",
                "Tampered installed photo incorrectly allowed an Undo or erased evidence");
        });
        Check("P5B_ALL_MOVE_BOUNDARY_CUTS_PRESERVE_ORIGINAL",() =>
        {
            string[] cuts = ["MOVE_INTENT_RECORDED", "MOVE_DIRECTORIES_READY",
                "MOVE_TEMP_FLUSHED_UNRECORDED", "MOVE_TEMP_RECORDED",
                "MOVE_OUTPUT_PUBLISHED_UNJOURNALLED", "MOVE_OUTPUT_PUBLISHED_RECORDED",
                "MOVE_SOURCE_ARCHIVE_INTENT", "MOVE_SOURCE_RELOCATED_UNJOURNALLED",
                "MOVE_SOURCE_ARCHIVED_RECORDED", "MOVE_COMMITTED"];
            foreach (var cut in cuts)
            {
                var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
                w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
                using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
                var mover=new DurableMoveCoordinator(db, name =>
                { if (name==cut) throw new SimulatedInterruptionException(name); });
                var prepared=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                Refuse<SimulatedInterruptionException>(()=>mover.ExecuteApprovedAsync(w,prepared).GetAwaiter().GetResult());
                var row=db.MoveItems(w.SessionId).Single();
                var sourceOriginal=File.Exists(prepared.Source) &&
                    NativeFileCapability.Identity(prepared.Source)==prepared.Identity;
                var archivedOriginal=File.Exists(row.OriginalArchive) &&
                    NativeFileCapability.Identity(row.OriginalArchive)==prepared.Identity;
                Ensure(sourceOriginal!=archivedOriginal,"Cut "+cut+": original missing or duplicated identity across locations");
                Ensure(mover.Inspect(w.SessionId).Count==1,"Cut "+cut+": journal no longer inspectable");
                Ensure(File.Exists(prepared.Source) || File.Exists(row.OriginalArchive),
                    "Cut "+cut+": original vanished");
            }
        });
        Check("P5B_ALL_UNDO_BOUNDARY_CUTS_RETAIN_SOURCE_AND_TARGET",() =>
        {
            string[] cuts = ["MOVE_UNDO_RESTORE_INTENT", "MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED",
                "MOVE_UNDO_SOURCE_RESTORED_RECORDED", "MOVE_UNDO_OUTPUT_ARCHIVE_INTENT",
                "MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED", "MOVE_UNDO_COMMITTED"];
            foreach (var cut in cuts)
            {
                var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
                w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
                using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
                var mover=new DurableMoveCoordinator(db, name =>
                { if (name==cut) throw new SimulatedInterruptionException(name); });
                var prepared=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                var moved=mover.ExecuteApprovedAsync(w,prepared).GetAwaiter().GetResult();
                Refuse<SimulatedInterruptionException>(()=>mover.UndoAsync(w.SessionId).GetAwaiter().GetResult());
                var observed=mover.Inspect(w.SessionId).Single();
                Ensure(File.Exists(moved.Source) || File.Exists(moved.OriginalArchive),
                    "Undo cut "+cut+": no preserved original exists");
                Ensure(File.Exists(moved.Destination) || File.Exists(moved.OutputArchive),
                    "Undo cut "+cut+": no preserved installed output exists");
                Ensure(!string.IsNullOrEmpty(observed.State),"Undo cut "+cut+": no journal authority");
            }
        });
        Check("P5B_STALE_APPROVAL_REFUSES_EXECUTION",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            w.Assign('T'); // invalidate the exact approved plan
            Refuse<IOException>(()=>mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult());
            Ensure(File.Exists(p.Source) && !File.Exists(p.Destination) &&
                db.MoveItems(w.SessionId).Count==0,"Stale approval triggered a Move");
        });
        Check("P5B_OCCUPIED_RESTORE_PATH_REFUSED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            File.WriteAllText(moved.Source,"NEW_OCCUPANT_DO_NOT_REPLACE");
            Refuse<IOException>(()=>mover.UndoAsync(w.SessionId).GetAwaiter().GetResult());
            Ensure(File.ReadAllText(moved.Source)=="NEW_OCCUPANT_DO_NOT_REPLACE" &&
                File.Exists(moved.OriginalArchive) && File.Exists(moved.Destination),
                "Undo clobbered occupied original or lost archive/target");
        });
        Check("P5C_MODE_DEFAULT_COPY_AND_EXPLICIT_APPROVAL",() =>
        {
            var mode=new P5COperationModeGate();
            Ensure(mode.Selected==ReviewOperationMode.Copy && mode.Approved is null,
                "Unexpected default mode/approval");
            Ensure(!mode.CanApply(ReviewOperationMode.Copy,true,false,true),
                "Default Copy silently inherited approval");
            mode.RecordApproval();
            Ensure(mode.CanApply(ReviewOperationMode.Copy,true,false,false),
                "Approved Copy unexpectedly requires fixture gate");
            Ensure(!mode.CanApply(ReviewOperationMode.Move,true,false,true),
                "Copy approval promoted to Move");
        });
        Check("P5C_MODE_SWITCH_REVOKES_BOTH_DIRECTIONS",() =>
        {
            var mode=new P5COperationModeGate();
            mode.RecordApproval();
            mode.Select(ReviewOperationMode.Move);
            Ensure(mode.Approved is null && !mode.CanApply(ReviewOperationMode.Move,true,false,true),
                "Copy-to-Move retained approval");
            mode.RecordApproval();
            Ensure(mode.CanApply(ReviewOperationMode.Move,true,false,true),"Test Move approval absent");
            Ensure(!mode.CanApply(ReviewOperationMode.Move,true,false,false),
                "Move approved outside fixture");
            mode.Select(ReviewOperationMode.Copy);
            Ensure(!mode.CanApply(ReviewOperationMode.Copy,true,false,true),
                "Move-to-Copy retained approval");
            mode.RecordApproval(); mode.Revoke();
            Ensure(mode.Approved is null,"Mode revoke did not expire approval");
        });
        Check("P5C_MOVE_HISTORY_BLOCKS_BOTH_OPERATION_MODES",() =>
        {
            var mode=new P5COperationModeGate(); mode.RecordApproval();
            Ensure(!mode.CanApply(ReviewOperationMode.Copy,true,true,true),
                "Move journal history permitted Copy");
            mode.Select(ReviewOperationMode.Move);mode.RecordApproval();
            Ensure(!mode.CanApply(ReviewOperationMode.Move,true,true,true),
                "Previous Move journal permitted new Move");
            Ensure(!mode.CanApply(ReviewOperationMode.Move,false,false,true),
                "Unapproved Move allowed");
        });
        Check("P5C_GUI_FIXTURE_POLICY_REQUIRES_EXACT_MARKED_ROOT",() =>
        {
            string r=Root();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var env=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,r);
                Ensure(!P5CGuiFixturePolicy.IsMarkedStore(db),"P5B marker alone authorized GUI Move");
                File.WriteAllText(Path.Combine(r,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
                Ensure(P5CGuiFixturePolicy.IsMarkedStore(db),"Exact tagged test state refused");
                P5CGuiFixturePolicy.VerifyExactWorkspace(db,Create(r));
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
                Ensure(!P5CGuiFixturePolicy.IsMarkedStore(db),"Mismatched environment fixture accepted");
            }
            finally { Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,env); }
        });
        Check("P5C_REVIEWED_OPERATION_ID_ROUTE_AND_UNDO_PERSIST",() =>
        {
            string r=Root();var w=Create(r);w.Assign('F');var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            string source=plan.Single().Source;
            string expected=SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult();
            Guid proposal=Guid.NewGuid();
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var mover=new DurableMoveCoordinator(db);
                var prepared=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                var moved=mover.ExecuteApprovedAsync(w,prepared,reviewedOperationId:proposal).GetAwaiter().GetResult();
                Ensure(moved.Id==proposal && moved.RouteKey=='F' && moved.State=="Committed", 
                    "Move plan ID / route key / state not frozen");
                Ensure(moved.OriginalArchive.Contains(proposal.ToString("N"),StringComparison.Ordinal),
                    "Reviewed original-recovery location disagrees with operation ID");
                w.RetireCommitted(new[]{moved.PhotoId});db.Save(w.CaptureSession());
                Ensure(db.AllMoveHistoryCount()==1 && db.MoveItems(Guid.NewGuid()).Count==0,
                    "Global Move history authority omitted retired session");
            }
            using (var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                var saved=db.Load()!;var resumed=new SortingWorkspace();resumed.RestoreSession(saved);
                var recorded=db.MoveItems(resumed.SessionId).Single();
                Ensure(recorded.RouteKey=='F' && recorded.Id==proposal,"Route/operation lost on restart");
                var before=db.MoveItems(resumed.SessionId).Single();
                var evidence=DurableMoveCoordinator.InspectRecorded(db,resumed.SessionId).Single();
                Ensure(evidence.State=="Committed" && db.MoveItems(resumed.SessionId).Single()==before,
                    "Read-only Move inspection modified journal");
                var undone=new DurableMoveCoordinator(db).UndoAsync(resumed.SessionId).GetAwaiter().GetResult();
                resumed.RestoreUndone(undone.PhotoId,undone.RouteKey);db.Save(resumed.CaptureSession());
                Ensure(File.Exists(source) && !File.Exists(recorded.Destination) &&
                    SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult()==expected &&
                    db.MoveItems(resumed.SessionId).Single().State=="Undone", 
                    "Move Undo failed after restart or changed original bytes");
            }
        });
        Check("P5C_PREVIEW_FAILURE_SURVIVES_REFRESH_AND_RETRIES_AFTER_UNDO", () =>
        {
            var status = new PreviewFailureState();
            var movedPhoto = Guid.NewGuid();
            var other = Guid.NewGuid();
            status.Record(movedPhoto, 7, "Original moved — recovery recorded", "Inspect recovery journal");
            Ensure(status.TryGet(movedPhoto, 7, out var title, out var text) &&
                title == "Original moved — recovery recorded" && text == "Inspect recovery journal",
                "Refresh lost the terminal preview status");
            Ensure(!status.TryGet(other, 7, out _, out _) && !status.TryGet(movedPhoto, 8, out _, out _),
                "Terminal preview status leaked into another photo or decode generation");
            status.Clear(); // Successful Undo/new decode must clear the moved-source failure.
            Ensure(!status.TryGet(movedPhoto, 7, out _, out _),
                "New preview after Undo retained the obsolete moved-source warning");
        });
        // P5D: exact, marked disposable Rename. Does NOT authorize user photographs.
        Check("P5D_FILENAME_EXTENSION_TRAVERSAL_AND_RESERVED_REFUSED",() =>
        {
            var from=Path.Combine("safe","photo_01.png");
            foreach(var invalid in new[]{"", " ","..\\stolen.png","../stolen.png","C:\\stolen.png",
                "photo_01.jpg","photo_01.png","PHOTO_01.PNG","CON.png","NUL.png",
                "folder/name.png","two:name.png","photo.png ","file?.png",".png"})
                Refuse<IOException>(()=>DurableRenameCoordinator.ValidateFilename(from,invalid));
            Ensure(DurableRenameCoordinator.ValidateFilename(from,"vacation_é.png")=="vacation_é.png",
                "Ordinary Unicode name should work");
        });
        Check("P5D_SQLITE_V5_PRESERVES_V4_CHECKPOINT_AND_BACKUP",() =>
        {
            var r=Root();var w=Create(r);
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state"))) db.Save(w.CaptureSession());
            var file=Path.Combine(r,"state","workspaces-v1.sqlite");
            using(var con=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file}"))
            {con.Open();using var c=con.CreateCommand();c.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE m6j_review_proposals; PRAGMA user_version=4";c.ExecuteNonQuery();}
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
            {
                Ensure(db.Load()!.SessionId==w.SessionId,"v4-to-v5 lost saved workspace");
                Ensure(db.AllRenameHistoryCount()==0,"v5 incorrectly invented Rename history");
                Ensure(db.MoveInstallationId()!=Guid.Empty,"v5 lost installation token");
            }
            Ensure(Directory.GetFiles(Path.Combine(r,"state"),"*.before-v5-*.sqlite").Length>0,
                "v4-to-v5 backup missing");
        });
        Check("P5D_VERIFIED_RENAME_RESTART_UNDO_LOGICAL_ID",() =>
        {
            var root=Root();var w=RenameFixture(root);
            var marker=Path.Combine(root,"P5C_GUI_TEST_ONLY.marker");
            File.WriteAllText(marker,P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                var photo=w.Current!;var initial=photo.Path;
                var hash=SortingWorkspace.ComputeSha256Async(initial).GetAwaiter().GetResult();
                var identity=NativeFileCapability.Identity(initial);
                Guid session=w.SessionId;string target;
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(w.CaptureSession());
                    var coordinator=new DurableRenameCoordinator(db);
                    var p=coordinator.Prepare(w,photo,"photo_test_renamed.png");
                    target=p.Target;
                    var row=coordinator.Apply(w,p);
                    Ensure(row.State=="Committed" && File.Exists(target) && !File.Exists(initial) &&
                        NativeFileCapability.Identity(target)==identity && DurableRenameCoordinator.ProveState(row),
                        "Native Rename did not preserve byte/file identity");
                    w.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                    db.Save(w.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    var restored=new SortingWorkspace();restored.RestoreSession(db.Load()!);
                    var row=db.RenameItems(session).Single();
                    Ensure(restored.Current!.Id==photo.Id && restored.Current.Path==target &&
                        DurableRenameCoordinator.Inspect(db,session).Single().Evidence.Contains("TARGET=OWNED"),
                        "Restart lost renamed logical photo ID or current native evidence");
                    var coordinator=new DurableRenameCoordinator(db);
                    var undone=coordinator.Undo(session);
                    restored.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(restored.CaptureSession());
                    Ensure(undone.State=="Undone" && File.Exists(initial) && !File.Exists(target) &&
                        NativeFileCapability.Identity(initial)==identity &&
                        SortingWorkspace.ComputeSha256Async(initial).GetAwaiter().GetResult()==hash &&
                        restored.Current!.Id==photo.Id && restored.Current.Path==initial,
                        "Rename Undo failed to restore same file and logical photo identity");
                }
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P5D_COLLISION_STALE_REVISION_REPLACED_SOURCE_REFUSED",() =>
        {
            var root=Root();var w=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var oldEnv=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                var renamer=new DurableRenameCoordinator(db);var p=renamer.Prepare(w,w.Current!,"new_original.png");
                File.WriteAllText(p.Target,"PROTECTED_OCCUPANT");
                Refuse<IOException>(()=>renamer.Apply(w,p));
                Ensure(File.ReadAllText(p.Target)=="PROTECTED_OCCUPANT" && File.Exists(p.Source) &&
                    db.AllRenameHistoryCount()==0,"Rename overwrote occupied target or journaled refused attempt");
                File.Delete(p.Target);
                w.SetCollisionChoice(CollisionChoice.Number);
                Refuse<IOException>(()=>renamer.Apply(w,p));
                Ensure(File.Exists(p.Source) && db.AllRenameHistoryCount()==0,"Stale workspace revision changed original");
                var replacement=renamer.Prepare(w,w.Current!,"new_original.png");
                var bytes=File.ReadAllBytes(replacement.Source);
                File.Delete(replacement.Source);File.WriteAllBytes(replacement.Source,bytes);
                Refuse<IOException>(()=>renamer.Apply(w,replacement));
                Ensure(File.Exists(replacement.Source) && db.AllRenameHistoryCount()==0,
                    "Different native file identity with equal bytes authorized Rename");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,oldEnv);}
        });
        Check("P5D_UNDO_OCCUPIED_RESTORATION_REFUSED",() =>
        {
            var root=Root();var w=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var oldEnv=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                var renamer=new DurableRenameCoordinator(db);
                var p=renamer.Prepare(w,w.Current!,"renamed_unique.png");
                var row=renamer.Apply(w,p);
                File.WriteAllText(row.Source,"UNOWNED_RETURN_OCCUPANT");
                Refuse<IOException>(()=>renamer.Undo(w.SessionId));
                Ensure(File.ReadAllText(row.Source)=="UNOWNED_RETURN_OCCUPANT" &&
                    File.Exists(row.Target) && db.RenameItems(w.SessionId).Single().State=="Committed",
                    "Undo unexpectedly replaced external occupant");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,oldEnv);}
        });
        Check("P5D_INTERRUPTION_BOUNDARIES_INSPECT_NO_AUTOREPLAY",() =>
        {
            // Explicitly classify the terminal RENAME_UNDONE checkpoint as an Undo cut.
            // Its spelling does NOT match the prefix "RENAME_UNDO_"; the earlier
            // prefix-only branch erroneously ran Apply rather than Undo for this cut.
            var applyCuts=new[]{"RENAME_INTENT_DURABLE","RENAME_RELOCATED_UNJOURNALLED",
                "RENAME_COMMITTED"};
            var undoCuts=new[]{"RENAME_UNDO_INTENT_DURABLE",
                "RENAME_UNDO_RELOCATED_UNJOURNALLED","RENAME_UNDONE"};
            foreach(var cut in applyCuts.Concat(undoCuts))
            {
                var root=Root();var w=RenameFixture(root);
                File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
                var oldEnv=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
                try
                {
                    Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                    // WorkspaceSessionStore holds workspaces-v1.lock with FileShare.None.
                    // Keep all interruption assertions inside its lifetime, then dispose
                    // the owner BEFORE opening the same workspace to test restart.
                    RenameProposal p;
                    DurableRenameItem recorded;
                    bool atSource, atTarget;
                    using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                    {
                        int cutHits=0;
                        var renamer=new DurableRenameCoordinator(db,name=>
                        {
                            if(name==cut)
                            {
                                cutHits++;
                                throw new SimulatedInterruptionException(name);
                            }
                        });
                        p=renamer.Prepare(w,w.Current!,"unique_renamed.png");
                        SimulatedInterruptionException interruption;
                        if(undoCuts.Contains(cut,StringComparer.Ordinal))
                        {
                            renamer.Apply(w,p);
                            interruption=Refuse<SimulatedInterruptionException>(()=>renamer.Undo(w.SessionId));
                        }
                        else interruption=Refuse<SimulatedInterruptionException>(()=>renamer.Apply(w,p));
                        Ensure(cutHits==1 && interruption.Message.Contains(cut,StringComparison.Ordinal),
                            "Boundary "+cut+" was not injected exactly once at the named checkpoint");
                        // Atomic same-directory Rename must retain exactly one native original.
                        atSource=File.Exists(p.Source); atTarget=File.Exists(p.Target);
                        Ensure(atSource!=atTarget,
                            "Boundary "+cut+" lost or duplicated the original photograph");
                        var livePath=atSource?p.Source:p.Target;
                        Ensure(NativeFileCapability.Identity(livePath)==p.NativeIdentity &&
                            SortingWorkspace.ComputeSha256Async(livePath).GetAwaiter().GetResult()==p.Hash,
                            "Boundary "+cut+" changed original byte/native ownership");
                        var expectedState=cut switch
                        {
                            "RENAME_INTENT_DURABLE" or "RENAME_RELOCATED_UNJOURNALLED" => "Intent",
                            "RENAME_COMMITTED" => "Committed",
                            "RENAME_UNDO_INTENT_DURABLE" or "RENAME_UNDO_RELOCATED_UNJOURNALLED" => "UndoIntent",
                            "RENAME_UNDONE" => "Undone",
                            _ => throw new Exception("Unexpected Rename cut: "+cut)
                        };
                        recorded=db.RenameItems(w.SessionId).Single();
                        Ensure(recorded.State==expectedState,
                            "Boundary "+cut+" saved "+recorded.State+" instead of "+expectedState);
                        var observed=DurableRenameCoordinator.Inspect(db,w.SessionId);
                        Ensure(observed.Count==1 && db.RenameItems(w.SessionId).Single()==recorded,
                            "Boundary "+cut+" lost history or changed journal during read-only inspection");
                    } // Exclusive workspace owner disposed: release workspaces-v1.lock.
                    // Reopening persistent state must not silently replay an interrupted Rename.
                    using(var reopened=new WorkspaceSessionStore(Path.Combine(root,"state")))
                    {
                        var reloaded=reopened.RenameItems(w.SessionId).Single();
                        Ensure(reloaded==recorded && File.Exists(p.Source)==atSource &&
                            File.Exists(p.Target)==atTarget,
                            "Boundary "+cut+" was silently replayed on workspace reopen");
                    }
                }
                finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,oldEnv);}
            }
        });
        Check("P6B_READ_ONLY_NONFIXTURE_CANDIDATE_NO_AUTHORIZATION",()=>
        {
            // A user-like tree (NOT a marked P5C test root) is inspected without
            // ever authorizing native Rename or touching its bytes.
            var baseDir=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_P6B_UNTRUSTED_"+Guid.NewGuid().ToString("N"));
            var sourceDir=Path.Combine(baseDir,"photos");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(Path.Combine(baseDir,"out"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(sourceDir,"photo_01.png"));
            var w=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            w.OpenSource(sourceDir);w.SetDestinationRoot(Path.Combine(baseDir,"out"));
            var original=Path.Combine(sourceDir,"photo_01.png");
            var expectedHash=SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(baseDir,"state"));
            Ensure(!P5CGuiFixturePolicy.IsMarkedStore(db),"User-like folder incorrectly accepted as test fixture");
            var assessment=ProductionRenameReadiness.Inspect(db,w,w.Current!,"photo_01_renamed.png");
            Ensure(assessment.PreliminaryChecksPass && !assessment.AuthorizedForMutation &&
                assessment.ObservedSha256==expectedHash && assessment.ObservedIdentity==w.Current!.NativeIdentity &&
                !File.Exists(assessment.ProposedTarget) && db.AllRenameHistoryCount()==0 &&
                SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()==expectedHash,
                "Read-only production probe mutated files, misread identity, or granted authorization");
        });
        Check("P6B_READ_ONLY_TARGET_COLLISION_AND_ALIAS",()=>
        {
            var root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var folder=Path.Combine(root,"source");
            File.WriteAllText(Path.Combine(folder,"Occupied.PNG"),"SENTINEL");
            var collision=ProductionRenameReadiness.Inspect(db,w,w.Current!,"occupied.png");
            var traversal=ProductionRenameReadiness.Inspect(db,w,w.Current!,"../escape.png");
            Ensure(!collision.PreliminaryChecksPass && collision.Blockers.Any(x=>x.Contains("aliases")) &&
                !traversal.PreliminaryChecksPass && traversal.Blockers.Any(x=>x.Contains("Invalid candidate")) &&
                File.ReadAllText(Path.Combine(folder,"Occupied.PNG"))=="SENTINEL" &&
                db.AllRenameHistoryCount()==0 && !collision.AuthorizedForMutation && !traversal.AuthorizedForMutation,
                "Read-only probe failed collision/traversal refusal or touched files");
        });
        Check("P6B_READ_ONLY_REPLACED_NATIVE_SOURCE_REFUSED",()=>
        {
            var root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var original=w.Current!.Path;
            var impostor=w.Current! with { NativeIdentity="fake-file-id-not-at-import" };
            var result=ProductionRenameReadiness.Inspect(db,w,impostor,"new_name.png");
            Ensure(!result.PreliminaryChecksPass && result.Blockers.Any(x=>x.Contains("changed since import")) &&
                db.AllRenameHistoryCount()==0 && File.Exists(original) && !result.AuthorizedForMutation,
                "Read-only probe accepted wrong recorded native identity");
        });
        Check("P6B_READ_ONLY_HISTORY_PREVENTS_FALSE_CLEARANCE",()=>
        {
            var root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var photo=w.Current!;
            var row=new DurableRenameItem(Guid.NewGuid(),db.MoveInstallationId(),w.SessionId,
                photo.Id,w.Revision,DateTime.UtcNow.Ticks,photo.Path,
                Path.Combine(Path.GetDirectoryName(photo.Path)!,"not_there.png"),
                new string('A',64),photo.NativeIdentity!,photo.Length,photo.LastWriteUtcTicks,"Intent",null);
            db.PutRenameItem(row);
            var result=ProductionRenameReadiness.Inspect(db,w,photo,"another_name.png");
            Ensure(!result.PreliminaryChecksPass && result.Blockers.Any(x=>x.Contains("history")) &&
                db.RenameItems(w.SessionId).Single()==row && !result.AuthorizedForMutation,
                "Read-only inspection disregarded unresolved journal or changed it");
        });
        Check("P6B1_DISTINCT_FIXTURE_RENAMES_RESTART_AND_LIFO_UNDO",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                Guid session=work.SessionId;
                var originals=work.Photos.ToDictionary(p=>p.Id,p=>p.Path);
                var originalHashes=originals.ToDictionary(k=>k.Key,
                    k=>SortingWorkspace.ComputeSha256Async(k.Value).GetAwaiter().GetResult());
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(work.CaptureSession());
                    var coordinator=new DurableRenameCoordinator(db);
                    for(var i=0;i<2;i++)
                    {
                        var current=work.Current!;
                        var proposal=coordinator.Prepare(work,current,$"distinct_{i}.png");
                        var row=coordinator.Apply(work,proposal);
                        Ensure(row.State=="Committed","Fixture Rename not committed");
                        work.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                        db.Save(work.CaptureSession());
                        if(i==0) work.Navigate(1);
                    }
                    Ensure(db.RenameItems(session).Count==2 &&
                        db.RenameItems(session).All(DurableRenameCoordinator.ProveState) &&
                        db.RenameItems(session)[0].Sequence<db.RenameItems(session)[1].Sequence,
                        "Two independently committed Renames missing or unordered");
                }
                using(var reopened=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    var saved=new SortingWorkspace();saved.RestoreSession(reopened.Load()!);
                    var rows=reopened.RenameItems(session);
                    Ensure(rows.Count==2 && rows.All(DurableRenameCoordinator.ProveState) &&
                        saved.Photos.Count==3 && saved.Photos.Any(p=>p.Path==rows[0].Target) &&
                        saved.Photos.Any(p=>p.Path==rows[1].Target),
                        "Restart lost Rename paths, history or logical photos");
                    var coordinator=new DurableRenameCoordinator(reopened);
                    var second=coordinator.Undo(session);
                    Ensure(second.Id==rows[1].Id && second.State=="Undone",
                        "Undo did not select newest committed Rename first");
                    saved.ApplyVerifiedRename(second.PhotoId,second.Target,second.Source);
                    reopened.Save(saved.CaptureSession());
                    var first=coordinator.Undo(session);
                    Ensure(first.Id==rows[0].Id && first.State=="Undone",
                        "Undo did not return to the earlier committed Rename");
                    saved.ApplyVerifiedRename(first.PhotoId,first.Target,first.Source);
                    reopened.Save(saved.CaptureSession());
                    Ensure(reopened.RenameItems(session).Count==2 &&
                        reopened.RenameItems(session).All(r=>r.State=="Undone" && DurableRenameCoordinator.ProveState(r)),
                        "Multi-Rename history was lost after Undo");
                }
                foreach(var (id,path) in originals)
                    Ensure(File.Exists(path) &&
                        SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==originalHashes[id],
                        "Original fixture bytes not preserved by multi-Undo");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6B1_REUSE_OF_EARLIER_PHOTO_OR_PATH_REFUSED",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                var renamer=new DurableRenameCoordinator(db);
                db.Save(work.CaptureSession());
                var p=renamer.Prepare(work,work.Current!,"prior_target.png");
                var first=renamer.Apply(work,p);
                work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                db.Save(work.CaptureSession());
                Refuse<IOException>(()=>renamer.Prepare(work,work.Current!,"second_name.png"));
                var latest=renamer.Undo(work.SessionId);
                work.ApplyVerifiedRename(latest.PhotoId,latest.Target,latest.Source);
                db.Save(work.CaptureSession());
                Refuse<IOException>(()=>renamer.Prepare(work,work.Current!,"third_name.png"));
                work.Navigate(1);
                Refuse<IOException>(()=>renamer.Prepare(work,work.Current!,Path.GetFileName(first.Target)));
                Ensure(db.AllRenameHistoryCount()==1 && File.Exists(first.Source) &&
                    !File.Exists(first.Target),"History reuse guard unexpectedly mutated fixture");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6B1_UNRESOLVED_SECOND_INTENT_PRESERVES_FIRST_AND_BLOCKS_FURTHER",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                var renamer=new DurableRenameCoordinator(db);
                db.Save(work.CaptureSession());
                var first=renamer.Apply(work,renamer.Prepare(work,work.Current!,"first_owned.png"));
                work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                db.Save(work.CaptureSession());
                work.Navigate(1);
                var cutRenamer=new DurableRenameCoordinator(db,name=>
                {
                    if(name=="RENAME_INTENT_DURABLE") throw new SimulatedInterruptionException(name);
                });
                var p=cutRenamer.Prepare(work,work.Current!,"second_intent.png");
                Refuse<SimulatedInterruptionException>(()=>cutRenamer.Apply(work,p));
                Ensure(db.RenameItems(work.SessionId).Count==2 &&
                    db.RenameItems(work.SessionId)[0].State=="Committed" &&
                    db.RenameItems(work.SessionId)[1].State=="Intent" &&
                    DurableRenameCoordinator.ProveState(first) && File.Exists(p.Source) &&
                    !File.Exists(p.Target),"Boundary cut lost earlier Rename or changed native bytes");
                Refuse<IOException>(()=>renamer.Undo(work.SessionId));
                Refuse<IOException>(()=>renamer.Prepare(work,work.Current!,"refuse_third.png"));
                Ensure(db.AllRenameHistoryCount()==2,"Unresolved history was overwritten");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6B1_CHANGED_PRIOR_TARGET_REFUSES_SECOND_RENAME",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                var renamer=new DurableRenameCoordinator(db);
                db.Save(work.CaptureSession());
                var first=renamer.Apply(work,renamer.Prepare(work,work.Current!,"first_owned.png"));
                work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                db.Save(work.CaptureSession());
                work.Navigate(1);
                File.AppendAllText(first.Target,"EXTERNAL_MUTATION");
                Refuse<IOException>(()=>renamer.Prepare(work,work.Current!,"second_owned.png"));
                Refuse<IOException>(()=>renamer.Undo(work.SessionId));
                Ensure(db.AllRenameHistoryCount()==1 && File.Exists(first.Target),
                    "Changed prior file overwritten or ignored");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6B2_GUI_DISTINCT_RENAME_AFTER_FIRST_HISTORY",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                db.Save(work.CaptureSession());
                var engine=new DurableRenameCoordinator(db);
                Ensure(FixtureRenameGuiGate.CanPrepare(db,work) && !FixtureRenameGuiGate.CanUndo(db,work),
                    "Fresh fixture GUI controls incorrectly gated");
                var p1=engine.Prepare(work,work.Current!,"gui_one.png");
                var row1=engine.Apply(work,p1);
                work.ApplyVerifiedRename(row1.PhotoId,row1.Source,row1.Target);
                db.Save(work.CaptureSession());
                Ensure(!FixtureRenameGuiGate.CanPrepare(db,work) && FixtureRenameGuiGate.CanUndo(db,work),
                    "Previously renamed photo should not get another GUI approval");
                work.Navigate(1);
                Ensure(FixtureRenameGuiGate.CanPrepare(db,work) && FixtureRenameGuiGate.CanUndo(db,work),
                    "Healthy first Rename should allow a distinct photo to be reviewed");
                var p2=engine.Prepare(work,work.Current!,"gui_two.png");
                var row2=engine.Apply(work,p2);
                work.ApplyVerifiedRename(row2.PhotoId,row2.Source,row2.Target);
                db.Save(work.CaptureSession());
                Ensure(db.RenameItems(work.SessionId).Count==2 && FixtureRenameGuiGate.CanUndo(db,work),
                    "Two committed Renames are not Undo-eligible");
                var u2=engine.Undo(work.SessionId);
                Ensure(u2.Id==row2.Id,"GUI fixture Undo did not take latest committed row");
                work.ApplyVerifiedRename(u2.PhotoId,u2.Target,u2.Source);
                db.Save(work.CaptureSession());
                Ensure(FixtureRenameGuiGate.CanUndo(db,work),"Earlier committed row should remain Undo-eligible");
                var u1=engine.Undo(work.SessionId);
                Ensure(u1.Id==row1.Id,"GUI fixture second Undo did not select first row");
                work.ApplyVerifiedRename(u1.PhotoId,u1.Target,u1.Source);
                db.Save(work.CaptureSession());
                Ensure(work.Current?.Id==row2.PhotoId &&
                    !FixtureRenameGuiGate.CanUndo(db,work) && !FixtureRenameGuiGate.CanPrepare(db,work),
                    "Fully undone second photo must not authorize re-renaming the same photo");
                // The cursor is still on photo 2. Advancing by +1 would select
                // the *unused* third photo and make the reuse assertion invalid.
                work.JumpToPhoto(1);
                Ensure(work.Current?.Id==row1.PhotoId && !FixtureRenameGuiGate.CanPrepare(db,work),
                    "Undone first photo reused despite immutable history");
                work.JumpToPhoto(3);
                Ensure(work.Current?.Id!=row1.PhotoId && work.Current?.Id!=row2.PhotoId &&
                    FixtureRenameGuiGate.CanPrepare(db,work),
                    "Unused third photo should remain eligible");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6B2_GUI_UNRESOLVED_HISTORY_FAILS_CLOSED",() =>
        {
            var root=Root();var work=RenameFixture(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                db.Save(work.CaptureSession());
                var current=work.Current!;
                var intent=new DurableRenameItem(Guid.NewGuid(),db.MoveInstallationId(),work.SessionId,
                    current.Id,work.Revision,DateTime.UtcNow.Ticks,current.Path,
                    Path.Combine(Path.GetDirectoryName(current.Path)!,"gui_interrupted.png"),
                    new string('A',64),current.NativeIdentity!,current.Length,current.LastWriteUtcTicks,"Intent",null);
                db.PutRenameItem(intent);
                Ensure(!FixtureRenameGuiGate.CanPrepare(db,work) && !FixtureRenameGuiGate.CanUndo(db,work),
                    "Unresolved Intent incorrectly exposed a GUI file-mutation action");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        // P6B3A: real native file mutations in an UNMARKED disposable directory.
        // This validates the handle/dirfd primitive, NOT approval, durable journal,
        // crash recovery or production GUI authority.
        Check("P6B3A_NATIVE_UNMARKED_SAME_DIRECTORY_RENAME_AND_RESTORE",()=>{
            string trial=Path.Combine(Path.GetTempPath(),"P6B3A_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trial);
            try {
                string from=Path.Combine(trial,"ordinary.png");
                string to=Path.Combine(trial,"accepted.png");
                File.Copy(Path.Combine(root,"source","photo_01.png"),from);
                string id=NativeFileCapability.Identity(from);
                string dir=NativeFileCapability.DirectoryIdentity(trial);
                var f=new FileInfo(from);
                string digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(from)));
                Ensure(!File.Exists(to),"Unexpected target in clean trial");
                NativeFileCapability.RelocateNoReplaceOwned(from,to,id,digest,f.Length,f.LastWriteTimeUtc.Ticks,dir);
                Ensure(!File.Exists(from) && File.Exists(to) && NativeFileCapability.Identity(to)==id &&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(to)))==digest,
                    "Native relocation did not preserve exact bytes and native identity");
                var moved=new FileInfo(to);
                NativeFileCapability.RelocateNoReplaceOwned(to,from,id,digest,moved.Length,moved.LastWriteTimeUtc.Ticks,dir);
                Ensure(File.Exists(from) && !File.Exists(to) && NativeFileCapability.Identity(from)==id,
                    "Native reverse relocation failed");
            } finally { Directory.Delete(trial,true); }
        });
        Check("P6B3A_NATIVE_OCCUPIED_TARGET_NO_REPLACE",()=>{
            string trial=Path.Combine(Path.GetTempPath(),"P6B3A_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trial);
            try {
                var from=Path.Combine(trial,"source.png");
                var to=Path.Combine(trial,"target.png");
                File.Copy(Path.Combine(root,"source","photo_01.png"),from);
                File.WriteAllText(to,"PROTECTED TARGET");
                var f=new FileInfo(from);
                var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(from)));
                Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplaceOwned(from,to,
                    NativeFileCapability.Identity(from),hash,f.Length,f.LastWriteTimeUtc.Ticks,
                    NativeFileCapability.DirectoryIdentity(trial)));
                Ensure(File.Exists(from) && File.ReadAllText(to)=="PROTECTED TARGET", "Occupied target changed");
            } finally { Directory.Delete(trial,true); }
        });
        Check("P6B3A_NATIVE_STALE_DIRECTORY_AND_SOURCE_REFUSED",()=>{
            string trial=Path.Combine(Path.GetTempPath(),"P6B3A_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trial);
            try {
                var from=Path.Combine(trial,"source.png");
                var to=Path.Combine(trial,"new.png");
                File.Copy(Path.Combine(root,"source","photo_01.png"),from);
                var f=new FileInfo(from);
                var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(from)));
                var dir=NativeFileCapability.DirectoryIdentity(trial);
                var id=NativeFileCapability.Identity(from);
                Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplaceOwned(from,to,
                    id,hash,f.Length,f.LastWriteTimeUtc.Ticks,"wrong-directory-identity"));
                Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplaceOwned(from,to,
                    "wrong-source-identity",hash,f.Length,f.LastWriteTimeUtc.Ticks,dir));
                Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplaceOwned(from,to,
                    id,new string('0',64),f.Length,f.LastWriteTimeUtc.Ticks,dir));
                Ensure(File.Exists(from) && !File.Exists(to),"Stale snapshot unexpectedly moved a file");
            } finally { Directory.Delete(trial,true); }
        });
        Check("P6B3A_NATIVE_HARDLINK_SOURCE_REFUSED",()=>{
            string trial=Path.Combine(Path.GetTempPath(),"P6B3A_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trial);
            try {
                var from=Path.Combine(trial,"source.png");
                var alias=Path.Combine(trial,"alias.png");
                var to=Path.Combine(trial,"new.png");
                File.Copy(Path.Combine(root,"source","photo_01.png"),from);
                NativeFileCapability.PublishLinkNoReplace(from,alias);
                var f=new FileInfo(from);
                var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(from)));
                Refuse<IOException>(()=>NativeFileCapability.RelocateNoReplaceOwned(from,to,
                    NativeFileCapability.Identity(from),hash,f.Length,f.LastWriteTimeUtc.Ticks,
                    NativeFileCapability.DirectoryIdentity(trial)));
                Ensure(File.Exists(from) && File.Exists(alias) && !File.Exists(to),
                    "Hardlinked source was renamed despite rejection policy");
            } finally { Directory.Delete(trial,true); }
        });
        // P6B3B: anchored native Rename integrated into the existing durable
        // journal, but ONLY under the unchanged generated-fixture policy.
        Check("P6B3B_ANCHORED_JOURNAL_RESTART_UNDO",()=>
        {
            var fixture=Root();var w=RenameFixture(fixture);
            File.WriteAllText(Path.Combine(fixture,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,fixture);
                Guid session=w.SessionId; Guid itemId; string original=w.Current!.Path;
                string initialHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
                string dirId=NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(original)!);
                using(var db=new WorkspaceSessionStore(Path.Combine(fixture,"state")))
                {
                    db.Save(w.CaptureSession());
                    var coordinator=new DurableRenameCoordinator(db);
                    var p=coordinator.PrepareOwnedFixture(w,w.Current!,"anchored_photo.png");
                    Ensure(p.ReviewedDirectoryIdentity==dirId,"Native parent identity not frozen at review");
                    var row=coordinator.Apply(w,p);
                    itemId=row.Id;
                    Ensure(row.State=="Committed" && row.ReviewedDirectoryIdentity==dirId &&
                        DurableRenameCoordinator.ProveState(row) && !File.Exists(original),
                        "Anchored native Rename did not commit ownership to journal");
                    w.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                    db.Save(w.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(fixture,"state")))
                {
                    var restored=new SortingWorkspace();restored.RestoreSession(db.Load()!);
                    var item=db.RenameItems(session).Single();
                    Ensure(item.Id==itemId && item.ReviewedDirectoryIdentity==dirId &&
                        DurableRenameCoordinator.ProveState(item) &&
                        restored.Current!.Path==item.Target,"Anchored journal did not survive restart");
                    var undone=new DurableRenameCoordinator(db).Undo(session);
                    Ensure(undone.Id==itemId && undone.State=="Undone" &&
                        DurableRenameCoordinator.ProveState(undone),"Anchored native Undo failed");
                    restored.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(restored.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(fixture,"state")))
                {
                    var item=db.RenameItems(session).Single();
                    Ensure(item.ReviewedDirectoryIdentity==dirId && item.State=="Undone" &&
                        DurableRenameCoordinator.ProveState(item) &&
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)))==initialHash,
                        "Final reopened Undo lost native ownership, directory identity or bytes");
                }
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous);}
        });
        Check("P6B3B_ANCHORED_COLLISION_PRESERVES_SOURCE_AND_JOURNAL",()=>
        {
            var fixture=Root();var w=RenameFixture(fixture);
            File.WriteAllText(Path.Combine(fixture,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,fixture);
                using var db=new WorkspaceSessionStore(Path.Combine(fixture,"state"));
                db.Save(w.CaptureSession());
                var coordinator=new DurableRenameCoordinator(db);
                var p=coordinator.PrepareOwnedFixture(w,w.Current!,"occupied_after_review.png");
                string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Source)));
                File.WriteAllText(p.Target,"protected-by-another-writer");
                Refuse<IOException>(()=>coordinator.Apply(w,p));
                Ensure(File.Exists(p.Source) && File.ReadAllText(p.Target)=="protected-by-another-writer" &&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Source)))==hash &&
                    db.AllRenameHistoryCount()==0,"Collision displaced original or created false Intent");
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous);}
        });
        Check("P6B3B_ANCHORED_AFTER_NATIVE_CUT_PRESERVES_INTENT",()=>
        {
            var fixture=Root();var w=RenameFixture(fixture);
            File.WriteAllText(Path.Combine(fixture,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,fixture);
                Guid session=w.SessionId;string source=w.Current!.Path;string target;
                using(var db=new WorkspaceSessionStore(Path.Combine(fixture,"state")))
                {
                    db.Save(w.CaptureSession());
                    var coordinator=new DurableRenameCoordinator(db,name=>
                    {
                        if(name=="RENAME_RELOCATED_UNJOURNALLED") throw new SimulatedInterruptionException(name);
                    });
                    var p=coordinator.PrepareOwnedFixture(w,w.Current!,"owned_cut.png");
                    target=p.Target;
                    Refuse<SimulatedInterruptionException>(()=>coordinator.Apply(w,p));
                    Ensure(db.RenameItems(session).Single().State=="Intent" &&
                        !File.Exists(source) && File.Exists(target),
                        "Native-cut simulation silently committed or misplaced photographed bytes");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(fixture,"state")))
                {
                    var rows=db.RenameItems(session);
                    Ensure(rows.Count==1 && rows[0].State=="Intent" &&
                        rows[0].ReviewedDirectoryIdentity is not null &&
                        DurableRenameCoordinator.Inspect(db,session).Single().Evidence.Contains("TARGET=OWNED"),
                        "Restart obscured anchored, interrupted operation evidence");
                    Refuse<IOException>(()=>new DurableRenameCoordinator(db).Undo(session));
                    Refuse<IOException>(()=>new DurableRenameCoordinator(db).PrepareOwnedFixture(w,w.Current!,"again.png"));
                    Ensure(File.Exists(target) && !File.Exists(source),"Unsafe recovery replay moved interrupted file");
                }
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous);}
        });
        // P6B-3C: ordinary, UNMARKED disposable source directory and state,
        // exercising the same anchored durable journal without the P5C marker.
        // This special lane is internal and restricted to generated /tmp roots.
        Check("P6B3C_UNMARKED_DURABLE_TWO_RENAMES_RESTART_LIFO_UNDO",()=>
        {
            var ordinary=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(ordinary,"source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(ordinary,"output"));
            foreach(var src in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Fixtures"),"*.png"))
                File.Copy(src,Path.Combine(source,Path.GetFileName(src)),overwrite:false);
            var work=RenameFixture(ordinary);
            // Freeze all three native image bytes, plus an output sentinel, before any mutation.
            var originalHashes=Directory.GetFiles(source,"*.png")
                .ToDictionary(path=>Path.GetFileName(path)!,
                    path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            File.WriteAllText(Path.Combine(ordinary,"output","DO_NOT_TOUCH.txt"),"protected-test-output");
            var session=work.SessionId;
            Guid firstId,secondId;
            try
            {
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    db.Save(work.CaptureSession());
                    Refuse<IOException>(()=>new DurableRenameCoordinator(db));
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary);
                    // A legacy unanchored proposal must never execute in this test lane.
                    var old=engine.Prepare(work,work.Current!,"not_anchored.png");
                    Refuse<IOException>(()=>engine.Apply(work,old));
                    Ensure(db.AllRenameHistoryCount()==0,"Unanchored trial made durable history");
                    var p=engine.PrepareOwnedFixture(work,work.Current!,"ordinary_first.png");
                    var row=engine.Apply(work,p);firstId=row.Id;
                    Ensure(row.State=="Committed" && row.ReviewedDirectoryIdentity is not null &&
                           DurableRenameCoordinator.ProveState(row),"Unmarked first journal commit invalid");
                    work.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                    work.Navigate(1);
                    var q=engine.PrepareOwnedFixture(work,work.Current!,"ordinary_second.png");
                    var second=engine.Apply(work,q);secondId=second.Id;
                    Ensure(second.State=="Committed" && DurableRenameCoordinator.ProveState(second) &&
                           db.RenameItems(session).Count==2,"Second independent unmarked journal commit missing");
                    work.ApplyVerifiedRename(second.PhotoId,second.Source,second.Target);
                    db.Save(work.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    var saved=new SortingWorkspace();saved.RestoreSession(db.Load()!);
                    var rows=db.RenameItems(session);
                    Ensure(rows.Count==2 && rows[0].Id==firstId && rows[1].Id==secondId &&
                           rows.All(DurableRenameCoordinator.ProveState),
                           "Restart lost unmarked directory journal ownership");
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary);
                    var undo2=engine.Undo(session);
                    Ensure(undo2.Id==secondId && undo2.State=="Undone" &&
                           DurableRenameCoordinator.ProveState(undo2),"Second Rename undo failed");
                    saved.ApplyVerifiedRename(undo2.PhotoId,undo2.Target,undo2.Source);
                    db.Save(saved.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    var saved=new SortingWorkspace();saved.RestoreSession(db.Load()!);
                    var undo1=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary).Undo(session);
                    Ensure(undo1.Id==firstId && undo1.State=="Undone", "First Rename undo out of order");
                    saved.ApplyVerifiedRename(undo1.PhotoId,undo1.Target,undo1.Source);
                    db.Save(saved.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    Ensure(db.RenameItems(session).Count==2 &&
                           db.RenameItems(session).All(x=>x.State=="Undone" && DurableRenameCoordinator.ProveState(x)) &&
                           Directory.GetFiles(source,"*.png").Length==3 &&
                           originalHashes.All(kv=>File.Exists(Path.Combine(source,kv.Key!)) &&
                               Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,kv.Key!))))==kv.Value) &&
                           File.ReadAllText(Path.Combine(ordinary,"output","DO_NOT_TOUCH.txt"))=="protected-test-output",
                           "Final ordinary unmarked restoration lost image bytes, output sentinel or journal history");
                }
            }
            finally {if(Directory.Exists(ordinary)) Directory.Delete(ordinary,true);}
        });
        Check("P6B3C_UNMARKED_OCCUPIED_TARGET_REFUSES_BEFORE_INTENT",()=>
        {
            var ordinary=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(ordinary,"source"));
            Directory.CreateDirectory(Path.Combine(ordinary,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(ordinary,"source","photo_01.png"));
            try
            {
                var work=RenameFixture(ordinary);
                using var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state"));
                db.Save(work.CaptureSession());
                var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary);
                var p=engine.PrepareOwnedFixture(work,work.Current!,"external_owner.png");
                File.WriteAllText(p.Target,"EXTERNAL_CONTENT_MUST_STAY");
                Refuse<IOException>(()=>engine.Apply(work,p));
                Ensure(File.ReadAllText(p.Target)=="EXTERNAL_CONTENT_MUST_STAY" &&
                       File.Exists(p.Source) && db.AllRenameHistoryCount()==0,
                       "Unmarked collision mutated file or wrote false journal Intent");
            }
            finally {if(Directory.Exists(ordinary)) Directory.Delete(ordinary,true);}
        });
        Check("P6B3C_UNMARKED_AFTER_NATIVE_CUT_NEVER_AUTO_REPLAYS",()=>
        {
            var ordinary=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(ordinary,"source"));
            Directory.CreateDirectory(Path.Combine(ordinary,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(ordinary,"source","photo_01.png"));
            var work=RenameFixture(ordinary);string source=work.Current!.Path,target;var session=work.SessionId;
            try
            {
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary,name=>
                    {
                        if(name=="RENAME_RELOCATED_UNJOURNALLED")
                            throw new SimulatedInterruptionException(name);
                    });
                    var p=engine.PrepareOwnedFixture(work,work.Current!,"after_native_cut.png");
                    target=p.Target;
                    Refuse<SimulatedInterruptionException>(()=>engine.Apply(work,p));
                    Ensure(db.RenameItems(session).Single().State=="Intent" &&
                           !File.Exists(source) && File.Exists(target),"Synthetic cut did not retain uncommitted evidence");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state")))
                {
                    var item=db.RenameItems(session).Single();
                    Ensure(item.State=="Intent" && item.ReviewedDirectoryIdentity is not null &&
                           DurableRenameCoordinator.Inspect(db,session).Single().Evidence.Contains("TARGET=OWNED"),
                           "Unmarked restart did not expose exact stranded native bytes");
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary);
                    Refuse<IOException>(()=>engine.Undo(session));
                    Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"retry.png"));
                    Ensure(!File.Exists(source) && File.Exists(target),"Unmarked retry changed stranded operation");
                }
            }
            finally {if(Directory.Exists(ordinary)) Directory.Delete(ordinary,true);}
        });
        Check("P6B3C_UNMARKED_SCOPE_REFUSES_NON_TEMP_AND_UNSCOPED_SOURCE",()=>
        {
            var ordinary=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(ordinary,"source"));
            Directory.CreateDirectory(Path.Combine(ordinary,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(ordinary,"source","photo_01.png"));
            try
            {
                var work=RenameFixture(ordinary);
                using var db=new WorkspaceSessionStore(Path.Combine(ordinary,"state"));
                Refuse<IOException>(()=>DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,
                    Path.GetDirectoryName(ordinary)!));
                Refuse<IOException>(()=>DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,
                    Path.Combine(ordinary,"source")));
                var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,ordinary);
                Directory.CreateDirectory(Path.Combine(ordinary,"other-output"));
                work.SetDestinationRoot(Path.Combine(ordinary,"other-output"));
                Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"outside.png"));
                Ensure(db.AllRenameHistoryCount()==0,"Scope refusal created a Rename Intent");
            }
            finally {if(Directory.Exists(ordinary)) Directory.Delete(ordinary,true);}
        });
        // P6B-3D: repeat after fully verified Undo, confined to the internal
        // unmarked disposable lane. The marked GUI lane remains unchanged.
        Check("P6B3D_UNMARKED_REPEAT_AFTER_UNDO_SURVIVES_RESTART",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(temp,"source");
            Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(temp,"output"));
            foreach(var asset in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Fixtures"),"*.png"))
                File.Copy(asset,Path.Combine(source,Path.GetFileName(asset)),false);
            var work=RenameFixture(temp);
            var id=work.Current!.Id;
            var firstName=work.Current.Path;
            var sha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstName)));
            try
            {
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp);
                    var p=engine.PrepareOwnedFixture(work,work.Current!,"round_one.png");
                    var first=engine.Apply(work,p);
                    work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                    db.Save(work.CaptureSession());
                    var undone=engine.Undo(work.SessionId);
                    Ensure(undone.Id==first.Id && undone.State=="Undone","First Rename not undone");
                    work.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(work.CaptureSession());
                    var again=engine.PrepareOwnedFixture(work,work.Current!,"round_two.png");
                    var next=engine.Apply(work,again);
                    work.ApplyVerifiedRename(next.PhotoId,next.Source,next.Target);
                    db.Save(work.CaptureSession());
                    Ensure(next.SupersedesRenameId==first.Id && next.State=="Committed" &&
                        db.RenameItems(work.SessionId).Count==2 &&
                        DurableRenameCoordinator.ProveState(next) &&
                        !File.Exists(firstName) && !File.Exists(first.Target),
                        "Repeat did not link immutable historical Undo to new committed generation");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    var saved=new SortingWorkspace();saved.RestoreSession(db.Load()!);
                    var history=db.RenameItems(saved.SessionId);
                    Ensure(history.Count==2 && history[0].State=="Undone" &&
                        history[1].State=="Committed" && history[1].SupersedesRenameId==history[0].Id &&
                        saved.Photos.Single(p=>p.Id==id).Path==history[1].Target,
                        "Repeat Rename restart lost chain or workspace checkpoint");
                    var undone=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp).Undo(saved.SessionId);
                    Ensure(undone.Id==history[1].Id && undone.State=="Undone","Second generation Undo failed");
                    saved.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(saved.CaptureSession());
                }
                Ensure(File.Exists(firstName) &&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstName)))==sha &&
                    !File.Exists(Path.Combine(source,"round_one.png")) &&
                    !File.Exists(Path.Combine(source,"round_two.png")),
                    "Repeat+Undo did not preserve exactly owned original bytes");
            }
            finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
        });
        Check("P6B3D_REPLACED_SOURCE_AFTER_UNDO_REFUSES_REPEAT",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(temp,"source"));
            Directory.CreateDirectory(Path.Combine(temp,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(temp,"source","photo_01.png"));
            var work=RenameFixture(temp);
            try
            {
                using var db=new WorkspaceSessionStore(Path.Combine(temp,"state"));
                db.Save(work.CaptureSession());
                var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp);
                var first=engine.Apply(work,engine.PrepareOwnedFixture(work,work.Current!,"first_undo.png"));
                work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                db.Save(work.CaptureSession());
                var undo=engine.Undo(work.SessionId);
                work.ApplyVerifiedRename(undo.PhotoId,undo.Target,undo.Source);
                db.Save(work.CaptureSession());
                var bytes=File.ReadAllBytes(first.Source);
                // Create the replacement while the approved inode is STILL present.
                // This prevents accidental Linux inode-number reuse on fast filesystems.
                var staged=Path.Combine(temp,"source","replacement_hold.png");
                File.WriteAllBytes(staged,bytes);
                File.SetLastWriteTimeUtc(staged,new DateTime(first.WriteTicks,DateTimeKind.Utc));
                File.Delete(first.Source);
                File.Move(staged,first.Source);
                Ensure(NativeFileCapability.Identity(first.Source)!=first.NativeIdentity &&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.Source)))==first.Hash &&
                    new FileInfo(first.Source).LastWriteTimeUtc.Ticks==first.WriteTicks,
                    "Replacement negative control did not preserve bytes/time and change inode");
                Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"must_refuse.png"));
                Ensure(db.AllRenameHistoryCount()==1 && !File.Exists(Path.Combine(temp,"source","must_refuse.png")),
                    "Replaced native inode was treated as historical original");
            }
            finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
        });
        Check("P6B3D_INTERRUPTED_REPEAT_PRESERVES_HISTORICAL_UNDO",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(temp,"source"));
            Directory.CreateDirectory(Path.Combine(temp,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(temp,"source","photo_01.png"));
            var work=RenameFixture(temp);
            try
            {
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp);
                    var first=engine.Apply(work,engine.PrepareOwnedFixture(work,work.Current!,"first_undo.png"));
                    work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                    db.Save(work.CaptureSession());
                    var undone=engine.Undo(work.SessionId);
                    work.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(work.CaptureSession());
                    var cutEngine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp,name=>
                    {
                        if(name=="RENAME_RELOCATED_UNJOURNALLED")
                            throw new SimulatedInterruptionException(name);
                    });
                    var proposal=cutEngine.PrepareOwnedFixture(work,work.Current!,"second_cut.png");
                    Refuse<SimulatedInterruptionException>(()=>cutEngine.Apply(work,proposal));
                    var rows=db.RenameItems(work.SessionId);
                    Ensure(rows.Count==2 && rows[0].State=="Undone" &&
                        rows[1].State=="Intent" && rows[1].SupersedesRenameId==rows[0].Id &&
                        !File.Exists(proposal.Source) && File.Exists(proposal.Target),
                        "Interrupted second generation damaged first historical Undo");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,temp);
                    Refuse<IOException>(()=>engine.Undo(work.SessionId));
                    Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"unsafe_retry.png"));
                    Ensure(db.RenameItems(work.SessionId)[0].State=="Undone" &&
                        db.RenameItems(work.SessionId)[1].State=="Intent" &&
                        File.Exists(Path.Combine(temp,"source","second_cut.png")),
                        "Interrupted generation replayed or erased evidence");
                }
            }
            finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
        });
        // P6B-3E: Windows-only opt-in product path, isolated to ordinary,
        // *unmarked* disposable source/output/state directories. Linux must
        // refuse creation even if opt-in env is set.
        Check("P6B3E_PRODUCTION_LANE_OPTIN_AND_PLATFORM_DENIAL",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"P6B3E_WIN_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(temp,"state"));
            var previous=Environment.GetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment);
            try
            {
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,null);
                using var db=new WorkspaceSessionStore(Path.Combine(temp,"state"));
                if(OperatingSystem.IsWindows())
                    Refuse<IOException>(()=>DurableRenameCoordinator.ForWindowsLocalTrial(db));
                else
                    Refuse<PlatformNotSupportedException>(()=>DurableRenameCoordinator.ForWindowsLocalTrial(db));
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,"1");
                if(!OperatingSystem.IsWindows())
                    Refuse<PlatformNotSupportedException>(()=>DurableRenameCoordinator.ForWindowsLocalTrial(db));
                else
                    Ensure(DurableRenameCoordinator.ForWindowsLocalTrial(db) is not null,
                        "Explicit Windows local factory refused a local NTFS workspace");
            }
            finally
            {
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,previous);
                if(Directory.Exists(temp)) Directory.Delete(temp,true);
            }
        });
        Check("P6B3E_LOCAL_TWO_RENAMES_RESTART_AND_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows()) return; // Linux is explicitly unavailable.
            var temp=Path.Combine(Path.GetTempPath(),"P6B3E_WIN_"+Guid.NewGuid().ToString("N"));
            var photos=Path.Combine(temp,"source");
            Directory.CreateDirectory(photos);Directory.CreateDirectory(Path.Combine(temp,"output"));
            foreach(var asset in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Fixtures"),"*.png"))
                File.Copy(asset,Path.Combine(photos,Path.GetFileName(asset)));
            var previous=Environment.GetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment);
            Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,"1");
            try
            {
                var work=RenameFixture(temp);
                var originals=work.Photos.ToDictionary(x=>x.Id,x=>
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Path))));
                var session=work.SessionId;
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForWindowsLocalTrial(db);
                    Ensure(WindowsRenameGuiGate.CanPrepare(db,work),"Fresh production-style GUI not eligible");
                    var proposal=engine.PrepareOwnedFixture(work,work.Current!,"local_one.png");
                    var first=engine.Apply(work,proposal);
                    work.ApplyVerifiedRename(first.PhotoId,first.Source,first.Target);
                    db.Save(work.CaptureSession());
                    work.Navigate(1);
                    var second=engine.PrepareOwnedFixture(work,work.Current!,"local_two.png");
                    var row=engine.Apply(work,second);
                    work.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                    db.Save(work.CaptureSession());
                    Ensure(db.RenameItems(session).Count==2 &&
                        WindowsRenameGuiGate.CanUndo(db,work),"Two local native Rename records not ready for Undo");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    var restored=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                    restored.RestoreSession(db.Load()!);
                    var history=db.RenameItems(session);
                    Ensure(history.Count==2 && history.All(x=>x.State=="Committed" &&
                        DurableRenameCoordinator.ProveState(x)),"Windows local Rename restart lost durable evidence");
                    var engine=DurableRenameCoordinator.ForWindowsLocalTrial(db);
                    var undone=engine.Undo(session);
                    restored.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(restored.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    var restored=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                    restored.RestoreSession(db.Load()!);
                    var undone=DurableRenameCoordinator.ForWindowsLocalTrial(db).Undo(session);
                    restored.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(restored.CaptureSession());
                    Ensure(db.RenameItems(session).All(x=>x.State=="Undone"),
                        "Windows local Rename history not fully undone");
                    foreach(var photo in restored.Photos)
                        Ensure(File.Exists(photo.Path) &&
                            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(photo.Path)))==originals[photo.Id],
                            "Windows local Rename+Undo changed original photograph bytes");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,previous);
                if(Directory.Exists(temp)) Directory.Delete(temp,true);
            }
        });
        Check("P6B3E_LOCAL_COLLISION_REFUSES_BEFORE_JOURNAL",()=>
        {
            if(!OperatingSystem.IsWindows()) return;
            var temp=Path.Combine(Path.GetTempPath(),"P6B3E_WIN_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(temp,"source");
            Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(temp,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(source,"photo_01.png"));
            var originalEnv=Environment.GetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment);
            Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,"1");
            try
            {
                var work=RenameFixture(temp);
                using var db=new WorkspaceSessionStore(Path.Combine(temp,"state"));
                db.Save(work.CaptureSession());
                var engine=DurableRenameCoordinator.ForWindowsLocalTrial(db);
                var proposed=engine.PrepareOwnedFixture(work,work.Current!,"occupied.png");
                File.WriteAllText(proposed.Target,"other-file-preserve");
                Refuse<IOException>(()=>engine.Apply(work,proposed));
                Ensure(File.ReadAllText(proposed.Target)=="other-file-preserve" &&
                    db.AllRenameHistoryCount()==0 && File.Exists(proposed.Source),
                    "Collision changed target/source or inserted unapproved intent");
            }
            finally
            {
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,originalEnv);
                if(Directory.Exists(temp)) Directory.Delete(temp,true);
            }
        });
        Check("P6B3E_LOCAL_AFTER_NATIVE_INTERRUPTION_NO_REPLAY",()=>
        {
            if(!OperatingSystem.IsWindows()) return;
            var temp=Path.Combine(Path.GetTempPath(),"P6B3E_WIN_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(temp,"source");
            Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(temp,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(source,"photo_01.png"));
            var originalEnv=Environment.GetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment);
            Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,"1");
            try
            {
                var work=RenameFixture(temp); Guid session=work.SessionId;
                string renamed;
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForWindowsLocalTrial(db,name=>
                    {
                        if(name=="RENAME_RELOCATED_UNJOURNALLED")
                            throw new SimulatedInterruptionException(name);
                    });
                    var p=engine.PrepareOwnedFixture(work,work.Current!,"local_cut.png");
                    renamed=p.Target;
                    Refuse<SimulatedInterruptionException>(()=>engine.Apply(work,p));
                    Ensure(db.RenameItems(session).Single().State=="Intent" &&
                        File.Exists(renamed) && !File.Exists(p.Source),
                        "Synthetic interrupted Windows Rename did not preserve native Intent");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(temp,"state")))
                {
                    var engine=DurableRenameCoordinator.ForWindowsLocalTrial(db);
                    Refuse<IOException>(()=>engine.Undo(session));
                    Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"retry.png"));
                    Ensure(db.RenameItems(session).Single().State=="Intent" && File.Exists(renamed),
                        "Unresolved Windows native Rename was automatically replayed");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(WindowsLocalRenamePolicy.OptInEnvironment,originalEnv);
                if(Directory.Exists(temp)) Directory.Delete(temp,true);
            }
        });
        // P6C1: full Rename Undo must not permanently trap the sorting workspace.
        // The Copy operation deliberately shares the SAME session and SQLite DB,
        // and is executed only after a native-verified, immutable closeout.
        Check("P6C1_FULL_UNDO_CLOSEOUT_COPY_RESTART_UNDO",()=>
        {
            var dir=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(dir,"source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(dir,"output"));
            var input=Path.Combine(source,"photo_01.png");
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),input);
            var before=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
            Guid session;
            string? copiedTo=null;
            try
            {
                var work=RenameFixture(dir);
                session=work.SessionId;
                using(var db=new WorkspaceSessionStore(Path.Combine(dir,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,dir);
                    var planned=engine.PrepareOwnedFixture(work,work.Current!,"p6c1_once.png");
                    var renamed=engine.Apply(work,planned);
                    work.ApplyVerifiedRename(renamed.PhotoId,renamed.Source,renamed.Target);
                    db.Save(work.CaptureSession());
                    Ensure(!db.IsRenameCloseoutVerified(session),"Committed Rename was improperly closed out");
                    Refuse<IOException>(()=>db.SealFullyUndoneRename(work));
                    var undone=engine.Undo(session);
                    work.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                    db.Save(work.CaptureSession());
                    db.SealFullyUndoneRename(work);
                    Ensure(db.IsRenameCloseoutVerified(session,work) &&
                        db.RenameItems(session).Count==1,"Full Rename Undo closeout not persisted");
                    var future=engine.PrepareOwnedFixture(work,work.Current!,"p6c1_forbidden.png");
                    Refuse<IOException>(()=>engine.Apply(work,future));
                    Ensure(!File.Exists(future.Target) && File.Exists(input),
                        "Closeout did not prevent a second Rename mutation");
                    work.Assign('F');
                    var plan=work.ReviewPlan();
                    work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
                    var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision)
                        .GetAwaiter().GetResult();
                    copiedTo=prepared[0].Destination;
                    var journal=new DurableCopyCoordinator(db);
                    var applied=journal.ApplyAsync(prepared).GetAwaiter().GetResult();
                    Ensure(applied.Completed==1 && applied.NeedsAttention==0 && File.Exists(copiedTo),
                        "Verified post-Rename Copy was not installed");
                    work.RetireCommitted(prepared.Select(x=>x.PhotoId));
                    db.Save(work.CaptureSession());
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(dir,"state")))
                {
                    var restored=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                    restored.RestoreSession(db.Load()!);
                    Ensure(restored.SessionId==session && db.IsRenameCloseoutVerified(session,restored),
                        "Restart lost sealed Rename closeout or source ownership");
                    var journal=new DurableCopyCoordinator(db);
                    journal.Reconcile(session);
                    Ensure(db.CopyItems(session).Count==1 &&
                        db.RenameItems(session).Count==1,"Mixed Copy/Rename history was not retained");
                    var undone=journal.UndoAsync(session).GetAwaiter().GetResult();
                    Ensure(undone.Completed==1 && !File.Exists(copiedTo),
                        "Copy Undo failed after sealed Rename closeout");
                    Ensure(db.IsRenameCloseoutVerified(session,restored),
                        "Copy Undo damaged immutable Rename closeout");
                }
                Ensure(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))==before,
                    "Rename/Copy/Undo changed the original image");
            }
            finally {if(Directory.Exists(dir)) Directory.Delete(dir,true);}
        });
        Check("P6C1_MIXED_COPY_BEFORE_CLOSEOUT_REFUSES",()=>
        {
            var dir=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(dir,"source");
            Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(dir,"output"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                Path.Combine(source,"photo_01.png"));
            try
            {
                var work=RenameFixture(dir);
                using var db=new WorkspaceSessionStore(Path.Combine(dir,"state"));
                db.Save(work.CaptureSession());
                var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,dir);
                var prop=engine.PrepareOwnedFixture(work,work.Current!,"p6c1_unsealed.png");
                var renamed=engine.Apply(work,prop);
                work.ApplyVerifiedRename(renamed.PhotoId,renamed.Source,renamed.Target);
                var undone=engine.Undo(work.SessionId);
                work.ApplyVerifiedRename(undone.PhotoId,undone.Target,undone.Source);
                db.Save(work.CaptureSession());
                work.Assign('F');
                var plan=work.ReviewPlan();
                work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
                var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision)
                    .GetAwaiter().GetResult();
                Refuse<IOException>(()=>new DurableCopyCoordinator(db).ApplyAsync(prepared).GetAwaiter().GetResult());
                Ensure(db.CopyItems(work.SessionId).Count==0 &&
                    !File.Exists(prepared[0].Destination),
                    "Unsealed Rename history admitted a Copy intent or output");
            }
            finally {if(Directory.Exists(dir)) Directory.Delete(dir,true);}
        });
        Check("P6C1_REPLACED_RESTORED_SOURCE_DENIES_CLOSEOUT",()=>
        {
            var dir=Path.Combine(Path.GetTempPath(),"P6B3C_ORDINARY_"+Guid.NewGuid().ToString("N"));
            var source=Path.Combine(dir,"source");
            Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(dir,"output"));
            var input=Path.Combine(source,"photo_01.png");
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),input);
            try
            {
                var work=RenameFixture(dir);
                using var db=new WorkspaceSessionStore(Path.Combine(dir,"state"));
                db.Save(work.CaptureSession());
                var engine=DurableRenameCoordinator.ForUnmarkedDisposableTrial(db,dir);
                var p=engine.PrepareOwnedFixture(work,work.Current!,"p6c1_replacement.png");
                var row=engine.Apply(work,p);
                work.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
                var undo=engine.Undo(work.SessionId);
                work.ApplyVerifiedRename(undo.PhotoId,undo.Target,undo.Source);
                db.Save(work.CaptureSession());
                // A delete followed by a write is NOT a deterministic native
                // replacement on Linux: ext4 and other filesystems may reuse
                // the just-freed inode immediately. Pre-allocate a DIFFERENT
                // regular file while the original is still alive, then replace
                // the path atomically. The bytes and mtime deliberately match:
                // only native ownership must cause closeout refusal.
                var bytes=File.ReadAllBytes(input);
                var timestamp=File.GetLastWriteTimeUtc(input);
                var originalNative=NativeFileCapability.Identity(input);
                var replacement=Path.Combine(source,
                    "p6c1_staged_replacement_"+Guid.NewGuid().ToString("N")+".png");
                File.Copy(input,replacement);
                File.SetLastWriteTimeUtc(replacement,timestamp);
                var replacementNative=NativeFileCapability.Identity(replacement);
                Ensure(originalNative!=replacementNative,
                    "Replacement fixture must start with a distinct live native identity");
                File.Move(replacement,input,overwrite:true);
                Ensure(NativeFileCapability.Identity(input)==replacementNative &&
                    NativeFileCapability.Identity(input)!=originalNative,
                    "Fixture replacement did not install the independently allocated file");
                Ensure(File.GetLastWriteTimeUtc(input)==timestamp &&
                    File.ReadAllBytes(input).AsSpan().SequenceEqual(bytes),
                    "Replacement fixture changed bytes or modification time");
                Refuse<IOException>(()=>db.SealFullyUndoneRename(work));
                Ensure(!db.HasRenameCloseout() && db.RenameItems(work.SessionId).Count==1 &&
                    db.RenameItems(work.SessionId)[0].State=="Undone" &&
                    !db.IsRenameCloseoutVerified(work.SessionId),
                    "Replaced same-byte original bypassed closeout native identity check");
            }
            finally {if(Directory.Exists(dir)) Directory.Delete(dir,true);}
        });
        // P6C2-A is strictly a database-wide negative-authority increment.
        // These tests do not authorize personal-photo Move or GUI mutation.
        Check("P6C2A_EMPTY_EPOCH_READ_ONLY_NO_MUTATION",() =>
        {
            var r=Root();var w=Create(r);
            var source=w.Photos[0].Path;
            var hash=SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var first=db.AssessFreshMoveEpoch();
            db.RequireFreshMoveEpoch();
            var second=db.AssessFreshMoveEpoch();
            Ensure(first.IsFresh && first==second && first.CopyRows==0 &&
                first.MoveRows==0 && first.RenameRows==0 && first.RenameCloseouts==0,
                "New installation already has Move/COPY/Rename journal authority");
            Ensure(!Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")) &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(r,"output")).Any() &&
                SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult()==hash,
                "Move epoch assessment mutated disk or source bytes");
        });
        Check("P6C2A_FOREIGN_UNDONE_MOVE_DENIES_NEW_SESSION",() =>
        {
            var r=Root();var old=Create(r);old.Assign('F');
            var plan=old.ReviewPlan();old.ApprovePlan(old.CurrentPlanId!.Value,old.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(old,plan).GetAwaiter().GetResult();
            var before=p.Hash;
            var item=mover.ExecuteApprovedAsync(old,p).GetAwaiter().GetResult();
            var undone=mover.UndoAsync(old.SessionId).GetAwaiter().GetResult();
            Ensure(undone.State=="Undone" && File.Exists(item.Source) &&
                File.Exists(item.OutputArchive) && !File.Exists(item.Destination),
                "Synthetic source not fully restored before session replay test");
            var another=Create(r);
            Ensure(another.SessionId!=old.SessionId,"Fresh-session test reused historical ID");
            another.Assign('F');var nextPlan=another.ReviewPlan();
            another.ApprovePlan(another.CurrentPlanId!.Value,another.Revision);
            var snapshot=db.AssessFreshMoveEpoch();
            Ensure(!snapshot.IsFresh && snapshot.MoveRows==1,
                "Undone historical Move incorrectly removed from installation-wide authority");
            Refuse<IOException>(()=>mover.PrepareApprovedAsync(another,nextPlan).GetAwaiter().GetResult());
            Ensure(db.MoveItems(another.SessionId).Count==0 &&
                db.MoveItems(old.SessionId).Single().State=="Undone" &&
                File.Exists(item.OutputArchive) &&
                SortingWorkspace.ComputeSha256Async(item.Source).GetAwaiter().GetResult()==before,
                "Fresh session silently bypassed terminal Move history or altered a recovery archive");
        });
        Check("P6C2A_FOREIGN_COPY_HISTORY_DENIES_MOVE_PREFLIGHT",() =>
        {
            var r=Root();var old=Create(r);var next=Create(r);next.Assign('F');
            var original=old.Photos[0].Path;
            var hash=SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),old.SessionId,old.Photos[0].Id,
                'F',Guid.NewGuid(),1,1,original,Path.Combine(r,"output","Family","photo_01.png"),
                Path.Combine(r,"output",".unused.part"),Path.Combine(r,"state","unused-recovery"),
                hash,NativeFileCapability.Identity(original),new FileInfo(original).Length,
                File.GetLastWriteTimeUtc(original).Ticks,null,null,0,"Undone",null));
            var snapshot=db.AssessFreshMoveEpoch();
            Ensure(snapshot.CopyRows==1 && !snapshot.IsFresh,
                "Historical Copy in another session was lost");
            Refuse<IOException>(()=>MoveReadinessProbe.InspectAsync(next,db).GetAwaiter().GetResult());
            var plan=next.ReviewPlan();next.ApprovePlan(next.CurrentPlanId!.Value,next.Revision);
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).PrepareApprovedAsync(next,plan).GetAwaiter().GetResult());
            Ensure(db.MoveItems(next.SessionId).Count==0 &&
                !Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")) &&
                SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()==hash,
                "Foreign Copy history preflight mutated a source or created Move journal");
        });
        Check("P6C2A_FOREIGN_RENAME_INTENT_DENIES_MOVE",() =>
        {
            var r=Root();var old=Create(r);var next=Create(r);next.Assign('F');
            var original=old.Photos[0].Path;
            var info=new FileInfo(original);
            var hash=SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            db.PutRenameItem(new DurableRenameItem(Guid.NewGuid(),db.MoveInstallationId(),
                old.SessionId,old.Photos[0].Id,old.Revision,DateTime.UtcNow.Ticks,
                original,Path.Combine(r,"source","historical-rename.png"),hash,
                NativeFileCapability.Identity(original),info.Length,info.LastWriteTimeUtc.Ticks,
                "Intent",null));
            var snapshot=db.AssessFreshMoveEpoch();
            Ensure(snapshot.RenameRows==1 && !snapshot.IsFresh && snapshot.MoveRows==0,
                "Unresolved Rename authority hidden by another session");
            Refuse<IOException>(()=>MoveReadinessProbe.InspectAsync(next,db).GetAwaiter().GetResult());
            var plan=next.ReviewPlan();next.ApprovePlan(next.CurrentPlanId!.Value,next.Revision);
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).PrepareApprovedAsync(next,plan).GetAwaiter().GetResult());
            Ensure(File.Exists(original) &&
                SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()==hash &&
                db.MoveItems(next.SessionId).Count==0 &&
                !Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")),
                "Unresolved Rename history triggered unauthorized Move or archive creation");
        });
        Check("P6C2A_FOREIGN_JOURNAL_AFTER_PREPARE_DENIES_EXECUTION",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var prepared=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            // Simulate a legitimate other-session journal appearing AFTER
            // exact-plan preflight. The executable stage must re-prove epoch.
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),Guid.NewGuid(),w.Photos[0].Id,
                'F',Guid.NewGuid(),1,1,prepared.Source,
                Path.Combine(r,"output","Travel","occupied-by-history.png"),
                Path.Combine(r,"output",".history.part"),Path.Combine(r,"state","historical-copy"),
                prepared.Hash,prepared.Identity,prepared.Length,prepared.WriteTicks,
                null,null,0,"Undone",null));
            Refuse<IOException>(()=>mover.ExecuteApprovedAsync(w,prepared).GetAwaiter().GetResult());
            Ensure(db.MoveItems(w.SessionId).Count==0 &&
                File.Exists(prepared.Source) && !File.Exists(prepared.Destination) &&
                !Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")) &&
                SortingWorkspace.ComputeSha256Async(prepared.Source).GetAwaiter().GetResult()==prepared.Hash,
                "Execute ignored a foreign journal written after exact-plan approval");
        });
        Check("P6C2A_SQL_INTENT_BYPASS_DENIED_ACROSS_SESSIONS",() =>
        {
            var r=Root();var old=Create(r);old.Assign('F');
            var plan=old.ReviewPlan();old.ApprovePlan(old.CurrentPlanId!.Value,old.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var prepared=mover.PrepareApprovedAsync(old,plan).GetAwaiter().GetResult();
            var first=mover.ExecuteApprovedAsync(old,prepared).GetAwaiter().GetResult();
            var anotherSession=Guid.NewGuid();
            var forgedNewIntent=first with { Id=Guid.NewGuid(), SessionId=anotherSession,
                PhotoId=Guid.NewGuid(),PlanId=Guid.NewGuid(),Sequence=DateTime.UtcNow.Ticks+1,
                State="Intent",TempIdentity=null,TargetIdentity=null,Error=null };
            Refuse<IOException>(()=>db.PutMoveItem(forgedNewIntent));
            Ensure(db.AllMoveHistoryCount()==1 &&
                db.MoveItems(old.SessionId).Single().State=="Committed" &&
                db.MoveItems(anotherSession).Count==0 &&
                !File.Exists(first.Source) && File.Exists(first.Destination) &&
                File.Exists(first.OriginalArchive),
                "Direct SQLite journal insertion created a cross-session Move or touched files");
        });
        // P6C2-B: independently observe terminal native evidence only.
        // IMPORTANT: a positive inspection must NEVER unlock PutMoveItem.
        Check("P6C2B_EMPTY_HISTORY_IS_NOT_REAUTHORIZATION",() =>
        {
            var r=Root();var w=Create(r);
            var original=w.Photos[0].Path;
            var hash=SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult();
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var evidence=db.InspectTerminalMoveHistory();
            Ensure(!evidence.CanConsiderLaterAuthorization && evidence.MoveRows==0 &&
                evidence.VerifiedUndoRows==0 && db.AssessFreshMoveEpoch().IsFresh &&
                !Directory.Exists(Path.Combine(r,"source",".photosift-reborn-recovery")) &&
                SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()==hash,
                "Empty journal read-only inspection created an epoch or modified files");
        });
        Check("P6C2B_UNDONE_HISTORY_HAS_NATIVE_RECOVERY_EVIDENCE",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var done=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            var proof=db.InspectTerminalMoveHistory();
            Ensure(proof.CanConsiderLaterAuthorization && proof.MoveRows==1 &&
                proof.VerifiedUndoRows==1 && !db.AssessFreshMoveEpoch().IsFresh &&
                db.MoveItems(w.SessionId).Single().State=="Undone" &&
                File.Exists(done.Source) && File.Exists(done.OutputArchive) &&
                !File.Exists(done.Destination),
                "Verified terminal Move evidence missing or P6C2A fresh guard weakened");
        });
        Check("P6C2B_COMMITTED_MOVE_IS_NOT_TERMINAL",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var done=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            var proof=db.InspectTerminalMoveHistory();
            Ensure(!proof.CanConsiderLaterAuthorization && proof.MoveRows==1 &&
                proof.VerifiedUndoRows==0 && db.MoveItems(w.SessionId).Single().State=="Committed" &&
                !File.Exists(done.Source) && File.Exists(done.OriginalArchive) &&
                File.Exists(done.Destination),
                "Committed Move was mistaken for fully undone history or mutated");
        });
        Check("P6C2B_SAME_BYTES_REPLACED_ARCHIVE_DENIED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var done=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(db.InspectTerminalMoveHistory().CanConsiderLaterAuthorization,
                "Fully undone fixture should be provable before replacement");
            var replacement=Path.Combine(Path.GetDirectoryName(done.OutputArchive)!,
                ".replacement-"+Guid.NewGuid().ToString("N")+".tmp");
            File.Copy(done.OutputArchive,replacement);
            var originalId=NativeFileCapability.Identity(done.OutputArchive);
            var replacementId=NativeFileCapability.Identity(replacement);
            Ensure(originalId!=replacementId,"Adversarial archive replacement not distinct before install");
            File.Move(replacement,done.OutputArchive,overwrite:true);
            Ensure(NativeFileCapability.Identity(done.OutputArchive)==replacementId &&
                SortingWorkspace.ComputeSha256Async(done.OutputArchive).GetAwaiter().GetResult()==p.Hash,
                "Archive replacement fixture did not retain bytes and change identity");
            var proof=db.InspectTerminalMoveHistory();
            Ensure(!proof.CanConsiderLaterAuthorization && proof.VerifiedUndoRows==0 &&
                File.Exists(done.Source) && db.MoveItems(w.SessionId).Single().State=="Undone",
                "Same-byte different-native-identity archive incorrectly cleared history");
        });
        Check("P6C2B_PROOF_NEVER_BYPASSES_SQL_INTENT_VETO",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var done=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            var proof=db.InspectTerminalMoveHistory();
            Ensure(proof.CanConsiderLaterAuthorization,"Precondition: terminal Move evidence absent");
            Refuse<IOException>(()=>db.RequireFreshMoveEpoch());
            var forged=done with {Id=Guid.NewGuid(),SessionId=Guid.NewGuid(),
                PhotoId=Guid.NewGuid(),PlanId=Guid.NewGuid(),
                Sequence=DateTime.UtcNow.Ticks,State="Intent",TempIdentity=null,
                TargetIdentity=null,Error=null};
            Refuse<IOException>(()=>db.PutMoveItem(forged));
            Ensure(db.AllMoveHistoryCount()==1 && File.Exists(done.Source) &&
                File.Exists(done.OutputArchive) && !File.Exists(done.Destination),
                "Read-only positive evidence accidentally unlocked a durable Move Intent");
        });
        Check("P6C2B_INTERMIXED_COPY_HISTORY_DENIED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var done=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(db.InspectTerminalMoveHistory().CanConsiderLaterAuthorization,
                "Precondition: terminal evidence should be present before foreign Copy history");
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),Guid.NewGuid(),w.Photos[0].Id,
                'F',Guid.NewGuid(),1,1,done.Source,
                Path.Combine(r,"output","Family","another.png"),
                Path.Combine(r,"output",".copy.part"),Path.Combine(r,"state","copy-archive"),
                p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,0,"Undone",null));
            var proof=db.InspectTerminalMoveHistory();
            Ensure(!proof.CanConsiderLaterAuthorization && proof.MoveRows==1 &&
                db.AssessFreshMoveEpoch().CopyRows==1 && File.Exists(done.OutputArchive) &&
                File.Exists(done.Source),
                "Move-only terminal evidence ignored a foreign Copy-history dependency");
        });
        // P6C2-B1: durable history closeout is separate from execution authority.
        // All cases use generated disposable native files; no GUI/production Move.
        Check("P6C2B1_EMPTY_HISTORY_SEAL_REFUSED",() =>
        {
            var root=Root();var w=Create(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            Refuse<IOException>(()=>db.SealTerminalMoveHistory());
            var state=db.InspectTerminalMoveCloseout();
            Ensure(!state.Present && !state.Verified && state.RecordedRows==0 &&
                db.AssessFreshMoveEpoch().IsFresh &&
                File.Exists(w.Photos[0].Path),
                "Empty installation incorrectly received terminal history closeout");
        });
        Check("P6C2B1_COMMITTED_HISTORY_SEAL_REFUSED",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            Refuse<IOException>(()=>db.SealTerminalMoveHistory());
            Ensure(!db.InspectTerminalMoveCloseout().Present &&
                db.MoveItems(w.SessionId).Single().State=="Committed" &&
                File.Exists(moved.Destination) && File.Exists(moved.OriginalArchive) &&
                !File.Exists(moved.Source),"Active Move was incorrectly sealed or mutated");
        });
        Check("P6C2B1_TERMINAL_SEAL_PERSISTS_AND_IS_IDEMPOTENT",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var state=Path.Combine(r,"state");
            DurableMoveItem moved;
            using (var db=new WorkspaceSessionStore(state))
            {
                var mover=new DurableMoveCoordinator(db);
                var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
                mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
                var proof=db.SealTerminalMoveHistory();
                Ensure(proof.Present && proof.Verified && proof.RecordedRows==1 &&
                    db.SealTerminalMoveHistory().Verified,
                    "Fully undone native Move could not be sealed idempotently");
            }
            using (var db=new WorkspaceSessionStore(state))
            {
                var persisted=db.InspectTerminalMoveCloseout();
                Ensure(persisted.Present && persisted.Verified && persisted.RecordedRows==1 &&
                    db.MoveItems(w.SessionId).Single().State=="Undone" &&
                    File.Exists(moved.Source) && File.Exists(moved.OutputArchive) &&
                    !File.Exists(moved.Destination),
                    "Closeout or native archive failed independent restart inspection");
            }
        });
        Check("P6C2B1_SAME_BYTES_REPLACED_ARCHIVE_REVOKES_CLOSEOUT",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(db.SealTerminalMoveHistory().Verified,"Baseline Move seal missing");
            var substitute=Path.Combine(Path.GetDirectoryName(moved.OutputArchive)!,
                ".b1-substitute-"+Guid.NewGuid().ToString("N"));
            File.Copy(moved.OutputArchive,substitute);
            var original=NativeFileCapability.Identity(moved.OutputArchive);
            var candidate=NativeFileCapability.Identity(substitute);
            Ensure(original!=candidate,"Replacement fixture failed to create distinct native file");
            File.Move(substitute,moved.OutputArchive,overwrite:true);
            Ensure(NativeFileCapability.Identity(moved.OutputArchive)==candidate &&
                SortingWorkspace.ComputeSha256Async(moved.OutputArchive).GetAwaiter().GetResult()==p.Hash,
                "Replaced archive native identity or bytes could not be proved");
            var stale=db.InspectTerminalMoveCloseout();
            Refuse<IOException>(()=>db.SealTerminalMoveHistory());
            Ensure(stale.Present && !stale.Verified &&
                db.MoveItems(w.SessionId).Single().State=="Undone" &&
                File.Exists(moved.Source) && File.Exists(moved.OutputArchive),
                "Native archive replacement failed to revoke read-only closeout proof");
        });
        Check("P6C2B1_FOREIGN_COPY_INVALIDATES_PERSISTED_CLOSEOUT",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(db.SealTerminalMoveHistory().Verified,"Baseline seal missing");
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),Guid.NewGuid(),w.Photos[0].Id,
                'F',Guid.NewGuid(),1,1,moved.Source,
                Path.Combine(r,"output","Family","foreign-copy.png"),
                Path.Combine(r,"output",".foreign-copy.part"),
                Path.Combine(r,"state","foreign-copy-archive"),
                p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,0,"Undone",null));
            var after=db.InspectTerminalMoveCloseout();
            Refuse<IOException>(()=>db.SealTerminalMoveHistory());
            Ensure(after.Present && !after.Verified &&
                db.AssessFreshMoveEpoch().CopyRows==1 &&
                File.Exists(moved.Source) && File.Exists(moved.OutputArchive),
                "Copy journal dependency did not invalidate a previously sealed Move history");
        });
        Check("P6C2B1_SEALED_HISTORY_NEVER_BYPASSES_SQL_INTENT",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(r,"state"));
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(db.SealTerminalMoveHistory().Verified,"Seal precondition missing");
            Refuse<IOException>(()=>db.RequireFreshMoveEpoch());
            var forged=moved with { Id=Guid.NewGuid(),SessionId=Guid.NewGuid(),
                PhotoId=Guid.NewGuid(),PlanId=Guid.NewGuid(),
                Sequence=DateTime.UtcNow.Ticks,State="Intent",TempIdentity=null,
                TargetIdentity=null,Error=null };
            Refuse<IOException>(()=>db.PutMoveItem(forged));
            Ensure(db.InspectTerminalMoveCloseout().Verified &&
                db.AllMoveHistoryCount()==1 &&
                File.Exists(moved.Source) && File.Exists(moved.OutputArchive) &&
                !File.Exists(moved.Destination),
                "Closeout accidentally granted cross-session executable Move authority");
        });
        Check("P6C2B1_SCHEMA_V7_BACKUP_PRESERVES_V6_JOURNALS",() =>
        {
            var r=Root();var w=Create(r);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            string state=Path.Combine(r,"state");
            string dbpath=Path.Combine(state,"workspaces-v1.sqlite");
            DurableMoveItem moved;
            using (var db=new WorkspaceSessionStore(state))
            {
                var mover=new DurableMoveCoordinator(db);
                var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
                moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
                mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            }
            // Construct a disposable v6-compatible DB from our own fixture;
            // this is NOT a production downgrade operation.
            using (var conn=new SqliteConnection($"Data Source={dbpath};Pooling=False"))
            {
                conn.Open();
                using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE move_closeout; DROP TABLE m6j_review_proposals; PRAGMA user_version=6;";
                cmd.ExecuteNonQuery();
            }
            using (var db=new WorkspaceSessionStore(state))
            {
                var proof=db.InspectTerminalMoveHistory();
                Ensure(proof.CanConsiderLaterAuthorization && proof.VerifiedUndoRows==1 &&
                    !db.InspectTerminalMoveCloseout().Present &&
                    db.MoveItems(w.SessionId).Single().State=="Undone" &&
                    File.Exists(moved.Source) && File.Exists(moved.OutputArchive),
                    "v6 to v7 migration lost historical Move/native evidence");
            }
            Ensure(Directory.GetFiles(state,"workspaces-v1.sqlite.before-v7-*.sqlite").Length==1,
                "v7 migration failed to back up the exact previous v6 database");
        });
        Check("P6C2B2_EMPTY_HISTORY_CANNOT_STAGE",() =>
        {
            var root=Root();var w=Create(root);w.Assign('F');
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            using var empty=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var p=DurableCopyCoordinator.PrepareAsync(w,plan,w.CurrentPlanId!.Value,
                w.Revision).GetAwaiter().GetResult().Single();
            var id=Guid.NewGuid();var installation=empty.MoveInstallationId();
            string folder=Path.GetDirectoryName(p.Destination)!;
            string archive=Path.Combine(Path.GetDirectoryName(p.Source)!,
                ".photosift-reborn-recovery",installation.ToString("N"),
                p.SessionId.ToString("N"),id.ToString("N"));
            var proposal=new DurableMoveItem(id,installation,p.SessionId,p.PhotoId,
                p.PlanId,p.Revision,DateTime.UtcNow.Ticks,p.Source,p.Destination,
                Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
                Path.Combine(archive,Path.GetFileName(p.Source)),
                Path.Combine(archive,"installed-"+Path.GetFileName(p.Destination)),
                p.OutputRoot,p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,"Intent",null,p.Key);
            Refuse<IOException>(()=>empty.StageNextMoveEpochProposal(proposal));
            Ensure(!empty.InspectStagedMoveEpoch().Present &&
                !empty.InspectTerminalMoveCloseout().Present &&
                empty.AssessFreshMoveEpoch().IsFresh && File.Exists(p.Source),
                "Empty native history incorrectly staged a candidate");
        });
        Check("P6C2B2_STAGED_TICKET_RESTART_READ_ONLY",() =>
        {
            var f=B2TicketFixture();
            using(var db=f.Db)
            {
                var staged=db.StageNextMoveEpochProposal(f.Next);
                Ensure(staged.Present && staged.Verified && staged.PredecessorRows==1 &&
                    db.StageNextMoveEpochProposal(f.Next).Verified &&
                    db.AllMoveHistoryCount()==1 && File.Exists(f.First.OutputArchive) &&
                    File.Exists(f.Next.Source) && !File.Exists(f.Next.Destination),
                    "Staging unexpectedly mutated photos or previous history");
            }
            using(var db=new WorkspaceSessionStore(Path.Combine(f.Root,"state")))
            {
                Ensure(db.InspectStagedMoveEpoch().Verified &&
                    db.InspectTerminalMoveCloseout().Verified && db.AllMoveHistoryCount()==1,
                    "Staged proposal did not survive SQLite restart");
            }
        });
        Check("P6C2B2_IMMUTABLE_TICKET_REJECTS_RETARGET",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"First ticket missing");
            Refuse<IOException>(()=>db.StageNextMoveEpochProposal(f.Next with {PlanId=Guid.NewGuid()}));
            Ensure(db.InspectStagedMoveEpoch().Verified && db.AllMoveHistoryCount()==1,
                "Ticket retargeting removed or replaced original evidence");
        });
        Check("P6C2B2_REPLACED_CANDIDATE_REVOKES_INSPECTION",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"Baseline staged proposal missing");
            string replacement=Path.Combine(Path.GetDirectoryName(f.Next.Source)!,
                ".b2-replacement-"+Guid.NewGuid().ToString("N"));
            File.Copy(f.Next.Source,replacement);
            File.SetLastWriteTimeUtc(replacement,new DateTime(f.Next.SourceWriteTicks,DateTimeKind.Utc));
            var identity=NativeFileCapability.Identity(replacement);
            Ensure(identity!=f.Next.SourceIdentity,"Replacement fixture reused native source identity");
            File.Move(replacement,f.Next.Source,overwrite:true);
            Ensure(NativeFileCapability.Identity(f.Next.Source)==identity &&
                !db.InspectStagedMoveEpoch().Verified && db.InspectStagedMoveEpoch().Present &&
                db.AllMoveHistoryCount()==1 && File.Exists(f.First.OutputArchive),
                "Same-byte, new-native-identity proposal was not invalidated");
        });
        Check("P6C2B2_FOREIGN_COPY_HISTORY_REVOKES_TICKET",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"Baseline ticket missing");
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
                'F',Guid.NewGuid(),1,1,f.First.Source,
                Path.Combine(f.Root,"output","Family","fake-history.png"),
                Path.Combine(f.Root,"output",".fake-history.part"),
                Path.Combine(f.Root,"state","fake-history-archive"),
                f.First.SourceHash,f.First.SourceIdentity,f.First.SourceLength,
                f.First.SourceWriteTicks,null,null,0,"Undone",null));
            Ensure(db.InspectStagedMoveEpoch().Present && !db.InspectStagedMoveEpoch().Verified &&
                db.AssessFreshMoveEpoch().CopyRows==1 && db.AllMoveHistoryCount()==1,
                "Foreign Copy history failed to invalidate a staged successor");
        });
        Check("P6C2B2_STAGING_NEVER_BYPASSES_SQL_INTENT",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"Staged proposal missing");
            Refuse<IOException>(()=>db.RequireFreshMoveEpoch());
            Refuse<IOException>(()=>db.PutMoveItem(f.Next));
            Ensure(db.AllMoveHistoryCount()==1 && db.InspectStagedMoveEpoch().Verified &&
                File.Exists(f.Next.Source) && !File.Exists(f.Next.Destination),
                "Staged ticket incorrectly conferred executable Move authority");
        });
        Check("P6C2B2_OCCUPIED_TARGET_INVALIDATES_TICKET",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"Baseline ticket missing");
            Directory.CreateDirectory(Path.GetDirectoryName(f.Next.Destination)!);
            File.WriteAllText(f.Next.Destination,"PREEXISTING-DO-NOT-CLOBBER");
            Ensure(db.InspectStagedMoveEpoch().Present && !db.InspectStagedMoveEpoch().Verified &&
                File.ReadAllText(f.Next.Destination)=="PREEXISTING-DO-NOT-CLOBBER" &&
                File.Exists(f.Next.Source),"Output collision was ignored or modified");
        });
        Check("P6C2B2_SCHEMA_V8_BACKUP_PRESERVES_V7_HISTORY",() =>
        {
            var f=B2TicketFixture();string state=Path.Combine(f.Root,"state");
            using(f.Db) Ensure(f.Db.InspectTerminalMoveCloseout().Verified,
                "Baseline v7 native seal missing");
            string path=Path.Combine(state,"workspaces-v1.sqlite");
            using(var conn=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE move_epoch_ticket; DROP TABLE m6j_review_proposals; PRAGMA user_version=7;";
                cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                Ensure(db.InspectTerminalMoveCloseout().Verified &&
                    !db.InspectStagedMoveEpoch().Present &&
                    db.MoveItems(f.First.SessionId).Single().State=="Undone" &&
                    db.AllMoveHistoryCount()==1,"v7 to v8 migration lost native history");
            }
            Ensure(Directory.GetFiles(state,"workspaces-v1.sqlite.before-v8-*.sqlite").Length==1,
                "v8 migration must create one consistent pre-v8 backup");
        });
        // P6C2-B3: one executable successor *only after distinct explicit
        // approval*. The normal fresh-epoch path and GUI locks are unchanged.
        Check("P6C2B3_TICKET_ALONE_DENIES_DIRECT_INTENT",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            Ensure(db.StageNextMoveEpochProposal(f.Next).Verified,"B2 ticket missing");
            Refuse<IOException>(()=>db.PutMoveItem(f.Next));
            Ensure(!db.InspectMoveEpochGrant().Present && db.AllMoveHistoryCount()==1 &&
                File.Exists(f.Next.Source),"Staged proposal alone inserted an unauthorized Intent");
        });
        Check("P6C2B3_EXPLICIT_ACKNOWLEDGEMENT_REQUIRED",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            var mover=new DurableMoveCoordinator(db);
            var p=mover.PrepareApprovedStagedSuccessorAsync(f.NextWork,f.NextPlan,f.Next)
                .GetAwaiter().GetResult();
            Refuse<IOException>(()=>mover.ApproveStagedSuccessor(f.NextWork,p,f.Next,false));
            Ensure(!db.InspectMoveEpochGrant().Present && db.AllMoveHistoryCount()==1,
                "Unacknowledged review created new grant or Intent");
        });
        Check("P6C2B3_APPROVED_SUCCESSOR_RESTART_UNDO",() =>
        {
            var f=B2TicketFixture();string state=Path.Combine(f.Root,"state");
            Guid next=f.Next.SessionId;string hash=f.Next.SourceHash;
            using(f.Db)
            {
                var db=f.Db;db.StageNextMoveEpochProposal(f.Next);
                var mover=new DurableMoveCoordinator(db);
                var p=mover.PrepareApprovedStagedSuccessorAsync(f.NextWork,f.NextPlan,f.Next)
                    .GetAwaiter().GetResult();
                mover.ApproveStagedSuccessor(f.NextWork,p,f.Next,true);
                Ensure(db.InspectMoveEpochGrant().Approved && db.AllMoveHistoryCount()==1,
                    "Explicit grant failed to persist or mutated history");
                var applied=mover.ExecuteApprovedStagedSuccessorAsync(f.NextWork,p,f.Next)
                    .GetAwaiter().GetResult();
                Ensure(applied.Id==f.Next.Id && applied.State=="Committed" &&
                    db.InspectMoveEpochGrant().Consumed && db.AllMoveHistoryCount()==2 &&
                    File.Exists(applied.Destination) && File.Exists(applied.OriginalArchive) &&
                    !File.Exists(applied.Source),"Successor failed native publication/journal consumption");
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                Ensure(db.InspectMoveEpochGrant().Consumed && db.MoveItems(next).Count==1,
                    "Consumed grant / successor Move history missing after restart");
                var done=new DurableMoveCoordinator(db).UndoAsync(next).GetAwaiter().GetResult();
                Ensure(done.State=="Undone" && File.Exists(done.Source) &&
                    !File.Exists(done.Destination) && File.Exists(done.OutputArchive) &&
                    SortingWorkspace.ComputeSha256Async(done.Source).GetAwaiter().GetResult()==hash &&
                    db.InspectMoveEpochGrant().Consumed && db.AllMoveHistoryCount()==2,
                    "Native Undo changed source, lost history or resurrected a grant");
            }
        });
        Check("P6C2B3_GRANT_IS_SINGLE_USE",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            var mover=new DurableMoveCoordinator(db);
            mover.ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
            db.PutMoveItem(f.Next); // atomically consumes the one grant plus Intent
            Ensure(db.InspectMoveEpochGrant().Consumed && db.AllMoveHistoryCount()==2,
                "Grant was not consumed atomically with Intent");
            Refuse<IOException>(()=>db.PutMoveItem(f.Next with {Id=Guid.NewGuid()}));
            Refuse<IOException>(()=>mover.ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true));
            Ensure(db.AllMoveHistoryCount()==2 && db.InspectMoveEpochGrant().Consumed &&
                File.Exists(f.Next.Source),"Consumed grant was reused or source mutated");
        });
        Check("P6C2B3_GRANT_REJECTS_FOREIGN_COPY_BEFORE_INTENT",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            new DurableMoveCoordinator(db).ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
            db.PutCopyItem(new DurableCopyItem(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
                'F',Guid.NewGuid(),1,1,f.Next.Source,
                Path.Combine(f.Root,"output","Family","foreign.png"),
                Path.Combine(f.Root,"output",".foreign.part"),
                Path.Combine(f.Root,"state","foreign-archive"),
                f.Next.SourceHash,f.Next.SourceIdentity,f.Next.SourceLength,
                f.Next.SourceWriteTicks,null,null,0,"Undone",null));
            Refuse<IOException>(()=>db.PutMoveItem(f.Next));
            Ensure(db.AllMoveHistoryCount()==1 && !db.InspectMoveEpochGrant().Consumed &&
                File.Exists(f.Next.Source),"Foreign Copy bypassed SQL successor history gate");
        });
        Check("P6C2B3_GRANT_REJECTS_REPLACED_SOURCE",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            new DurableMoveCoordinator(db).ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
            string replacement=Path.Combine(Path.GetDirectoryName(f.Next.Source)!,
                "p6c2b3-distinct-"+Guid.NewGuid().ToString("N")+".png");
            var ticks=File.GetLastWriteTimeUtc(f.Next.Source);
            File.Copy(f.Next.Source,replacement);
            File.SetLastWriteTimeUtc(replacement,ticks);
            Ensure(NativeFileCapability.Identity(replacement)!=f.Next.SourceIdentity,
                "Fixture replacement not native-distinct");
            File.Move(replacement,f.Next.Source,overwrite:true);
            Refuse<IOException>(()=>db.PutMoveItem(f.Next));
            Ensure(db.AllMoveHistoryCount()==1 && !db.InspectMoveEpochGrant().Consumed &&
                !db.InspectMoveEpochGrant().Approved && File.Exists(f.Next.Source),
                "Same-byte native replacement admitted a staged Move Intent");
        });
        Check("P6C2B3_RETARGET_FAILED_TRANSACTION_PRESERVES_GRANT",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            new DurableMoveCoordinator(db).ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
            Refuse<IOException>(()=>db.PutMoveItem(f.Next with {PlanId=Guid.NewGuid()}));
            Ensure(db.InspectMoveEpochGrant().Approved && db.AllMoveHistoryCount()==1,
                "Failed exact-intent transaction consumed capability");
            db.PutMoveItem(f.Next);
            Ensure(db.InspectMoveEpochGrant().Consumed && db.AllMoveHistoryCount()==2,
                "Valid followup Intent did not atomically consume preserved grant");
        });
        Check("P6C2B3_INTERRUPTED_INTENT_PRESERVES_GRANT_AND_SOURCE",() =>
        {
            var f=B2TicketFixture();string state=Path.Combine(f.Root,"state");
            using(f.Db)
            {
                var db=f.Db;db.StageNextMoveEpochProposal(f.Next);
                new DurableMoveCoordinator(db).ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
                var cut=new DurableMoveCoordinator(db,name=>
                {
                    if(name=="MOVE_INTENT_RECORDED")
                        throw new SimulatedInterruptionException(name);
                });
                Refuse<SimulatedInterruptionException>(()=>cut.ExecuteApprovedStagedSuccessorAsync(
                    f.NextWork,f.Prepared,f.Next).GetAwaiter().GetResult());
                Ensure(db.InspectMoveEpochGrant().Consumed &&
                    db.MoveItems(f.Next.SessionId).Single().State=="Intent" &&
                    File.Exists(f.Next.Source) && !File.Exists(f.Next.Destination),
                    "Interruption discarded intent, reused grant or mutated source");
            }
            using(var db=new WorkspaceSessionStore(state))
                Ensure(db.InspectMoveEpochGrant().Consumed &&
                    db.MoveItems(f.Next.SessionId).Single().State=="Intent" &&
                    File.Exists(f.Next.Source) && !File.Exists(f.Next.Destination),
                    "Startup replayed incomplete Move or unconsumed the frozen grant");
        });
        Check("P6C2B3_REPLACED_PREDECESSOR_ARCHIVE_REVOKES_GRANTED_INTENT",() =>
        {
            var f=B2TicketFixture();using var db=f.Db;
            db.StageNextMoveEpochProposal(f.Next);
            new DurableMoveCoordinator(db).ApproveStagedSuccessor(f.NextWork,f.Prepared,f.Next,true);
            var prior=db.MoveItems(f.First.SessionId).Single();
            var originalIdentity=NativeFileCapability.Identity(prior.OutputArchive);
            var bytes=File.ReadAllBytes(prior.OutputArchive);
            var mtime=File.GetLastWriteTimeUtc(prior.OutputArchive);
            var replacement=Path.Combine(Path.GetDirectoryName(prior.OutputArchive)!,
                "p6c2b3-prior-archive-"+Guid.NewGuid().ToString("N")+".tmp");
            File.WriteAllBytes(replacement,bytes);
            File.SetLastWriteTimeUtc(replacement,mtime);
            Ensure(NativeFileCapability.Identity(replacement)!=originalIdentity,
                "Predecessor archive adversarial fixture must allocate a new native file");
            File.Move(replacement,prior.OutputArchive,overwrite:true);
            Ensure(File.ReadAllBytes(prior.OutputArchive).AsSpan().SequenceEqual(bytes) &&
                File.GetLastWriteTimeUtc(prior.OutputArchive)==mtime,
                "Adversarial fixture unexpectedly changed archive contents/mtime");
            Refuse<IOException>(()=>db.PutMoveItem(f.Next));
            Ensure(db.AllMoveHistoryCount()==1 &&
                !db.InspectMoveEpochGrant().Consumed &&
                File.Exists(f.Next.Source),
                "Historical archive native replacement bypassed exact Intent ownership check");
        });
        Check("P6C2B3_SCHEMA_V9_BACKUP_PRESERVES_V8_TICKET",() =>
        {
            var f=B2TicketFixture();string state=Path.Combine(f.Root,"state");
            using(f.Db) Ensure(f.Db.StageNextMoveEpochProposal(f.Next).Verified,
                "v8 staged ticket missing");
            string path=Path.Combine(state,"workspaces-v1.sqlite");
            using(var conn=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE move_epoch_grant; DROP TABLE m6j_review_proposals; PRAGMA user_version=8;";
                cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
                Ensure(db.InspectStagedMoveEpoch().Verified &&
                    !db.InspectMoveEpochGrant().Present &&
                    db.InspectTerminalMoveCloseout().Verified &&
                    db.AllMoveHistoryCount()==1,
                    "v8 to v9 migration changed old ticket, native closeout or journal");
            Ensure(Directory.GetFiles(state,"workspaces-v1.sqlite.before-v9-*.sqlite").Length==1,
                "v9 migration must produce one SQLite-consistent backup");
        });
        // P6C2-B4A: GUI successor binding does NOT repeal P6C2-A global history.
        // Native first-Move Undo / restored checkpoint are recreated per test.
        Check("P6C2B4A_RESTARTED_GUI_REVISION_CAS_ACCEPTS_ONLY_RESTORATION",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();
                var reloaded=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                reloaded.RestoreSession(db.Load()!);
                var onScreen=reloaded.CaptureSession();
                Ensure(onScreen.Revision==saved.Revision+1 &&
                    onScreen.SessionId==saved.SessionId,
                    "Restart setup must prove the real one-revision approval invalidation");
                var next=db.BeginFirstGuiSuccessor(onScreen);
                Ensure(db.InspectGuiSuccessor(next).Verified &&
                    db.AllMoveHistoryCount()==1 && db.Load()!.SessionId==next.SessionId,
                    "Real GUI restart lost predecessor binding or the atomic session transition");
            });
        });
        Check("P6C2B4A_UNSAVED_GUI_SELECTION_AND_REVISION_DRIFT_DENIED",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();
                var reloaded=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                reloaded.RestoreSession(db.Load()!);
                var onScreen=reloaded.CaptureSession();
                // A valid session ID alone, or a fabricated revision jump, is
                // not an authorization to replace the durable checkpoint.
                var changedCursor=onScreen with {Index=onScreen.Index==0?1:0};
                Refuse<IOException>(()=>db.BeginFirstGuiSuccessor(changedCursor));
                Refuse<IOException>(()=>db.BeginFirstGuiSuccessor(onScreen with {
                    Revision=checked(onScreen.Revision+1)}));
                Ensure(db.Load()!.SessionId==saved.SessionId &&
                    !db.InspectGuiSuccessor(saved).Present &&
                    db.AllMoveHistoryCount()==1 &&
                    db.MoveItems(old.SessionId).Single().State=="Undone",
                    "Rejected GUI mismatch changed checkpoint or immutable Move history");
                var next=db.BeginFirstGuiSuccessor(onScreen);
                Ensure(db.InspectGuiSuccessor(next).Verified,
                    "A refused changed GUI selection poisoned the intact checkpoint");
            });
        });
        Check("P6C2B4A_ATOMIC_CHECKPOINT_PRESERVES_PREDECESSOR",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                Ensure(db.SealTerminalMoveHistory().Verified,"Missing verified B1 history closeout");
                var next=db.BeginFirstGuiSuccessor(saved);
                var evidence=db.InspectGuiSuccessor(next);
                Ensure(evidence.Verified && evidence.SessionId==next.SessionId &&
                    db.Load()!.SessionId==next.SessionId && old.SessionId!=next.SessionId &&
                    next.Photos.Length==1 && Path.GetFileName(next.Photos[0].Path)=="photo_02.png" &&
                    next.Assignments.Length==0 && db.AllMoveHistoryCount()==1 &&
                    File.Exists(Path.Combine(root,"source","photo_01.png")) &&
                    File.Exists(Path.Combine(root,"source","photo_02.png")),
                    "New successor session discarded or bypassed first Move evidence");
            });
        });
        Check("P6C2B4A_SESSION_CHANGE_ALONE_DENIES_SQL_INTENT",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();var next=db.BeginFirstGuiSuccessor(saved);
                var work=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                work.RestoreSession(next);var f=PrepareB4Candidate(db,work);
                Refuse<IOException>(()=>db.PutMoveItem(f.Ticket));
                Ensure(db.AllMoveHistoryCount()==1 &&
                    !db.InspectMoveEpochGrant().Present &&
                    db.InspectGuiSuccessor(work.CaptureSession()).Verified,
                    "Fresh session incorrectly became a Move Intent capability");
            });
        });
        Check("P6C2B4A_GUI_SUCCESSOR_REVIEW_GRANT_MOVE_UNDO",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();var next=db.BeginFirstGuiSuccessor(saved);
                var work=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                work.RestoreSession(next);var f=PrepareB4Candidate(db,work);
                Ensure(db.StageNextMoveEpochProposal(f.Ticket).Verified,"B2 successor ticket did not stage");
                var mover=new DurableMoveCoordinator(db);
                var prep=mover.PrepareApprovedStagedSuccessorAsync(work,f.Plan,f.Ticket)
                    .GetAwaiter().GetResult();
                mover.ApproveStagedSuccessor(work,prep,f.Ticket,true);
                var moved=mover.ExecuteApprovedStagedSuccessorAsync(work,prep,f.Ticket)
                    .GetAwaiter().GetResult();
                Ensure(moved.State=="Committed" && db.InspectMoveEpochGrant().Consumed &&
                    db.AllMoveHistoryCount()==2 && db.InspectGuiSuccessor(work.CaptureSession()).Verified,
                    "B4 current session lost predecessor binding after B3 Move");
                work.RetireCommitted(new[]{moved.PhotoId});db.Save(work.CaptureSession());
                var reloaded=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                reloaded.RestoreSession(db.Load()!);
                Ensure(db.InspectGuiSuccessor(reloaded.CaptureSession()).Verified &&
                    db.MoveItems(reloaded.SessionId).Count==1,"Restart projection lost successor ownership");
                var undone=mover.UndoAsync(reloaded.SessionId).GetAwaiter().GetResult();
                reloaded.RestoreUndone(undone.PhotoId,'F');db.Save(reloaded.CaptureSession());
                Ensure(undone.State=="Undone" && db.InspectGuiSuccessor(reloaded.CaptureSession()).Verified &&
                    db.MoveItems(old.SessionId).Single().State=="Undone" &&
                    db.MoveItems(next.SessionId).Single().State=="Undone" &&
                    db.AllMoveHistoryCount()==2 && File.Exists(undone.OutputArchive) &&
                    File.Exists(undone.Source) && !File.Exists(undone.Destination) &&
                    File.Exists(Path.Combine(root,"source","photo_01.png")),
                    "Second Move Undo lost an original, archive or historical journal");
            });
        });
        Check("P6C2B4A_DUPLICATE_EPOCH_TRANSITION_REFUSED",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();var next=db.BeginFirstGuiSuccessor(saved);
                Refuse<IOException>(()=>db.BeginFirstGuiSuccessor(saved));
                Ensure(db.InspectGuiSuccessor(next).Verified && db.AllMoveHistoryCount()==1 &&
                    db.Load()!.SessionId==next.SessionId,
                    "Duplicate transition changed immutable journal or checkpoint");
            });
        });
        Check("P6C2B4A_REPLACED_PHOTO02_REVOKES_SESSION",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();var next=db.BeginFirstGuiSuccessor(saved);
                string original=next.Photos[0].Path;
                string replacement=Path.Combine(root,"source",".b4-different-existing-file");
                File.Copy(original,replacement);
                File.SetLastWriteTimeUtc(replacement,new DateTime(next.Photos[0].LastWriteUtcTicks,DateTimeKind.Utc));
                string otherId=NativeFileCapability.Identity(replacement);
                Ensure(otherId!=next.Photos[0].NativeIdentity,"Adversarial photo replacement fixture duplicated native identity");
                File.Move(replacement,original,overwrite:true);
                Ensure(!db.InspectGuiSuccessor(next).Verified &&
                    File.Exists(original) && db.AllMoveHistoryCount()==1,
                    "Different inode/FileId with same bytes and mtime did not revoke transition");
            });
        });
        Check("P6C2B4A_MISSING_FIXTURE_ENV_REFUSES_TRANSITION",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,null);
                try {Refuse<IOException>(()=>db.BeginFirstGuiSuccessor(saved));}
                finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);}
                Ensure(db.Load()!.SessionId==saved.SessionId &&
                    db.AllMoveHistoryCount()==1 && !db.InspectGuiSuccessor(saved).Present,
                    "Unmarked workspace created a successor session");
            });
        });
        Check("P6C2B4A_SQLITE_V10_BACKUP_PRESERVES_V9_JOURNAL",() =>
        {
            string? state=null;Guid priorId=Guid.Empty;
            WithB4GuiFixture((db,old,saved,root)=>
            {
                state=Path.Combine(root,"state");priorId=old.SessionId;
                Ensure(db.SealTerminalMoveHistory().Verified,"Expected v9 terminal seal before upgrade test");
            });
            string file=Path.Combine(state!,"workspaces-v1.sqlite");
            using(var conn=new SqliteConnection($"Data Source={file};Pooling=False"))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE move_gui_successor; DROP TABLE m6j_review_proposals; PRAGMA user_version=9;";
                cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                Ensure(db.Load()!.SessionId==priorId && db.MoveItems(priorId).Count==1 &&
                    db.MoveItems(priorId)[0].State=="Undone" &&
                    db.InspectTerminalMoveCloseout().Verified &&
                    !db.InspectGuiSuccessor(db.Load()!).Present,
                    "v9 to v10 migration lost an existing Move journal or predecessor checkpoint");
            }
            Ensure(Directory.GetFiles(state!,"workspaces-v1.sqlite.before-v10-*.sqlite").Length==1,
                "v10 migration did not leave one SQLite-consistent v9 backup");
        });
        // P6C2-B4B0: schema v11 is an append-only receipt ledger ONLY.
        // Its existence must NEVER become Move execution authorization.
        Check("P6C2B4B0_NATIVE_TERMINAL_LEDGER_SEALS_AND_IS_IDEMPOTENT",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                var first=db.SealMoveEpochLedger();
                Ensure(first.Verified && first.SealedRows==1 && first.MoveRows==1 &&
                    db.InspectMoveEpochLedger().Verified,
                    "First natively undone Move did not produce a verified terminal receipt");
                var again=db.SealMoveEpochLedger();
                Ensure(again.Verified && again.SealedRows==1 && again.MoveRows==1 &&
                    !db.InspectMoveEpochGrant().Present &&
                    !db.InspectStagedMoveEpoch().Present,
                    "Idempotent terminal sealing minted Move approval or duplicate records");
                Refuse<IOException>(()=>db.RequireFreshMoveEpoch());
                Ensure(db.MoveItems(old.SessionId).Single().State=="Undone" &&
                    File.Exists(Path.Combine(root,"source","photo_01.png")),
                    "Receipt sealing destroyed immutable historical recovery");
            });
        });
        Check("P6C2B4B0_TERMINAL_PREFIX_APPEND_REJECTS_ACTIVE_SUCCESSOR",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                Ensure(db.SealMoveEpochLedger().Verified,"Initial ledger seal missing");
                db.SealTerminalMoveHistory();
                var next=db.BeginFirstGuiSuccessor(saved);
                var work=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                work.RestoreSession(next);
                var f=PrepareB4Candidate(db,work);
                db.StageNextMoveEpochProposal(f.Ticket);
                var mover=new DurableMoveCoordinator(db);
                var prep=mover.PrepareApprovedStagedSuccessorAsync(work,f.Plan,f.Ticket)
                    .GetAwaiter().GetResult();
                mover.ApproveStagedSuccessor(work,prep,f.Ticket,true);
                var moved=mover.ExecuteApprovedStagedSuccessorAsync(work,prep,f.Ticket)
                    .GetAwaiter().GetResult();
                Ensure(moved.State=="Committed" && db.AllMoveHistoryCount()==2,
                    "The B4A second Move did not commit");
                var interim=db.InspectMoveEpochLedger();
                Ensure(interim.Verified && interim.SealedRows==1 && interim.MoveRows==2,
                    "Active Move must NOT mutate completed predecessor receipt");
                Refuse<IOException>(()=>db.SealMoveEpochLedger());
                var still=db.InspectMoveEpochLedger();
                Ensure(still.Verified && still.SealedRows==1 && still.MoveRows==2,
                    "Refused seal changed terminal prefix");
                work.RetireCommitted(new[]{moved.PhotoId});
                db.Save(work.CaptureSession());
                var undone=mover.UndoAsync(next.SessionId).GetAwaiter().GetResult();
                work.RestoreUndone(undone.PhotoId,'F');
                db.Save(work.CaptureSession());
                var completed=db.SealMoveEpochLedger();
                Ensure(undone.State=="Undone" && completed.Verified &&
                    completed.SealedRows==2 && completed.MoveRows==2 &&
                    db.MoveItems(old.SessionId).Single().State=="Undone" &&
                    db.MoveItems(next.SessionId).Single().State=="Undone",
                    "Second terminal Move receipt failed to append without resetting old history");
                var stable=db.SealMoveEpochLedger();
                Ensure(stable.Verified && stable.SealedRows==2 && stable.MoveRows==2,
                    "Completed two-Move epoch chain is not idempotent");
            });
        });
        Check("P6C2B4B0_REPLACED_NATIVE_ARCHIVE_REVOKES_LEDGER",() =>
        {
            WithB4GuiFixture((db,old,saved,root)=>
            {
                Ensure(db.SealMoveEpochLedger().Verified,"Native receipt unavailable");
                var entry=db.MoveItems(old.SessionId).Single();
                string temp=Path.Combine(Path.GetDirectoryName(entry.OutputArchive)!,".replacement-"+
                    Guid.NewGuid().ToString("N"));
                File.Copy(entry.OutputArchive,temp);
                File.Move(temp,entry.OutputArchive,overwrite:true);
                Ensure(!db.InspectMoveEpochLedger().Verified,
                    "Same-byte replacement of native Undo archive retained ledger clearance");
                Refuse<IOException>(()=>db.SealMoveEpochLedger());
            });
        });
        Check("P6C2B4B0_SQLITE_V11_BACKUP_PRESERVES_V10_SUCCESSOR",() =>
        {
            string? state=null; Guid first=Guid.Empty; WorkspaceSessionSnapshot? next=null;
            WithB4GuiFixture((db,old,saved,root)=>
            {
                db.SealTerminalMoveHistory();
                next=db.BeginFirstGuiSuccessor(saved);
                first=old.SessionId;
                state=Path.Combine(root,"state");
            });
            string path=Path.Combine(state!,"workspaces-v1.sqlite");
            // Construct a genuine v10-shaped database in place for migration:
            // legacy v8/v9/v10 tables and content remain intact.
            using(var connection=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();using var cmd=connection.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE move_epoch_ledger; DROP TABLE m6j_review_proposals; PRAGMA user_version=10;";
                cmd.ExecuteNonQuery();
            }
            // WithB4GuiFixture deliberately restores the previous environment
            // on exit. Re-establish ONLY its exact generated-fixture marker
            // while verifying the migrated successor. Without it, the existing
            // production IsMarkedStore guard correctly refuses the inspection;
            // that denial is NOT evidence of a damaged v11 migration.
            string? priorFixtureRoot=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            try
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,
                    Directory.GetParent(state!)!.FullName);
                using(var db=new WorkspaceSessionStore(state))
                {
                    var loaded=db.Load();
                    Ensure(loaded?.SessionId==next!.SessionId,
                        "v11 migration changed B4A's persisted session checkpoint");
                    var binding=db.InspectGuiSuccessor(next!);
                    Ensure(binding.Verified,
                        "v11 migration could not verify B4A successor: "+binding.Reason);
                    Ensure(db.MoveItems(first).Single().State=="Undone" &&
                        db.InspectTerminalMoveCloseout().Verified &&
                        !db.InspectMoveEpochLedger().Present,
                        "v11 migration altered predecessor, closeout or receipt absence");
                    var sealedHistory=db.SealMoveEpochLedger();
                    Ensure(sealedHistory.Verified && sealedHistory.SealedRows==1 &&
                        sealedHistory.MoveRows==1,
                        "Migrated v10 journal could not be independently native-sealed");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,priorFixtureRoot);
            }
            var copies=Directory.GetFiles(state!,"workspaces-v1.sqlite.before-v11-*.sqlite");
            Ensure(copies.Length>=1,"v11 migration did not create a SQLite-consistent backup");
            using(var prior=new SqliteConnection($"Data Source={copies.Last()};Mode=ReadOnly;Pooling=False"))
            {
                prior.Open();using var cmd=prior.CreateCommand();
                cmd.CommandText="PRAGMA user_version;";
                Ensure(Convert.ToInt32(cmd.ExecuteScalar())==10,"v11 backup not the prior v10 schema");
                cmd.CommandText="SELECT COUNT(*) FROM move_gui_successor";
                Ensure(Convert.ToInt32(cmd.ExecuteScalar())==1,
                    "v11 pre-migration backup lost the existing B4A successor receipt");
            }
        });
        // B4B1A: v12 immutable ticket + explicitly approved SINGLE USE grant
        // for the third distinct generated fixture Move. No GUI authorization.
        Check("P6C2B4B1A_TICKET_STAGED_WITHOUT_INTENT_AUTHORITY",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                Refuse<IOException>(()=>db.PutMoveItem(row));
                var ticket=db.StageAdditionalEpochAuthorization(row);
                Ensure(ticket.Verified && !ticket.Approved && !ticket.Consumed &&
                    ticket.Epoch==3 && db.AllMoveHistoryCount()==2,
                    "Staging did not preserve two predecessor journals");
                Refuse<IOException>(()=>db.PutMoveItem(row));
                Refuse<IOException>(()=>db.ApproveAdditionalEpochAuthorization(row,false));
                Ensure(db.InspectAdditionalEpochAuthorization(row) is
                    {Verified:true,Approved:false,Consumed:false},
                    "Proposal alone or refused checkbox produced an executable grant");
            });
        });
        Check("P6C2B4B1A_IMMUTABLE_TICKET_DENIES_RETARGET_AND_REUSE",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                db.StageAdditionalEpochAuthorization(row);
                var changed=row with {Destination=Path.Combine(root,"output","Other","photo_03.png")};
                Refuse<IOException>(()=>db.StageAdditionalEpochAuthorization(changed));
                var approved=db.ApproveAdditionalEpochAuthorization(row,true);
                Ensure(approved.Verified && approved.Approved,
                    "Explicitly approved third epoch did not survive inspection");
                Refuse<IOException>(()=>db.ApproveAdditionalEpochAuthorization(row,true));
                Refuse<IOException>(()=>db.StageAdditionalEpochAuthorization(row));
                Ensure(db.AllMoveHistoryCount()==2 && File.Exists(row.Source),
                    "Retarget or duplicate approval silently affected source or journal");
            });
        });
        Check("P6C2B4B1A_THIRD_NATIVE_MOVE_RESTART_UNDO_ONE_GRANT",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                db.StageAdditionalEpochAuthorization(row);
                var review=work.GetApprovedPlan();
                var prepared=mover.PrepareApprovedAdditionalEpochAsync(work,review,row)
                    .GetAwaiter().GetResult();
                Refuse<IOException>(()=>db.PutMoveItem(row));
                mover.ApproveAdditionalEpoch(work,prepared,row,true);
                var moved=mover.ExecuteApprovedAdditionalEpochAsync(work,prepared,row)
                    .GetAwaiter().GetResult();
                Ensure(moved.State=="Committed" && db.AllMoveHistoryCount()==3 &&
                    db.InspectAdditionalEpochAuthorization(row).Consumed &&
                    !File.Exists(row.Source),
                    "Third generated Move did not consume its exact grant");
                Refuse<IOException>(()=>db.PutMoveItem(row with {Id=Guid.NewGuid()}));
                work.RetireCommitted(new[]{moved.PhotoId});db.Save(work.CaptureSession());
                var undone=mover.UndoAsync(work.SessionId).GetAwaiter().GetResult();
                work.RestoreUndone(undone.PhotoId,'F');db.Save(work.CaptureSession());
                Ensure(undone.State=="Undone" && db.AllMoveHistoryCount()==3 &&
                    db.SealMoveEpochLedger() is {Verified:true,SealedRows:3,MoveRows:3} &&
                    File.Exists(row.Source),
                    "Third Move Undo failed to restore source and append terminal chain");
            });
        });
        Check("P6C2B4B1A_REPLACED_PREDECESSOR_REVOKES_INTENT",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                db.StageAdditionalEpochAuthorization(row);
                db.ApproveAdditionalEpochAuthorization(row,true);
                var old=db.MoveItems(db.Load()!.SessionId);
                Ensure(old.Count==0,"Third session should have no existing Move");
                var first=Directory.GetFiles(Path.Combine(root,"source",
                    ".photosift-reborn-recovery"),"installed-photo_01.png",
                    SearchOption.AllDirectories).Single();
                string replacement=first+".altered";
                File.Copy(first,replacement);
                File.Move(replacement,first,overwrite:true);
                Refuse<IOException>(()=>db.PutMoveItem(row));
                Ensure(db.AllMoveHistoryCount()==2 && File.Exists(row.Source) &&
                    !db.InspectAdditionalEpochAuthorization(row).Verified,
                    "Same-byte predecessor archive replacement bypassed native proof");
            });
        });
        Check("P6C2B4B1A_SCHEMA_V12_BACKUP_PRESERVES_V11_LEDGER",() =>
        {
            string? state=null; string? expectedHead=null;
            WithB4GuiFixture((db,old,saved,root)=>
            {
                var sealedEvidence=db.SealMoveEpochLedger();
                Ensure(sealedEvidence.Verified && sealedEvidence.SealedRows==1,
                    "Native v11 ledger fixture not sealed");
                state=Path.Combine(root,"state");
                // Preserve original fixture session identity for migration proof.
                expectedHead=old.SessionId.ToString("N");
            });
            string path=Path.Combine(state!,"workspaces-v1.sqlite");
            using(var conn=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE move_epoch_authorization; DROP TABLE m6j_review_proposals; PRAGMA user_version=11;";
                cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                Ensure(db.InspectMoveEpochLedger() is {Verified:true,SealedRows:1} &&
                    db.AllMoveHistoryCount()==1 && db.MoveItems(Guid.ParseExact(expectedHead!,"N"))
                    .Single().State=="Undone",
                    "v12 migration modified older Move history or terminal ledger");
            }
            var backups=Directory.GetFiles(state!,"workspaces-v1.sqlite.before-v12-*.sqlite");
            Ensure(backups.Length>=1,"v11 to v12 migration produced no consistent backup");
            using var previous=new SqliteConnection($"Data Source={backups.Last()};Mode=ReadOnly;Pooling=False");
            previous.Open();using var check=previous.CreateCommand();
            check.CommandText="PRAGMA user_version;";
            Ensure(Convert.ToInt32(check.ExecuteScalar())==11,"v12 backup is not the old schema");
            check.CommandText="SELECT COUNT(*) FROM move_epoch_ledger";
            Ensure(Convert.ToInt32(check.ExecuteScalar())==1,"v12 backup lost linked native receipt");
        });
        Check("P6C2B4B1B_ATOMIC_GUI_CHECKPOINT_AND_RESTART",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                var saved=db.Load()!;
                var fromDisk=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                fromDisk.RestoreSession(saved);
                Ensure(db.InspectThirdGuiSession(saved) is {Present:true,Verified:true} &&
                    db.InspectThirdGuiSession(fromDisk.CaptureSession()).Verified &&
                    db.AllMoveHistoryCount()==2 && db.MoveItems(saved.SessionId).Count==0,
                    "Third GUI checkpoint did not survive read-only restore with both prior Moves");
                Refuse<IOException>(()=>db.BeginThirdGuiSession(work.CaptureSession()));
                Ensure(db.Load()!.SessionId==saved.SessionId && db.AllMoveHistoryCount()==2,
                    "Duplicate transition modified immutable checkpoint or history");
            });
        });
        Check("P6C2B4B1B_THIRD_GUI_BINDING_REJECTS_SESSION_TAMPER",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                var original=db.Load()!;
                var changed=original with { SessionId=Guid.NewGuid() };
                Ensure(!db.InspectThirdGuiSession(changed).Verified,
                    "Foreign third session id passed immutable GUI binding");
                changed=original with { Photos=Array.Empty<Photo>() };
                Ensure(!db.InspectThirdGuiSession(changed).Verified,
                    "Missing third photograph passed exact fixture binding");
                changed=original with { CollisionMode=CollisionChoice.Number };
                Ensure(!db.InspectThirdGuiSession(changed).Verified,
                    "Unsafe collision mode passed exact third GUI binding");
                Ensure(db.InspectThirdGuiSession(original).Verified &&
                    File.Exists(Path.Combine(root,"source","photo_03.png")),
                    "Read-only tamper probes unexpectedly altered owned source");
            });
        });
        Check("P6C2B4B1B_THIRD_GUI_NATIVE_REPLACEMENT_REVOKES_BINDING",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                var saved=db.Load()!;
                Ensure(db.InspectThirdGuiSession(saved).Verified,
                    "Third fixture should initially be natively verified");
                var photo=Path.Combine(root,"source","photo_03.png");
                var replacement=photo+".other";
                File.Copy(photo,replacement);
                File.Move(replacement,photo,overwrite:true);
                Ensure(!db.InspectThirdGuiSession(saved).Verified &&
                    db.AllMoveHistoryCount()==2,
                    "Same-byte new native photo03 identity bypassed third GUI inspection");
            });
        });
        // P6C2-B4B1B-G: A GUI restart after the third COMMITTED Move must
        // permit the one legitimate native Undo. The first two terminal
        // predecessors remain proven by immutable, native-checked receipts;
        // the un-undone third row is NOT a terminal predecessor.
        Check("P6C2B4B1B_THIRD_COMMITTED_RESTART_PRESERVES_UNDO",() =>
        {
            WithThirdEpochFixture((db,mover,work,row,root)=>
            {
                db.StageAdditionalEpochAuthorization(row);
                var review=work.GetApprovedPlan();
                var prepared=mover.PrepareApprovedAdditionalEpochAsync(work,review,row)
                    .GetAwaiter().GetResult();
                mover.ApproveAdditionalEpoch(work,prepared,row,true);
                var moved=mover.ExecuteApprovedAdditionalEpochAsync(work,prepared,row)
                    .GetAwaiter().GetResult();
                Ensure(moved.State=="Committed" && db.AllMoveHistoryCount()==3 &&
                    db.InspectMoveEpochLedger() is {Verified:true,SealedRows:2,MoveRows:3},
                    "Committed third Move damaged sealed predecessor receipts");
                work.RetireCommitted(new[]{moved.PhotoId});
                db.Save(work.CaptureSession());
                var restarted=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                restarted.RestoreSession(db.Load()!);
                Ensure(db.InspectThirdGuiSession(restarted.CaptureSession()) is
                    {Present:true,Verified:true} &&
                    db.MoveItems(restarted.SessionId) is {Count:1} active &&
                    active[0].State=="Committed" && !File.Exists(row.Source),
                    "Third committed Move must keep exact GUI restart+Undo eligibility");
                var undone=mover.UndoAsync(restarted.SessionId).GetAwaiter().GetResult();
                restarted.RestoreUndone(undone.PhotoId,'F');
                db.Save(restarted.CaptureSession());
                Ensure(db.SealMoveEpochLedger() is {Verified:true,SealedRows:3,MoveRows:3} &&
                    db.InspectThirdGuiSession(restarted.CaptureSession()).Verified &&
                    File.Exists(row.Source) && !File.Exists(row.Destination),
                    "Third verified Undo did not restore source and third sealed receipt");
            });
        });
        Check("P6C2B4B1B_SQLITE_V13_BACKUP_PRESERVES_V12_MOVE_HISTORY",() =>
        {
            string? state=null;Guid priorSession=Guid.Empty;
            WithB4GuiFixture((db,first,saved,root)=>
            {
                state=Path.Combine(root,"state");
                priorSession=saved.SessionId;
                Ensure(db.SealMoveEpochLedger().Verified,"Native v12 Move receipt missing");
            });
            var path=Path.Combine(state!,"workspaces-v1.sqlite");
            using(var conn=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE move_gui_third_session; DROP TABLE m6j_review_proposals; PRAGMA user_version=12;";
                cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                Ensure(db.MoveItems(priorSession).Single().State=="Undone" &&
                    db.InspectMoveEpochLedger() is {Verified:true,SealedRows:1} &&
                    db.Load()!.SessionId==priorSession &&
                    !db.InspectThirdGuiSession(db.Load()!).Present,
                    "v13 migration corrupted v12 Move receipts or invents a GUI transition");
            }
            var backups=Directory.GetFiles(state!,"workspaces-v1.sqlite.before-v13-*.sqlite");
            Ensure(backups.Length>0,"v12 to v13 migration did not preserve SQLite backup");
            using var copy=new SqliteConnection($"Data Source={backups.Last()};Mode=ReadOnly;Pooling=False");
            copy.Open();using var check=copy.CreateCommand();
            check.CommandText="PRAGMA user_version;";
            Ensure(Convert.ToInt32(check.ExecuteScalar())==12,"v13 backup is not original schema v12");
            check.CommandText="SELECT COUNT(*) FROM move_epoch_ledger";
            Ensure(Convert.ToInt32(check.ExecuteScalar())==1,"v13 backup lost immutable native receipt");
        });
        // P6D1: Reject-review is ONLY a persisted, reversible triage decision.
        // No file mutation, archive ownership, journal grant or Trash integration.
        Check("P6D1_REJECT_MARK_IS_NONDESTRUCTIVE_AND_UNDOABLE",()=>{
            string t=Root();var w=Create(t);var p=w.Current!;
            string before=SortingWorkspace.ComputeSha256Async(p.Path).GetAwaiter().GetResult();
            long prior=w.Revision;
            w.MarkCurrentForRejectReview();
            Ensure(w.PendingRejectCount==1 && w.IsPendingReject(p.Id) &&
                w.AssignmentCount==0 && w.Index==1 && w.Revision>prior,
                "Reject decision was not safely staged");
            Ensure(File.Exists(p.Path) &&
                SortingWorkspace.ComputeSha256Async(p.Path).GetAwaiter().GetResult()==before &&
                !Directory.Exists(Path.Combine(t,"source",".photosift-reborn-recovery")),
                "Reject-review marked a photograph by touching files/recovery");
            w.UndoLastRejectReview();
            Ensure(w.PendingRejectCount==0 && w.Index==0 && w.Current!.Id==p.Id &&
                !w.IsPendingReject(p.Id) && File.Exists(p.Path),
                "Reject Undo did not restore the exact staged choice");
        });
        Check("P6D1_REJECT_RESTART_PERSISTS_AND_SKIPS_NAVIGATION",()=>{
            string t=Root();var w=Create(t);var first=w.Current!;
            w.MarkCurrentForRejectReview();
            using(var db=new WorkspaceSessionStore(Path.Combine(t,"state"))) db.Save(w.CaptureSession());
            using(var db=new WorkspaceSessionStore(Path.Combine(t,"state"))){
                var prior=db.Load()!;var restored=new SortingWorkspace();
                restored.RestoreSession(prior);
                Ensure(restored.PendingRejectCount==1 && restored.IsPendingReject(first.Id) &&
                    restored.Current?.Id!=first.Id,"Reject review lost on SQLite restart");
                restored.JumpToPhoto(restored.Photos.Count);
                Ensure(restored.JumpToNextUnassigned() && restored.Current!.Id!=first.Id,
                    "Next unassigned tried to sort a Reject-review photo");
                restored.UndoLastRejectReview();
                Ensure(restored.Current!.Id==first.Id && restored.PendingRejectCount==0,
                    "Reject-review LIFO Undo lost identity after restart");
                var legacy=restored.CaptureSession() with {Schema=3,PendingRejectIds=null,RemovedPhotos=null};
                var old=new SortingWorkspace();old.RestoreSession(legacy);
                Ensure(old.PendingRejectCount==0,"Legacy snapshot invented reject history");
            }
        });
        Check("P6D1_REJECT_REFUSES_ASSIGNED_OR_TAMPERED_SNAPSHOT",()=>{
            string t=Root();var w=Create(t);var first=w.Current!;
            w.Assign('F');w.JumpToPhoto(1);
            Refuse<InvalidOperationException>(()=>w.MarkCurrentForRejectReview());
            w.ClearAssignments();w.MarkCurrentForRejectReview();w.JumpToPhoto(1);
            Refuse<InvalidOperationException>(()=>w.Assign('F'));
            Refuse<InvalidOperationException>(()=>w.RemoveCurrentFromSession());
            var current=w.CaptureSession();
            var other=new SortingWorkspace();
            Refuse<InvalidDataException>(()=>other.RestoreSession(current with
                {PendingRejectIds=new[]{first.Id,first.Id}}));
            Refuse<InvalidDataException>(()=>other.RestoreSession(current with
                {PendingRejectIds=new[]{Guid.NewGuid()}}));
            Refuse<InvalidDataException>(()=>other.RestoreSession(current with
                {Assignments=new[]{new SavedAssignment(first.Id,'F')}}));
            Ensure(other.PendingRejectCount==0 && other.Photos.Count==0,
                "Rejected corrupt snapshot partially modified restored workspace");
        });
        Check("P6D1_REJECT_NEVER_ENTERS_COPY_PLAN",()=>{
            string t=Root();var w=Create(t);
            w.JumpToPhoto(3);var rejected=w.Current!;
            w.MarkCurrentForRejectReview();
            w.JumpToPhoto(1);var copied=w.Current!;
            w.Assign('F');
            var plan=w.ReviewPlan();
            Ensure(plan.Count==1 &&
                SortingWorkspace.PathComparer.Equals(plan[0].Source,copied.Path) &&
                !SortingWorkspace.PathComparer.Equals(plan[0].Source,rejected.Path) &&
                w.PendingRejectCount==1,"Reject review leaked into copy destination plan");
            w.ClearAssignments();
            Ensure(w.PendingRejectCount==1,"Clearing copy choices removed unrelated Reject-review mark");
            w.UndoLastRejectReview();
            Ensure(w.Current!.Id==rejected.Id && w.PendingRejectCount==0,
                "Reject-review undo after route staging lost stable photo identity");
        });
        Check("P6D1_REJECT_MARK_REVOKES_APPROVED_COPY_PLAN",()=>{
            string t=Root();var w=Create(t);
            w.Assign('F');
            var reviewed=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.IsCurrentPlanApproved && reviewed.Count==1,
                "Disposable fixture Copy approval setup did not succeed");
            w.JumpToPhoto(3);w.MarkCurrentForRejectReview();
            Ensure(!w.IsCurrentPlanApproved && w.CurrentPlanId is null,
                "Reject review failed to invalidate an existing Copy approval");
            w.UndoLastRejectReview();
            Ensure(!w.IsCurrentPlanApproved && w.PendingRejectCount==0,
                "Undo Reject incorrectly resurrected stale Copy approval");
        });
        // P6D2A: actual native Reject relocation to a separately owned archive,
        // never Copy/Move history, never deletion, no automatic interruption replay.
        Check("P6D2A_STAGED_REJECT_IS_NONDESTRUCTIVE_AND_NOT_A_GRANT",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var reject=new DurableRejectCoordinator(db);
                var staged=reject.Stage(work,photo.Id);
                Ensure(staged.State=="Staged" && db.AllRejectHistoryCount()==1 &&
                    File.Exists(photo.Path) && !File.Exists(staged.Archive) &&
                    !Directory.Exists(Path.GetDirectoryName(staged.Archive)!),
                    "Staging already relocated a source or produced an archive");
                Refuse<IOException>(()=>reject.Approve(staged,false));
                Refuse<IOException>(()=>reject.Execute(staged));
                Ensure(reject.Inspect().Evidence=="UNTOUCHED_SOURCE_VERIFIED",
                    "Read-only native Reject staged inspection not proven");
            });
        });
        Check("P6D2A_EXPLICIT_APPROVAL_IMMUTABLE_TICKET_AND_NO_REUSE",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db);
                var staged=engine.Stage(work,photo.Id);
                Refuse<IOException>(()=>db.PutRejectItem(staged with
                    {Archive=staged.Archive+"-retarget",State="Approved"}));
                var approved=engine.Approve(staged,true);
                Ensure(approved.State=="Approved" && File.Exists(photo.Path),
                    "Approval mutated an original file");
                Refuse<IOException>(()=>engine.Approve(staged,true));
                Refuse<IOException>(()=>engine.Execute(staged));
                Refuse<IOException>(()=>engine.Stage(work,photo.Id));
                Ensure(db.RejectItems().Single().State=="Approved",
                    "Reject explicit grant changed after refused repeat");
            });
        });
        Check("P6D2A_NATIVE_REJECT_COMMIT_RESTART_UNDO_SEPARATE_JOURNAL",()=>{
            string root=Root();
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            var previous=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
            try
            {
                var work=RenameFixture(root);var photo=work.Current!;
                var initial=SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult();
                work.MarkCurrentForRejectReview();
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(work.CaptureSession());
                    var engine=new DurableRejectCoordinator(db);
                    var approved=engine.Approve(engine.Stage(work,photo.Id),true);
                    var committed=engine.Execute(approved);
                    Ensure(committed.State=="Committed" && !File.Exists(photo.Path) &&
                        File.Exists(committed.Archive) && db.CopyItems(work.SessionId).Count==0 &&
                        db.MoveItems(work.SessionId).Count==0 &&
                        db.RenameItems(work.SessionId).Count==0 &&
                        engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                        "Native Reject commit or separate SQLite journal failed");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    var engine=new DurableRejectCoordinator(db);
                    Ensure(engine.Inspect() is {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"},
                        "Restart did not preserve committed original Reject archive");
                    var restored=engine.Undo(work.SessionId);
                    Ensure(restored.State=="Undone" && File.Exists(photo.Path) &&
                        !File.Exists(restored.Archive) &&
                        SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult()==initial &&
                        engine.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED",
                        "Native Undo lost original bytes or archive identity");
                    Refuse<IOException>(()=>engine.Undo(work.SessionId));
                    Ensure(db.AllRejectHistoryCount()==1,"Undo silently erased the historical Reject row");
                }
            }
            finally{Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,previous);}
        });
        Check("P6D2A_REPLACED_SAME_BYTES_SOURCE_REVOKES_APPROVAL",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db);
                var staged=engine.Stage(work,photo.Id);
                byte[] bytes=File.ReadAllBytes(photo.Path);
                var ticks=new FileInfo(photo.Path).LastWriteTimeUtc;
                var replacement=Path.Combine(root,"source",".replacement-native-"+Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(replacement,bytes);
                Ensure(NativeFileCapability.Identity(replacement)!=staged.SourceIdentity,
                    "Replacement fixture did not produce a distinct native file identity");
                File.SetLastWriteTimeUtc(replacement,ticks);
                File.Delete(photo.Path);
                NativeFileCapability.RelocateNoReplace(replacement,photo.Path);
                Refuse<IOException>(()=>engine.Approve(staged,true));
                Ensure(db.RejectItems().Single().State=="Staged" &&
                    !File.Exists(staged.Archive),"Replaced native original was accepted");
            });
        });
        Check("P6D2A_UNDO_OCCUPIED_SOURCE_PRESERVES_ARCHIVE",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db);
                var committed=engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
                File.WriteAllText(photo.Path,"external occupant do not clobber");
                Refuse<IOException>(()=>engine.Undo(work.SessionId));
                Ensure(File.ReadAllText(photo.Path)=="external occupant do not clobber" &&
                    File.Exists(committed.Archive) &&
                    db.RejectItems().Single().State=="Committed",
                    "Occupied restoration changed source/archive or advanced Undo intent");
            });
        });
        Check("P6D2A_INTERRUPTED_NATIVE_CUT_NEVER_AUTO_REPLAYS",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db,stage=>{
                    if(stage=="REJECT_ORIGINAL_RELOCATED_UNRECORDED")
                        throw new SimulatedInterruptionException(stage);
                });
                var approved=engine.Approve(engine.Stage(work,photo.Id),true);
                Refuse<SimulatedInterruptionException>(()=>engine.Execute(approved));
                var unresolved=db.RejectItems().Single();
                Ensure(unresolved.State=="RelocateIntent" && File.Exists(unresolved.Archive) &&
                    !File.Exists(photo.Path),"Interruption erased uncommitted operation evidence");
                var inspector=new DurableRejectCoordinator(db);
                Ensure(inspector.Inspect().Evidence=="UNRESOLVED_PRESERVE_ALL_NO_REPLAY",
                    "Uncertain relocation was misrepresented as completed");
                Refuse<IOException>(()=>inspector.Undo(work.SessionId));
                Refuse<IOException>(()=>inspector.Execute(approved));
                Ensure(db.RejectItems().Single().State=="RelocateIntent",
                    "Uncertain original was automatically replayed");
            });
        });
        Check("P6D2A_V14_MIGRATION_BACKUP_PRESERVES_V13_SAVED_MARK",()=>{
            string root=Root();var work=RenameFixture(root);
            var original=work.Current!;work.MarkCurrentForRejectReview();
            string state=Path.Combine(root,"state"),database=Path.Combine(state,"workspaces-v1.sqlite");
            using(var db=new WorkspaceSessionStore(state))db.Save(work.CaptureSession());
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                db.Open();using var cmd=db.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; DROP TABLE reject_successor_grants; DROP TABLE reject_items; DROP TABLE m6j_review_proposals; PRAGMA user_version=13;";cmd.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
            {
                var saved=db.Load()!;
                Ensure(saved.PendingRejectIds is {Length:1} &&
                    saved.PendingRejectIds[0]==original.Id && db.AllRejectHistoryCount()==0,
                    "v14 migration erased a saved Reject-review decision");
            }
            string backup=Directory.GetFiles(state,"*.before-v14-*.sqlite").Single();
            using(var oldDb=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=backup,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            {
                oldDb.Open();using var cmd=oldDb.CreateCommand();
                cmd.CommandText="PRAGMA user_version;";
                Ensure(Convert.ToInt32(cmd.ExecuteScalar())==13,
                    "Reject migration backup did not preserve exact prior version");
                cmd.CommandText="SELECT json FROM workspace_checkpoint WHERE id=1";
                Ensure((cmd.ExecuteScalar() as string)?.Contains(original.Id.ToString("D"))==true,
                    "Reject migration backup lost prior checkpoint");
            }
        });
        // P6D2B1: owner-observed Stage refusal after closing the GUI between
        // marking and staging. RestoreSession adds one in-memory revision but
        // intentionally does NOT save it; only the untouched persisted snapshot
        // may authorize the separate native Reject journal.
        Check("P6D2B1_RESTARTED_MARK_STAGES_WITH_PERSISTED_REVISION",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var saved=db.Load()!;
                var reopened=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                reopened.RestoreSession(saved);
                Ensure(reopened.Revision==saved.Revision+1 &&
                    reopened.PendingRejectCount==1 &&
                    reopened.IsPendingReject(photo.Id) &&
                    db.Load()!.Revision==saved.Revision,
                    "Restart setup did not preserve the exact durable mark and benign revision drift");
                var engine=new DurableRejectCoordinator(db);
                var staged=engine.Stage(reopened,photo.Id);
                Ensure(staged.State=="Staged" &&
                    staged.SessionRevision==saved.Revision &&
                    db.Load()!.Revision==saved.Revision &&
                    File.Exists(photo.Path) && !File.Exists(staged.Archive) &&
                    engine.Inspect().Evidence=="UNTOUCHED_SOURCE_VERIFIED",
                    "Restarted GUI stage failed to bind the original persisted snapshot");
                var approved=engine.Approve(staged,true);
                var committed=engine.Execute(approved);
                Ensure(committed.State=="Committed" &&
                    engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED" &&
                    !File.Exists(photo.Path),
                    "Restarted stage did not retain valid downstream native approval");
                var undone=engine.Undo(reopened.SessionId);
                Ensure(undone.State=="Undone" && File.Exists(photo.Path) &&
                    engine.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED" &&
                    db.RejectItems().Count==1,
                    "Restarted stage/approve/execute/undo failed to retain journal");
            });
        });
        Check("P6D2B1_RESTART_REJECTS_NONCANONICAL_DRIFT",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var saved=db.Load()!;
                var engine=new DurableRejectCoordinator(db);
                var stale=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                stale.RestoreSession(saved with {Revision=checked(saved.Revision+1)});
                Refuse<IOException>(()=>engine.Stage(stale,photo.Id));
                var modified=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                modified.RestoreSession(saved);
                modified.SetCollisionChoice(CollisionChoice.Number);
                Refuse<IOException>(()=>engine.Stage(modified,photo.Id));
                Ensure(db.Load()!.Revision==saved.Revision &&
                    db.AllRejectHistoryCount()==0 && File.Exists(photo.Path) &&
                    !Directory.Exists(Path.Combine(root,"source",
                        ".photosift-reborn-reject-recovery")),
                    "Changed/forged resumed workspace created native Reject history");
            });
        });
        // P6E1: multi-photo Reject-review is a real, persistent non-mutating
        // triage queue. Nothing below authorizes the single-photo native lane.
        Check("P6E1_MULTI_REJECT_QUEUE_ORDER_AND_SELECTIVE_UNMARK",()=>{
            var root=Root();var work=Create(root);
            var first=work.Current!;work.MarkCurrentForRejectReview();
            var second=work.Current!;work.MarkCurrentForRejectReview();
            var third=work.Current!;work.MarkCurrentForRejectReview();
            Ensure(work.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(
                new[]{first.Id,second.Id,third.Id}),"Reject queue did not retain marking order");
            work.UnmarkRejectReview(second.Id);
            Ensure(work.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(
                new[]{first.Id,third.Id}) && work.Index==1 &&
                !work.IsPendingReject(second.Id) && work.IsPendingReject(first.Id) &&
                work.IsPendingReject(third.Id),"Selective unmark altered unrelated queue entries");
            Ensure(Directory.EnumerateFiles(Path.Combine(root,"source"),"photo_*.png").Count()==3 &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(root,"source"),
                    ".photosift-reborn-reject-recovery").Any(),
                "Multi-photo review unexpectedly changed native originals");
        });
        Check("P6E1_MULTI_REJECT_RESTART_AND_SELECTIVE_UNMARK",()=>{
            var root=Root();var work=Create(root);
            var first=work.Current!;work.MarkCurrentForRejectReview();
            var second=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var loaded=new SortingWorkspace();loaded.RestoreSession(db.Load()!);
            Ensure(loaded.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(
                new[]{first.Id,second.Id}),"Saved multi-photo queue did not survive restart");
            loaded.UnmarkRejectReview(first.Id);
            db.Save(loaded.CaptureSession());
            var again=new SortingWorkspace();again.RestoreSession(db.Load()!);
            Ensure(again.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(new[]{second.Id}) &&
                again.PendingRejectCount==1 && again.IsPendingReject(second.Id) &&
                !again.IsPendingReject(first.Id) && db.AllRejectHistoryCount()==0,
                "Selective unmark did not persist or created a native Reject journal");
        });
        Check("P6E1_MULTI_REJECT_INVALID_UNMARK_REFUSES_UNCHANGED",()=>{
            var root=Root();var work=Create(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            var before=System.Text.Json.JsonSerializer.Serialize(work.CaptureSession());
            Refuse<InvalidOperationException>(()=>work.UnmarkRejectReview(Guid.Empty));
            Refuse<InvalidOperationException>(()=>work.UnmarkRejectReview(Guid.NewGuid()));
            Ensure(before==System.Text.Json.JsonSerializer.Serialize(work.CaptureSession()) &&
                work.IsPendingReject(photo.Id),"Rejected unmark changed the saved workspace");
        });
        Check("P6E1_MULTI_REJECT_SELECTIVE_UNMARK_REVOKES_COPY_APPROVAL",()=>{
            var root=Root();var work=Create(root);
            var first=work.Current!;work.MarkCurrentForRejectReview();
            var second=work.Current!;work.MarkCurrentForRejectReview();
            work.Assign('F');
            var plan=work.ReviewPlan();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            Ensure(work.IsCurrentPlanApproved && plan.Count==1,"Test setup lacked approved copy plan");
            work.UnmarkRejectReview(first.Id);
            Ensure(!work.IsCurrentPlanApproved && work.PendingRejectCount==1 &&
                work.IsPendingReject(second.Id) && !work.IsPendingReject(first.Id),
                "Selective unmark bypassed approved Copy invalidation or changed other marks");
        });
        Check("P6E1_MULTI_REVIEW_MARKS_DO_NOT_AUTHORIZE_NATIVE_REJECT",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var other=work.Current!;
                work.MarkCurrentForRejectReview();
                db.Save(work.CaptureSession());
                Ensure(work.PendingRejectCount==2 && work.IsPendingReject(other.Id),
                    "Multi-mark test setup lacks two distinct stable photographs");
                var reject=new DurableRejectCoordinator(db);
                Refuse<IOException>(()=>reject.Stage(work,photo.Id));
                Refuse<IOException>(()=>reject.Stage(work,other.Id));
                Ensure(db.AllRejectHistoryCount()==0 &&
                    Directory.EnumerateFiles(Path.Combine(root,"source"),"photo_*.png").Count()==3 &&
                    !Directory.Exists(Path.Combine(root,"source",".photosift-reborn-reject-recovery")),
                    "Multiple review marks bypassed native one-operation gate");
            });
        });
        // P6E2A: terminal Reject receipts + independent successor grant.
        // All native filesystem tests use only new disposable P5C fixtures.
        Check("P6E2A_REJECT_EMPTY_OR_ACTIVE_HISTORY_CANNOT_SEAL",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                Refuse<IOException>(()=>db.SealRejectTerminalHistory());
                var engine=new DurableRejectCoordinator(db);
                var staged=engine.Stage(work,photo.Id);
                Refuse<IOException>(()=>db.SealRejectTerminalHistory());
                Ensure(db.AllRejectHistoryCount()==1 &&
                    !db.InspectRejectTerminalReceipts(1).Verified &&
                    File.Exists(photo.Path),"Active Reject accidentally gained terminal authority");
            });
        });
        Check("P6E2A_TWO_DISTINCT_REJECT_EPOCHS_RESTART_UNDO",()=>{
            string root=Root();
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),P5CGuiFixturePolicy.MarkerContents);
            string? prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
            try
            {
                var firstWork=RenameFixture(root);var first=firstWork.Current!;
                var firstHash=SortingWorkspace.ComputeSha256Async(first.Path).GetAwaiter().GetResult();
                firstWork.MarkCurrentForRejectReview();
                Guid firstId;
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(firstWork.CaptureSession());
                    var engine=new DurableRejectCoordinator(db);
                    var one=engine.Execute(engine.Approve(engine.Stage(firstWork,first.Id),true));
                    firstId=one.Id;
                    Ensure(one.State=="Committed", "First native Reject was not committed");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    var engine=new DurableRejectCoordinator(db);
                    Ensure(engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                        "First Reject committed evidence did not persist");
                    engine.Undo(firstWork.SessionId);
                    var evidence=db.SealRejectTerminalHistory();
                    Ensure(evidence.Verified && evidence.Sealed==1 && evidence.Historical==1 &&
                        db.SealRejectTerminalHistory().Head==evidence.Head &&
                        db.RejectItems().Single().Id==firstId &&
                        SortingWorkspace.ComputeSha256Async(first.Path).GetAwaiter().GetResult()==firstHash,
                        "First terminal Reject receipt failed or altered restored original");
                }
                var secondWork=RenameFixture(root);
                secondWork.JumpToPhoto(2);
                var second=secondWork.Current!;
                Ensure(second.Id!=first.Id && second.Path!=first.Path &&
                    secondWork.SessionId!=firstWork.SessionId,
                    "Second Reject should be independent photograph and session");
                var secondHash=SortingWorkspace.ComputeSha256Async(second.Path).GetAwaiter().GetResult();
                secondWork.MarkCurrentForRejectReview();
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(secondWork.CaptureSession());
                    var engine=new DurableRejectCoordinator(db);
                    Refuse<IOException>(()=>engine.Stage(secondWork,second.Id));
                    Refuse<IOException>(()=>engine.StageSuccessor(secondWork,second.Id,false));
                    var staged=engine.StageSuccessor(secondWork,second.Id,true);
                    Ensure(staged.State=="Staged" && db.RejectItems().Count==2 &&
                        File.Exists(second.Path) && !File.Exists(staged.Archive),
                        "Successor staged incorrectly or cut original early");
                    Refuse<IOException>(()=>db.PutRejectItem(staged with {Id=Guid.NewGuid()}));
                    Refuse<IOException>(()=>engine.StageSuccessor(secondWork,second.Id,true));
                    var committed=engine.Execute(engine.Approve(staged,true));
                    Ensure(committed.State=="Committed" && File.Exists(committed.Archive) &&
                        !File.Exists(second.Path) && db.RejectItems()[0].State=="Undone",
                        "Second native Reject lost archive or predecessor history");
                }
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    var engine=new DurableRejectCoordinator(db);
                    Ensure(engine.Inspect().State=="Committed" &&
                        engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                        "Second Reject committed archive did not survive restart");
                    var restored=engine.Undo(secondWork.SessionId);
                    Ensure(restored.State=="Undone" && File.Exists(second.Path) &&
                        SortingWorkspace.ComputeSha256Async(second.Path).GetAwaiter().GetResult()==secondHash &&
                        SortingWorkspace.ComputeSha256Async(first.Path).GetAwaiter().GetResult()==firstHash &&
                        db.SealRejectTerminalHistory() is {Verified:true,Sealed:2,Historical:2} &&
                        db.RejectItems().Select(r=>r.Id).Distinct().Count()==2,
                        "Two distinct native Reject/Undo epochs or final terminal chain failed");
                }
            }
            finally {Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6E2A_REPLACED_TERMINAL_SOURCE_REVOKES_SUCCESSOR",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db);
                engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
                engine.Undo(work.SessionId);
                Ensure(db.SealRejectTerminalHistory().Verified,
                    "Terminal native predecessor was not sealed");
                var bytes=File.ReadAllBytes(photo.Path);
                var stamp=File.GetLastWriteTimeUtc(photo.Path);
                var replacement=Path.Combine(root,"source",".external-replacement-"+Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(replacement,bytes);
                Ensure(NativeFileCapability.Identity(replacement)!=NativeFileCapability.Identity(photo.Path),
                    "Replacement did not receive a distinct native inode");
                File.SetLastWriteTimeUtc(replacement,stamp);
                File.Delete(photo.Path);
                NativeFileCapability.RelocateNoReplace(replacement,photo.Path);
                Ensure(!db.InspectRejectTerminalReceipts(1).Verified,
                    "Same-byte external source replacement retained successor authority");
                var next=RenameFixture(root);next.JumpToPhoto(2);
                var selected=next.Current!;next.MarkCurrentForRejectReview();
                db.Save(next.CaptureSession());
                Refuse<IOException>(()=>engine.StageSuccessor(next,selected.Id,true));
                Ensure(db.AllRejectHistoryCount()==1 &&
                    db.RejectItems()[0].State=="Undone",
                    "Replaced predecessor corrupted historical Reject row");
            });
        });
        Check("P6E2A_TERMINAL_RECEIPT_BYTES_CANNOT_BE_REWRITTEN",()=>{
            WithRejectFixture((db,work,photo,root)=>{
                var engine=new DurableRejectCoordinator(db);
                var committed=engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
                engine.Undo(work.SessionId);
                Ensure(db.SealRejectTerminalHistory().Verified,"First native terminal receipt missing");
                using var external=new SqliteConnection(new SqliteConnectionStringBuilder
                    {DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
                external.Open();using var update=external.CreateCommand();
                update.CommandText="UPDATE reject_terminal_receipts SET previous_digest='FAKE' WHERE ordinal=1";
                Ensure(update.ExecuteNonQuery()==1,"Test failed to mutate receipt");
                Ensure(!db.InspectRejectTerminalReceipts(1).Verified,
                    "Tampered immutable terminal receipt was silently accepted");
            });
        });
        Check("P6E2A_SQLITE_V15_UPGRADE_HAS_PREMIGRATION_BACKUP",()=>{
            string root=Root();var work=RenameFixture(root);
            work.MarkCurrentForRejectReview();
            string state=Path.Combine(root,"state"),database=Path.Combine(state,"workspaces-v1.sqlite");
            using(var db=new WorkspaceSessionStore(state))db.Save(work.CaptureSession());
            using(var sqlite=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                sqlite.Open();using var cmd=sqlite.CreateCommand();
                cmd.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE reject_terminal_receipts; "+
                    "DROP TABLE reject_successor_grants; DROP TABLE m6j_review_proposals; PRAGMA user_version=14;";
                cmd.ExecuteNonQuery();
            }
            using(var upgraded=new WorkspaceSessionStore(state))
                Ensure(upgraded.Load()!.PendingRejectIds is {Length:1} &&
                    upgraded.InspectRejectTerminalReceipts().Historical==0,
                    "v15 migration changed saved review queue or invented terminal Reject");
            var backups=Directory.GetFiles(state,"*.before-v15-*.sqlite");
            Ensure(backups.Length==1,"Missing consistent v14 pre-upgrade backup");
            using var older=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=backups[0],Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            older.Open();using var read=older.CreateCommand();
            read.CommandText="PRAGMA user_version;";
            Ensure(Convert.ToInt64(read.ExecuteScalar())==14,"Backup is not v14 pre-migration");
        });
        // P6E2B: real native two-epoch ownership evidence + exact GUI-only
        // persisted transition. No home/personal photographs touched.
        Check("P6E2B_SECOND_GUI_CHECKPOINT_TWO_NATIVE_REJECT_EPOCHS",()=>{
            string root=Root();WriteSecondRejectGuiManifest(root);
            File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),
                P5CGuiFixturePolicy.MarkerContents);
            var prior=Environment.GetEnvironmentVariable(P5CGuiFixturePolicy.EnvName);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,root);
            try
            {
                var first=RenameFixture(root);var photo01=first.Current!;
                first.MarkCurrentForRejectReview();
                using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                {
                    db.Save(first.CaptureSession());
                    var engine=new DurableRejectCoordinator(db);
                    engine.Execute(engine.Approve(engine.Stage(first,photo01.Id),true));
                    engine.Undo(first.SessionId);
                    Ensure(db.RejectItems().Single().State=="Undone",
                        "First native Reject Undo was not terminal");
                    Refuse<IOException>(()=>db.BeginSecondRejectGuiSession(first.CaptureSession(),false));
                    var next=db.BeginSecondRejectGuiSession(first.CaptureSession(),true);
                    Ensure(db.RejectItems().Count==1 && next.Photos.Length==1 &&
                        Path.GetFileName(next.Photos[0].Path)=="photo_02.png" &&
                        next.PendingRejectIds is {Length:1} &&
                        next.PendingRejectIds[0]==next.Photos[0].Id &&
                        db.InspectRejectTerminalReceipts(1).Verified,
                        "Second GUI transition did not preserve sealed first Reject");
                    Refuse<IOException>(()=>db.BeginSecondRejectGuiSession(first.CaptureSession(),true));
                    var restarted=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                    restarted.RestoreSession(next);
                    Ensure(db.InspectSecondRejectGuiSession(restarted.CaptureSession()).Verified,
                        "Second GUI checkpoint could not be inspected after RestoreSession");
                    var second=restarted.Photos.Single();
                    var stage=engine.StageSuccessor(restarted,second.Id,true);
                    Ensure(db.InspectSecondRejectGuiSession(restarted.CaptureSession()).Verified &&
                        engine.Inspect().State=="Staged" && db.RejectItems().Count==2,
                        "Second independent GUI staging failed");
                    engine.Approve(stage,true);
                    Ensure(db.InspectSecondRejectGuiSession(restarted.CaptureSession()).Verified,
                        "Approved second journal invalidated immutable GUI checkpoint");
                    engine.Execute(db.RejectItems()[^1]);
                    Ensure(engine.Inspect() is {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"} &&
                        db.InspectSecondRejectGuiSession(restarted.CaptureSession()).Verified,
                        "Committed second GUI Reject cannot be verified after restart");
                    engine.Undo(restarted.SessionId);
                    Ensure(engine.Inspect() is {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"} &&
                        db.InspectSecondRejectGuiSession(restarted.CaptureSession()).Verified &&
                        db.SealRejectTerminalHistory() is {Verified:true,Sealed:2} &&
                        db.RejectItems().Count==2 &&
                        File.Exists(photo01.Path) && File.Exists(second.Path),
                        "Second Reject Undo lost prior native evidence or restored original");
                }
            }
            finally{Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,prior);}
        });
        Check("P6E2B_SECOND_GUI_RESTARTED_SNAPSHOT_OR_NATIVE_DRIFT_REFUSED",()=>{
            WithRejectFixture((db,first,photo,root)=>{
                WriteSecondRejectGuiManifest(root);
                var engine=new DurableRejectCoordinator(db);
                engine.Execute(engine.Approve(engine.Stage(first,photo.Id),true));
                engine.Undo(first.SessionId);
                var next=db.BeginSecondRejectGuiSession(first.CaptureSession(),true);
                var live=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                live.RestoreSession(next);
                Ensure(db.InspectSecondRejectGuiSession(live.CaptureSession()).Verified,
                    "Untampered GUI snapshot failed");
                Ensure(!db.InspectSecondRejectGuiSession(live.CaptureSession() with
                    {Revision=live.Revision+5}).Verified,
                    "Changed restoration revision was incorrectly authorized");
                using var ext=new SqliteConnection(new SqliteConnectionStringBuilder
                    {DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
                ext.Open();using var change=ext.CreateCommand();
                change.CommandText="UPDATE reject_gui_second_session SET receipt_head='TAMPER' WHERE id=1";
                Ensure(change.ExecuteNonQuery()==1,"Test could not alter GUI provenance row");
                Ensure(!db.InspectSecondRejectGuiSession(live.CaptureSession()).Verified,
                    "Tampered second GUI native receipt head remained authorized");
            });
        });
        Check("P6E2B_V16_BACKUP_PRESERVES_V15_REJECT_HISTORY",()=>{
            string root=Root();var work=RenameFixture(root);
            work.MarkCurrentForRejectReview();
            string state=Path.Combine(root,"state"),dbFile=Path.Combine(state,"workspaces-v1.sqlite");
            using(var db=new WorkspaceSessionStore(state))db.Save(work.CaptureSession());
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=dbFile,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var command=conn.CreateCommand();
                command.CommandText="DROP TABLE reject_gui_second_session; DROP TABLE m6j_review_proposals; PRAGMA user_version=15;";
                command.ExecuteNonQuery();
            }
            using(var db=new WorkspaceSessionStore(state))
                Ensure(db.Load()!.PendingRejectIds is {Length:1} &&
                    !db.InspectSecondRejectGuiSession(work.CaptureSession()).Present,
                    "v16 changed existing saved Reject-review checkpoint");
            var backups=Directory.GetFiles(state,"*.before-v16-*.sqlite");
            Ensure(backups.Length==1,"v16 consistent backup missing");
            using var old=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=backups[0],Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            old.Open();using var c=old.CreateCommand();c.CommandText="PRAGMA user_version;";
            Ensure(Convert.ToInt64(c.ExecuteScalar())==15,
                "v16 pre-migration backup does not preserve v15");
        });
        // P6F1: the viewer must identify an intentionally archived generated
        // Reject photo only when the current native inspection independently
        // verifies the EXACT current operation and source/archive identities.
        Check("P6F1_VERIFIED_REJECT_PREVIEW_REQUIRES_FRESH_MATCHING_EVIDENCE",()=>
        {
            var session=Guid.NewGuid();var photoId=Guid.NewGuid();var operation=Guid.NewGuid();
            var source=Path.Combine("test","source","photo_02.png");
            var archive=Path.Combine("test","source",".photosift-reborn-reject-recovery","owned","photo_02.png");
            var photo=new Photo(source,30,12345) { Id=photoId };
            var row=new DurableRejectItem(operation,Guid.NewGuid(),session,photoId,1,
                source,archive,new string('A',64),"synthetic identity",30,12345,"Committed");
            var evidence=new RejectRecoveryObservation(operation,"Committed",source,archive,"OWNED_ARCHIVE_VERIFIED");
            Ensure(RecoveryPreviewPresentation.VerifiedCommittedRejectForPhoto(photo,session,row,evidence),
                "Verified committed Reject should have human-readable archive preview");
            Ensure(RecoveryPreviewPresentation.Heading.Contains("recovery") &&
                RecoveryPreviewPresentation.Detail.Contains("Undo native Reject") &&
                RecoveryPreviewPresentation.Detail.Contains("automatically"),
                "Explanation must identify archive, manual Undo, and non-automatic recovery");
        });
        Check("P6F1_MISSING_OR_TAMPERED_REJECT_NEVER_GETS_VERIFIED_PREVIEW",()=>
        {
            var session=Guid.NewGuid();var id=Guid.NewGuid();var op=Guid.NewGuid();
            var path=Path.Combine("synthetic","source","photo_02.png");
            var archive=Path.Combine("synthetic","recovery","photo_02.png");
            var photo=new Photo(path,99,12345) { Id=id };
            var row=new DurableRejectItem(op,Guid.NewGuid(),session,id,4,path,archive,
                new string('B',64),"synthetic identity",99,12345,"Committed");
            var good=new RejectRecoveryObservation(op,"Committed",path,archive,"OWNED_ARCHIVE_VERIFIED");
            bool Safe(DurableRejectItem? r, RejectRecoveryObservation? e,Guid s) =>
                RecoveryPreviewPresentation.VerifiedCommittedRejectForPhoto(photo,s,r,e);
            Ensure(!Safe(row,null,session) && !Safe(null,good,session) &&
                !Safe(row,good,Guid.NewGuid()) &&
                !Safe(row,good with {Operation=Guid.NewGuid()},session) &&
                !Safe(row,good with {Evidence="ARCHIVE_UNVERIFIED"},session) &&
                !Safe(row,good with {Archive=archive+".tampered"},session) &&
                !Safe(row,good with {Source=path+".tampered"},session) &&
                !Safe(row with {State="Undone"},good,session) &&
                !Safe(row with {PhotoId=Guid.NewGuid()},good,session) &&
                !Safe(row,good with {State="Staged"},session),
                "An absent, old, mismatched, tampered or unverified Reject must remain a missing-photo warning");
        });
        // P6F2A: meaningful multi-photo decision review for ordinary owner-selected
        // collections. These are review-only state changes, not native Move/Reject.
        Check("P6F2_COLLECTION_DECISION_CENTER_GROUPS_REAL_PHOTOS",()=>
        {
            var r=Root();var w=Create(r);
            w.Assign('F');w.Assign('T');w.MarkCurrentForRejectReview();
            var view=CollectionDecisionPresentation.FromSnapshot(w.CaptureSession());
            Ensure(view.Rows.Count==3 && view.Assigned==2 && view.RejectReview==1 &&
                view.Unassigned==0 && view.Removed==0,"Group counts incorrect");
            Ensure(view.Rows[0].State=="Assigned" && view.Rows[0].RouteKey=='F' &&
                view.Rows[1].State=="Assigned" && view.Rows[1].RouteKey=='T' &&
                view.Rows[2].State=="RejectReview","Saved photo identity/order lost");
            Ensure(view.Rows.All(row=>File.Exists(row.Path)),"Review moved a source");
            Ensure(!Directory.EnumerateFiles(Path.Combine(r,"output"),"*",SearchOption.AllDirectories).Any(),
                "Read-only review wrote output files");
        });
        Check("P6F2_SELECTIVE_FOLDER_CLEAR_PERSISTS_OTHER_CHOICES_AND_REJECT",()=>
        {
            var r=Root();var w=Create(r);
            var photos=w.Photos.ToArray();
            w.Assign('F');w.Assign('T');w.MarkCurrentForRejectReview();
            w.ClearSelectedAssignment(photos[0].Id);
            Ensure(w.AssignmentCount==1 && w.PendingRejectCount==1 &&
                w.CategoryFor(photos[0].Path) is null &&
                w.CategoryFor(photos[1].Path)=='T' && w.IsPendingReject(photos[2].Id),
                "Clearing an earlier decision altered another photograph");
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
                db.Save(w.CaptureSession());
            var resumed=new SortingWorkspace();
            using(var db=new WorkspaceSessionStore(Path.Combine(r,"state")))
                resumed.RestoreSession(db.Load()!);
            var view=CollectionDecisionPresentation.FromSnapshot(resumed.CaptureSession());
            Ensure(view.Unassigned==1 && view.Assigned==1 && view.RejectReview==1 &&
                view.Rows[0].PhotoId==photos[0].Id && view.Rows[0].State=="Unassigned" &&
                view.Rows[1].PhotoId==photos[1].Id && view.Rows[1].RouteKey=='T' &&
                view.Rows[2].PhotoId==photos[2].Id && view.Rows[2].State=="RejectReview",
                "Restored selective state lost identities or other decisions");
            resumed.UndoDecision();
            Ensure(resumed.AssignmentCount==0 && resumed.PendingRejectCount==1,
                "Old deleted decision resurrected during LIFO Undo");
        });
        Check("P6F2_SELECTIVE_FOLDER_CLEAR_REVOKES_APPROVAL_AND_REFUSES_BAD_IDS",()=>
        {
            var r=Root();var w=Create(r);var photos=w.Photos.ToArray();
            w.Assign('F');w.Assign('T');
            var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.IsCurrentPlanApproved,"Copy approval precondition not established");
            long before=w.Revision;
            Refuse<InvalidOperationException>(()=>w.ClearSelectedAssignment(Guid.NewGuid()));
            Refuse<InvalidOperationException>(()=>w.ClearSelectedAssignment(photos[2].Id));
            Ensure(w.Revision==before && w.IsCurrentPlanApproved,"Invalid selection changed approval");
            w.ClearSelectedAssignment(photos[0].Id);
            Ensure(!w.IsCurrentPlanApproved && w.Revision>before &&
                w.AssignmentCount==1 && w.CategoryFor(photos[1].Path)=='T',
                "Selective clear did not revoke approved Copy plan");
            Refuse<IOException>(()=>w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult());
            Ensure(!Directory.EnumerateFiles(Path.Combine(r,"output"),"*",SearchOption.AllDirectories).Any(),
                "Stale approved Copy plan modified output after selective clearing");
        });
        Check("P6F2_APPLIED_COPY_AND_MALFORMED_OVERVIEW_REFUSE",()=>
        {
            var r=Root();var w=Create(r);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');
            var plan=w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var result=w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult();
            Ensure(result.Copied==1,"Synthetic Copy setup failed");
            var old=w.Revision;
            Refuse<InvalidOperationException>(()=>w.ClearSelectedAssignment(ids[0]));
            Ensure(w.Revision==old && w.AssignmentCount==1 &&
                File.Exists(plan[0].Destination),"Applied Copy barrier changed state");
            var snap=w.CaptureSession();
            Refuse<InvalidDataException>(()=>CollectionDecisionPresentation.FromSnapshot(
                snap with {PendingRejectIds=new[]{ids[0]}}));
            Refuse<InvalidDataException>(()=>CollectionDecisionPresentation.FromSnapshot(
                snap with {Assignments=new[]{new SavedAssignment(ids[0],'F'),new SavedAssignment(ids[0],'T')}}));
        });
        // P6F3A1: ordinary collection workflow bridge. These tests operate
        // on synthetic files and saved choices ONLY; no native Reject grant.
        Check("P6F3A1_COLLECTION_UNMARK_SELECTED_PERSISTS_OTHER_CHOICES",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');w.MarkCurrentForRejectReview();w.MarkCurrentForRejectReview();
            long oldRevision=w.Revision;
            w.UnmarkRejectReview(ids[1]);
            Ensure(w.Revision>oldRevision && w.AssignmentCount==1 &&
                w.PendingRejectCount==1 && !w.IsPendingReject(ids[1]) &&
                w.IsPendingReject(ids[2]) && w.CategoryFor(w.Photos[0].Path)=='F',
                "Selective unmark touched another decision or failed to change revision");
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                db.Save(w.CaptureSession());
            var reloaded=new SortingWorkspace();
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                reloaded.RestoreSession(db.Load()!);
            var summary=CollectionDecisionPresentation.FromSnapshot(reloaded.CaptureSession());
            Ensure(summary.Rows.Count==3 && summary.Assigned==1 && summary.RejectReview==1 &&
                summary.Unassigned==1 && summary.Rows[0].PhotoId==ids[0] &&
                summary.Rows[1].State=="Unassigned" &&
                summary.Rows[2].PhotoId==ids[2] && summary.Rows[2].State=="RejectReview",
                "Saved collection lost selected decision identity on restart");
            Ensure(w.Photos.All(p=>File.Exists(p.Path)) &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "Review-only selection modified original or output files");
        });
        Check("P6F3A1_UNMARK_INVALID_STABLE_ID_FAILS_WITHOUT_CHANGE",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.MarkCurrentForRejectReview();
            var before=w.CaptureSession();
            Refuse<InvalidOperationException>(()=>w.UnmarkRejectReview(Guid.Empty));
            Refuse<InvalidOperationException>(()=>w.UnmarkRejectReview(Guid.NewGuid()));
            Refuse<InvalidOperationException>(()=>w.UnmarkRejectReview(ids[1]));
            var after=w.CaptureSession();
            Ensure(after.Revision==before.Revision &&
                after.PendingRejectIds!.SequenceEqual(before.PendingRejectIds!) &&
                w.PendingRejectCount==1 && w.IsPendingReject(ids[0]),
                "Failed selection changed saved review state");
        });
        Check("P6F3A1_UNMARK_REVOKES_PREVIOUS_COPY_APPROVAL",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');w.MarkCurrentForRejectReview();
            w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.IsCurrentPlanApproved,"Precondition: reviewed Copy approval missing");
            w.UnmarkRejectReview(ids[1]);
            Ensure(!w.IsCurrentPlanApproved && w.PendingRejectCount==0 &&
                w.AssignmentCount==1 && w.CategoryFor(w.Photos[0].Path)=='F',
                "Review mark removal retained stale Copy authorization");
            Refuse<IOException>(()=>w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult());
            Ensure(!Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "Revoked Copy unexpectedly modified output");
        });
        Check("P6F3A1_COLLECTION_ACTIONS_RESPECT_NATIVE_HISTORY_LOCK",()=>
        {
            var id=Guid.NewGuid();
            var folder=new CollectionDecisionRow(id,"synthetic",1,"Assigned",'F',"Family");
            var reject=new CollectionDecisionRow(id,"synthetic",1,"RejectReview",null,"");
            bool Can(CollectionDecisionRow? row,CollectionDecisionAction choice,
                bool viewer,bool folderClear,bool unmark) =>
                CollectionDecisionActionPolicy.CanOffer(row,choice,viewer,folderClear,unmark);
            Ensure(Can(folder,CollectionDecisionAction.ClearFolderChoice,true,true,true) &&
                !Can(reject,CollectionDecisionAction.ClearFolderChoice,true,true,true) &&
                Can(reject,CollectionDecisionAction.UnmarkRejectReview,true,true,true) &&
                !Can(folder,CollectionDecisionAction.UnmarkRejectReview,true,true,true),
                "UI policy confused separate collection decision types");
            Ensure(!Can(folder,CollectionDecisionAction.ClearFolderChoice,false,false,false) &&
                !Can(reject,CollectionDecisionAction.UnmarkRejectReview,false,false,false) &&
                !Can(reject,CollectionDecisionAction.Show,false,false,false) &&
                !Can(null,CollectionDecisionAction.Show,true,true,true),
                "Recovery history or missing choice bypassed collection UI lock");
        });
        // P6F3A2: searchable ordinary collection review is display-only.
        Check("P6F3A2_SEARCH_CASE_INSENSITIVE_FILENAME_ONLY",()=>
        {
            var first=Guid.NewGuid();var second=Guid.NewGuid();
            var rows=new[]{
                new CollectionDecisionRow(first,Path.Combine("privateFamily","IMG_001.PNG"),1,
                    "Assigned",'F',"Family"),
                new CollectionDecisionRow(second,Path.Combine("IMG_001","Portrait.jpeg"),2,
                    "Unassigned",null,"")};
            var result=CollectionDecisionFilter.Apply(rows,0,"  img_001.PnG  ");
            Ensure(result.Length==1 && result[0].PhotoId==first && result[0].Position==1,
                "Filename search changed stable photo identity/position or matched parent folders");
            Ensure(CollectionDecisionFilter.Apply(rows,0,"privateFamily").Length==0 &&
                CollectionDecisionFilter.Apply(rows,0,"IMG_001").Length==1,
                "Filename search leaked ancestor folder into matching results");
        });
        Check("P6F3A2_SEARCH_FILTER_INTERSECTION_PRESERVES_ORDER",()=>
        {
            var ids=Enumerable.Range(0,4).Select(_=>Guid.NewGuid()).ToArray();
            var rows=new[]{
                new CollectionDecisionRow(ids[0],"photo_A.png",1,"Assigned",'F',"Family"),
                new CollectionDecisionRow(ids[1],"photo_B.png",2,"RejectReview",null,""),
                new CollectionDecisionRow(ids[2],"photo_C.png",3,"Unassigned",null,""),
                new CollectionDecisionRow(ids[3],"other.png",4,"Assigned",'T',"Trips")};
            Ensure(CollectionDecisionFilter.Apply(rows,2,"photo").Select(x=>x.PhotoId)
                .SequenceEqual(new[]{ids[0]}) &&
                CollectionDecisionFilter.Apply(rows,3,"PHOTO").Select(x=>x.PhotoId)
                .SequenceEqual(new[]{ids[1]}) &&
                CollectionDecisionFilter.Apply(rows,1,"photo").Select(x=>x.PhotoId)
                .SequenceEqual(new[]{ids[2]}) &&
                CollectionDecisionFilter.Apply(rows,0,"photo").Select(x=>x.PhotoId)
                .SequenceEqual(ids.Take(3)),
                "State filter and query changed order or mixed saved decisions");
        });
        Check("P6F3A2_SEARCH_EMPTY_NULL_NO_SAVED_MUTATION",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');w.MarkCurrentForRejectReview();
            var before=w.CaptureSession();
            var rows=CollectionDecisionPresentation.FromSnapshot(before).Rows;
            var selected=CollectionDecisionFilter.Apply(rows,0,"  ");
            var selectedNull=CollectionDecisionFilter.Apply(rows,0,null);
            Ensure(selected.Select(x=>x.PhotoId).SequenceEqual(ids) &&
                selectedNull.Select(x=>x.PhotoId).SequenceEqual(ids),
                "Blank search hid or reordered original photograph identities");
            var after=w.CaptureSession();
            Ensure(after.Revision==before.Revision && after.SessionId==before.SessionId &&
                after.Assignments!.SequenceEqual(before.Assignments!) &&
                after.PendingRejectIds!.SequenceEqual(before.PendingRejectIds!) &&
                w.Photos.All(p=>File.Exists(p.Path)) &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "Collection read-only search changed workspace, originals or output");
        });
        Check("P6F3A2_SEARCH_INVALID_VIEW_OR_IDENTITIES_REFUSED",()=>
        {
            var id=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"synthetic.png",1,"Assigned",'F',"Family");
            Refuse<ArgumentOutOfRangeException>(()=>CollectionDecisionFilter.Apply(new[]{row},-1,null));
            Refuse<ArgumentOutOfRangeException>(()=>CollectionDecisionFilter.Apply(new[]{row},4,null));
            Refuse<InvalidDataException>(()=>CollectionDecisionFilter.Apply(new[]{row,row},0,null));
            Refuse<InvalidDataException>(()=>CollectionDecisionFilter.Apply(new[]{
                new CollectionDecisionRow(Guid.Empty,"synthetic.png",1,"Assigned",'F',"Family")},0,null));
            Refuse<InvalidDataException>(()=>CollectionDecisionFilter.Apply(new[]{
                new CollectionDecisionRow(id,"synthetic.png",1,"NativeAuthorized",null,"")},0,null));
        });
        // P6F3B1: exact-ID filtered batch is only staged decision data.
        // No test authorizes or invokes native Copy/Move/Rename/Reject.
        Check("P6F3B1_BATCH_NONCONTIGUOUS_IDS_PERSIST_ACROSS_RESTART",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            Ensure(ids.Length>=3,"Expected three generated photographs");
            var before=w.CaptureSession();int cursor=w.Index;
            w.AssignReviewedBatch(new[]{ids[0],ids[2]},'F',before.SessionId,before.Revision);
            Ensure(w.Revision==before.Revision+1 && w.Index==cursor &&
                w.AssignmentCount==2 && w.CategoryFor(w.Photos[0].Path)=='F' &&
                w.CategoryFor(w.Photos[2].Path)=='F' &&
                w.CategoryFor(w.Photos[1].Path) is null,"Wrong batch stage scope");
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                db.Save(w.CaptureSession());
            var restored=new SortingWorkspace();
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
                restored.RestoreSession(db.Load()!);
            var review=CollectionDecisionPresentation.FromSnapshot(restored.CaptureSession());
            Ensure(review.Assigned==2 && review.Unassigned==1 && review.RejectReview==0 &&
                review.Rows[0].PhotoId==ids[0] && review.Rows[2].PhotoId==ids[2] &&
                File.Exists(w.Photos[0].Path) && File.Exists(w.Photos[2].Path) &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "Saved batch changed original files or decision identities");
        });
        Check("P6F3B1_BATCH_WRONG_REVISION_SESSION_DUPLICATE_MISSING_REFUSED",()=>
        {
            var w=Create(Root());var snap=w.CaptureSession();
            var ids=w.Photos.Select(p=>p.Id).ToArray();
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[0]},'F',Guid.NewGuid(),snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[0]},'F',snap.SessionId,snap.Revision+1));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[0],ids[0]},'F',snap.SessionId,snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[0],Guid.NewGuid()},'F',snap.SessionId,snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{Guid.Empty},'F',snap.SessionId,snap.Revision));
            Ensure(w.Revision==snap.Revision && w.AssignmentCount==0 &&
                w.CaptureSession().Decisions.Length==0,"Invalid reviewed batch partially changed state");
        });
        Check("P6F3B1_BATCH_REJECT_AND_ALREADY_ASSIGNED_BLOCK_ALL",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.MarkCurrentForRejectReview();var snap=w.CaptureSession();
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[1],ids[0]},'F',snap.SessionId,snap.Revision));
            Ensure(w.Revision==snap.Revision && w.AssignmentCount==0 &&
                w.IsPendingReject(ids[0]),"Reject mark was bypassed/changed during invalid batch");
            w.UnmarkRejectReview(ids[0]);w.Assign('F');snap=w.CaptureSession();
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{ids[2],ids[0]},'T',snap.SessionId,snap.Revision));
            Ensure(w.Revision==snap.Revision && w.AssignmentCount==1 &&
                w.CategoryFor(w.Photos[2].Path) is null,"Assigned photo polluted batch");
        });
        Check("P6F3B1_BATCH_LIMITS_EXISTING_ROUTE_ONLY_AND_NO_MUTATION",()=>
        {
            var w=Create(Root());var snap=w.CaptureSession();var id=w.Photos[0].Id;
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(Array.Empty<Guid>(),'F',snap.SessionId,snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(Enumerable.Repeat(id,101).ToArray(),'F',snap.SessionId,snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{id},'?',snap.SessionId,snap.Revision));
            Refuse<InvalidOperationException>(()=>w.AssignReviewedBatch(new[]{id},'Q',snap.SessionId,snap.Revision));
            Ensure(w.AssignmentCount==0 && w.Revision==snap.Revision &&
                !w.Routes.Any(route=>route.Key=='Q'),"Batch silently created destination or changed work");
        });
        Check("P6F3B1_BATCH_REVOKES_PREVIOUS_COPY_APPROVAL",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            w.Assign('F');w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.IsCurrentPlanApproved,"Precondition: Copy approval not established");
            var snap=w.CaptureSession();
            w.AssignReviewedBatch(new[]{ids[2]},'T',snap.SessionId,snap.Revision);
            Ensure(!w.IsCurrentPlanApproved && w.AssignmentCount==2 &&
                w.CategoryFor(w.Photos[1].Path) is null,"Batch did not revoke previous Copy approval");
            Refuse<IOException>(()=>w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult());
            Ensure(!Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "Stale Copy approval performed unreviewed file operations");
        });
        Check("P6F3B1_BATCH_UNDO_LIFO_NO_NATIVE_OPERATIONS",()=>
        {
            var root=Root();var w=Create(root);var ids=w.Photos.Select(p=>p.Id).ToArray();
            var snap=w.CaptureSession();
            w.AssignReviewedBatch(new[]{ids[0],ids[2]},'F',snap.SessionId,snap.Revision);
            w.UndoDecision();
            Ensure(w.AssignmentCount==1 && w.CategoryFor(w.Photos[0].Path)=='F' &&
                w.CategoryFor(w.Photos[2].Path) is null,"Batch undo did not track last photo");
            w.UndoDecision();
            Ensure(w.AssignmentCount==0 && w.CaptureSession().Decisions.Length==0 &&
                w.Photos.All(p=>File.Exists(p.Path)) &&
                !Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Any(),
                "LIFO batch undo touched original files or retained choices");
        });
        Check("P6F3B1_BATCH_UI_POLICY_GATES_EXACT_UNASSIGNED_SET",()=>
        {
            var rows=Enumerable.Range(0,2).Select(i=>
                new CollectionDecisionRow(Guid.NewGuid(),"generated"+i+".png",i+1,
                    "Unassigned",null,"")).ToArray();
            Ensure(CollectionDecisionActionPolicy.CanOfferBatch(rows,true) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(rows,false) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(Array.Empty<CollectionDecisionRow>(),true) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(Enumerable.Repeat(rows[0],101).ToArray(),true) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(new[]{rows[0],rows[0]},true) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(new[]{rows[0],rows[1] with {State="RejectReview"}},true) &&
                !CollectionDecisionActionPolicy.CanOfferBatch(new[]{rows[0],rows[1] with {State="Assigned"}},true),
                "Batch offer bypassed history lock, identity or eligibility");
        });
        // P6F3C2: selective clearing of a restored Copy choice must not depend
        // on an ordinary undo entry consumed by RetireCommitted. The IO seam
        // re-proves all native Copy-Undo history immediately before mutation.
        Check("P6F3C2_CLEAR_RESTORED_CHOICES_PRESERVES_OTHER_ROUTES_AND_ARCHIVES",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            var ids=work.Photos.Select(p=>p.Id).ToArray();
            foreach(var _ in ids) work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==3,
                "Fixture initial three-copy batch failed");
            work.RetireCommitted(ids);db.Save(work.CaptureSession());
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==3,
                "Fixture native Undo failed");
            foreach(var p in prepared)work.RestoreUndone(p.PhotoId,p.Key);
            db.Save(work.CaptureSession());
            Ensure(work.CaptureSession().Decisions.Length==0 &&
                CopyUndoContinuation.Inspect(db,work).Verified,
                "Restored Copy fixture lacks fresh terminal proof");
            var entries=db.CopyItems(work.SessionId).ToArray();
            var hashes=entries.Select(e=>SortingWorkspace.ComputeSha256Async(e.Recovery)
                .GetAwaiter().GetResult()).ToArray();
            work.ReviewPlan();work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            Ensure(work.IsCurrentPlanApproved,"Test did not establish volatile Copy approval");
            CopyUndoChoiceEditor.ClearRestoredChoice(db,work,ids[0]);
            Ensure(!work.IsCurrentPlanApproved && work.AssignmentCount==2 &&
                work.CategoryFor(work.Photos[0].Path) is null &&
                work.CategoryFor(work.Photos[1].Path)=='F' &&
                work.CategoryFor(work.Photos[2].Path)=='F' &&
                work.CaptureSession().Decisions.Length==0 &&
                db.CopyItems(work.SessionId).SequenceEqual(entries),
                "Clear changed other routes, journals, or retained stale approval");
            db.Save(work.CaptureSession());
            var reload=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            reload.RestoreSession(db.Load()!);
            Ensure(reload.AssignmentCount==2 &&
                reload.CategoryFor(reload.Photos[0].Path) is null &&
                reload.CategoryFor(reload.Photos[1].Path)=='F',
                "Selective cleared choice did not persist after restart");
            for(int i=0;i<entries.Length;i++)
                Ensure(File.Exists(entries[i].Recovery) &&
                    SortingWorkspace.ComputeSha256Async(entries[i].Recovery).GetAwaiter().GetResult()==hashes[i] &&
                    !File.Exists(entries[i].Destination) && File.Exists(entries[i].Source),
                    "Selective clearing changed an owned archive, freed destination or original");
        });
        Check("P6F3C2_REFUSES_WITHOUT_TERMINAL_PROOF_OR_MATCHED_PHOTO",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            var p=work.Photos[0];
            // A caller creating a no-undo-history assignment cannot forge a
            // journal entitlement via a GUI flag or in-memory route.
            work.RestoreUndone(p.Id,'F');
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var before=work.CaptureSession();
            Refuse<IOException>(()=>CopyUndoChoiceEditor.ClearRestoredChoice(db,work,p.Id));
            Ensure(work.Revision==before.Revision && work.CategoryFor(p.Path)=='F' &&
                work.AssignmentCount==1 && db.AllCopyHistoryCount()==0,
                "Missing terminal Copy history changed a staged choice");
        });
        Check("P6F3C2_REFUSES_ARCHIVE_DRIFT_AND_UNSAVED_REASSIGNMENT",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);work.Assign('F');
            var photo=work.Photos[0];var plan=work.ReviewPlan();
            var prep=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prep).GetAwaiter().GetResult().Completed==1,
                "Proof refusal Copy setup failed");
            work.RetireCommitted(new[]{photo.Id});db.Save(work.CaptureSession());
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,
                "Proof refusal Undo setup failed");
            work.RestoreUndone(photo.Id,'F');db.Save(work.CaptureSession());
            var archive=db.CopyItems(work.SessionId).Single().Recovery;
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,"Proof refusal baseline invalid");
            work.JumpToPhoto(1);work.Assign('T');
            var changed=work.Revision;
            Refuse<IOException>(()=>CopyUndoChoiceEditor.ClearRestoredChoice(db,work,photo.Id));
            Ensure(work.Revision==changed && work.CategoryFor(photo.Path)=='T',
                "Unsaved altered route bypassed checkpoint proof");
            work.UndoDecision();db.Save(work.CaptureSession());
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,
                "After valid decision undo terminal proof not restored");
            // Archive still exists but replacing its bytes must revoke clearing.
            File.AppendAllText(archive,"altered archive");
            var untouched=work.Revision;
            Refuse<IOException>(()=>CopyUndoChoiceEditor.ClearRestoredChoice(db,work,photo.Id));
            Ensure(work.Revision==untouched && work.CategoryFor(photo.Path)=='F',
                "Changed archive bytes enabled selective route clearing");
        });
        // P6F2B: generated (never personal) ordinary-shaped source and output
        // folders WITHOUT any P5C/P3B2 fixture marker. This exercises the new
        // separate backend lane using the same durable reject journal, immutable
        // exact-path transition state machine and native no-replace primitive.
        Check("P6F3C1_TERMINAL_COPY_RESTART_PROOF_AND_NO_FILE_CHANGES",()=>
        {
            var root=Root(); var work=NativeCopyUndoFixture(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Initial Copy failed");
            var done=engine.UndoAsync(work.SessionId).GetAwaiter().GetResult();
            Ensure(done.Completed==1 && done.Detail is null,"Initial Undo failed");
            var proof=CopyUndoContinuation.Inspect(db,work);
            Ensure(proof.Verified && proof.HistoricalRows==1,"Terminal proof failed: "+proof.Reason);
            // Simulate a normal restart WITHOUT re-saving. RestoreSession
            // advances the revision to revoke old approval, but this must not
            // make a bit-for-bit matching saved Copy history permanently locked.
            var reopened=new SortingWorkspace { IdentityResolver=NativeFileCapability.Identity };
            reopened.RestoreSession(db.Load()!);
            var afterRestart=CopyUndoContinuation.Inspect(db,reopened);
            Ensure(afterRestart.Verified,"Unchanged saved Copy Undo refused after restart: "+afterRestart.Reason);
            // An ordinary unsaved edit can also advance the revision by one.
            // It must NOT get the special restored-revision exemption.
            work.SetCollisionChoice(CollisionChoice.Number);
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,
                "Unsaved collision-mode revision change was treated as a safe restore");
            Ensure(!File.Exists(plan[0].Destination) && db.CopyItems(work.SessionId).Single().State=="Undone", "Proof mutated file/journal");
        });
        Check("P6F3C1_REPEAT_COPY_AFTER_VERIFIED_UNDO_PRESERVES_BOTH_ARCHIVES",()=>
        {
            var root=Root(); var work=NativeCopyUndoFixture(root); work.Assign('F');
            var plan=work.ReviewPlan();
            var first=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state")); db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(first).GetAwaiter().GetResult().Completed==1,"First Copy failed");
            work.RetireCommitted(first.Select(p=>p.PhotoId));db.Save(work.CaptureSession());
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,"First Copy Undo failed");
            foreach(var item in first) work.RestoreUndone(item.PhotoId,item.Key);
            db.Save(work.CaptureSession());
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,"First terminal state not proved");
            var savedRestart=new SortingWorkspace { IdentityResolver=NativeFileCapability.Identity };
            savedRestart.RestoreSession(db.Load()!);
            var restartProof=CopyUndoContinuation.Inspect(db,savedRestart);
            Ensure(restartProof.Verified,"New epoch prerequisite refused after load-only restart: "+restartProof.Reason);
            var firstArchive=db.CopyItems(work.SessionId).Single().Recovery;
            var secondPlan=work.ReviewPlan();
            var second=DurableCopyCoordinator.PrepareAsync(work,secondPlan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            Ensure(engine.ApplyAsync(second).GetAwaiter().GetResult().Completed==1,"Fresh reviewed second Copy failed");
            var two=db.CopyItems(work.SessionId);
            Ensure(two.Count==2 && two.Count(x=>x.State=="Undone")==1 && two.Count(x=>x.State=="Committed")==1,
                "Second Copy rewrote first historical row");
            Ensure(File.Exists(firstArchive),"First archived output lost after second Copy");
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,"Second verified Copy Undo failed");
            foreach(var item in second) work.RestoreUndone(item.PhotoId,item.Key);
            db.Save(work.CaptureSession());
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified &&
                db.CopyItems(work.SessionId).Count==2 &&
                db.CopyItems(work.SessionId).All(x=>x.State=="Undone"),
                "Second terminal proof or historical archive preservation failed");
        });
        Check("P6F3C1_REFUSES_COMMITTED_AND_UNRESOLVED_COPY",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup copy failed");
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Committed history granted continuation");
            var row=db.CopyItems(work.SessionId).Single();
            db.PutCopyItem(row with {State="NeedsAttention"});
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Unresolved history granted continuation");
        });
        Check("P6F3C1_REVOKES_ON_ARCHIVE_OR_ORIGINAL_IDENTITY_DRIFT",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup copy failed");
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,"Setup Undo failed");
            var row=db.CopyItems(work.SessionId).Single();
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,"Setup terminal proof failed");
            var replacement=row.Recovery+".held";
            File.Move(row.Recovery,replacement);
            File.Copy(replacement,row.Recovery);
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Same-bytes replacement archive granted continuation");
            File.Delete(row.Recovery);File.Move(replacement,row.Recovery);
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,"Archive restore did not revalidate");
            var old=row.Source+".held";
            File.Move(row.Source,old);File.Copy(old,row.Source);
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Same-bytes replacement original granted continuation");
        });
        Check("P6F3C1_MARKER_AND_OCCUPIED_TARGET_REFUSE",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup Copy failed");
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,"Setup Undo failed");
            var row=db.CopyItems(work.SessionId).Single();
            File.WriteAllText(row.Destination,"FOREIGN");
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Occupied target granted continuation");
            File.Delete(row.Destination);
            var owned=Directory.GetParent(Directory.GetParent(Directory.GetParent(row.Recovery)!.FullName)!.FullName)!.FullName;
            File.WriteAllText(Path.Combine(owned,".photosift-owner-v1"),"CHANGED");
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Tampered owner marker granted continuation");
        });
        Check("P6F3C1_REFUSES_FOREIGN_COPY_HISTORY",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);work.Assign('F');
            var plan=work.ReviewPlan();
            var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));db.Save(work.CaptureSession());
            var engine=new DurableCopyCoordinator(db);
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"Setup Copy failed");
            Ensure(engine.UndoAsync(work.SessionId).GetAwaiter().GetResult().Completed==1,"Setup Undo failed");
            Ensure(CopyUndoContinuation.Inspect(db,work).Verified,"Setup history does not verify");
            var row=db.CopyItems(work.SessionId).Single();
            db.PutCopyItem(row with { Id=Guid.NewGuid(), SessionId=Guid.NewGuid() });
            Ensure(!CopyUndoContinuation.Inspect(db,work).Verified,"Foreign history was ignored");
        });
        Check("P6F2B_UNMARKED_ORDINARY_REJECT_COMMIT_RESTART_UNDO",()=>
        {
            string root=OrdinaryRejectRoot();
            var work=RenameFixture(root);var photo=work.Current!;
            var hash=SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult();
            work.MarkCurrentForRejectReview();
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
            {
                db.Save(work.CaptureSession());
                Refuse<IOException>(()=>new DurableRejectCoordinator(db));
                var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
                var staged=engine.Stage(work,photo.Id);
                Ensure(staged.State=="Staged" && File.Exists(photo.Path) &&
                    !File.Exists(staged.Archive),"Ordinary Reject staging moved a source");
                Refuse<IOException>(()=>engine.Approve(staged,false));
                var approved=engine.Approve(staged,true);
                Ensure(File.Exists(photo.Path),"Ordinary Reject approval moved source");
                var committed=engine.Execute(approved);
                Ensure(committed.State=="Committed" && !File.Exists(photo.Path) &&
                    File.Exists(committed.Archive) &&
                    engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED" &&
                    db.RejectItems().Count==1 && db.AllMoveHistoryCount()==0 &&
                    db.AllRenameHistoryCount()==0 && db.CopyItems(work.SessionId).Count==0,
                    "Ordinary Reject commit lost owned archive or contaminated journals");
            }
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
            {
                var restored=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                restored.RestoreSession(db.Load()!);
                var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,restored);
                Ensure(engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                    "Ordinary Reject restart could not verify owned original");
                var undone=engine.Undo(work.SessionId);
                Ensure(undone.State=="Undone" && !File.Exists(undone.Archive) &&
                    File.Exists(photo.Path) &&
                    NativeFileCapability.Identity(photo.Path)==undone.SourceIdentity &&
                    SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult()==hash &&
                    engine.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED",
                    "Ordinary Reject native restoration lost identity/bytes");
                Refuse<IOException>(()=>engine.Undo(work.SessionId));
            }
        });
        Check("P6F2B_ORDINARY_REJECT_STALE_OR_UNBOUND_SCOPE_REFUSES",()=>
        {
            string root=OrdinaryRejectRoot();
            var work=RenameFixture(root);var photo=work.Current!;
            work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            var staged=engine.Stage(work,photo.Id);
            var changed=work.CaptureSession() with {PendingRejectIds=Array.Empty<Guid>()};
            db.Save(changed);
            Refuse<IOException>(()=>engine.Approve(staged,true));
            Ensure(db.RejectItems().Single().State=="Staged" &&
                File.Exists(photo.Path) && !File.Exists(staged.Archive),
                "Stale ordinary mark crossed native approval boundary");
            Refuse<IOException>(()=>DurableRejectCoordinator.ForOrdinaryWorkspace(db,
                new SortingWorkspace()));
        });
        Check("P6F2B_ORDINARY_REJECT_SAME_BYTES_REPLACEMENT_REFUSES",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            var staged=engine.Stage(work,photo.Id);
            var replacement=Path.Combine(root,"source","replacement-generated.tmp");
            File.Copy(photo.Path,replacement,overwrite:false);
            Ensure(NativeFileCapability.Identity(replacement)!=staged.SourceIdentity,
                "Ordinary test replacement did not obtain a distinct inode/file index");
            File.SetLastWriteTimeUtc(replacement,new DateTime(staged.SourceWriteTicks,DateTimeKind.Utc));
            File.Delete(photo.Path);NativeFileCapability.RelocateNoReplace(replacement,photo.Path);
            Refuse<IOException>(()=>engine.Approve(staged,true));
            Ensure(db.RejectItems().Single().State=="Staged" &&
                !File.Exists(staged.Archive),
                "Ordinary Reject accepted replacement of original bytes/metadata");
        });
        Check("P6F2B_ORDINARY_REJECT_OCCUPIED_UNDO_REFUSES",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            var committed=engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
            File.WriteAllText(photo.Path,"synthetic external occupant (must preserve)");
            Refuse<IOException>(()=>engine.Undo(work.SessionId));
            Ensure(File.ReadAllText(photo.Path)=="synthetic external occupant (must preserve)" &&
                File.Exists(committed.Archive) &&
                db.RejectItems().Single().State=="Committed",
                "Occupied Undo discarded real owned archive or external occupant");
        });
        Check("P6F2B_ORDINARY_REJECT_INTERRUPTION_NEVER_REPLAYS",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work,phase=>
            {
                if(phase=="REJECT_ORIGINAL_RELOCATED_UNRECORDED")
                    throw new SimulatedInterruptionException(phase);
            });
            var approved=engine.Approve(engine.Stage(work,photo.Id),true);
            Refuse<SimulatedInterruptionException>(()=>engine.Execute(approved));
            var uncertain=db.RejectItems().Single();
            Ensure(uncertain.State=="RelocateIntent" && !File.Exists(photo.Path) &&
                File.Exists(uncertain.Archive),"Interruption did not retain ambiguous owned archive");
            var resumed=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            Ensure(resumed.Inspect().Evidence=="UNRESOLVED_PRESERVE_ALL_NO_REPLAY",
                "Interrupted ordinary Reject was silently classified committed");
            Refuse<IOException>(()=>resumed.Undo(work.SessionId));
            Refuse<IOException>(()=>resumed.Execute(approved));
            Ensure(db.RejectItems().Single().State=="RelocateIntent" &&
                File.Exists(uncertain.Archive),"Interrupted ordinary Reject was replayed or lost");
        });
        // P6F2C: the public GUI remains closed to personal photographs.
        // The explicit synthetic *GUI* fence is distinct from the ordinary
        // native coordinator, so the existing P6F2B backend tests still use
        // entirely unmarked ordinary folders.
        Check("P6F2C_GUI_PROBE_EXACT_ISOLATED_SCOPE_REQUIRED",()=>
        {
            var oldHome=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
            var oldProbe=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT");
            try
            {
                var root=OrdinaryGuiProbeRoot();
                var work=RenameFixture(root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                db.Save(work.CaptureSession());
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",Path.Combine(root,"state"));
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",root);
                Ensure(OrdinaryRejectGuiProbePolicy.IsRequested &&
                    OrdinaryRejectGuiProbePolicy.Require(db,work)==root,
                    "Exact ordinary synthetic GUI path was not accepted");
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",Path.Combine(root,"source"));
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,work));
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",Path.Combine(root,"state"));
                File.WriteAllText(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker"),"old fixture marker must refuse");
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,work));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",oldHome);
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",oldProbe);
            }
        });
        Check("P6F2C_GUI_PROBE_CHANGED_PHOTO_OR_SENTINEL_REFUSES",()=>
        {
            var oldHome=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
            var oldProbe=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT");
            try
            {
                var root=OrdinaryGuiProbeRoot();
                var work=RenameFixture(root);
                using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
                db.Save(work.CaptureSession());
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",Path.Combine(root,"state"));
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",root);
                File.AppendAllText(Path.Combine(root,"source","photo_01.png"),"synthetic tampering");
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,work));
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png"),
                    Path.Combine(root,"source","photo_01.png"),overwrite:true);
                File.AppendAllText(Path.Combine(root,"output","PROTECTED_SENTINEL.txt"),"tampered");
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,work));
                Ensure(db.AllRejectHistoryCount()==0,"A read-only GUI gate created native history");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",oldHome);
                Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT",oldProbe);
            }
        });
        // P6F2D: first ordinary-folder terminal-history closeout. This does
        // NOT release the GUI or authorize successor operation. The tests use
        // fresh synthetic folders only, and never mutate prior owner fixtures.
        Check("P6F2D_ORDINARY_NATIVE_UNDO_TERMINAL_SEAL_AND_IDEMPOTENCE",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            Refuse<IOException>(()=>db.SealOrdinaryRejectTerminalHistory());
            var first=engine.Stage(work,photo.Id);
            Refuse<IOException>(()=>db.SealOrdinaryRejectTerminalHistory());
            var committed=engine.Execute(engine.Approve(first,true));
            Refuse<IOException>(()=>db.SealOrdinaryRejectTerminalHistory());
            engine.Undo(work.SessionId);
            var seal=db.SealOrdinaryRejectTerminalHistory();
            Ensure(seal.Verified && seal.Sealed==1 && seal.Historical==1 &&
                db.InspectOrdinaryRejectTerminalReceipts(1).Verified &&
                db.SealOrdinaryRejectTerminalHistory().Head==seal.Head &&
                db.RejectItems().Single().State=="Undone" && File.Exists(photo.Path) &&
                !File.Exists(committed.Archive) && db.AllMoveHistoryCount()==0 &&
                db.AllRenameHistoryCount()==0,
                "Ordinary native terminal sealing/idempotence failed");
            Refuse<IOException>(()=>engine.Stage(work,photo.Id));
        });
        Check("P6F2D_ORDINARY_TERMINAL_SAME_BYTES_REPLACEMENT_REFUSES",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            var committed=engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
            engine.Undo(work.SessionId);
            var replacement=Path.Combine(root,"source","external-new-inode.tmp");
            File.Copy(photo.Path,replacement,false);
            File.SetLastWriteTimeUtc(replacement,new DateTime(committed.SourceWriteTicks,DateTimeKind.Utc));
            Ensure(NativeFileCapability.Identity(replacement)!=committed.SourceIdentity,
                "Test failed to create a different native identity");
            File.Delete(photo.Path);NativeFileCapability.RelocateNoReplace(replacement,photo.Path);
            Refuse<IOException>(()=>db.SealOrdinaryRejectTerminalHistory());
            Ensure(!db.InspectOrdinaryRejectTerminalReceipts(1).Verified &&
                db.RejectItems().Single().State=="Undone",
                "Replaced restored original received ordinary terminal authority");
        });
        Check("P6F2D_ORDINARY_TERMINAL_RECEIPT_TAMPER_REFUSES",()=>
        {
            string root=OrdinaryRejectRoot();var work=RenameFixture(root);
            var photo=work.Current!;work.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(work.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,work);
            engine.Execute(engine.Approve(engine.Stage(work,photo.Id),true));
            engine.Undo(work.SessionId);
            Ensure(db.SealOrdinaryRejectTerminalHistory().Verified,
                "Expected ordinary receipt missing");
            using var raw=new SqliteConnection(new SqliteConnectionStringBuilder
                {DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
            raw.Open();using var alter=raw.CreateCommand();
            alter.CommandText="UPDATE reject_terminal_receipts SET previous_digest='EXTERNAL' WHERE ordinal=1";
            Ensure(alter.ExecuteNonQuery()==1,"Synthetic terminal tamper setup failed");
            Ensure(!db.InspectOrdinaryRejectTerminalReceipts(1).Verified,
                "Tampered ordinary native receipt still passed inspection");
            Refuse<IOException>(()=>db.SealOrdinaryRejectTerminalHistory());
        });
        Check("P6F2D_ORDINARY_TWO_DISTINCT_NATIVE_EPOCHS_AFTER_RECEIPT",()=>
        {
            string root=OrdinaryRejectRoot();
            var first=RenameFixture(root);var photo01=first.Current!;
            first.MarkCurrentForRejectReview();
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
            {
                db.Save(first.CaptureSession());
                var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,first);
                engine.Execute(engine.Approve(engine.Stage(first,photo01.Id),true));
                engine.Undo(first.SessionId);
                Ensure(db.SealOrdinaryRejectTerminalHistory() is {Verified:true,Sealed:1},
                    "First ordinary Undo not sealed");
            }
            var second=RenameFixture(root);second.JumpToPhoto(2);
            var photo02=second.Current!;
            Ensure(second.SessionId!=first.SessionId && photo02.Id!=photo01.Id &&
                !SortingWorkspace.PathComparer.Equals(photo02.Path,photo01.Path),
                "Ordinary second epoch needs distinct photo and session");
            second.MarkCurrentForRejectReview();
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
            {
                db.Save(second.CaptureSession());
                var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,second);
                Refuse<IOException>(()=>engine.Stage(second,photo02.Id));
                Refuse<IOException>(()=>engine.StageSuccessor(second,photo02.Id,false));
                var staged=engine.StageSuccessor(second,photo02.Id,true);
                Ensure(staged.State=="Staged" && db.RejectItems().Count==2 &&
                    db.InspectOrdinaryRejectTerminalReceipts(1).Verified &&
                    engine.Inspect().Evidence=="UNTOUCHED_SOURCE_VERIFIED",
                    "Ordinary successor staging/inspection did not honor the native receipt lane");
                Refuse<IOException>(()=>engine.StageSuccessor(second,photo02.Id,true));
                var approved=engine.Approve(staged,true);
                Ensure(engine.Inspect().Evidence=="UNTOUCHED_SOURCE_VERIFIED",
                    "Approved ordinary successor inspection lost the verified native receipt lane");
                var committed=engine.Execute(approved);
                Ensure(committed.State=="Committed" && !File.Exists(photo02.Path) &&
                    File.Exists(committed.Archive) && engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED" &&
                    File.Exists(photo01.Path),"Ordinary second native archive or predecessor lost");
            }
            using(var db=new WorkspaceSessionStore(Path.Combine(root,"state")))
            {
                var restored=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                restored.RestoreSession(db.Load()!);
                var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,restored);
                Ensure(engine.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                    "Ordinary second native recovery not verifiable after restart");
                engine.Undo(second.SessionId);
                Ensure(engine.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED",
                    "Restored ordinary successor inspection lost the native receipt lane");
                Ensure(File.Exists(photo01.Path) && File.Exists(photo02.Path) &&
                    db.SealOrdinaryRejectTerminalHistory() is {Verified:true,Sealed:2,Historical:2} &&
                    db.RejectItems().Select(x=>x.SessionId).Distinct().Count()==2,
                    "Ordinary two-epoch independent native Undo and terminal history failed");
            }
        });
        Check("P6F2D_ORDINARY_TAMPERED_PREDECESSOR_BLOCKS_SECOND_STAGE",()=>
        {
            string root=OrdinaryRejectRoot();
            var first=RenameFixture(root);var photo01=first.Current!;
            first.MarkCurrentForRejectReview();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(first.CaptureSession());
            var engine=DurableRejectCoordinator.ForOrdinaryWorkspace(db,first);
            engine.Execute(engine.Approve(engine.Stage(first,photo01.Id),true));
            engine.Undo(first.SessionId);
            Ensure(db.SealOrdinaryRejectTerminalHistory().Verified,
                "First ordinary native terminal receipt not sealed");
            var replace=Path.Combine(root,"source","external-replacement.tmp");
            File.Copy(photo01.Path,replace,false);
            File.SetLastWriteTimeUtc(replace,File.GetLastWriteTimeUtc(photo01.Path));
            File.Delete(photo01.Path);
            NativeFileCapability.RelocateNoReplace(replace,photo01.Path);
            var second=RenameFixture(root);second.JumpToPhoto(2);
            var photo02=second.Current!;second.MarkCurrentForRejectReview();
            db.Save(second.CaptureSession());
            var next=DurableRejectCoordinator.ForOrdinaryWorkspace(db,second);
            Refuse<IOException>(()=>next.StageSuccessor(second,photo02.Id,true));
            Ensure(!db.InspectOrdinaryRejectTerminalReceipts(1).Verified &&
                db.RejectItems().Count==1 && File.Exists(photo02.Path),
                "External replacement bypassed predecessor native identity gate");
        });
        // P6F2D2: exact generated ordinary GUI successor. Backend exercised
        // through the SAME public calls wired to UI; native app/owner clicks still
        // require separate physical GUI verification before acceptance.
        Check("P6F2D2_ORDINARY_TWO_GUI_SESSIONS_NATIVE_RESTART_UNDO",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                CompleteFirstOrdinaryGuiReject(db,first,engine);
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(
                    first.CaptureSession(),false));
                var next=db.BeginSecondOrdinaryRejectGuiSession(first.CaptureSession(),true);
                Ensure(db.RejectItems().Count==1 &&
                    db.InspectOrdinaryRejectTerminalReceipts(1) is {Verified:true,Sealed:1} &&
                    next.SessionId!=first.SessionId && next.Photos.Length==1 &&
                    Path.GetFileName(next.Photos[0].Path)=="photo_02.png",
                    "Ordinary second GUI checkpoint did not preserve first terminal native receipt");
                var live=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                live.RestoreSession(db.Load()!);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(live.CaptureSession()).Verified &&
                    OrdinaryRejectGuiProbePolicy.Require(db,live)==root,
                    "Restored second GUI checkpoint was not provable");
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(
                    first.CaptureSession(),true));
                var successor=DurableRejectCoordinator.ForOrdinaryWorkspace(db,live);
                Refuse<IOException>(()=>successor.StageSuccessor(live,live.Photos[0].Id,false));
                var stage=successor.StageSuccessor(live,live.Photos[0].Id,true);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(live.CaptureSession()).Verified &&
                    successor.Inspect().Evidence=="UNTOUCHED_SOURCE_VERIFIED",
                    "Second staged ordinary GUI source/receipt verification failed");
                Refuse<IOException>(()=>successor.StageSuccessor(live,live.Photos[0].Id,true));
                var approved=successor.Approve(stage,true);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(live.CaptureSession()).Verified,
                    "Second GUI approval lost immutable checkpoint");
                var committed=successor.Execute(approved);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(live.CaptureSession()).Verified &&
                    !File.Exists(live.Photos[0].Path) && File.Exists(committed.Archive) &&
                    successor.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                    "Second native relocation or owner receipt verification uncertain");
                var restarted=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                restarted.RestoreSession(db.Load()!);
                var recovery=DurableRejectCoordinator.ForOrdinaryWorkspace(db,restarted);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(restarted.CaptureSession()).Verified &&
                    recovery.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                    "Restarted second native archive proof failed");
                recovery.Undo(restarted.SessionId);
                Ensure(db.InspectSecondOrdinaryRejectGuiSession(restarted.CaptureSession()).Verified &&
                    db.SealOrdinaryRejectTerminalHistory() is {Verified:true,Sealed:2,Historical:2} &&
                    File.Exists(first.Photos[0].Path) && File.Exists(restarted.Photos[0].Path) &&
                    db.RejectItems().Select(r=>r.SessionId).Distinct().Count()==2,
                    "Second restored source, independently sealed history or session separation failed");
            });
        });
        Check("P6F2D2_ORDINARY_GUI_CAS_REVOKES_CHANGED_PRIOR_SNAPSHOT",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                CompleteFirstOrdinaryGuiReject(db,first,engine);
                var drift=first.CaptureSession() with {Revision=first.Revision+3};
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(drift,true));
                Ensure(db.RejectItems().Count==1 &&
                    !db.InspectSecondOrdinaryRejectGuiSession(first.CaptureSession()).Present,
                    "Invalid prior GUI revision created an unauthorized second session");
            });
        });
        Check("P6F2D2_ORDINARY_GUI_RECEIPT_TAMPER_REFUSES_SECOND",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                CompleteFirstOrdinaryGuiReject(db,first,engine);
                Ensure(db.SealOrdinaryRejectTerminalHistory().Verified,"First ordinary native receipt missing");
                using var sql=new SqliteConnection(new SqliteConnectionStringBuilder
                    {DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
                sql.Open(); using var alter=sql.CreateCommand();
                alter.CommandText="UPDATE reject_terminal_receipts SET previous_digest='TAMPERED' WHERE ordinal=1";
                Ensure(alter.ExecuteNonQuery()==1,"Synthetic receipt tamper setup failed");
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(
                    first.CaptureSession(),true));
                Ensure(db.RejectItems().Count==1 &&
                    !db.InspectSecondOrdinaryRejectGuiSession(first.CaptureSession()).Present,
                    "Tampered predecessor created successor GUI authority");
            });
        });
        Check("P6F2D2_ORDINARY_GUI_SECOND_IDENTITY_REPLACEMENT_REFUSES",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                CompleteFirstOrdinaryGuiReject(db,first,engine);
                var next=db.BeginSecondOrdinaryRejectGuiSession(first.CaptureSession(),true);
                var live=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                live.RestoreSession(next);
                var second=live.Photos[0];
                string replacement=Path.Combine(root,"source","other_photo.tmp");
                File.Copy(second.Path,replacement,false);
                File.SetLastWriteTimeUtc(replacement,new DateTime(second.LastWriteUtcTicks,DateTimeKind.Utc));
                Ensure(NativeFileCapability.Identity(replacement)!=second.NativeIdentity,
                    "Synthetic replacement was not independently identified");
                File.Delete(second.Path);
                NativeFileCapability.RelocateNoReplace(replacement,second.Path);
                Ensure(!db.InspectSecondOrdinaryRejectGuiSession(live.CaptureSession()).Verified,
                    "Byte-identical second-source replacement was accepted");
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,live));
                Ensure(db.RejectItems().Count==1,"Refused replacement created native Reject intent");
            });
        });
        Check("P6F2D2_ORDINARY_GUI_SCOPE_SENTINEL_REFUSES",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                CompleteFirstOrdinaryGuiReject(db,first,engine);
                File.AppendAllText(Path.Combine(root,"output","PROTECTED_SENTINEL.txt"),"TAMPER");
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,first));
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(
                    first.CaptureSession(),true));
                Ensure(db.RejectItems().Count==1 &&
                    !db.InspectSecondOrdinaryRejectGuiSession(first.CaptureSession()).Present,
                    "Sentinel change created unauthorized successor history");
            });
        });
        Check("P6F2D2B_FORGED_SYNTHETIC_MANIFEST_REJECTED",()=>
        {
            WithTwoOrdinaryGui((db,first,engine,root)=>
            {
                // Even a matching, rewritten manifest is not allowed to turn
                // the probe into an arbitrary image operation.
                var photo=Path.Combine(root,"source","photo_02.png");
                File.AppendAllText(photo,"FORGED_OTHER_BYTES");
                var manifest=Path.Combine(root,"P6F2D2_PROBE_SCOPE.json");
                var badHash=SortingWorkspace.ComputeSha256Async(photo).GetAwaiter().GetResult();
                var firstHash=SortingWorkspace.ComputeSha256Async(
                    Path.Combine(root,"source","photo_01.png")).GetAwaiter().GetResult();
                File.WriteAllText(manifest,System.Text.Json.JsonSerializer.Serialize(new {
                    schema=2, origin="P6F2D2_TWO_GENERATED_ONLY",
                    sha256_01=firstHash,sha256_02=badHash,
                    sentinel=P6F2D2GeneratedScope.Sentinel
                }));
                Refuse<IOException>(()=>OrdinaryRejectGuiProbePolicy.Require(db,first));
                Refuse<IOException>(()=>db.BeginSecondOrdinaryRejectGuiSession(
                    first.CaptureSession(),true));
                Ensure(db.RejectItems().Count==0,"Forged generated-manifest obtained native authority");
            });
        });
        // P6G1: real read-only technical metadata now available from F12.
        Check("P6G1_PNG_GIF_BMP_HEADER_DIMENSIONS",()=>
        {
            var png=PhotoTechnicalMetadataReader.Parse(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png")));
            Ensure(png.Format=="PNG" && png.Width is >0 && png.Height is >0,
                "Bundled PNG fixture header was not recognized");
            var gif=PhotoTechnicalMetadataReader.Parse(new byte[] {(byte)'G',(byte)'I',(byte)'F',
                (byte)'8',(byte)'9',(byte)'a',3,0,7,0});
            Ensure(gif.Format=="GIF" && gif.Width==3 && gif.Height==7,"GIF dimensions");
            var bmp=new byte[26];bmp[0]=(byte)'B';bmp[1]=(byte)'M';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18,4),11);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22,4),-5);
            var info=PhotoTechnicalMetadataReader.Parse(bmp);
            Ensure(info.Format=="BMP" && info.Width==11 && info.Height==5,"BMP top-down dimensions");
        });
        Check("P6G1_BOUNDED_JPEG_EXIF_ORIENTATION_CAMERA_DATE",()=>
        {
            var info=PhotoTechnicalMetadataReader.Parse(MinimalExifJpeg());
            Ensure(info.Format=="JPEG" && info.Width==20 && info.Height==10 &&
                info.ExifOrientation==6 && info.CameraMake=="Nikon" &&
                info.CameraModel=="Z9" && info.DateTimeOriginal=="2026:10:15 08:24:30",
                "Synthetic EXIF orientation/camera/date not parsed correctly");
            Ensure(!info.Description.Contains("GPS",StringComparison.OrdinalIgnoreCase) ||
                info.Description.Contains("No GPS/location fields are displayed.",StringComparison.Ordinal),
                "Metadata display must not expose GPS values");
        });
        Check("P6G1_EXIF_CORRUPT_OFFSETS_REFUSED_WITHOUT_EXCEPTION",()=>
        {
            var bad=MinimalExifJpeg();
            // Make the 0th IFD offset point beyond the bounded buffer.
            bad[6+6+4]=0xff;bad[6+6+5]=0xff;bad[6+6+6]=0xff;bad[6+6+7]=0x7f;
            var info=PhotoTechnicalMetadataReader.Parse(bad);
            Ensure(info.Format=="JPEG" && info.ExifOrientation is null &&
                info.Width==20 && info.Height==10,"Corrupted EXIF leaked untrusted values");
        });
        Check("P6G1_UNKNOWN_AND_TRUNCATED_IMAGE_HEADERS_FAIL_CLOSED",()=>
        {
            var unknown=PhotoTechnicalMetadataReader.Parse(new byte[] {1,2,3,4,5});
            Ensure(unknown.Format=="Unsupported/unknown" && unknown.Width is null,"Unknown header");
            var shortPng=PhotoTechnicalMetadataReader.Parse(new byte[] {137,80,78,71,13,10,26,10,0});
            Ensure(shortPng.Width is null && shortPng.Height is null,"Truncated PNG dimensions");
            var randomJpeg=PhotoTechnicalMetadataReader.Parse(new byte[] {255,216,255,225,255,255,1,2});
            Ensure(randomJpeg.Width is null && randomJpeg.CameraModel is null,"Bogus JPEG length");
        });
        Check("P6G1_IMPORTED_SOURCE_MUTATION_PREVENTS_METADATA_INSPECTION",()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_P6G1_METADATA_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path=Path.Combine(root,"synthetic.png");
            try
            {
                File.WriteAllBytes(path,File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory,"Fixtures","photo_01.png")));
                var fi=new FileInfo(path);
                var original=new Photo(path,fi.Length,fi.LastWriteTimeUtc.Ticks)
                {NativeIdentity=NativeFileCapability.IsSupported ? NativeFileCapability.Identity(path) : null};
                Ensure(PhotoTechnicalMetadataReader.Inspect(original).Format=="PNG", "Unmodified PNG metadata refused");
                var disguised=Path.Combine(root,"disguised.jpg");
                File.Copy(path,disguised);
                var disguisedInfo=new FileInfo(disguised);
                var disguisedPhoto=new Photo(disguised,disguisedInfo.Length,disguisedInfo.LastWriteTimeUtc.Ticks);
                Refuse<InvalidDataException>(()=>PhotoTechnicalMetadataReader.Inspect(disguisedPhoto));
                using(var output=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None))output.WriteByte(17);
                Refuse<IOException>(()=>PhotoTechnicalMetadataReader.Inspect(original));
            }
            finally {Directory.Delete(root,true);}
        });
        // P6G2A: positive and negative *read-only* ordinary-operation workbench
        // coverage. Every scenario uses fresh generated photographs and new SQLite.
        Check("P6G2A_FRESH_ORDINARY_PHOTO_RENAME_ONLY_PREFLIGHT",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            db.Save(work.CaptureSession());
            var p=work.Current!;
            var report=OrdinaryOperationsSafetyAudit.Inspect(db,work,p,"safe_new.png");
            Ensure(!report.AuthorizedForMutation && report.Sha256 is {Length:64} &&
                   report.NativeIdentity==p.NativeIdentity &&
                   report.Operations.Count==3 &&
                   report.Operations[1].PreliminaryChecksPass &&
                   !report.Operations[0].PreliminaryChecksPass &&
                   !report.Operations[2].PreliminaryChecksPass,
                "Ordinary read-only Rename did not retain precise independent gates");
            Ensure(db.AllRenameHistoryCount()==0 && db.AllRejectHistoryCount()==0 &&
                   db.AllMoveHistoryCount()==0 && db.AllCopyHistoryCount()==0,
                "Read-only audit created native history");
        });
        Check("P6G2A_ONE_STAGED_MOVE_ORDINARY_PREFLIGHT_NO_APPROVAL",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            work.Assign('F');work.JumpToPhoto(1);db.Save(work.CaptureSession());
            var report=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"different.png");
            Ensure(report.Operations[0].PreliminaryChecksPass &&
                   !report.Operations[0].AuthorizedForMutation &&
                   !report.Operations[1].PreliminaryChecksPass &&
                   !report.Operations[2].PreliminaryChecksPass &&
                   !work.IsCurrentPlanApproved,
                "Move preflight staged or approved a native operation");
        });
        Check("P6G2A_ONE_REJECT_MARK_PREFLIGHT_STAYS_NONDESTRUCTIVE",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            work.MarkCurrentForRejectReview();work.JumpToPhoto(1);db.Save(work.CaptureSession());
            var before=NativeFileCapability.Identity(work.Photos[0].Path);
            var report=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"clear_name.png");
            Ensure(report.Operations[2].PreliminaryChecksPass &&
                   !report.Operations[2].AuthorizedForMutation &&
                   !report.Operations[0].PreliminaryChecksPass &&
                   NativeFileCapability.Identity(work.Photos[0].Path)==before &&
                   db.AllRejectHistoryCount()==0,
                "Reject readiness mutated the original or granted native Reject");
        });
        Check("P6G2A_STALE_FILE_IDENTITY_AND_SAVED_CHECKPOINT_REFUSED",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            db.Save(work.CaptureSession());
            work.Assign('F'); // deliberately UNSAVED, no revision-equivalent proof
            var stale=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"safe_new.png");
            Ensure(stale.Operations.All(x=>!x.PreliminaryChecksPass),
                "Unsaved assignment passed ordinary preflight");
            var path=work.Photos[0].Path;
            using(var write=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None))write.WriteByte(17);
            db.Save(work.CaptureSession());
            var changed=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"safe_new.png");
            Ensure(changed.Operations.All(x=>!x.PreliminaryChecksPass),
                "Changed source passed operation read-only preflight");
        });
        Check("P6G2A_COLLISION_ALIAS_AND_MALFORMED_RENAME_REFUSED",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);db.Save(work.CaptureSession());
            var collision=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"PHOTO_02.png");
            Ensure(!collision.Operations[1].PreliminaryChecksPass,
                "Portable case alias collision accepted");
            var invalid=OrdinaryOperationsSafetyAudit.Inspect(db,work,work.Photos[0],"../bad.png");
            Ensure(!invalid.Operations[1].PreliminaryChecksPass &&
                   !invalid.AuthorizedForMutation,"Path traversal Rename accepted");
        });
        // P6G2B: ordinary FIRST Move only. Fresh generated PNG data, no test
        // marker recognized by the ordinary coordinator; the same durable
        // Move journal/Undo core used by the GUI is exercised on real inodes.
        Check("P6G2B_ORDINARY_FIRST_MOVE_COMMIT_RESTART_UNDO",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            var photo=work.Photos[0];work.Assign('F'); db.Save(work.CaptureSession());
            Ensure(OrdinaryFirstMovePolicy.CanOffer(db,work),"Fresh generated ordinary Move not offered");
            var before=NativeFileCapability.Identity(photo.Path);
            var hash=SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult();
            var plan=work.ReviewPlan();work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            var mover=DurableMoveCoordinator.ForOrdinaryFirstEpoch(db);
            var p=mover.PrepareApprovedAsync(work,plan).GetAwaiter().GetResult();
            var row=mover.ExecuteApprovedAsync(work,p).GetAwaiter().GetResult();
            Ensure(row.State=="Committed" && !File.Exists(photo.Path) && File.Exists(row.Destination) &&
                File.Exists(row.OriginalArchive) && db.AllMoveHistoryCount()==1,
                "Ordinary Move did not durably relocate original after copy publication");
            work.RetireCommitted(new[]{row.PhotoId});db.Save(work.CaptureSession());
            var restored=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            restored.RestoreSession(db.Load()!);
            var undo=DurableMoveCoordinator.ForOrdinaryFirstEpoch(db).UndoAsync(restored.SessionId)
                .GetAwaiter().GetResult();
            Ensure(undo.State=="Undone" && File.Exists(photo.Path) &&
                NativeFileCapability.Identity(photo.Path)==before &&
                SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult()==hash &&
                File.Exists(row.OutputArchive) && !File.Exists(row.Destination) &&
                db.MoveItems(work.SessionId).Single().State=="Undone",
                "Ordinary Move recovery failed independently verified Undo");
        });
        Check("P6G2B_ORDINARY_REFUSES_UNSAVED_CHECKPOINT",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);var path=work.Photos[0].Path;
            var before=SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult();
            work.Assign('F'); // stage but do NOT save
            var plan=work.ReviewPlan();work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            Refuse<IOException>(()=>DurableMoveCoordinator.ForOrdinaryFirstEpoch(db)
                .PrepareApprovedAsync(work,plan).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(path) &&
                SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==before,
                "Unsaved ordinary Move staged mutation");
        });
        Check("P6G2B_ORDINARY_REFUSES_PREVIOUS_OPERATION_HISTORY",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var w=NativeCopyUndoFixture(root);w.Assign('F');db.Save(w.CaptureSession());
            var plan=w.ReviewPlan();w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            var mover=DurableMoveCoordinator.ForOrdinaryFirstEpoch(db);
            var p=mover.PrepareApprovedAsync(w,plan).GetAwaiter().GetResult();
            var moved=mover.ExecuteApprovedAsync(w,p).GetAwaiter().GetResult();
            mover.UndoAsync(w.SessionId).GetAwaiter().GetResult();
            Ensure(!OrdinaryFirstMovePolicy.CanOffer(db,w),"Historical Move silently unlocked future Move");
            var another=NativeCopyUndoFixture(root);another.Assign('T');db.Save(another.CaptureSession());
            var next=another.ReviewPlan();another.ApprovePlan(another.CurrentPlanId!.Value,another.Revision);
            Refuse<IOException>(()=>DurableMoveCoordinator.ForOrdinaryFirstEpoch(db)
                .PrepareApprovedAsync(another,next).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==1 && File.Exists(moved.OutputArchive) &&
                !File.Exists(moved.Destination),"Historical ordinary Undo evidence changed");
        });
        Check("P6G2B_ORDINARY_REFUSES_LINKED_INPUT_AND_ROUTE_ALIAS",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);
            work.Assign('F');db.Save(work.CaptureSession());
            var plan=work.ReviewPlan();work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            var first=plan[0];
            var prep=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            var forged=prep with {Destination=Path.Combine(work.DestinationRoot!,"Travel",Path.GetFileName(first.Source))};
            Refuse<IOException>(()=>OrdinaryFirstMovePolicy.RequireExact(db,work,forged));
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(first.Source),
                "Forged ordinary Move proposal created a journal or changed source");
        });
        // P6G2C: first ordinary local Rename across Windows and Linux. The
        // generated source has no legacy fixture-authorization marker. Both
        // normal commit/restart/Undo and fail-closed refusals use native journals.
        Check("P6G2C_ORDINARY_FIRST_RENAME_COMMIT_RESTART_UNDO",()=>
        {
            var root=OrdinaryRejectRoot();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);db.Save(work.CaptureSession());
            var photo=work.Photos[0];
            Ensure(OrdinaryFirstRenamePolicy.CanOffer(db,work),"Fresh ordinary Rename not offered");
            var id=NativeFileCapability.Identity(photo.Path);
            var hash=SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult();
            var engine=DurableRenameCoordinator.ForOrdinaryFirstEpoch(db);
            var p=engine.PrepareOwnedFixture(work,photo,"ordinary_renamed.png");
            var row=engine.Apply(work,p);
            Ensure(row.State=="Committed" && !File.Exists(photo.Path) &&
                File.Exists(row.Target) && db.AllRenameHistoryCount()==1,
                "Ordinary first Rename did not commit");
            work.ApplyVerifiedRename(photo.Id,row.Source,row.Target);db.Save(work.CaptureSession());
            var restored=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            restored.RestoreSession(db.Load()!);
            Ensure(OrdinaryFirstRenamePolicy.CanUndo(db,restored),"Restarted ordinary Rename proof failed");
            var undone=DurableRenameCoordinator.ForOrdinaryFirstEpoch(db).Undo(restored.SessionId);
            Ensure(undone.State=="Undone" && File.Exists(row.Source) && !File.Exists(row.Target) &&
                NativeFileCapability.Identity(row.Source)==id &&
                SortingWorkspace.ComputeSha256Async(row.Source).GetAwaiter().GetResult()==hash,
                "Ordinary Rename Undo lost native original identity or bytes");
            restored.ApplyVerifiedRename(photo.Id,row.Target,row.Source);db.Save(restored.CaptureSession());
            Ensure(db.RenameItems(work.SessionId).Single().State=="Undone",
                "Ordinary Rename terminal journal was not retained");
        });
        Check("P6G2C_ORDINARY_RENAME_UNSAVED_CHECKPOINT_REFUSED",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);db.Save(work.CaptureSession());
            var path=work.Current!.Path;
            work.Assign('F'); // unsaved assignment also violates clean-scoped Rename
            Refuse<IOException>(()=>DurableRenameCoordinator.ForOrdinaryFirstEpoch(db)
                .PrepareOwnedFixture(work,work.Current!,"unsaved_rename.png"));
            Ensure(File.Exists(path) && db.AllRenameHistoryCount()==0,
                "Unsaved Rename mutated source or wrote durable intent");
        });
        Check("P6G2C_ORDINARY_RENAME_CASE_ALIAS_AND_TRAVERSAL_REFUSED",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=NativeCopyUndoFixture(root);db.Save(work.CaptureSession());
            var engine=DurableRenameCoordinator.ForOrdinaryFirstEpoch(db);
            Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"PHOTO_02.png"));
            Refuse<IOException>(()=>engine.PrepareOwnedFixture(work,work.Current!,"../evil.png"));
            Ensure(db.AllRenameHistoryCount()==0 && File.Exists(work.Current!.Path),
                "Invalid filename wrote ordinary Rename journal");
        });
        Check("P6G2C_ORDINARY_RENAME_PREVIOUS_EPOCH_LOCKS_FRESH_RENAME",()=>
        {
            var root=OrdinaryRejectRoot();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var w=NativeCopyUndoFixture(root);db.Save(w.CaptureSession());
            var engine=DurableRenameCoordinator.ForOrdinaryFirstEpoch(db);
            var p=engine.PrepareOwnedFixture(w,w.Current!,"renamed_once.png");
            var row=engine.Apply(w,p);w.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
            db.Save(w.CaptureSession());engine.Undo(w.SessionId);
            w.ApplyVerifiedRename(row.PhotoId,row.Target,row.Source);db.Save(w.CaptureSession());
            Ensure(!OrdinaryFirstRenamePolicy.CanOffer(db,w),
                "Historical Rename Undo silently reopened fresh authority");
            Refuse<IOException>(()=>engine.PrepareOwnedFixture(w,w.Current!,"renamed_twice.png"));
            Ensure(db.AllRenameHistoryCount()==1 && File.Exists(row.Source),
                "Historical ordinary Rename files or journal changed");
        });
        // P6G2D: The everyday first-Reject GUI shares the already-proven
        // ordinary coordinator, but its new scope gate is independently tested.
        // All sources here are GENERATED synthetic PNGs, never user photographs.
        Check("P6G2D_FIRST_REJECT_SAVED_MARK_STAGE_WITHOUT_RELOCATION",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var photo=w.Current!;w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            Ensure(OrdinaryFirstRejectPolicy.CanStage(db,w),"Fresh marked ordinary Reject policy refused");
            var before=NativeFileCapability.Identity(photo.Path);
            var row=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w).Stage(w,photo.Id);
            Ensure(row.State=="Staged" && db.AllRejectHistoryCount()==1 &&
                File.Exists(photo.Path) && !File.Exists(row.Archive) &&
                NativeFileCapability.Identity(photo.Path)==before,
                "Native first Reject staging mutated the generated original");
            Ensure(OrdinaryFirstRejectPolicy.CanInspectHistory(db,w),
                "Historical ordinary Reject stage was not inspectable");
            Ensure(!OrdinaryFirstRejectPolicy.CanStage(db,w),
                "A staged operation offered a second fresh Reject");
        });
        Check("P6G2D_FIRST_REJECT_COMMIT_RESTART_UNDO_PRESERVES_NATIVE_ID",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            Ensure(OrdinaryFirstRejectPolicy.CanStage(db,w),"First Reject must require durable mark");
            var native=NativeFileCapability.Identity(photo.Path);
            var hash=SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult();
            var coord=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var staged=coord.Stage(w,photo.Id);
            var approved=coord.Approve(staged,true);
            Ensure(File.Exists(photo.Path),"Approval moved original");
            var committed=coord.Execute(approved);
            Ensure(committed.State=="Committed" && !File.Exists(photo.Path) &&
                File.Exists(committed.Archive) &&
                OrdinaryFirstRejectPolicy.CanInspectHistory(db,w) &&
                coord.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                "First ordinary Reject ownership was not verified after commit");
            var restart=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            restart.RestoreSession(db.Load()!);
            var recovered=DurableRejectCoordinator.ForOrdinaryWorkspace(db,restart);
            Ensure(OrdinaryFirstRejectPolicy.CanInspectHistory(db,restart) &&
                recovered.Inspect().Evidence=="OWNED_ARCHIVE_VERIFIED",
                "Restarted first Reject lost durable history");
            var undone=recovered.Undo(restart.SessionId);
            Ensure(undone.State=="Undone" && File.Exists(photo.Path) &&
                NativeFileCapability.Identity(photo.Path)==native &&
                SortingWorkspace.ComputeSha256Async(photo.Path).GetAwaiter().GetResult()==hash &&
                !File.Exists(committed.Archive) &&
                recovered.Inspect().Evidence=="RESTORED_SOURCE_VERIFIED" &&
                !OrdinaryFirstRejectPolicy.CanStage(db,restart),
                "Native Reject Undo lost original identity or reopened first-epoch authority");
        });
        Check("P6G2D_REJECT_UNSAVED_AND_MULTIMARK_DENIED",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            Ensure(!OrdinaryFirstRejectPolicy.CanStage(db,w),
                "Unmarked collection received native Reject authority");
            w.MarkCurrentForRejectReview();
            Ensure(!OrdinaryFirstRejectPolicy.CanStage(db,w),
                "Unsaved Reject review mark was authorized");
            db.Save(w.CaptureSession());
            Ensure(OrdinaryFirstRejectPolicy.CanStage(db,w),
                "Exact saved first Reject review was not recognized");
            w.JumpToPhoto(2);w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            Ensure(!OrdinaryFirstRejectPolicy.CanStage(db,w) && db.AllRejectHistoryCount()==0,
                "Multiple Reject marks were permitted to stage");
        });
        Check("P6G2D_REJECT_FOREIGN_HISTORY_DENIES_REUSE",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            // MarkCurrentForRejectReview intentionally advances the viewer.
            // Stage must use the stable ID of the *marked* photograph, not
            // w.Current (which now points at a different unmarked photo).
            var marked=w.Current!;
            var original=marked.Path;
            var native=NativeFileCapability.Identity(original);
            var originalHash=SortingWorkspace.ComputeSha256Async(original)
                .GetAwaiter().GetResult();
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            Ensure(w.Current is not null && w.Current.Id!=marked.Id &&
                w.IsPendingReject(marked.Id) && !w.IsPendingReject(w.Current.Id),
                "Reject-review cursor advance/stable marked identity regression");
            var coord=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var row=coord.Stage(w,marked.Id);
            Ensure(row.PhotoId==marked.Id && row.State=="Staged" &&
                !File.Exists(row.Archive) && File.Exists(original) &&
                NativeFileCapability.Identity(original)==native,
                "First stage targeted wrong photo or moved the native original");
            Ensure(!OrdinaryFirstRejectPolicy.CanStage(db,w),
                "Existing staged Reject re-enabled fresh Reject");
            var rejection=Refuse<IOException>(()=>coord.Stage(w,marked.Id));
            Ensure(rejection.Message.Contains("Existing file-operation history",StringComparison.Ordinal),
                "Second Stage refused for the wrong reason; must reach the history guard");
            var rows=db.RejectItems();
            Ensure(File.Exists(original) &&
                SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()==originalHash &&
                NativeFileCapability.Identity(original)==native &&
                rows.Count==1 && rows[0].Id==row.Id && rows[0].PhotoId==marked.Id &&
                rows[0].State=="Staged" && !File.Exists(rows[0].Archive),
                "Repeat Stage changed the original or mutated the durable staged history");
        });
        // P6G3A: one installation-wide observation, never successor authority.
        // All files are fresh, generated OS-temp fixtures; no owner fixtures.
        Check("P6G3A_UNIFIED_HISTORY_EMPTY_NEVER_GRANTS",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            var status=db.InspectUnifiedHistory();
            Ensure(status.Kind==UnifiedHistoryKind.Fresh && status.TotalRows==0 &&
                !status.AuthorizesNativeOperation && !status.GrantsSuccessorEpoch,
                "Fresh read-only audit invented native write authority");
        });
        Check("P6G3A_REJECT_STAGE_COMMIT_UNDO_TRANSITIONS",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coordinator=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var staged=coordinator.Stage(w,photo.Id);
            var unresolved=db.InspectUnifiedHistory();
            Ensure(unresolved.Kind==UnifiedHistoryKind.NeedsRecovery &&
                !unresolved.GrantsSuccessorEpoch && File.Exists(photo.Path),
                "Staged Reject was incorrectly described as terminal");
            var committed=coordinator.Execute(coordinator.Approve(staged,true));
            var active=db.InspectUnifiedHistory();
            Ensure(active.Kind==UnifiedHistoryKind.ActiveOwned &&
                !active.AuthorizesNativeOperation && !File.Exists(photo.Path),
                "Committed Reject was not identified as an active owned operation");
            coordinator.Undo(w.SessionId);
            var terminal=db.InspectUnifiedHistory();
            Ensure(terminal.Kind==UnifiedHistoryKind.AllTerminalVerified &&
                terminal.RejectRows==1 && terminal.TotalRows==1 &&
                !terminal.GrantsSuccessorEpoch && !terminal.AuthorizesNativeOperation &&
                File.Exists(photo.Path) && !File.Exists(committed.Archive),
                "Verified terminal Reject incorrectly authorized a successor or lost restored original");
        });
        Check("P6G3A_REJECT_TERMINAL_TAMPER_REFUSED",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coordinator=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            coordinator.Execute(coordinator.Approve(coordinator.Stage(w,photo.Id),true));
            coordinator.Undo(w.SessionId);
            var before=db.InspectUnifiedHistory();
            Ensure(before.Kind==UnifiedHistoryKind.AllTerminalVerified,"Baseline Reject Undo unproved");
            var op=db.RejectItems().Single();
            var marker=Path.Combine(Path.GetDirectoryName(op.Archive)!,".photosift-reject-operation-v1");
            File.AppendAllText(marker,"tampered-test-only");
            var after=db.InspectUnifiedHistory();
            Ensure(after.Kind==UnifiedHistoryKind.NeedsRecovery &&
                !after.AuthorizesNativeOperation && File.Exists(photo.Path),
                "Altered native recovery marker was accepted");
        });
        Check("P6G3A_RENAME_TERMINAL_READONLY",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            var coordinator=DurableRenameCoordinator.ForOrdinaryFirstEpoch(db);
            var p=coordinator.PrepareOwnedFixture(w,w.Current!,"renamed_owner.png");
            var done=coordinator.Apply(w,p);
            var committed=db.InspectUnifiedHistory();
            Ensure(committed.Kind==UnifiedHistoryKind.ActiveOwned &&
                !committed.AuthorizesNativeOperation,"Native Rename committed audit failed");
            w.ApplyVerifiedRename(done.PhotoId,done.Source,done.Target);db.Save(w.CaptureSession());
            coordinator.Undo(w.SessionId);
            var terminal=db.InspectUnifiedHistory();
            Ensure(terminal.Kind==UnifiedHistoryKind.AllTerminalVerified &&
                terminal.RenameRows==1 && !terminal.GrantsSuccessorEpoch &&
                File.Exists(done.Source),
                "Native Rename verified Undo was not correctly observed");
        });
        Check("P6G3A_POST_UNDO_SAVED_REVISION_NEVER_AUTHORIZES",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coordinator=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var operation=coordinator.Stage(w,photo.Id);
            coordinator.Execute(coordinator.Approve(operation,true));
            coordinator.Undo(w.SessionId);
            var previous=db.Load()!;
            var journal=db.RejectItems().Single();
            Ensure(previous.Revision==journal.SessionRevision,
                "Fixture did not preserve exact original staging revision");
            // Reproduce only the OBSERVED +1 persisted-state shape. The event
            // causing the owner's original revision increment is still unknown.
            db.Save(previous with {Revision=checked(previous.Revision+1)});
            var now=db.Load()!;
            var assessment=db.InspectUnifiedHistory();
            Ensure(now.Revision==journal.SessionRevision+1 &&
                db.RejectItems().Single()==journal &&
                assessment.Kind==UnifiedHistoryKind.AllTerminalVerified &&
                !assessment.AuthorizesNativeOperation && !assessment.GrantsSuccessorEpoch &&
                !OrdinaryFirstRejectPolicy.CanStage(db,w) && File.Exists(photo.Path),
                "Post-Undo saved revision invented native authority or changed journal");
        });
        Check("P6G3A_FOREIGN_MIXED_JOURNALS_FAIL_CLOSED",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coordinator=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var row=coordinator.Stage(w,photo.Id);
            Ensure(db.InspectUnifiedHistory().Kind==UnifiedHistoryKind.NeedsRecovery,
                "A staged history should not be writable");
            // ATTACK SIMULATION ONLY: adversarial cross-type history injected
            // into a brand new disposable SQLite. Normal PutCopyItem refuses
            // this combination. Never run this against an owner's database.
            using(var second=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString()))
            {
                second.Open();
                using var cmd=second.CreateCommand();
                cmd.CommandText="INSERT INTO copy_items(id,session,sequence,json) "+
                    "VALUES($id,$session,1,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$session",w.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Synthetic adversarial row was not installed");
            }
            var status=db.InspectUnifiedHistory();
            Ensure(status.Kind==UnifiedHistoryKind.ForeignMixed && status.CopyRows==1 &&
                status.RejectRows==1 && !status.AuthorizesNativeOperation &&
                !status.GrantsSuccessorEpoch && db.RejectItems().Single()==row &&
                File.Exists(photo.Path),
                "Inconsistent Copy+Reject history was accidentally accepted");
        });
        Check("P6G3A_DANGLING_CLOSEOUT_PREVENTS_FALSE_FRESH",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            Ensure(db.InspectUnifiedHistory().Kind==UnifiedHistoryKind.Fresh,
                "New disposable database should have no native operation history");
            // Deliberately corrupt a newly generated synthetic DB by inserting
            // a dangling ancillary closeout, not an owner's saved work.
            using(var second=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString()))
            {
                second.Open();
                using var cmd=second.CreateCommand();
                cmd.CommandText="INSERT INTO rename_closeout(id,session,digest,row_count) "+
                    "VALUES(1,$session,'SYNTHETIC_INVALID',1)";
                cmd.Parameters.AddWithValue("$session",w.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Synthetic dangling closeout injection refused");
            }
            var observed=db.InspectUnifiedHistory();
            Ensure(observed.Kind==UnifiedHistoryKind.NeedsRecovery &&
                observed.TotalRows==0 && observed.AuxiliaryRows==1 &&
                !observed.GrantsSuccessorEpoch && !observed.AuthorizesNativeOperation,
                "Dangling terminal closeout incorrectly treated as a fresh installation");
        });
        // P6G3B: diagnostics may explain mixed/terminal rows to a future
        // recovery viewer, but never mint a successor or bypass native locks.
        Check("P6G3B_EMPTY_INVENTORY_IS_NOT_AUTHORITY",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            var result=db.InspectUnifiedHistoryInventory();
            Ensure(result.ScanComplete && result.Rows.Count==0 &&
                result.Installation.Kind==UnifiedHistoryKind.Fresh &&
                !result.AuthorizesNativeOperation && !result.GrantsSuccessorEpoch,
                "An empty inventory minted operation authority");
        });
        Check("P6G3B_REJECT_STAGE_AND_UNDO_INVENTORY",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coord=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var staged=coord.Stage(w,photo.Id);
            var pending=db.InspectUnifiedHistoryInventory();
            Ensure(pending.ScanComplete && pending.Rows.Count==1 &&
                pending.Rows[0].State=="Staged" &&
                pending.Rows[0].Proof==UnifiedRowProof.NotVerified &&
                pending.IsRecoveryReviewRequired,
                "Unfinished Reject was incorrectly native-proven");
            coord.Execute(coord.Approve(staged,true));coord.Undo(w.SessionId);
            var done=db.InspectUnifiedHistoryInventory();
            Ensure(done.ScanComplete && done.Rows.Count==1 &&
                done.Rows[0].Family=="Reject" && done.Rows[0].State=="Undone" &&
                done.Rows[0].Proof==UnifiedRowProof.NativeTerminalVerified &&
                !done.AuthorizesNativeOperation && !done.GrantsSuccessorEpoch &&
                !done.IsRecoveryReviewRequired && File.Exists(photo.Path),
                "Verified terminal Reject inventory failed or invented fresh authority");
        });
        Check("P6G3B_MIXED_JOURNAL_INVENTORY_NEVER_APPROVES",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coord=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            var staged=coord.Stage(w,photo.Id);
            coord.Execute(coord.Approve(staged,true));coord.Undo(w.SessionId);
            // Fresh disposable SQL attack injection. No owner's fixture.
            using(var second=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString()))
            {
                second.Open();using var cmd=second.CreateCommand();
                cmd.CommandText="INSERT INTO copy_items(id,session,sequence,json) "+
                    "VALUES($id,$session,1,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$session",w.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Synthetic foreign row injection failed");
            }
            var inventory=db.InspectUnifiedHistoryInventory();
            Ensure(inventory.ScanComplete && inventory.Rows.Count==2 &&
                inventory.Installation.Kind==UnifiedHistoryKind.ForeignMixed &&
                inventory.Rows.Any(x=>x.Family=="Reject" &&
                    x.Proof==UnifiedRowProof.NativeTerminalVerified) &&
                inventory.Rows.Any(x=>x.Family=="Copy" &&
                    x.Proof==UnifiedRowProof.NotVerified) &&
                inventory.IsRecoveryReviewRequired && !inventory.GrantsSuccessorEpoch &&
                !inventory.AuthorizesNativeOperation && File.Exists(photo.Path),
                "Mixed foreign row was hidden, treated as proven, or made writable");
        });
        Check("P6G3B_REJECT_NATIVE_MARKER_TAMPER_IS_VISIBLE",()=>
        {
            var root=OrdinaryRejectRoot();var w=RenameFixture(root);
            var photo=w.Current!;
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            w.MarkCurrentForRejectReview();db.Save(w.CaptureSession());
            var coord=DurableRejectCoordinator.ForOrdinaryWorkspace(db,w);
            coord.Execute(coord.Approve(coord.Stage(w,photo.Id),true));coord.Undo(w.SessionId);
            var before=db.InspectUnifiedHistoryInventory();
            Ensure(before.Rows.Single().Proof==UnifiedRowProof.NativeTerminalVerified,
                "Reject baseline terminal evidence not established");
            var row=db.RejectItems().Single();
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(row.Archive)!,
                ".photosift-reject-operation-v1"),"synthetic-tamper");
            var after=db.InspectUnifiedHistoryInventory();
            Ensure(after.Rows.Single().Proof==UnifiedRowProof.NotVerified &&
                after.IsRecoveryReviewRequired && !after.AuthorizesNativeOperation &&
                !after.GrantsSuccessorEpoch,
                "Tampered native recovery marker survived inventory proof");
        });
        // P6G3C-R0: two real, disjoint native Copy operations with first still
        // COMMITTED. Do not run these on any owner GUI/recovery fixture.
        Check("P6G3C_COMMITTED_COPY_REVIEW_GRANT_CONSUME_COMMIT_SELECTIVE_UNDO",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);var ids=work.Photos.Select(p=>p.Id).ToArray();
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);
            using(var schemaConnection=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            {
                schemaConnection.Open();using var schemaCmd=schemaConnection.CreateCommand();
                schemaCmd.CommandText="PRAGMA user_version;";
                Ensure(Convert.ToInt32(schemaCmd.ExecuteScalar())==19,
                    "M6J-A3-P2A-R1 fresh workspace must use schema v19 and retain Copy-successor tests.");
            }
            work.Assign('F');var firstPlan=work.ReviewPlan();
            var first=DurableCopyCoordinator.PrepareAsync(work,firstPlan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(first).GetAwaiter().GetResult().Completed==1,"First native Copy failed");
            var prior=db.CopyItems(work.SessionId).Single();
            Ensure(prior.State=="Committed" && File.Exists(prior.Destination),"First Copy not committed");
            work.RetireCommitted(new[]{ids[0]});db.Save(work.CaptureSession());
            // A genuine saved-workspace restart invalidates all old volatile
            // approvals before the second, newly reviewed operation.
            var resumed=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            resumed.RestoreSession(db.Load()!);
            resumed.JumpToPhoto(2);
            Ensure(resumed.Current!.Id==ids[1] && resumed.Current.Id!=ids[0],
                "Second Copy must select a different saved photograph after restart.");
            resumed.Assign('T');var secondPlan=resumed.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(resumed,secondPlan,
                resumed.CurrentPlanId!.Value,resumed.Revision).GetAwaiter().GetResult().Single();
            Ensure(next.PhotoId==ids[1] && next.PhotoId!=prior.PhotoId &&
                !SortingWorkspace.PathComparer.Equals(next.Source,prior.Source) &&
                !SortingWorkspace.PathComparer.Equals(next.Destination,prior.Destination),
                "Successor fixture did not produce an independent reviewed Copy candidate.");
            resumed.ApprovePlan(resumed.CurrentPlanId!.Value,resumed.Revision);
            db.Save(resumed.CaptureSession());
            var grant=db.ApproveCommittedCopySuccessor(resumed,next);
            Ensure(!db.InspectUnifiedHistory().AuthorizesNativeOperation &&
                db.AllCopyHistoryCount()==1,"Successor approval prematurely modified native history");
            Refuse<IOException>(()=>engine.ApplyAsync(new[]{next}).GetAwaiter().GetResult());
            Ensure(db.AllCopyHistoryCount()==1,"Unpermitted Copy wrote a second native Intent");
            Ensure(engine.ApplyAsync(new[]{next},successor:grant).GetAwaiter().GetResult().Completed==1,
                "Second genuinely committed Copy failed");
            var rows=db.CopyItems(work.SessionId);
            Ensure(rows.Count==2 && rows.All(x=>x.State=="Committed") &&
                File.Exists(rows[0].Destination) && File.Exists(rows[1].Destination) &&
                db.HasAnyCopySuccessorGrant,"Independent real native successor did not commit");
            Refuse<IOException>(()=>engine.ApplyAsync(new[]{next},successor:grant).GetAwaiter().GetResult());
            Ensure(db.CopyItems(work.SessionId).Count==2,"Replayed grant wrote an extra Intent");
            var restarted=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            restarted.RestoreSession(db.Load()!);
            Ensure(restarted.SessionId==work.SessionId,"Native successor restart changed the saved session");
            var selective=engine.UndoLatestIndependentCopyAsync(work.SessionId).GetAwaiter().GetResult();
            rows=db.CopyItems(work.SessionId);
            Ensure(selective.Completed==1 && rows.Count==2 &&
                rows[0].State=="Committed" && rows[1].State=="Undone" &&
                File.Exists(rows[0].Destination) && !File.Exists(rows[1].Destination) &&
                File.Exists(rows[1].Recovery) &&
                NativeFileCapability.Identity(rows[0].Destination)==rows[0].TargetIdentity &&
                NativeFileCapability.Identity(rows[1].Recovery)==rows[1].TargetIdentity &&
                work.Photos.All(x=>File.Exists(x.Path)),
                "Selective latest Undo affected the committed predecessor or originals");
        });
        Check("P6G3C_REVIEW_REQUIRED_NO_GRANT_ON_UNAPPROVED_OR_STALE_CHECKPOINT",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var plan=work.ReviewPlan();var prep=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(prep).GetAwaiter().GetResult().Completed==1,"First Copy fixture failed");
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(2);
            Ensure(work.Current!.Id==work.Photos[1].Id &&
                work.Current.Id!=work.Photos[0].Id,
                "P6G3C disjoint successor test must select photo TWO, not the committed photo.");
            work.Assign('T');var secondPlan=work.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(work,secondPlan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            db.Save(work.CaptureSession());
            var noApproval=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,next));
            Ensure(noApproval.Message.Contains("Fresh saved, single-item Copy review",StringComparison.Ordinal),
                "Unapproved test refused for an unrelated history or photo-identity reason.");
            Ensure(!db.HasAnyCopySuccessorGrant,"Unapproved candidate persisted a grant");
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            work.JumpToPhoto(3);
            Ensure(work.Current!.Id==work.Photos[2].Id,
                "Stale-review fixture must target third unsorted photo before a new decision.");
            work.Assign('W');
            Ensure(work.Revision!=next.Revision && !work.IsCurrentPlanApproved,
                "Stale-review fixture failed to invalidate the previous saved approval.");
            var staleCheckpoint=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,next));
            Ensure(staleCheckpoint.Message.Contains("Fresh saved, single-item Copy review",StringComparison.Ordinal),
                "Stale checkpoint test refused for an unrelated history or photo-identity reason.");
            Ensure(!db.HasAnyCopySuccessorGrant,"Stale checkpoint persisted a grant");
        });
        Check("P6G3C_COMMITTED_COPY_TAMPER_BLOCKS_NEW_NATIVE_INTENT",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var plan=work.ReviewPlan();var prep=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(prep).GetAwaiter().GetResult().Completed==1,"First Copy fixture failed");
            var prior=db.CopyItems(work.SessionId).Single();
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(2);
            Ensure(work.Current!.Id==work.Photos[1].Id &&
                work.Current.Id!=work.Photos[0].Id,
                "P6G3C disjoint successor test must select photo TWO, not the committed photo.");
            work.Assign('T');var plan2=work.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(work,plan2,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            File.AppendAllText(prior.Destination,"tampered-owned-copy");
            var changedPrior=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,next));
            Ensure(changedPrior.Message.Contains("Committed Copy native ownership",StringComparison.Ordinal),
                "Tampered-output test did not reach the native predecessor proof.");
            Ensure(!db.HasAnyCopySuccessorGrant && db.AllCopyHistoryCount()==1 &&
                !File.Exists(next.Destination),"Changed prior bytes authorized an unrelated native successor");
        });
        Check("P6G3C_REJECT_SAME_PHOTO_COMMITTED_COPY_REUSE",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var plan=work.ReviewPlan();var prepared=DurableCopyCoordinator.PrepareAsync(work,plan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,"First Copy failed");
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(1);
            Ensure(work.Current!.Id==work.Photos[0].Id,
                "Same-photo reuse refusal must actually stage the PREVIOUSLY copied original.");
            work.Assign('T');var second=work.ReviewPlan();
            var reused=DurableCopyCoordinator.PrepareAsync(work,second,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            var samePhoto=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,reused));
            Ensure(samePhoto.Message.Contains("not a disjoint, owned committed predecessor",StringComparison.Ordinal),
                "Same-photo test did not reach the exact predecessor-isolation refusal.");
            Ensure(db.AllCopyHistoryCount()==1 && !db.HasAnyCopySuccessorGrant &&
                !File.Exists(reused.Destination),"Committed Copy source reused in a successor");
        });
        Check("P6G3C_STALE_GRANT_AFTER_OUTPUT_TAMPER_LEAVES_NO_NEW_INTENT",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var p1=work.ReviewPlan();var original=DurableCopyCoordinator.PrepareAsync(work,p1,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(original).GetAwaiter().GetResult().Completed==1,"Copy fixture failed");
            var prior=db.CopyItems(work.SessionId).Single();
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(2);
            Ensure(work.Current!.Id==work.Photos[1].Id &&
                work.Current.Id!=work.Photos[0].Id,
                "P6G3C disjoint successor test must select photo TWO, not the committed photo.");
            work.Assign('T');var p2=work.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(work,p2,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            var permit=db.ApproveCommittedCopySuccessor(work,next);
            File.AppendAllText(prior.Destination,"changed after approval");
            var changedAfterGrant=engine.ApplyAsync(new[]{next},successor:permit).GetAwaiter().GetResult();
            Ensure(changedAfterGrant.Completed==0 && changedAfterGrant.NeedsAttention==0 &&
                (changedAfterGrant.Detail?.Contains("Committed Copy native ownership",StringComparison.Ordinal) ?? false),
                "Tampered predecessor must fail before any durable successor Intent, not just report a generic batch error.");
            Ensure(db.AllCopyHistoryCount()==1 && !File.Exists(next.Destination),
                "Stale committed predecessor created a phantom native journal row or destination");
            using(var grantRead=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            {
                grantRead.Open();using var cmd=grantRead.CreateCommand();
                cmd.CommandText="SELECT state,used_intent FROM copy_successor_grants";
                using var rows=cmd.ExecuteReader();
                Ensure(rows.Read() && rows.GetInt64(0)==0 && rows.IsDBNull(1) && !rows.Read(),
                    "Pre-Intent native refusal consumed the approved successor permit.");
            }
        });
        Check("P6G3C_FOREIGN_CLOSEOUT_VETOES_NATIVE_SUCCESSOR_APPROVAL",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var p1=work.ReviewPlan();var original=DurableCopyCoordinator.PrepareAsync(work,p1,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(original).GetAwaiter().GetResult().Completed==1,"Copy fixture failed");
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(2);
            Ensure(work.Current!.Id==work.Photos[1].Id &&
                work.Current.Id!=work.Photos[0].Id,
                "P6G3C disjoint successor test must select photo TWO, not the committed photo.");
            work.Assign('T');var p2=work.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(work,p2,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            // Adversarial extra closeout on generated test db ONLY.
            using(var external=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                external.Open();using var cmd=external.CreateCommand();
                cmd.CommandText="INSERT INTO rename_closeout(id,session,digest,row_count) VALUES(1,$s,'FOREIGN',1)";
                cmd.Parameters.AddWithValue("$s",work.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Foreign auxiliary fixture not installed");
            }
            var foreignHistory=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,next));
            Ensure(foreignHistory.Message.Contains("Auxiliary native history requires separate predecessor review",StringComparison.Ordinal),
                "Foreign-closeout test did not reach the intended auxiliary-history veto.");
            Ensure(db.AllCopyHistoryCount()==1 && !db.HasAnyCopySuccessorGrant &&
                !File.Exists(next.Destination),"Foreign closeout enabled successor");
        });
        Check("P6G3C_HARDLINKED_PREDECESSOR_BLOCKS_COPY_SUCCESSOR",()=>
        {
            var root=Root();var work=NativeCopyUndoFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var engine=new DurableCopyCoordinator(db);work.Assign('F');
            var firstPlan=work.ReviewPlan();
            var first=DurableCopyCoordinator.PrepareAsync(work,firstPlan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            Ensure(engine.ApplyAsync(first).GetAwaiter().GetResult().Completed==1,"Initial Copy failed");
            var prior=db.CopyItems(work.SessionId).Single();
            work.RetireCommitted(new[]{work.Photos[0].Id});db.Save(work.CaptureSession());
            work.JumpToPhoto(2);
            Ensure(work.Current!.Id==work.Photos[1].Id &&
                work.Current.Id!=work.Photos[0].Id,
                "P6G3C disjoint successor test must select photo TWO, not the committed photo.");
            work.Assign('T');var secondPlan=work.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(work,secondPlan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult().Single();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);db.Save(work.CaptureSession());
            NativeFileCapability.PublishLinkNoReplace(prior.Destination,prior.Destination+".synthetic-hardlink");
            Ensure(!NativeFileCapability.IsSingleLinkRegularFile(prior.Destination),
                "Synthetic owned output hardlink did not alter native link count");
            var linkedPrior=Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(work,next));
            Ensure(linkedPrior.Message.Contains("Committed Copy native ownership",StringComparison.Ordinal),
                "Hardlink refusal did not reach the single-link predecessor verification.");
            Ensure(!db.HasAnyCopySuccessorGrant && db.AllCopyHistoryCount()==1 &&
                !File.Exists(next.Destination),"Hardlinked predecessor minted native permission");
        });
        // P6G3D: read-only everyday guidance. Never a file-operation grant.
        Check("P6G3D_EMPTY_COLLECTION_GUIDES_ADD",() =>
        {
            var view=GuidedWorkspacePresentation.Describe(0,0,0,0,false,false,true,false);
            Ensure(view.Heading.Contains("Add your photographs") && !view.Attention,
                "Empty collection guidance should offer import, never a native action.");
        });
        Check("P6G3D_CHOICE_GUIDES_REVIEW",() =>
        {
            var view=GuidedWorkspacePresentation.Describe(5,2,1,0,true,false,true,false);
            Ensure(view.Heading.Contains("Review your choices") && view.NextStep.Contains("2 photos") &&
                !view.Attention, "Saved assignments must lead to Review, not automatic Apply.");
        });
        Check("P6G3D_RECOVERY_OVERRIDES_SORT",() =>
        {
            var view=GuidedWorkspacePresentation.Describe(5,2,0,1,true,true,true,false);
            Ensure(view.Attention && view.Heading.Contains("previous file changes") &&
                !view.NextStep.Contains("Apply;"), "Historical native operations cannot look like new permission.");
        });
        Check("P6G3D_SAVE_FAILURE_OVERRIDES_HISTORY",() =>
        {
            var view=GuidedWorkspacePresentation.Describe(3,1,0,0,true,true,false,false);
            Ensure(view.Attention && view.Heading.Contains("Saved work needs attention"),
                "Unhealthy saved state must be first priority.");
        });
        Check("P6G3D_INVALID_COUNTS_REFUSED",() =>
        {
            Refuse<InvalidDataException>(() => GuidedWorkspacePresentation.Describe(
                2,2,1,0,true,false,true,false));
            Refuse<InvalidDataException>(() => GuidedWorkspacePresentation.Describe(
                -1,0,0,0,true,false,true,false));
        });
        // P6G3F-R2: four additional compiled, pure-source boundary checks.
        // Existing 335 tests above are not removed or renumbered.
        Check("P6G3F_R2_EXACT_SNAPSHOT_AND_FULL_COPY",() =>
        {
            var session=Guid.NewGuid();var plan=Guid.NewGuid();
            string source=Path.Combine(Path.GetTempPath(),"r2 source", "same.png");
            string target=Path.Combine(Path.GetTempPath(),"r2 output", "Family", "same.png");
            var snapshot=StudioExactReviewSnapshot.Create(session,plan,7,false,CollisionChoice.Skip,
                new[] {new SortPlanItem(source,target,'F',20,9)},
                new[] {new SkippedCollision(Path.Combine(Path.GetTempPath(),"other","same.png"),
                    Path.Combine(Path.GetTempPath(),"already","same.png"),"occupied")});
            Ensure(snapshot.Included.Length==1 && snapshot.Skipped.Length==1 &&
                snapshot.Included[0].Source==source && snapshot.Included[0].Destination==target &&
                snapshot.CopyAllExactDetails().Contains("Exact source: "+source) &&
                snapshot.CopyAllExactDetails().Contains("Attempted destination: "+snapshot.Skipped[0].AttemptedDestination),
                "Exact path sets must remain complete and independent of shortened friendly labels.");
        });
        Check("P6G3F_R2_ATTEMPT_IDENTITY_FAIL_CLOSED",() =>
        {
            var sess=Guid.NewGuid();var id=Guid.NewGuid();
            var ticket=new StudioReviewAttempt(9,sess,id,4,ReviewOperationMode.Copy,CollisionChoice.Refuse);
            Ensure(ticket.Matches(9,sess,id,4,ReviewOperationMode.Copy,CollisionChoice.Refuse),
                "Original frozen attempt should correlate.");
            Ensure(!ticket.Matches(10,sess,id,4,ReviewOperationMode.Copy,CollisionChoice.Refuse) &&
                !ticket.Matches(9,Guid.NewGuid(),id,4,ReviewOperationMode.Copy,CollisionChoice.Refuse) &&
                !ticket.Matches(9,sess,Guid.NewGuid(),4,ReviewOperationMode.Copy,CollisionChoice.Refuse) &&
                !ticket.Matches(9,sess,id,5,ReviewOperationMode.Copy,CollisionChoice.Refuse) &&
                !ticket.Matches(9,sess,id,4,ReviewOperationMode.Move,CollisionChoice.Refuse) &&
                !ticket.Matches(9,sess,id,4,ReviewOperationMode.Copy,CollisionChoice.Number),
                "Old generation, session, plan, revision, mode and policy must be refused.");
        });
        Check("P6G3F_R2_EMPTY_AND_MOVE_POLICY",() =>
        {
            Refuse<InvalidDataException>(()=>StudioExactReviewSnapshot.Create(Guid.NewGuid(),null,1,
                false,CollisionChoice.Refuse,Array.Empty<SortPlanItem>(),Array.Empty<SkippedCollision>()));
            var allSkipped=StudioExactReviewSnapshot.Create(Guid.NewGuid(),null,1,false,CollisionChoice.Skip,
                Array.Empty<SortPlanItem>(),new[] {new SkippedCollision("src","dst","exists")});
            Ensure(allSkipped.Included.Length==0 && allSkipped.Skipped.Length==1,
                "All-skipped display must retain exclusions without creating Apply permission.");
            Refuse<InvalidDataException>(()=>StudioExactReviewSnapshot.Create(Guid.NewGuid(),null,1,
                true,CollisionChoice.Number,new[] {new SortPlanItem("source","target",'F',1,2)},
                Array.Empty<SkippedCollision>(),Guid.NewGuid(),Guid.NewGuid()));
        });
        Check("P6G3F_R2_MOVE_RECOVERY_AND_INVISIBLE_PATH",() =>
        {
            var sess=Guid.NewGuid();var install=Guid.NewGuid();var move=Guid.NewGuid();
            string src=Path.Combine(Path.GetTempPath(),"case","photo_01.png");
            string dest=Path.Combine(Path.GetTempPath(),"result","Family","photo_01.png");
            var view=StudioExactReviewSnapshot.Create(sess,Guid.NewGuid(),4,true,CollisionChoice.Refuse,
                new[] {new SortPlanItem(src,dest,'F',42,11)},Array.Empty<SkippedCollision>(),install,move);
            string recovery=Path.Combine(Path.GetDirectoryName(src)!,".photosift-reborn-recovery",
                install.ToString("N"),sess.ToString("N"),move.ToString("N"),"photo_01.png");
            Ensure(view.Included[0].OriginalRecovery==recovery &&
                view.Included[0].OutputUndoRecovery!.Contains("installed-photo_01.png") &&
                StudioExactReviewSnapshot.DisplayWarning("name\u202E.png").Contains("U+202E"),
                "Move exact recovery and invisible path diagnostics must retain native values.");
        });
        // M2 compiled checks: the UI exclusion lease cannot itself approve,
        // execute, save or persist a file operation.
        Check("P6G3F_M2_EXCLUSIVE_REVIEW_LEASE",() =>
        {
            var lease=new StudioEmbeddedReviewLease();
            var id=lease.Begin();
            Ensure(lease.Active && !lease.DecisionPending,"Review must own a pending lane.");
            Refuse<InvalidOperationException>(()=>lease.Begin());
            lease.End(id);
            Ensure(!lease.Active,"End must release the lane.");
        });
        Check("P6G3F_M2_SINGLE_USE_DECISION",() =>
        {
            var lease=new StudioEmbeddedReviewLease();var id=lease.Begin();
            Ensure(lease.TryDecide(id) && lease.DecisionPending && !lease.TryDecide(id),
                "Approval/cancel callback must be single-use.");
            lease.End(id);
        });
        Check("P6G3F_M2_LATE_CALLBACK_REFUSAL",() =>
        {
            var lease=new StudioEmbeddedReviewLease();var first=lease.Begin();
            lease.End(first);var next=lease.Begin();
            Ensure(!lease.TryDecide(first) && lease.TryDecide(next),
                "An old view must never resolve a newer Review.");
            lease.End(next);
        });
        Check("P6G3F_M2_CANNOT_RELEASE_WRONG_OWNER",() =>
        {
            var lease=new StudioEmbeddedReviewLease();var id=lease.Begin();
            Refuse<InvalidOperationException>(()=>lease.End(id+1));
            Ensure(lease.Active && lease.TryDecide(id),"Wrong-owner release must fail closed.");
            lease.End(id);
        });
        // M3: complete read-only Copy history browser. No native write authority.
        Check("P6G3F_M3_ALL_RECORDED_ROWS_VISIBLE",() =>
        {
            var session=Guid.NewGuid();var plan=Guid.NewGuid();
            DurableCopyItem Make(long seq,string state)
            {
                string source=Path.Combine(Path.GetTempPath(),"history",$"img{seq}.png");
                string dest=Path.Combine(Path.GetTempPath(),"history-output",$"img{seq}.png");
                return new DurableCopyItem(Guid.NewGuid(),session,Guid.NewGuid(),'F',plan,7,seq,
                    source,dest,dest+".part",Path.Combine(Path.GetTempPath(),"owned",$"img{seq}.png"),
                    "hash","identity",50,0,null,null,0,state,null);
            }
            var rows=Enumerable.Range(1,25).Select(x=>Make(x,x%2==0?"Committed":"Undone")).ToArray();
            var visible=StudioHistoryBrowserPresentation.Project(rows);
            Ensure(visible.Length==25 && visible[0].Sequence==25 && visible[24].Sequence==1 &&
                StudioHistoryBrowserPresentation.Filter(visible,0,null).Length==25,
                "History must never confuse the six-row Changes preview with the complete journal.");
        });
        Check("P6G3F_M3_FILTER_PRESERVES_IDENTITY",() =>
        {
            StudioRecordedCopyRow Record(string name,string status)=>new(Guid.NewGuid(),Guid.NewGuid(),3,
                status,'F',Path.Combine(Path.GetTempPath(),"a",name),
                Path.Combine(Path.GetTempPath(),"b",name),Path.Combine(Path.GetTempPath(),"c",name),null);
            var rows=new[]{Record("same.png","Committed"),Record("other.png","Undone"),
                Record("same.png","NeedsAttention")};
            var filtered=StudioHistoryBrowserPresentation.Filter(rows,3,"SAME.PNG");
            Ensure(filtered.Length==1 && filtered[0].Id==rows[2].Id &&
                StudioHistoryBrowserPresentation.Filter(rows,2,null).Single().Id==rows[1].Id,
                "Filtering cannot fabricate journal rows or cross-match ambiguous basenames.");
        });
        Check("P6G3F_M3_RECORDED_NOT_VERIFIED_NOW",() =>
        {
            var row=new StudioRecordedCopyRow(Guid.NewGuid(),Guid.NewGuid(),3,"Undone",'F',
                "source","dest","archive",null);
            Ensure(row.ExactDetails.Contains("Checked now: Not checked") &&
                !row.ExactDetails.Contains("ARCHIVE_VERIFIED") &&
                row.ExactDetails.Contains("Exact owned recovery: archive"),
                "Historical Undone must not be described as freshly verified archive evidence.");
        });
        Check("P6G3F_M3_COUNTS_AND_INVALID_FILTER",() =>
        {
            var empty=Array.Empty<StudioRecordedCopyRow>();
            Ensure(StudioHistoryBrowserPresentation.CountLabel(empty,0).Contains("0 recorded Copy files"),
                "Empty history must not claim completion.");
            var one=new[]{new StudioRecordedCopyRow(Guid.NewGuid(),Guid.NewGuid(),1,"Committed",'F',
                "src","dst","owned",null)};
            Ensure(StudioHistoryBrowserPresentation.CountLabel(one,1).Contains("1 recorded Copy file ·") &&
                StudioHistoryBrowserPresentation.CountLabel(one,1).Contains("Showing 1 of 1") &&
                !StudioHistoryBrowserPresentation.CountLabel(one,1).Contains("checked now"),
                "Recorded count needs correct singular grammar, no present-tense verification.");
            Refuse<ArgumentOutOfRangeException>(() => StudioHistoryBrowserPresentation.Filter(one,9,null));
            Refuse<ArgumentOutOfRangeException>(() => StudioHistoryBrowserPresentation.CountLabel(one,2));
        });
        // M4A: moving native command controls into the ordinary Changes view
        // must not erase distinct operation semantics or native restrictions.
        Check("P6G3F_M4A_DISTINCT_ORIGINAL_EFFECTS",() =>
        {
            var copy=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Copy);
            var move=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Move);
            var rename=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Rename);
            var reject=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Reject);
            Ensure(copy.OriginalEffect.Contains("remain in place") &&
                move.OriginalEffect.Contains("vacates the original") &&
                rename.OriginalEffect.Contains("path changes") &&
                reject.OriginalEffect.Contains("ORIGINAL into app-owned recovery"),
                "Studio operation families must never share misleading file-effect claims.");
        });
        Check("P6G3F_M4A_REJECT_DURABLE_STAGE_TRUTH",() =>
        {
            var reject=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Reject);
            Ensure(reject.ProposalCanBeDurableBeforeFileMovement &&
                reject.ReviewAndConsent.Contains("durable proposal") &&
                reject.Recovery.Contains("Stage/Approved history remains recorded") &&
                !StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Copy)
                    .ProposalCanBeDurableBeforeFileMovement,
                "A Reject proposal remains durable after UI cancellation; it is not a Copy choice.");
        });
        Check("P6G3F_M4A_MOVE_RENAME_FAMILY_LIMITS",() =>
        {
            var move=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Move);
            var rename=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Rename);
            Ensure(move.Scope.Contains("one supported first-epoch local photo") &&
                move.Scope.Contains("Refuse collisions") &&
                move.Scope.Contains("no skips") &&
                rename.Scope.Contains("One native-supported local photograph") &&
                rename.Recovery.Contains("safe and vacant"),
                "The UI must not imply general batch Move or unconstrained Rename Undo.");
        });
        Check("P6G3F_M4A_INVALID_FAMILY_FAILS",() =>
        {
            Refuse<ArgumentOutOfRangeException>(() =>
                StudioNativeFamilyPresentation.Describe((StudioNativeFamily)99));
            Ensure(StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Copy)
                .ReviewAndConsent.Contains("Approve does not Apply"),
                "Unknown family must refuse; Copy must retain a separate Apply.");
        });
        // M4B: Move review shares the contained Change workspace, but native
        // Move proof, recovery ownership and separate Apply remain untouched.
        Check("P6G3F_M4B_EMBEDDED_MOVE_POLICY",() =>
        {
            var sess=Guid.NewGuid();var plan=Guid.NewGuid();
            string src=Path.Combine(Path.GetTempPath(),"m4b", "original.png");
            string dst=Path.Combine(Path.GetTempPath(),"m4b-output", "F", "original.png");
            var move=StudioExactReviewSnapshot.Create(sess,plan,8,true,CollisionChoice.Refuse,
                new[]{new SortPlanItem(src,dst,'F',12,24)},Array.Empty<SkippedCollision>(),Guid.NewGuid(),Guid.NewGuid());
            StudioEmbeddedFamilyPolicy.RequireContained(move,ReviewOperationMode.Move,false);
            Ensure(move.CopyAllExactDetails().Contains("Retained original recovery:") &&
                move.CopyAllExactDetails().Contains("Output Undo recovery:"),
                "A Move Review must present BOTH recovery paths without granting file authority.");
            Refuse<InvalidDataException>(()=>StudioEmbeddedFamilyPolicy.RequireContained(move,
                ReviewOperationMode.Copy,false));
        });
        Check("P6G3F_M4B_COPY_CANNOT_INHERIT_MOVE",() =>
        {
            var copy=StudioExactReviewSnapshot.Create(Guid.NewGuid(),Guid.NewGuid(),2,false,
                CollisionChoice.Skip,new[]{new SortPlanItem("source","destination",'F',1,3)},
                Array.Empty<SkippedCollision>());
            StudioEmbeddedFamilyPolicy.RequireContained(copy,ReviewOperationMode.Copy,false);
            Refuse<InvalidDataException>(()=>StudioEmbeddedFamilyPolicy.RequireContained(copy,
                ReviewOperationMode.Move,false));
            Refuse<InvalidDataException>(()=>StudioEmbeddedFamilyPolicy.RequireContained(copy,
                ReviewOperationMode.Copy,true));
        });
        Check("P6G3F_M4B_FAMILY_LEASE_STALE_AND_WRONG_MODE",() =>
        {
            var lease=new StudioEmbeddedReviewLease();
            long move=lease.Begin(ReviewOperationMode.Move);
            Ensure(lease.ActiveFamily==ReviewOperationMode.Move &&
                !lease.TryDecide(move,ReviewOperationMode.Copy) && !lease.DecisionPending,
                "A stale Copy callback cannot settle a Move review.");
            Ensure(lease.TryDecide(move,ReviewOperationMode.Move) &&
                !lease.TryDecide(move,ReviewOperationMode.Move),
                "Move consent is single-use.");
            lease.End(move);
            long copy=lease.Begin(ReviewOperationMode.Copy);
            Ensure(!lease.TryDecide(move,ReviewOperationMode.Move) &&
                !lease.TryDecide(copy,ReviewOperationMode.Move) &&
                lease.TryDecide(copy,ReviewOperationMode.Copy),
                "Previous Move owner cannot resolve a newer Copy review.");
            lease.End(copy);
        });
        Check("P6G3F_M4B_ALL_FAMILY_RECORDED_PROJECTION",() =>
        {
            var session=Guid.NewGuid();var photo=Guid.NewGuid();var install=Guid.NewGuid();
            var plan=Guid.NewGuid();string source=Path.Combine(Path.GetTempPath(),"a","same.png");
            string target=Path.Combine(Path.GetTempPath(),"b","same.png");
            var copy=new DurableCopyItem(Guid.NewGuid(),session,photo,'F',plan,2,1,
                source,target,target+".part","owned", "hash","identity",1,2,null,null,0,"Committed",null);
            var move=new DurableMoveItem(Guid.NewGuid(),install,session,photo,plan,2,2,
                source,target,"temp","retained-original","undo-archive","output","hash",
                "identity",1,2,null,null,"Undone",null);
            var rename=new DurableRenameItem(Guid.NewGuid(),install,session,photo,2,3,
                source,target,"hash","identity",1,2,"Committed",null);
            var reject=new DurableRejectItem(Guid.NewGuid(),install,session,photo,2,
                source,"recovery","hash","identity",1,2,"Staged",null);
            var rows=StudioFamilyHistoryPresentation.Project(session,new[]{copy},new[]{move},
                new[]{rename},new[]{reject});
            Ensure(rows.Length==4 && rows.Select(r=>r.Family).Distinct().Count()==4 &&
                rows.Single(r=>r.Family==StudioNativeFamily.Move).ExactDetails.Contains("undo-archive") &&
                rows.Single(r=>r.Family==StudioNativeFamily.Reject).Display.Contains("Pending") &&
                StudioFamilyHistoryPresentation.CountLabel(rows,4).Contains("Move 1"),
                "Every family must retain its own recorded consequences and exact recovery data.");
        });
        Check("P6G3F_M4B_HISTORY_SEARCH_SAME_NAME_IDENTITY",() =>
        {
            var session=Guid.NewGuid();
            var rows=new[]{
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Copy,session,1,
                    "Committed","a\\same.png","a\\copy.png","archive",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,session,2,
                    "Undone","b\\same.png","b\\move.png","retained",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,
                    "Approved","c\\other.png","owned","saved",null)};
            var result=StudioFamilyHistoryPresentation.Filter(rows,2,"SAME.PNG");
            Ensure(result.Length==1 && result[0].Id==rows[1].Id &&
                StudioFamilyHistoryPresentation.Filter(rows,4,null).Single().Id==rows[2].Id &&
                !result[0].ExactDetails.Contains("ARCHIVE_VERIFIED"),
                "Search/filter may not merge same-named files, claim verification or invent identity.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioFamilyHistoryPresentation.Filter(rows,9,null));
        });
        Check("P6G3F_M4B_HISTORY_FOREIGN_DUPLICATE_REFUSED",() =>
        {
            var session=Guid.NewGuid();var foreign=Guid.NewGuid();var id=Guid.NewGuid();
            DurableRejectItem Reject(Guid sid)=>new(id,Guid.NewGuid(),sid,Guid.NewGuid(),1,
                "original","archive","hash","identity",1,1,"Staged",null);
            Refuse<InvalidDataException>(()=>StudioFamilyHistoryPresentation.Project(session,
                Array.Empty<DurableCopyItem>(),Array.Empty<DurableMoveItem>(),
                Array.Empty<DurableRenameItem>(),new[]{Reject(foreign)}));
            Refuse<InvalidDataException>(()=>StudioFamilyHistoryPresentation.Project(session,
                Array.Empty<DurableCopyItem>(),Array.Empty<DurableMoveItem>(),
                Array.Empty<DurableRenameItem>(),new[]{Reject(session),Reject(session)}));
            Ensure(StudioFamilyHistoryPresentation.CountLabel(
                Array.Empty<StudioRecordedFamilyRow>(),0).Contains("0 recorded files"),
                "An empty session must not masquerade as a completed operation.");
        });
        // M4C: all-family History is the default Studio experience. It is
        // historical triage ONLY, and must not become execution/recovery proof.
        Check("P6G3F_M4C_FAMILY_STATUS_DISJOINT",() =>
        {
            Ensure(StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Reject,"Staged")==StudioRecordedOutcome.Pending &&
                StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Reject,"Approved")==StudioRecordedOutcome.Pending &&
                StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Copy,"Staged")==StudioRecordedOutcome.NeedsAttention &&
                StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Move,"Approved")==StudioRecordedOutcome.NeedsAttention &&
                StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Copy,"Committed")==StudioRecordedOutcome.Completed &&
                StudioFamilyHistoryPresentation.Classify(StudioNativeFamily.Rename,"Undone")==StudioRecordedOutcome.Undone,
                "Only native Reject Stage/Approved may be described as pending. Other states need investigation.");
        });
        Check("P6G3F_M4C_STATUS_AND_FAMILY_FILTER_INTERSECT",() =>
        {
            var session=Guid.NewGuid();
            var rows=new[]{
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Copy,session,1,"Committed","source/a.png","target/a.png","archive",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,session,2,"Undone","source/a.png","target/a.png","retained",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,"Staged","source/b.png","recovery/b.png","owned",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,"Incomplete","source/c.png","recovery/c.png","owned",null)};
            Ensure(StudioFamilyHistoryPresentation.Filter(rows,0,1,"a.png").Length==1 &&
                StudioFamilyHistoryPresentation.Filter(rows,2,2,"a.png")[0].Id==rows[1].Id &&
                StudioFamilyHistoryPresentation.Filter(rows,4,3,"b.png")[0].Id==rows[2].Id &&
                StudioFamilyHistoryPresentation.Filter(rows,0,4,"c.png")[0].Id==rows[3].Id &&
                StudioFamilyHistoryPresentation.Filter(rows,3,3,null).Length==0,
                "Status, family and search filters must intersect without changing row identity.");
        });
        Check("P6G3F_M4C_ERRORS_NEVER_HEALTHY",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var bad=new StudioRecordedFamilyRow(id,StudioNativeFamily.Copy,session,1,"Committed","source","target","archive","MISMATCH");
            var unknown=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Rename,session,2,"Unknown","source","target","",null);
            Ensure(bad.Outcome==StudioRecordedOutcome.NeedsAttention && unknown.Outcome==StudioRecordedOutcome.NeedsAttention &&
                StudioFamilyHistoryPresentation.Filter(new[]{bad,unknown},0,4,null).Length==2 &&
                StudioFamilyHistoryPresentation.Triage(new[]{bad,unknown}).Contains("2 recorded item(s) need attention"),
                "A journal issue must not be hidden by a historical Committed label.");
        });
        Check("P6G3F_M4C_SELECTION_FAMILY_AND_GUID",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var copy=new StudioRecordedFamilyRow(id,StudioNativeFamily.Copy,session,1,"Undone","original","copied","archive",null);
            var reject=new StudioRecordedFamilyRow(id,StudioNativeFamily.Reject,session,0,"Approved","original","owned","retained",null);
            var rows=new[]{copy,reject};
            Ensure(StudioFamilyHistoryPresentation.SelectedIndex(rows,StudioNativeFamily.Copy,id)==0 &&
                StudioFamilyHistoryPresentation.SelectedIndex(rows,StudioNativeFamily.Reject,id)==1 &&
                StudioFamilyHistoryPresentation.SelectedIndex(rows,StudioNativeFamily.Move,id)==-1 &&
                StudioFamilyHistoryPresentation.SelectedIndex(rows,null,id)==-1 &&
                StudioFamilyHistoryPresentation.SelectedIndex(Array.Empty<StudioRecordedFamilyRow>(),StudioNativeFamily.Copy,id)==-1,
                "A same GUID across families cannot retarget a selection or reselect after filtering.");
        });
        Check("P6G3F_M4C_DURABLE_REJECT_TRIAGE",() =>
        {
            var sess=Guid.NewGuid();
            var pending=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,sess,0,"Approved","photo","owned","original",null);
            var message=StudioFamilyHistoryPresentation.Triage(new[]{pending});
            Ensure(message.Contains("durable Reject proposal") && message.Contains("survives leaving this page") &&
                pending.ExactDetails.Contains("closing this page does not cancel it") &&
                !pending.ExactDetails.Contains("ARCHIVE_VERIFIED"),
                "Durable Stage/Approved is neither ephemeral consent nor a verified recovery outcome.");
        });
        Check("P6G3F_M4C_NO_HISTORY_NEVER_ALL_CLEAR",() =>
        {
            var empty=StudioFamilyHistoryPresentation.Triage(Array.Empty<StudioRecordedFamilyRow>());
            Ensure(empty.Contains("Older or foreign-session journals") &&
                !empty.Contains("verified") &&
                StudioFamilyHistoryPresentation.CountLabel(Array.Empty<StudioRecordedFamilyRow>(),0).Contains("0 recorded files"),
                "An empty session browser cannot claim the entire installation is safe.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioFamilyHistoryPresentation.Filter(
                Array.Empty<StudioRecordedFamilyRow>(),0,5,null));
            Refuse<ArgumentOutOfRangeException>(()=>StudioFamilyHistoryPresentation.Filter(
                Array.Empty<StudioRecordedFamilyRow>(),-1,0,null));
        });
        // M5A Collection Lens: photographer-facing saved decisions, no native
        // grants or new filesystem authority. All assertions use in-memory rows.
        Check("P6G3F_M5A_COLLECTION_PROGRESS_COUNTS",() =>
        {
            var rows=new[]{
                new CollectionDecisionRow(Guid.NewGuid(),"/input/a.png",1,"Assigned",'F',"Family"),
                new CollectionDecisionRow(Guid.NewGuid(),"/input/b.png",2,"Unassigned",null,""),
                new CollectionDecisionRow(Guid.NewGuid(),"/input/c.png",3,"RejectReview",null,"")};
            var overview=new CollectionDecisionOverview(rows,1,1,1,2);
            var progress=StudioCollectionLens.Progress(overview);
            Ensure(progress.Contains("1 awaiting choice") && progress.Contains("1 saved folder choices") &&
                progress.Contains("1 marked for review") && progress.Contains("2 removed") &&
                progress.Contains("no file changes") && !progress.Contains("ARCHIVE_VERIFIED"),
                "Counts reflect saved decisions, never native operation or recovery success.");
        });
        Check("P6G3F_M5A_ROUTE_BREAKDOWN_NOT_OUTPUTS",() =>
        {
            var rows=new[]{
                new CollectionDecisionRow(Guid.NewGuid(),"/input/a.jpg",1,"Assigned",'T',"Travel"),
                new CollectionDecisionRow(Guid.NewGuid(),"/input/b.jpg",2,"Assigned",'F',"Family"),
                new CollectionDecisionRow(Guid.NewGuid(),"/input/c.jpg",3,"Assigned",'F',"Family")};
            var text=StudioCollectionLens.Routes(new CollectionDecisionOverview(rows,3,0,0,0));
            Ensure(text.Contains("F · Family: 2 chosen") && text.Contains("T · Travel: 1 chosen") &&
                text.IndexOf("Family",StringComparison.Ordinal)<text.IndexOf("Travel",StringComparison.Ordinal) &&
                text.Contains("Not an output inventory"),
                "Destination summaries are route-key ordered saved choices, not copied files.");
        });
        Check("P6G3F_M5A_EXACT_SELECTION_STATE_DISJOINT",() =>
        {
            var id=Guid.NewGuid();
            var assigned=StudioCollectionLens.ExactSelection(new(id,"/input/same.png",4,"Assigned",'F',"Family"));
            var reject=StudioCollectionLens.ExactSelection(new(id,"/input/same.png",4,"RejectReview",null,""));
            var undecided=StudioCollectionLens.ExactSelection(new(id,"/input/same.png",4,"Unassigned",null,""));
            Ensure(assigned.Contains("Saved folder choice") && reject.Contains("No native Reject move") &&
                undecided.Contains("No destination approved") &&
                assigned.Contains(id.ToString()) && assigned.Contains("/input/same.png") &&
                !undecided.Contains("approved file operation"),
                "Exact original and each saved state must remain distinct from native consent.");
        });
        Check("P6G3F_M5A_SAME_FILENAME_REQUIRES_PHOTO_ID",() =>
        {
            var x=new CollectionDecisionRow(Guid.NewGuid(),"/one/same.jpg",1,"Unassigned",null,"");
            var y=new CollectionDecisionRow(Guid.NewGuid(),"/two/same.jpg",2,"Unassigned",null,"");
            Ensure(StudioCollectionLens.RequireFresh(y,new[]{x,y}).PhotoId==y.PhotoId,
                "Basenames are not identity, even when filenames match.");
            Refuse<InvalidDataException>(()=>StudioCollectionLens.RequireFresh(y,new[]{x}));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.RequireFresh(y,new[]{x,y,y}));
        });
        Check("P6G3F_M5A_SELECTION_DRIFT_REFUSED",() =>
        {
            var original=new CollectionDecisionRow(Guid.NewGuid(),"/a/photo.png",2,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioCollectionLens.RequireFresh(original,new[]{original with {Position=3}}));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.RequireFresh(original,new[]{original with {Path="/b/photo.png"}}));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.RequireFresh(original,new[]{original with {State="Assigned",RouteKey='F',RouteLabel="Family"}}));
            Ensure(StudioCollectionLens.RequireFresh(original,new[]{original})==original,
                "Navigation must refuse drift in position, imported path or saved decision.");
        });
        Check("P6G3F_M5A_NEXT_UNDECIDED_STABLE_ORDER",() =>
        {
            var assigned=new CollectionDecisionRow(Guid.NewGuid(),"/a.jpg",1,"Assigned",'F',"Family");
            var late=new CollectionDecisionRow(Guid.NewGuid(),"/z.jpg",7,"Unassigned",null,"");
            var early=new CollectionDecisionRow(Guid.NewGuid(),"/b.jpg",2,"Unassigned",null,"");
            Ensure(StudioCollectionLens.NextUndecided(new[]{late,assigned,early})?.PhotoId==early.PhotoId &&
                StudioCollectionLens.NextUndecided(new[]{assigned}) is null,
                "Continue directs to earliest undecided position, never automatically changes a choice.");
            Refuse<InvalidDataException>(()=>StudioCollectionLens.NextUndecided(new[]{late,late}));
        });
        Check("P6G3F_M5A_MALFORMED_ROWS_AND_TOTALS_FAIL_CLOSED",() =>
        {
            var row=new CollectionDecisionRow(Guid.NewGuid(),"/p.jpg",1,"Assigned",'F',"Family");
            Refuse<InvalidDataException>(()=>StudioCollectionLens.Progress(new CollectionDecisionOverview(new[]{row},0,0,1,0)));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.Routes(new CollectionDecisionOverview(new[]{row,row},2,0,0,0)));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.ExactSelection(row with {RouteKey=null}));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.ExactSelection(row with {PhotoId=Guid.Empty}));
            Refuse<InvalidDataException>(()=>StudioCollectionLens.ExactSelection(row with {Position=0}));
        });
        // M5B selected photographic preview stays in the ONE existing bounded
        // decoder lane. No thumbnail crawler, auto-preview on row refresh,
        // filesystem mutations or data-backed test photographs.
        Check("P6G3F_M5B_TICKET_EXACT_IMPORTED_IDENTITY",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/portrait.jpg",2,"Unassigned",null,"");
            var p=new Photo(row.Path,64,123456){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,7);
            var request=new PreviewWorker.Request(id,row.Path,64,123456,7);
            Ensure(ticket.Matches(session,row,new[]{row},p,request),"Exact on-demand preview ticket rejected.");
        });
        Check("P6G3F_M5B_CROSS_SESSION_PREVIEW_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.png",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            Ensure(!ticket.Matches(Guid.NewGuid(),row,new[]{row},p,
                new PreviewWorker.Request(id,row.Path,10,222,10)),
                "Foreign session was allowed to display a photo preview.");
        });
        Check("P6G3F_M5B_STALE_DECODE_GENERATION_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.jpg",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            Ensure(!ticket.Matches(session,row,new[]{row},p,
                new PreviewWorker.Request(id,row.Path,10,222,9)),
                "Old decode generation was allowed to replace a newer preview.");
        });
        Check("P6G3F_M5B_SELECTION_AND_SAVED_DECISION_DRIFT_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.jpg",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            var req=new PreviewWorker.Request(id,row.Path,10,222,10);
            Ensure(!ticket.Matches(session,row with {RouteKey='F',RouteLabel="Family",State="Assigned"},
                    new[]{row},p,req) &&
                !ticket.Matches(session,row,new[]{row with {Position=2}},p,req) &&
                !ticket.Matches(session,null,new[]{row},p,req),
                "Saved selection drift did not invalidate the preview.");
        });
        Check("P6G3F_M5B_SOURCE_VERSION_AND_PATH_DRIFT_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.jpg",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            var req=new PreviewWorker.Request(id,row.Path,10,222,10);
            Ensure(!ticket.Matches(session,row,new[]{row},new Photo(row.Path,11,222){Id=id},req) &&
                !ticket.Matches(session,row,new[]{row},p,req with {WriteTicks=223}) &&
                !ticket.Matches(session,row,new[]{row},p,req with {Path="/photos/other.jpg"}),
                "Original metadata or path drift did not revoke preview.");
        });
        Check("P6G3F_M5B_DUPLICATE_AND_ALIASED_SELECTION_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.jpg",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            var req=new PreviewWorker.Request(id,row.Path,10,222,10);
            Ensure(!ticket.Matches(session,row,new[]{row,row},p,req),
                "Duplicate stable photo identities were accepted.");
            Refuse<InvalidDataException>(()=>StudioCollectionPreviewTicket.Issue(session,row,
                new Photo("/photos/other.jpg",10,222){Id=id},10));
            Refuse<InvalidDataException>(()=>StudioCollectionPreviewTicket.Issue(Guid.Empty,row,p,10));
        });
        Check("P6G3F_M5B_WRONG_PHOTO_AND_MISSING_PREVIEW_REFUSED",() =>
        {
            var id=Guid.NewGuid();var session=Guid.NewGuid();
            var row=new CollectionDecisionRow(id,"/photos/a.jpg",1,"Unassigned",null,"");
            var p=new Photo(row.Path,10,222){Id=id};
            var ticket=StudioCollectionPreviewTicket.Issue(session,row,p,10);
            var wrong=new CollectionDecisionRow(Guid.NewGuid(),"/photos/a.jpg",1,"Unassigned",null,"");
            Ensure(!ticket.Matches(session,wrong,new[]{row},p,
                 new PreviewWorker.Request(id,row.Path,10,222,10)) &&
                !ticket.Matches(session,row,new[]{row},null,
                 new PreviewWorker.Request(id,row.Path,10,222,10)),
                "Duplicate basenames or missing imported records retargeted preview.");
        });
        // M5C: fast deliberate photographic review; even on the same filename
        // each step uses the exact filtered saved-photo identity and never grants Apply.
        Check("P6G3F_M5C_FORWARD_BACKWARD_EXACT_ORDER",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",3,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/a/two.jpg",4,"Assigned",'F',"Family");
            var c=new CollectionDecisionRow(Guid.NewGuid(),"/a/three.jpg",8,"Unassigned",null,"");
            var rows=new[]{a,b,c};
            Ensure(StudioCollectionReviewDeck.Adjacent(rows,a,rows,1) is {Index:1, Row:var x} && x.PhotoId==b.PhotoId &&
                StudioCollectionReviewDeck.Adjacent(rows,c,rows,-1) is {Index:1, Row:var y} && y.PhotoId==b.PhotoId,
                "Adjacent review must use visible order and stable IDs.");
        });
        Check("P6G3F_M5C_BOUNDARIES_NO_WRAP_OR_MUTATION",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/a/two.jpg",2,"Unassigned",null,"");
            var rows=new[]{a,b};
            Ensure(StudioCollectionReviewDeck.Adjacent(rows,a,rows,-1) is null &&
                StudioCollectionReviewDeck.Adjacent(rows,b,rows,1) is null,
                "Boundaries cannot wrap to an unrelated photograph.");
        });
        Check("P6G3F_M5C_DUPLICATE_BASENAMES_USE_ID",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/one/shot.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/two/shot.jpg",2,"Unassigned",null,"");
            Ensure(StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,new[]{a,b},1)?.Row.PhotoId==b.PhotoId,
                "Duplicate filenames cannot alias the preview selection.");
        });
        Check("P6G3F_M5C_CHANGED_SELECTED_DECISION_REFUSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/a/two.jpg",2,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,
                new[]{a with {State="Assigned",RouteKey='T',RouteLabel="Travel"},b},1));
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,
                new[]{a with {Path="/a/replaced.jpg"},b},1));
        });
        Check("P6G3F_M5C_CHANGED_TARGET_DECISION_REFUSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/a/two.jpg",2,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,
                new[]{a,b with {Position=8}},1));
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,
                new[]{a,b with {Path="/a/other.jpg"}},1));
        });
        Check("P6G3F_M5C_DUPLICATE_AND_MISSING_IDENTITIES_REFUSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/a/two.jpg",2,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,a},a,new[]{a,b},1));
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b with {Position=1}},a,new[]{a,b},1));
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{b},a,new[]{a,b},1));
            Refuse<InvalidDataException>(()=>StudioCollectionReviewDeck.Adjacent(new[]{a,b},a,new[]{a},1));
        });
        Check("P6G3F_M5C_ONE_STEP_ONLY_AND_POSITION_FEEDBACK",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/a/one.jpg",1,"Unassigned",null,"");
            var rows=new[]{a};
            Refuse<ArgumentOutOfRangeException>(()=>StudioCollectionReviewDeck.Adjacent(rows,a,rows,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioCollectionReviewDeck.Adjacent(rows,a,rows,2));
            Ensure(StudioCollectionReviewDeck.Position(rows,0).Contains("Photograph 1 of 1") &&
                StudioCollectionReviewDeck.Position(rows,-1).Contains("Select one") &&
                !StudioCollectionReviewDeck.Position(rows,0).Contains("approved"),
                "Review position must not claim native approval.");
        });
        // M5D: paged on-demand photographic contact sheet; no implicit background decode.
        Check("P6G3F_M5D_PAGE_BOUNDED_FOUR",() =>
        {
            var rows=Enumerable.Range(1,9).Select(i=>new CollectionDecisionRow(
                Guid.NewGuid(),$"/shot/{i}.jpg",i,"Unassigned",null,"")).ToArray();
            var one=StudioContactSheetPolicy.Page(rows,rows,0);
            var two=StudioContactSheetPolicy.Page(rows,rows,4);
            var three=StudioContactSheetPolicy.Page(rows,rows,8);
            Ensure(one.Rows.Count==4 && two.Rows.Count==4 && three.Rows.Count==1 &&
                StudioContactSheetPolicy.PageSize==4 && one.Total==9,
                "Contact sheet must never retain more than four page entries.");
        });
        Check("P6G3F_M5D_BOUNDARY_AND_NO_WRAP",() =>
        {
            var rows=Enumerable.Range(1,5).Select(i=>new CollectionDecisionRow(
                Guid.NewGuid(),$"/shot/{i}.jpg",i,"Unassigned",null,"")).ToArray();
            var first=StudioContactSheetPolicy.Page(rows,rows,0);
            var last=StudioContactSheetPolicy.Page(rows,rows,4);
            Ensure(StudioContactSheetPolicy.Step(first,-1) is null &&
                StudioContactSheetPolicy.Step(first,1)==4 &&
                StudioContactSheetPolicy.Step(last,1) is null &&
                StudioContactSheetPolicy.Step(last,-1)==0,"Contact sheet page boundary wrapped.");
        });
        Check("P6G3F_M5D_INVALID_OFFSET_FAILS_CLOSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/shot/a.jpg",1,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioContactSheetPolicy.Page(new[]{a},new[]{a},-1));
            Refuse<InvalidDataException>(()=>StudioContactSheetPolicy.Page(new[]{a},new[]{a},1));
            Refuse<InvalidDataException>(()=>StudioContactSheetPolicy.Page(new[]{a},new[]{a},4));
        });
        Check("P6G3F_M5D_IDENTITIES_AND_POSITIONS_UNIQUE",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/shot/a.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/shot/b.jpg",2,"Unassigned",null,"");
            Refuse<InvalidDataException>(()=>StudioContactSheetPolicy.Page(new[]{a,a},new[]{a,b},0));
            Refuse<InvalidDataException>(()=>StudioContactSheetPolicy.Page(new[]{a,b with {Position=1}},new[]{a,b},0));
        });
        Check("P6G3F_M5D_SAVED_DECISION_DRIFT_REFUSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/shot/a.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/shot/b.jpg",2,"Unassigned",null,"");
            var page=StudioContactSheetPolicy.Page(new[]{a,b},new[]{a,b},0);
            Ensure(!StudioContactSheetPolicy.SamePage(page,new[]{a,b},
                new[]{a,b with {State="Assigned",RouteKey='A',RouteLabel="Album"}}),
                "Saved decision changed but sheet remains valid.");
        });
        Check("P6G3F_M5D_FILTERED_ORDER_AND_PATH_DRIFT_REFUSED",() =>
        {
            var a=new CollectionDecisionRow(Guid.NewGuid(),"/shot/a.jpg",1,"Unassigned",null,"");
            var b=new CollectionDecisionRow(Guid.NewGuid(),"/shot/b.jpg",2,"Unassigned",null,"");
            var page=StudioContactSheetPolicy.Page(new[]{a,b},new[]{a,b},0);
            Ensure(!StudioContactSheetPolicy.SamePage(page,new[]{b,a},new[]{a,b}) &&
                !StudioContactSheetPolicy.SamePage(page,new[]{a,b},new[]{a,b with {Path="/shot/new.jpg"}}),
                "Filtered reordering or path drift retargeted page.");
        });
        Check("P6G3F_M5D_EMPTY_PAGE_AND_DIRECTION_GUARDS",() =>
        {
            var empty=StudioContactSheetPolicy.Page(Array.Empty<CollectionDecisionRow>(),
                Array.Empty<CollectionDecisionRow>(),0);
            Ensure(empty.Rows.Count==0 && empty.Total==0 && empty.Label.Contains("No photographs"),
                "Empty Collection implied preview content.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioContactSheetPolicy.Step(empty,2));
        });
        // M6D: deterministic presentation-only UI decisions; no file access.
        Check("P6G3F_M6D_COMPLETED_SHEET_NOT_LOADING",() =>
        {
            Ensure(StudioExperiencePresentation.ContactSheetCompletion(4,0)==
                "4 previews ready · original photographs unchanged.","Completed previews still appear loading.");
            Ensure(!StudioExperiencePresentation.ContactSheetCompletion(4,0).Contains("loading",
                StringComparison.OrdinalIgnoreCase),"A completed sheet has a stale loading status.");
        });
        Check("P6G3F_M6D_PARTIAL_PREVIEW_FAILURE_VISIBLE",() =>
        {
            var label=StudioExperiencePresentation.ContactSheetCompletion(4,1);
            Ensure(label.Contains("3 previews ready") && label.Contains("1 unavailable"),
                "Partial decode failure disguised as complete success.");
        });
        Check("P6G3F_M6D_ALL_FAILED_NEVER_SUCCESS",() =>
        {
            var label=StudioExperiencePresentation.ContactSheetCompletion(2,2);
            Ensure(label.Contains("Could not preview 2") && !label.Contains("ready"),
                "Failed previews were reported as ready.");
        });
        Check("P6G3F_M6D_INVALID_COMPLETION_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.ContactSheetCompletion(3,4));
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.ContactSheetCompletion(-1,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.ContactSheetCompletion(3,-1));
        });
        Check("P6G3F_M6D_RESPONSIVE_GALLERY_BOUNDED",() =>
        {
            double narrow=StudioExperiencePresentation.GalleryTileWidth(420);
            double wide=StudioExperiencePresentation.GalleryTileWidth(1650);
            Ensure(narrow>=185 && narrow<=390 && wide>=185 && wide<=390 &&
                wide>200,"Gallery tile sizes are not bounded and photo-prominent.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.GalleryTileWidth(double.NaN));
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.GalleryTileWidth(0));
        });
        Check("P6G3F_M6D_EMPTY_HISTORY_NOT_FAILURE",() =>
        {
            Ensure(StudioExperiencePresentation.ShowEmptyHistory(0,true),"Empty-session state absent.");
            Ensure(!StudioExperiencePresentation.ShowEmptyHistory(0,false) &&
                !StudioExperiencePresentation.ShowEmptyHistory(1,true),
                "Unavailable or populated history displayed as empty success.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.ShowEmptyHistory(-1,true));
        });
        // M6E: cross-DPI window placement policy and measured photo layout.
        // Pure presentation tests; actual native caption requires owner GUI.
        Check("P6G3F_M6E_WINDOW_CENTER_INSIDE_WORKAREA",() =>
        {
            var p=StudioWindowPlacementPolicy.CenterInWorkArea(0,0,1280,720,
                1.0,1360,780,640,480);
            Ensure(p.WidthDip<=1256 && p.HeightDip<=696 &&
                p.XPixel>=0 && p.YPixel>=0 && p.XPixel+(int)Math.Ceiling(p.WidthDip)<=1280 &&
                p.YPixel+(int)Math.Ceiling(p.HeightDip)<=720,
                "Window bounds escape primary work area.");
        });
        Check("P6G3F_M6E_WINDOW_DPI_AND_OFFSET",() =>
        {
            var p=StudioWindowPlacementPolicy.CenterInWorkArea(-1920,22,1920,1030,
                1.5,1360,780,640,480);
            Ensure(p.WidthDip<=1920/1.5 && p.HeightDip<=1030/1.5 &&
                p.XPixel>=-1920 && p.YPixel>=22 &&
                p.XPixel+(int)Math.Ceiling(p.WidthDip*1.5)<=0 &&
                p.YPixel+(int)Math.Ceiling(p.HeightDip*1.5)<=1052,
                "Scaled secondary-monitor placement escapes work area.");
        });
        Check("P6G3F_M6E_WINDOW_INVALID_SCALE_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioWindowPlacementPolicy.CenterInWorkArea(
                0,0,1280,720,0,1360,780,640,480));
            Refuse<ArgumentOutOfRangeException>(()=>StudioWindowPlacementPolicy.CenterInWorkArea(
                0,0,1280,720,double.NaN,1360,780,640,480));
            Refuse<ArgumentOutOfRangeException>(()=>StudioWindowPlacementPolicy.CenterInWorkArea(
                0,0,0,720,1,1360,780,640,480));
        });
        Check("P6G3F_M6E_GALLERY_MEASURED_FOUR_UP",() =>
        {
            var wide=StudioExperiencePresentation.GalleryTileWidth(1200);
            var narrow=StudioExperiencePresentation.GalleryTileWidth(510);
            Ensure(4*(wide+26)<=1200 && wide>=170 &&
                narrow>=170 && narrow<=390,
                "Measured gallery cards overflow available panel width.");
        });
        Check("P6G3F_M6E_GALLERY_INVALID_WIDTH_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.GalleryTileWidth(double.NaN));
            Refuse<ArgumentOutOfRangeException>(()=>StudioExperiencePresentation.GalleryTileWidth(-1));
        });
        // M6F: actual card placement is assigned as explicit Grid row/column,
        // eliminating WrapPanel-dependent surprise wrapping. Source/owner GUI
        // gates must also verify real Avalonia arranged bounds; these are pure.
        Check("P6G3F_M6F_FOUR_VISIBLE_CARDS_ONE_GRID_ROW",() =>
        {
            var p=StudioGalleryGridPolicy.Plan(1480,4);
            Ensure(p.Columns==4 && p.Rows==1 &&
                p.RowOf(0)==0 && p.RowOf(3)==0 &&
                p.ColumnOf(0)==0 && p.ColumnOf(3)==3,
                "Wide gallery failed explicit four-cell row assignment.");
        });
        Check("P6G3F_M6F_RESPONSIVE_GRID_PLACEMENT",() =>
        {
            var p3=StudioGalleryGridPolicy.Plan(1000,4);
            var p2=StudioGalleryGridPolicy.Plan(760,4);
            var p1=StudioGalleryGridPolicy.Plan(460,4);
            Ensure(p3.Columns==3 && p3.Rows==2 && p3.RowOf(3)==1 &&
                p2.Columns==2 && p2.Rows==2 && p2.ColumnOf(3)==1 &&
                p1.Columns==1 && p1.Rows==4 && p1.RowOf(3)==3,
                "Responsive grid mapping or row count is wrong.");
        });
        Check("P6G3F_M6F_FINAL_PAGE_ONLY_TWO_GRID_CELLS",() =>
        {
            var p=StudioGalleryGridPolicy.Plan(1600,2);
            Ensure(p.Columns==2 && p.Rows==1 && p.ColumnOf(1)==1,
                "Last page created phantom cards.");
        });
        Check("P6G3F_M6F_REFUSE_INVALID_LAYOUT_INPUT",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioGalleryGridPolicy.Plan(double.NaN,4));
            Refuse<ArgumentOutOfRangeException>(()=>StudioGalleryGridPolicy.Plan(0,4));
            Refuse<ArgumentOutOfRangeException>(()=>StudioGalleryGridPolicy.Plan(1000,-1));
            Refuse<ArgumentOutOfRangeException>(()=>StudioGalleryGridPolicy.Plan(1000,5));
        });
        Check("P6G3F_M6F_GRID_SELECTION_INDICES_FAIL_CLOSED",() =>
        {
            var p=StudioGalleryGridPolicy.Plan(1300,4);
            Refuse<ArgumentOutOfRangeException>(()=>p.RowOf(4));
            Refuse<ArgumentOutOfRangeException>(()=>p.ColumnOf(-1));
            var empty=StudioGalleryGridPolicy.Plan(800,0);
            Ensure(empty.Rows==0,"Empty Gallery has phantom row.");
            Refuse<ArgumentOutOfRangeException>(()=>empty.ColumnOf(0));
        });
        // M6A: contained ordinary Rename consent has NO native authority;
        // snapshot drift and re-entrant/late callbacks must always fail closed.
        Check("P6G3F_M6A_RENAME_FROZEN_EXACT_NATIVE_PATHS",() =>
        {
            var sid=Guid.NewGuid();var pid=Guid.NewGuid();
            var dir=Path.Combine(Path.GetTempPath(),"photosift-m6a-generated");
            var from=Path.Combine(dir,"sample.png");var to=Path.Combine(dir,"sample-renamed.png");
            var proposal=new RenameProposal(Guid.NewGuid(),sid,pid,42,from,to,new string('A',64),
                "GENERATED-ID-ONLY",128,456);
            StudioRenameReviewGate.RequireFrozen(proposal,sid,42,pid,from);
        });
        Check("P6G3F_M6A_RENAME_REVOKES_WRONG_SESSION_OR_PHOTO",() =>
        {
            var sid=Guid.NewGuid();var pid=Guid.NewGuid();
            var src=Path.Combine(Path.GetTempPath(),"m6a", "a.png");
            var to=Path.Combine(Path.GetTempPath(),"m6a", "b.png");
            var p=new RenameProposal(Guid.NewGuid(),sid,pid,9,src,to,new string('B',64),"GENERATED-ID",3,4);
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p,Guid.NewGuid(),9,pid,src));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p,sid,9,Guid.NewGuid(),src));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p,sid,9,pid,"other.png"));
        });
        Check("P6G3F_M6A_RENAME_REVOKES_REVISION_AND_EMPTY_ID",() =>
        {
            var sid=Guid.NewGuid();var pid=Guid.NewGuid();
            var src=Path.Combine(Path.GetTempPath(),"m6a","a.png");var to=Path.Combine(Path.GetTempPath(),"m6a","b.png");
            var p=new RenameProposal(Guid.NewGuid(),sid,pid,7,src,to,new string('C',64),"GENERATED-ID",5,2);
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p,sid,8,pid,src));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with {Id=Guid.Empty},sid,7,pid,src));
        });
        Check("P6G3F_M6A_RENAME_REJECTS_DIRECTORY_AND_EXTENSION",() =>
        {
            var sid=Guid.NewGuid();var pid=Guid.NewGuid();
            var dir=Path.Combine(Path.GetTempPath(),"m6a");var from=Path.Combine(dir,"a.png");
            var p=new RenameProposal(Guid.NewGuid(),sid,pid,2,from,Path.Combine(dir,"b.png"),
                new string('D',64),"GENERATED-ID",5,2);
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with
                {Target=Path.Combine(Path.GetTempPath(),"different","b.png")},sid,2,pid,from));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with
                {Target=Path.Combine(dir,"b.jpg")},sid,2,pid,from));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with
                {Target=from},sid,2,pid,from));
        });
        Check("P6G3F_M6A_RENAME_REJECTS_MISSING_PROOF",() =>
        {
            var sid=Guid.NewGuid();var pid=Guid.NewGuid();
            var dir=Path.Combine(Path.GetTempPath(),"m6a");var src=Path.Combine(dir,"a.png");
            var p=new RenameProposal(Guid.NewGuid(),sid,pid,5,src,Path.Combine(dir,"b.png"),
                new string('E',64),"GENERATED-ID",5,2);
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with {Hash=""},sid,5,pid,src));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with {NativeIdentity=""},sid,5,pid,src));
            Refuse<InvalidDataException>(()=>StudioRenameReviewGate.RequireFrozen(p with {Length=-1},sid,5,pid,src));
        });
        Check("P6G3F_M6A_RENAME_LEASE_SINGLE_USE_AND_NO_REENTRANCE",() =>
        {
            var lease=new StudioRenameReviewLease();var ticket=lease.Begin();
            Refuse<InvalidOperationException>(()=>lease.Begin());
            Ensure(lease.Active && !lease.DecisionPending && lease.TryDecide(ticket) &&
                lease.DecisionPending && !lease.TryDecide(ticket),"Review double-granted one decision.");
            lease.End(ticket);
            Ensure(!lease.Active && !lease.TryDecide(ticket),"Ended Rename review reused decision.");
        });
        Check("P6G3F_M6A_RENAME_LEASE_WRONG_AND_STALE_OWNER",() =>
        {
            var lease=new StudioRenameReviewLease();var first=lease.Begin();
            Ensure(!lease.TryDecide(first+1),"Wrong Rename ticket approved.");
            Refuse<InvalidOperationException>(()=>lease.End(first+1));
            Ensure(lease.Active, "Wrong callback released review owner.");
            lease.End(first);
            var next=lease.Begin();
            Ensure(!lease.TryDecide(first) && lease.TryDecide(next),"Stale Rename ticket approved.");
            lease.End(next);
        });
        // M6B: phase-tagged contained Reject consent never becomes a reusable
        // native grant. No actual file operation is invoked by these tests.
        Check("P6G3F_M6B_REJECT_THREE_DURABLE_PHASES_DISTINCT",() =>
        {
            Ensure(StudioRejectReviewGate.ExpectedState(StudioRejectPhase.ApproveStaged)=="Staged" &&
                StudioRejectReviewGate.ExpectedEvidence(StudioRejectPhase.ApproveStaged)=="UNTOUCHED_SOURCE_VERIFIED" &&
                StudioRejectReviewGate.ExpectedState(StudioRejectPhase.ExecuteApproved)=="Approved" &&
                StudioRejectReviewGate.ExpectedEvidence(StudioRejectPhase.ExecuteApproved)=="UNTOUCHED_SOURCE_VERIFIED" &&
                StudioRejectReviewGate.ExpectedState(StudioRejectPhase.RestoreCommitted)=="Committed" &&
                StudioRejectReviewGate.ExpectedEvidence(StudioRejectPhase.RestoreCommitted)=="OWNED_ARCHIVE_VERIFIED",
                "Reject phases conflated approval, native execution or verified Undo.");
        });
        Check("P6G3F_M6B_REJECT_EXACT_DURABLE_ROW",() =>
        {
            var sid=Guid.NewGuid();var install=Guid.NewGuid();var pid=Guid.NewGuid();
            var origin=Path.Combine(Path.GetTempPath(),"m6b-generated","original.png");
            var owned=Path.Combine(Path.GetTempPath(),"m6b-generated","owned","original.png");
            var row=new DurableRejectItem(Guid.NewGuid(),install,sid,pid,11,origin,owned,
                new string('A',64),"GENERATED-NATIVE-ID",31,123,"Staged");
            StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.ApproveStaged,sid,install,pid);
            StudioRejectReviewGate.RequireExact(row with {State="Approved"},StudioRejectPhase.ExecuteApproved,sid,install,pid);
            StudioRejectReviewGate.RequireExact(row with {State="Committed"},StudioRejectPhase.RestoreCommitted,sid,install,pid);
        });
        Check("P6G3F_M6B_REJECT_FOREIGN_SESSION_INSTALLATION_PHOTO",() =>
        {
            var sid=Guid.NewGuid();var install=Guid.NewGuid();var pid=Guid.NewGuid();
            var row=new DurableRejectItem(Guid.NewGuid(),install,sid,pid,1,
                Path.Combine(Path.GetTempPath(),"m6b","o.png"),
                Path.Combine(Path.GetTempPath(),"m6b","owned.png"),new string('B',64),"ID",4,8,"Staged");
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.ApproveStaged,Guid.NewGuid(),install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.ApproveStaged,sid,Guid.NewGuid(),pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.ApproveStaged,sid,install,Guid.NewGuid()));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {Id=Guid.Empty},StudioRejectPhase.ApproveStaged,sid,install,pid));
        });
        Check("P6G3F_M6B_REJECT_PHASE_MISMATCHES_REFUSED",() =>
        {
            var sid=Guid.NewGuid();var install=Guid.NewGuid();var pid=Guid.NewGuid();
            var row=new DurableRejectItem(Guid.NewGuid(),install,sid,pid,1,
                Path.Combine(Path.GetTempPath(),"m6b","o.png"),
                Path.Combine(Path.GetTempPath(),"m6b","archive.png"),new string('C',64),"ID",1,1,"Staged");
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.ExecuteApproved,sid,install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row,StudioRejectPhase.RestoreCommitted,sid,install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {State="Committed",Error="uncertain"},StudioRejectPhase.RestoreCommitted,sid,install,pid));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRejectReviewGate.ExpectedState((StudioRejectPhase)99));
        });
        Check("P6G3F_M6B_REJECT_MISSING_OR_ALIASED_OWNERSHIP_REFUSED",() =>
        {
            var sid=Guid.NewGuid();var install=Guid.NewGuid();var pid=Guid.NewGuid();
            var src=Path.Combine(Path.GetTempPath(),"m6b","photo.png");
            var row=new DurableRejectItem(Guid.NewGuid(),install,sid,pid,1,src,
                Path.Combine(Path.GetTempPath(),"m6b","owned.png"),new string('D',64),"ID",2,3,"Staged");
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {Archive=src},StudioRejectPhase.ApproveStaged,sid,install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {SourceHash=new string('Z',64)},StudioRejectPhase.ApproveStaged,sid,install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {SourceIdentity=""},StudioRejectPhase.ApproveStaged,sid,install,pid));
            Refuse<InvalidDataException>(()=>StudioRejectReviewGate.RequireExact(row with {SourceLength=0},StudioRejectPhase.ApproveStaged,sid,install,pid));
        });
        Check("P6G3F_M6B_REJECT_LEASE_PHASE_SINGLE_USE",() =>
        {
            var lease=new StudioRejectReviewLease();var id=lease.Begin(StudioRejectPhase.ExecuteApproved);
            Refuse<InvalidOperationException>(()=>lease.Begin(StudioRejectPhase.ApproveStaged));
            Ensure(!lease.TryDecide(id,StudioRejectPhase.ApproveStaged) &&
                lease.TryDecide(id,StudioRejectPhase.ExecuteApproved) &&
                !lease.TryDecide(id,StudioRejectPhase.ExecuteApproved) &&
                lease.DecisionPending,"Wrong phase or repeated decision accepted.");
            lease.End(id);
            Ensure(!lease.Active && !lease.TryDecide(id,StudioRejectPhase.ExecuteApproved),
                "Ended Reject consent reused.");
        });
        Check("P6G3F_M6B_REJECT_LEASE_STALE_OWNER_AND_REENTRANCE",() =>
        {
            var lease=new StudioRejectReviewLease();var first=lease.Begin(StudioRejectPhase.ApproveStaged);
            Ensure(!lease.TryDecide(first+1,StudioRejectPhase.ApproveStaged),"Wrong ticket approved.");
            Refuse<InvalidOperationException>(()=>lease.End(first+1));
            Ensure(lease.Active,"Wrong owner ended active review.");
            lease.End(first);var second=lease.Begin(StudioRejectPhase.RestoreCommitted);
            Ensure(!lease.TryDecide(first,StudioRejectPhase.ApproveStaged) &&
                !lease.TryDecide(second,StudioRejectPhase.ExecuteApproved) &&
                lease.TryDecide(second,StudioRejectPhase.RestoreCommitted),
                "Stale phase granted new review.");
            lease.End(second);
        });
        // M6H: pure, non-authoritative source-picker and onboarding policy.
        // These tests do not grant any filesystem operation or replace GUI proof.
        Check("P6G3F_M6H_SOURCE_PICKER_EXPLICIT_CHOICES",() =>
        {
            Ensure(StudioImportGuidance.RequireSource(0)==StudioImportSource.Cancel &&
                StudioImportGuidance.RequireSource(1)==StudioImportSource.Files &&
                StudioImportGuidance.RequireSource(2)==StudioImportSource.Folder,
                "Source picker branches are ambiguous.");
        });
        Check("P6G3F_M6H_SOURCE_PICKER_UNKNOWN_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportGuidance.RequireSource(-1));
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportGuidance.RequireSource(3));
        });
        Check("P6G3F_M6H_NO_OUTPUT_NEXT_STEP",() =>
        {
            Ensure(StudioImportGuidance.Describe(2,false,false).Contains("results folder") &&
                StudioImportGuidance.Describe(2,false,false).Contains("originals") &&
                !StudioImportGuidance.Describe(2,false,false).Contains("Apply"),
                "Missing output folder messaging is misleading.");
        });
        Check("P6G3F_M6H_READY_AND_EMPTY_STATES",() =>
        {
            Ensure(StudioImportGuidance.Describe(0,true,false).Contains("Choose individual") &&
                StudioImportGuidance.Describe(2,true,false).Contains("review exact paths") &&
                StudioImportGuidance.Describe(0,false,false).Contains("Add your photos"),
                "Setup guidance does not match imported/result state.");
        });
        Check("P6G3F_M6H_HISTORY_LOCK_TAKES_PRIORITY",() =>
        {
            Ensure(StudioImportGuidance.Describe(2,false,true).Contains("History & recovery") &&
                StudioImportGuidance.Describe(0,true,true).Contains("History & recovery"),
                "Historical operation lock hidden by onboarding copy.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportGuidance.Describe(-1,true,false));
        });
        // M6H2: presentation-only regression surface. These helpers have no
        // file access or mutation permissions. Existing native tests stay intact.
        Check("P6G3F_M6H2_EMPTY_HISTORY_STAYS_CAUTIONARY",() =>
        {
            var label=StudioOperationOverview.Summary(Array.Empty<StudioRecordedFamilyRow>());
            Ensure(label.Contains("No file operations recorded in this session") &&
                label.Contains("Earlier recovery history") &&
                StudioOperationOverview.Recent(Array.Empty<StudioRecordedFamilyRow>()).Length==0,
                "Empty session implied global recovery clearance.");
        });
        Check("P6G3F_M6H2_MIXED_FAMILY_RECORDED_COUNTS",() =>
        {
            var session=Guid.NewGuid();
            var rows=new[]{
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Copy,session,1,"Committed","/a/one.jpg","/b/one.jpg","",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,session,2,"Undone","/a/two.jpg","/b/two.jpg","/r/two.jpg",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Rename,session,3,"Committed","/a/three.jpg","/a/rename.jpg","",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,"Approved","/a/four.jpg","","/r/four.jpg",null)};
            var result=StudioOperationOverview.Summary(rows);
            var recent=StudioOperationOverview.Recent(rows);
            Ensure(result.Contains("4 recorded") && result.Contains("2 completed") &&
                result.Contains("1 undone") && result.Contains("1 pending") &&
                recent.Length==4 && recent.Any(x=>x.StartsWith("Copy")) &&
                recent.Any(x=>x.StartsWith("Move")) &&
                recent.Any(x=>x.StartsWith("Rename")) &&
                recent.Any(x=>x.StartsWith("Reject")),"Recorded native families were hidden.");
        });
        Check("P6G3F_M6H2_ATTENTION_NOT_SILENT_SUCCESS",() =>
        {
            var row=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,Guid.NewGuid(),1,
                "Ambiguous","/a/x.jpg","/b/x.jpg","","Identity uncertain");
            Ensure(StudioOperationOverview.Summary(new[]{row}).Contains("1 need attention") &&
                StudioOperationOverview.Recent(new[]{row})[0].Contains("Needs attention"),
                "Ambiguous journal classified as success.");
        });
        Check("P6G3F_M6H2_RECENT_SUMMARY_BOUNDED",() =>
        {
            var id=Guid.NewGuid();
            var rows=Enumerable.Range(0,9).Select(i=>new StudioRecordedFamilyRow(Guid.NewGuid(),
                StudioNativeFamily.Copy,id,i,"Committed","/s/"+i+".png","/d/"+i+".png","",null)).ToArray();
            Ensure(StudioOperationOverview.Recent(rows).Length==4 &&
                StudioOperationOverview.Recent(rows,8).Length==8 &&
                StudioOperationOverview.Recent(rows,1).Length==1,
                "Changes list is unbounded or lost history accounting.");
        });
        Check("P6G3F_M6H2_INVALID_SUMMARY_LIMIT_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioOperationOverview.Recent(Array.Empty<StudioRecordedFamilyRow>(),0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioOperationOverview.Recent(Array.Empty<StudioRecordedFamilyRow>(),9));
            Refuse<ArgumentNullException>(()=>StudioOperationOverview.Summary(null!));
        });
        Check("P6G3F_M6H2_CURRENT_EVIDENCE_NEVER_IMPLIED",() =>
        {
            Ensure(StudioOperationOverview.EvidenceBoundary.Contains("not current-file verification") &&
                StudioOperationOverview.EvidenceBoundary.Contains("Inspect recovery") &&
                !StudioOperationOverview.EvidenceBoundary.Contains("verified current"),
                "Recorded history became an identity grant.");
        });
        Check("P6G3F_M6H2_GALLERY_BEFORE_CLICK_VISIBLE",() =>
        {
            Ensure(StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.NotRequested).Contains("Load four thumbnails") &&
                StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Loading).Contains("on request"),
                "Blank gallery lacks explicit on-demand explanation.");
        });
        Check("P6G3F_M6H2_GALLERY_READY_CLEARS_PLACEHOLDER",() =>
        {
            Ensure(StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Ready)=="" &&
                StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Unavailable).Contains("Original remains unchanged"),
                "Gallery overlays decoded image or masks failure.");
        });
        Check("P6G3F_M6H2_UNKNOWN_GALLERY_STATE_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioContactSheetPreviewCues.Tile((StudioSheetVisualState)42));
        });
        Check("P6G3F_M6H2_IMPORT_NOTICE_SHORT_AND_TRUTHFUL",() =>
        {
            var ok=StudioImportStatusPresentation.Short(2,4,true,0);
            var warning=StudioImportStatusPresentation.Short(2,4,false,2);
            Ensure(ok.Contains("2 photographs added") && ok.Contains("4 in Collection") &&
                ok.Contains("Originals unchanged") && ok.Contains("Full import counts") &&
                !ok.Contains("Copy committed") &&
                warning.Contains("2 warning(s)") && warning.Contains("choose Results folder"),
                "Import toast lost warnings, truth, or next action.");
        });
        Check("P6G3F_M6H2_INVALID_IMPORT_COUNTERS_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportStatusPresentation.Short(-1,4,true,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportStatusPresentation.Short(3,2,true,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioImportStatusPresentation.Short(1,1,false,-2));
        });
        // M6H5: presentation-only compiled regressions. All data is in memory.
        // Never construct a SessionStore, native coordinator or replay token.
        Check("P6G3F_M6H5_FOUR_FAMILY_SESSION_HEADLINE",() =>
        {
            var session=Guid.NewGuid();
            var rows=new[]{
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Copy,session,1,"Undone","/a/c.png","/b/c.png","/r/c.png",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,session,2,"Undone","/a/m.png","/b/m.png","/r/m.png",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Rename,session,3,"Undone","/a/n.png","/a/new.png","",null),
                new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,"Undone","/a/r.png","Original relocated into app-owned recovery","/r/r.png",null)};
            var v=StudioHistoryExperiencePresentation.Summary(rows);
            Ensure(v.Contains("4 recorded") && v.Contains("Copy 1") &&
                v.Contains("Move 1") && v.Contains("Rename 1") &&
                v.Contains("Reject 1") && v.Contains("not current file verification"),
                "Four-family headline is Copy-centric or implies verified evidence.");
        });
        Check("P6G3F_M6H5_ZERO_HISTORY_NOT_GLOBAL_CLEARANCE",() =>
        {
            var v=StudioHistoryExperiencePresentation.Summary(Array.Empty<StudioRecordedFamilyRow>());
            Ensure(v.Contains("Other sessions") && v.Contains("Inspect recovery") &&
                !v.Contains("Safe to apply"),"Empty session implied clean global journals.");
        });
        Check("P6G3F_M6H5_COPY_DIAGNOSTICS_ONLY_FOR_COPY",() =>
        {
            var session=Guid.NewGuid();
            var move=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Move,session,1,"Undone","/a/m.png","/b/m.png","",null);
            var copy=move with {Id=Guid.NewGuid(),Family=StudioNativeFamily.Copy};
            Ensure(!StudioHistoryExperiencePresentation.ShowCopyDiagnostics(new[]{move}) &&
                StudioHistoryExperiencePresentation.ShowCopyDiagnostics(new[]{move,copy}),
                "Move-only history still opens an irrelevant Copy diagnostic.");
        });
        Check("P6G3F_M6H5_REJECT_PENDING_NOT_COMPLETED",() =>
        {
            var session=Guid.NewGuid();
            var pending=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,session,0,"Approved","/a/r.png","","/r/r.png",null);
            var v=StudioHistoryExperiencePresentation.Summary(new[]{pending});
            Ensure(v.Contains("pending Reject") && v.Contains("not yet executed"),
                "Approved Reject proposal was presented as relocation.");
        });
        Check("P6G3F_M6H5_ISSUE_OVERRIDES_COMMITTED",() =>
        {
            var row=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Copy,Guid.NewGuid(),1,"Committed","/a/x","/b/x","", "unverified");
            var v=StudioHistoryExperiencePresentation.Summary(new[]{row});
            Ensure(v.Contains("need attention") && v.Contains("Preserve saved state"),
                "Error-bearing recorded Committed was presented as healthy.");
        });
        Check("P6G3F_M6H5_FAMILY_SPECIFIC_UNDONE_INSTRUCTIONS",() =>
        {
            var s=Guid.NewGuid();
            string Next(StudioNativeFamily f) => StudioFamilyHistoryPresentation.NextStep(
                new StudioRecordedFamilyRow(Guid.NewGuid(),f,s,1,"Undone","/a/x.png","/b/x.png","/r/x.png",null));
            Ensure(Next(StudioNativeFamily.Copy).Contains("owned output archive") &&
                Next(StudioNativeFamily.Move).Contains("output Undo archive") &&
                Next(StudioNativeFamily.Rename).Contains("original name and native identity") &&
                Next(StudioNativeFamily.Reject).Contains("Reject marks are separate"),
                "Undone guidance incorrectly directs another execution or ignores family.");
        });
        Check("P6G3F_M6H5_FAMILY_COMPLETED_STILL_NO_GRANT",() =>
        {
            var s=Guid.NewGuid();
            foreach(var f in new[]{StudioNativeFamily.Copy,StudioNativeFamily.Move,StudioNativeFamily.Rename,StudioNativeFamily.Reject})
            {
                var step=StudioFamilyHistoryPresentation.NextStep(new StudioRecordedFamilyRow(
                    Guid.NewGuid(),f,s,1,"Committed","/a/x.png","/b/x.png","/r/x.png",null));
                Ensure(step.Contains("Inspect") && step.Contains("before separately considering"),
                    "Completed row bypassed current evidence gate.");
            }
        });
        Check("P6G3F_M6H5_RENAME_ALREADY_UNDONE_STARTUP_NO_REPEAT",() =>
        {
            var cue=StudioHistoryExperiencePresentation.RenameStartupLocked;
            Ensure(cue.Contains("Recorded Undo may already be complete") &&
                cue.Contains("New file operations remain locked") &&
                !cue.Contains("Undo all, then"),"Startup incorrectly demands repeat Undo.");
        });
        Check("P6G3F_M6H5_SAVED_MARK_AND_JOURNAL_DISJOINT",() =>
        {
            var cue=StudioHistoryExperiencePresentation.ChangesDecisionBoundary;
            Ensure(cue.Contains("Reject-review marks") && cue.Contains("NOT disk actions") &&
                cue.Contains("does not automatically clear"),
                "Historical Undo silently cleared saved choice semantics.");
        });
        Check("P6G3F_M6H5_READONLY_CONTRACT_FAIL_CLOSED",() =>
        {
            Refuse<ArgumentNullException>(()=>StudioHistoryExperiencePresentation.Summary(null!));
            Refuse<ArgumentNullException>(()=>StudioHistoryExperiencePresentation.ShowCopyDiagnostics(null!));
            var boundary=StudioHistoryExperiencePresentation.EvidenceBoundary;
            Ensure(boundary.Contains("NOT live file evidence") &&
                boundary.Contains("never grants Apply, Undo, replay or history reset"),
                "History overview became an implicit file operation grant.");
        });
        // M6H6: current-evidence report is DISPLAY ONLY and spans four
        // independent journals; fixture authority and previous 447 gates stay intact.
        Check("P6G3F_M6H6_FOUR_FAMILY_INSPECTOR_INVENTORY",() =>
        {
            var v=StudioRecoveryInspectionPresentation.SessionHeading(1,2,3,4);
            Ensure(v.Contains("Copy 1") && v.Contains("Move 2") &&
                v.Contains("Rename 3") && v.Contains("Reject 4") &&
                v.Contains("historical only") && v.Contains("other sessions"),
                "Inspector inventory excluded a family or claimed global evidence.");
        });
        Check("P6G3F_M6H6_EMPTY_SESSION_NOT_GLOBAL_CLEAN",() =>
        {
            var v=StudioRecoveryInspectionPresentation.SessionHeading(0,0,0,0);
            Ensure(v.Contains("other sessions may still hold binding") &&
                !v.Contains("safe to apply") && !v.Contains("no recovery exists"),
                "Empty session must never clear foreign history locks.");
        });
        Check("P6G3F_M6H6_COPY_EVIDENCE_FAMILY_SCOPED",() =>
        {
            var v=StudioRecoveryInspectionPresentation.CopyEvidence(2,1,3);
            Ensure(v.Contains("COPY ARCHIVE EVIDENCE") && v.Contains("1/2 VERIFIED") &&
                v.Contains("1 ARCHIVE ALERT") && v.Contains("COPY JOURNAL NEED ATTENTION: 3") &&
                !v.Contains("MOVE VERIFIED"),
                "Copy-only archive counts were claimed as other-family evidence.");
        });
        Check("P6G3F_M6H6_INVALID_TOTALS_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.SessionHeading(-1,0,0,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.SessionHeading(0,-1,0,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.SessionHeading(0,0,-1,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.SessionHeading(0,0,0,-1));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.CopyEvidence(0,1,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.CopyEvidence(2,-1,0));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.CopyEvidence(2,1,-1));
        });
        Check("P6G3F_M6H6_UNDONE_NO_REPEAT_OR_RELEASE",() =>
        {
            foreach (var family in new[]{StudioNativeFamily.Copy,StudioNativeFamily.Move,
                StudioNativeFamily.Rename,StudioNativeFamily.Reject})
            {
                var msg=StudioRecoveryInspectionPresentation.NextStep(family,"Undone");
                Ensure(msg.Contains("already") &&
                    !msg.Contains("Undo all") &&
                    !msg.Contains("safe to start another"),
                    "Recorded Undo erroneously directed another Undo or opened execution.");
            }
        });
        Check("P6G3F_M6H6_UNKNOWN_HISTORY_LOCKED",() =>
        {
            var unknown=StudioRecoveryInspectionPresentation.NextStep(StudioNativeFamily.Move,"Intent");
            var blank=StudioRecoveryInspectionPresentation.NextStep(StudioNativeFamily.Copy,"");
            Ensure(unknown.Contains("blocks new file operations") &&
                blank.Contains("UNKNOWN HISTORICAL STATE") && blank.Contains("no new operation"),
                "Unrecognized/recovering journal status was described as permission.");
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryInspectionPresentation.NextStep((StudioNativeFamily)99,"Undone"));
        });
        Check("P6G3F_M6H6_CHANGES_REJECT_NOT_COPY",() =>
        {
            var v=StudioChangesContextPresentation.Explain(true,false,false,false,true,false,false,false);
            Ensure(v.Contains("REJECT HISTORY LOCK") && v.Contains("never Trash") &&
                v.Contains("Saved Reject marks") && !v.Contains("COPY selected"),
                "Reject historical workspace mislabeled as an active Copy operation.");
        });
        Check("P6G3F_M6H6_CHANGES_MOVE_RENAME_STATUS",() =>
        {
            var m=StudioChangesContextPresentation.Explain(true,false,true,false,false,false,false,false);
            var n=StudioChangesContextPresentation.Explain(true,false,false,true,false,false,false,false);
            Ensure(m.Contains("MOVE HISTORY LOCK") && m.Contains("already be recorded") &&
                n.Contains("RENAME HISTORY LOCK") && n.Contains("verified closeout is separate"),
                "Changes instructions ignored the existing native history lock.");
        });
        Check("P6G3F_M6H6_CHANGES_FAIL_CLOSED_MULTI_AND_UNHEALTHY",() =>
        {
            var mixed=StudioChangesContextPresentation.Explain(true,false,true,true,false,false,false,false);
            var bad=StudioChangesContextPresentation.Explain(false,false,false,false,false,false,false,false);
            var attention=StudioChangesContextPresentation.Explain(true,false,false,false,false,false,true,false);
            Ensure(mixed.Contains("MULTIPLE FILE-OPERATION HISTORY LOCKS") &&
                bad.Contains("RECOVERY UNAVAILABLE") && attention.Contains("RECOVERY ATTENTION") &&
                !mixed.Contains("COPY selected") && !bad.Contains("COPY selected"),
                "Danger/unknown states fell back to a normal active-mode prompt.");
        });
        Check("P6G3F_M6H6_UNDONE_TRIAGE_NO_REPEAT",() =>
        {
            var row=new StudioRecordedFamilyRow(Guid.NewGuid(),StudioNativeFamily.Reject,Guid.NewGuid(),0,
                "Undone","/a/r.png","owned recovery","/r/r.png",null);
            var summary=StudioFamilyHistoryPresentation.Triage(new[]{row});
            Ensure(summary.Contains("already undone") && summary.Contains("NOT live verification") &&
                summary.Contains("do not repeat"),
                "Already-Undone history still offered a repeat Undo instruction.");
        });

        // M6J-A1: identity/path dependency diagnostics are NOT approvals.
        // These synthetic tests never request filesystem mutation authority.
        Check("M6J_A1_EMPTY_HISTORY_NEVER_GRANTS",() =>
        {
            var report=M6JDependencyInspector.Analyze(Array.Empty<M6JOperationFootprint>());
            Ensure(report.Kind==M6JDependencyKind.EmptyObservation && report.ExaminedRows==0 &&
                !report.AuthorizesNativeOperation && !report.GrantsSuccessorEpoch &&
                !report.AuthorizesSelectiveUndo,"Empty read-only graph granted native permission");
        });
        Check("M6J_A1_FOUR_DISJOINT_FAMILIES_STILL_NO_GRANT",() =>
        {
            string prefix=Path.Combine(Path.GetTempPath(),"M6J_A1_"+Guid.NewGuid().ToString("N"));
            M6JOperationFootprint Make(string family,int n) => new(
                family,Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),
                Guid.NewGuid(),"Undone",true,new[] {
                    new M6JPathClaim("Source",Path.Combine(prefix,"source",$"photo{n}.png")),
                    new M6JPathClaim("Output",Path.Combine(prefix,"out",$"photo{n}.png"))
                });
            var report=M6JDependencyInspector.Analyze(new[] {
                Make("Copy",1),Make("Move",2),Make("Rename",3),Make("Reject",4)
            });
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.ExaminedRows==4 && report.Edges.Count==0 &&
                !report.AuthorizesNativeOperation && !report.GrantsSuccessorEpoch &&
                !report.AuthorizesSelectiveUndo,
                "Four lexically separate families were incorrectly promoted to approval");
        });
        Check("M6J_A1_SAME_PHOTO_DEPENDENCY",() =>
        {
            var id=Guid.NewGuid(); string prefix=Path.Combine(Path.GetTempPath(),"M6J_A1_SH");
            M6JOperationFootprint Make(string family,int n)=>new(family,Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),id,"Undone",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(prefix,$"s{n}.png")),
                    new M6JPathClaim("Destination",Path.Combine(prefix,$"d{n}.png"))});
            var result=M6JDependencyInspector.Analyze(new[]{Make("Copy",1),Make("Move",2)});
            Ensure(result.Kind==M6JDependencyKind.DependencyDetected &&
                result.Edges.Any(e=>e.Kind==M6JConflictKind.SamePhoto) &&
                !result.AuthorizesSelectiveUndo,"Shared photo dependency not detected");
        });
        Check("M6J_A1_SHARED_DESTINATION_COLLISION",() =>
        {
            string dir=Path.Combine(Path.GetTempPath(),"M6J_A1_SHARED");
            var a=new M6JOperationFootprint("Copy",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(dir,"a.png")),
                    new M6JPathClaim("Destination",Path.Combine(dir,"one.png"))});
            var b=new M6JOperationFootprint("Rename",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Committed",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(dir,"b.png")),
                    new M6JPathClaim("Target",Path.Combine(dir,"one.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedPath) &&
                report.Kind==M6JDependencyKind.DependencyDetected && !report.GrantsSuccessorEpoch,
                "Two operation families shared one destination without dependency warning");
        });
        Check("M6J_A1_RECOVERY_ROOT_CONTAINMENT",() =>
        {
            var dir=Path.Combine(Path.GetTempPath(),"M6J_A1_RECOVERY");
            var root=Path.Combine(dir,".photosift-reborn-reject-recovery","own");
            var a=new M6JOperationFootprint("Reject",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(dir,"a.png")),
                    new M6JPathClaim("Archive",Path.Combine(root,"a.png")),
                    new M6JPathClaim("OwnedRecoveryRoot",root,true)});
            var b=new M6JOperationFootprint("Move",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(root,"nested","b.png")),
                    new M6JPathClaim("Destination",Path.Combine(dir,"b.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Edges.Any(e=>e.Kind==M6JConflictKind.RecoveryRootContainsPath) &&
                !report.AuthorizesNativeOperation,"Owned recovery containment was ignored");
        });
        Check("M6J_A1_UNVERIFIED_JOURNAL_REFUSES",() =>
        {
            var dir=Path.Combine(Path.GetTempPath(),"M6J_A1_WEAK");
            var a=new M6JOperationFootprint("Reject",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Intent",false,new[]{
                    new M6JPathClaim("Source",Path.Combine(dir,"a.png")),
                    new M6JPathClaim("Archive",Path.Combine(dir,"r.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a});
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                !report.AuthorizesSelectiveUndo,"Interrupted operation was treated as proven");
        });
        Check("M6J_A1_NONABSOLUTE_PATH_REFUSES",() =>
        {
            var a=new M6JOperationFootprint("Copy",Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",true,new[]{
                    new M6JPathClaim("Source","relative.png"),
                    new M6JPathClaim("Destination",Path.Combine(Path.GetTempPath(),"d.png"))});
            Ensure(M6JDependencyInspector.Analyze(new[]{a}).Kind==
                M6JDependencyKind.IncompleteOrUnverified,"Relative path was treated as exact native identity");
        });
        Check("M6J_A1_DUPLICATE_OPERATION_ID_ACROSS_FAMILY",() =>
        {
            var id=Guid.NewGuid().ToString("N");var dir=Path.Combine(Path.GetTempPath(),"M6J_A1_ID");
            M6JOperationFootprint Make(string family,int n)=>new(family,id,
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",true,new[]{
                    new M6JPathClaim("Source",Path.Combine(dir,$"s{n}.png")),
                    new M6JPathClaim("Destination",Path.Combine(dir,$"d{n}.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{Make("Copy",1),Make("Reject",2)});
            Ensure(report.Edges.Any(e=>e.Kind==M6JConflictKind.DuplicateOperationId) &&
                !report.GrantsSuccessorEpoch,"Reused GUID across native families was not flagged");
        });
        Check("M6J_A1_INCOMPLETE_BOUNDARY_REFUSES",() =>
        {
            var report=M6JDependencyInspector.Analyze(Array.Empty<M6JOperationFootprint>(),false);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                !report.AuthorizesNativeOperation,"Unstable empty history became fresh authority");
        });
        Check("M6J_A1_ACTUAL_EMPTY_STORE_READONLY",() =>
        {
            string root=OrdinaryRejectRoot();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());
            var result=db.InspectM6JReadOnlyDependencies();
            Ensure(result.Kind==M6JDependencyKind.EmptyObservation &&
                result.ExaminedRows==0 && !result.AuthorizesNativeOperation &&
                !result.AuthorizesSelectiveUndo && !result.GrantsSuccessorEpoch,
                "Actual saved installation graph created native authority");
        });
        // M6J-A2: a durable, exact prospective-review record is not a native
        // write grant and cannot bypass the four already accepted Put* guards.
        // Each test uses a separate disposable synthetic workspace.
        M6JProspectiveCandidate M6JCandidate(SortingWorkspace w,string root,
            string? destination=null)
        {
            var photo=w.Photos[0];
            string source=photo.Path;
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            return new M6JProspectiveCandidate(Guid.NewGuid(),"Move",w.SessionId,photo.Id,
                source,destination??Path.Combine(root,"output","review-only-next.png"),
                photo.NativeIdentity!,hash,photo.Length,photo.LastWriteUtcTicks);
        }
        Check("M6J_A2_STAGED_ROW_HAS_ZERO_MUTATION_AUTHORITY",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            var staged=db.StageM6JProspectiveReview(c);
            var observed=db.InspectM6JProspectiveReview(c.Id);
            Ensure(staged.State=="Staged" && !staged.AuthorizesNativeOperation &&
                observed.State=="Staged" && observed.BasisStillMatches &&
                !observed.CanConsumeIntoIntent &&
                File.Exists(c.Source) && !File.Exists(c.Destination),
                "Staging alone wrote files or promoted native authority.");
        });
        Check("M6J_A2_EXPLICIT_REVIEW_SEALS_BUT_NEVER_GRANTS",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            var result=db.ReviewM6JProspectiveCandidate(c.Id,c,true);
            var observed=db.InspectM6JProspectiveReview(c.Id);
            Ensure(result.State=="Reviewed" && observed.State=="Reviewed" &&
                observed.BasisStillMatches && !result.AuthorizesNativeOperation &&
                !result.CanConsumeIntoIntent && !observed.AuthorizesSelectiveUndo &&
                File.Exists(c.Source) && !File.Exists(c.Destination),
                "Review granted operation/Undo permission.");
        });
        Check("M6J_A2_MISSING_EXPLICIT_ACKNOWLEDGEMENT_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,false));
            Ensure(db.InspectM6JProspectiveReview(c.Id).State=="Staged",
                "Missing approval advanced a persisted stage.");
        });
        Check("M6J_A2_REVIEW_REPLAY_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            db.ReviewM6JProspectiveCandidate(c.Id,c,true);
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,true));
        });
        Check("M6J_A2_ALTERED_REVIEWED_DESTINATION_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            var changed=c with {Destination=Path.Combine(root,"output","OTHER.png")};
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,changed,true));
            Ensure(db.InspectM6JProspectiveReview(c.Id).State=="Staged",
                "Altered exact review advanced staged row.");
        });
        Check("M6J_A2_STALE_CHECKPOINT_REVOKES_REVIEW",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            db.Save(w.CaptureSession() with {Revision=w.Revision+1});
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,true));
            Ensure(!db.InspectM6JProspectiveReview(c.Id).BasisStillMatches,
                "Changed persistent checkpoint left proposal current.");
        });
        Check("M6J_A2_NATIVE_SOURCE_REPLACEMENT_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            File.Delete(c.Source);File.WriteAllText(c.Source,"replaced native file - different bytes");
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,true));
        });
        Check("M6J_A2_OCCUPIED_DESTINATION_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            File.WriteAllText(c.Destination,"foreign owner sentinel");
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,true));
            Ensure(File.ReadAllText(c.Destination)=="foreign owner sentinel",
                "Occupied output was touched by a review-only code path.");
        });
        Check("M6J_A2_SECOND_OPEN_STAGE_REFUSED",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            try { db.StageM6JProspectiveReview(c with {Id=Guid.NewGuid(),
                Destination=Path.Combine(root,"output","another.png")});
                throw new Exception("Second stage should have been refused"); }
            catch (SqliteException) { }
            Ensure(db.InspectM6JProspectiveReview(c.Id).State=="Staged",
                "First reviewed row was lost during duplicate stage refusal.");
        });
        Check("M6J_A2_CANCEL_IS_TERMINAL_REPLAY_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            var cancelled=db.CancelM6JProspectiveReview(c.Id);
            Ensure(cancelled.State=="Cancelled" && !cancelled.CanConsumeIntoIntent &&
                !db.InspectM6JProspectiveReview(c.Id).BasisStillMatches,
                "Cancelled M6J review remained active.");
            Refuse<IOException>(()=>db.ReviewM6JProspectiveCandidate(c.Id,c,true));
            Refuse<IOException>(()=>db.CancelM6JProspectiveReview(c.Id));
            db.StageM6JProspectiveReview(c with {Id=Guid.NewGuid()});
        });
        Check("M6J_A2_RESTART_PERSISTS_NO_GRANT",() =>
        {
            string root=Root();var w=RenameFixture(root);var c=M6JCandidate(w,root);
            string directory=Path.Combine(root,"state");
            using(var db=new WorkspaceSessionStore(directory)) {
                db.Save(w.CaptureSession());
                db.StageM6JProspectiveReview(c);
                db.ReviewM6JProspectiveCandidate(c.Id,c,true);
            }
            using(var reopened=new WorkspaceSessionStore(directory)) {
                var report=reopened.InspectM6JProspectiveReview(c.Id);
                Ensure(report.State=="Reviewed" && report.BasisStillMatches &&
                    !report.AuthorizesNativeOperation && !report.CanConsumeIntoIntent,
                    "Review became native permission after cold DB restart.");
            }
        });
        Check("M6J_A2_WRONG_SAVED_PHOTO_ID_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root) with {PhotoId=Guid.NewGuid()};
            Refuse<IOException>(()=>db.StageM6JProspectiveReview(c));
        });
        Check("M6J_A2_SOURCE_TARGET_ALIAS_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            Refuse<IOException>(()=>db.StageM6JProspectiveReview(c with {Destination=c.Source}));
        });
        Check("M6J_A2_PERSISTED_DIGEST_TAMPER_REFUSES",() =>
        {
            string root=Root();var w=RenameFixture(root);
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            db.Save(w.CaptureSession());var c=M6JCandidate(w,root);
            db.StageM6JProspectiveReview(c);
            using(var foreign=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                foreign.Open();
                using var cmd=foreign.CreateCommand();
                cmd.CommandText="UPDATE m6j_review_proposals SET candidate_digest='BAD' WHERE id=$id";
                cmd.Parameters.AddWithValue("$id",c.Id.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Synthetic tamper did not affect proposal row.");
            }
            Ensure(db.InspectM6JProspectiveReview(c.Id).State=="NeedsAttention",
                "Modified DB proposal digest was accepted as reviewed authority.");
        });
        Check("M6J_A2_V17_MIGRATION_BACKUP_PRESERVES_SAVED_CHECKPOINT",() =>
        {
            string root=Root();var w=RenameFixture(root);
            string state=Path.Combine(root,"state");
            using(var db=new WorkspaceSessionStore(state)) db.Save(w.CaptureSession());
            // Synthetic legacy simulation ONLY: v17 has no A2 proposal table.
            using(var previous=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=Path.Combine(state,"workspaces-v1.sqlite"),
                Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                previous.Open();using var cmd=previous.CreateCommand();
                cmd.CommandText="DROP TABLE m6j_review_proposals; PRAGMA user_version=17;";
                cmd.ExecuteNonQuery();
            }
            using(var upgraded=new WorkspaceSessionStore(state)) {
                Ensure(upgraded.Load()?.SessionId==w.SessionId,
                    "v18 migration erased saved session identity.");
                var candidate=M6JCandidate(w,root);
                Ensure(upgraded.StageM6JProspectiveReview(candidate).State=="Staged",
                    "v17->v18 migration did not create the durable review table.");
            }
            var backups=Directory.GetFiles(state,"workspaces-v1.sqlite.before-v18-*.sqlite");
            Ensure(backups.Length==1,"v18 migration did not preserve one consistent legacy backup.");
            using var backup=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=backups[0],Mode=SqliteOpenMode.ReadOnly,Pooling=false
            }.ToString());
            backup.Open();using var query=backup.CreateCommand();
            query.CommandText="PRAGMA user_version;";
            Ensure(Convert.ToInt32(query.ExecuteScalar())==17,
                "v18 backup does not preserve the predecessor schema version.");
            query.CommandText="SELECT json FROM workspace_checkpoint WHERE id=1";
            Ensure((query.ExecuteScalar() as string)?.Contains(w.SessionId.ToString("D"))==true,
                "v18 backup does not preserve the checkpoint bytes.");
        });
        // M6J-A3-P1: frozen exact Copy -> Move Intent SHAPE. Still read-only:
        // no new native Intent, successor epoch or Undo is authorized here.
        (string Root, SortingWorkspace Work, WorkspaceSessionStore Db,
            M6JProspectiveCandidate Candidate, DurableMoveItem Intent, DurableCopyItem Prior)
            A3DisjointCopyMoveFixture()
        {
            string root=Root();var work=NativeCopyUndoFixture(root);
            var originalId=work.Photos[0].Id;
            var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var copy=new DurableCopyCoordinator(db);
            work.Assign('F');var firstPlan=work.ReviewPlan();
            var first=DurableCopyCoordinator.PrepareAsync(work,firstPlan,
                work.CurrentPlanId!.Value,work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value,work.Revision);
            db.Save(work.CaptureSession());
            Ensure(copy.ApplyAsync(first).GetAwaiter().GetResult().Completed==1,
                "A3-P1 synthetic predecessor Copy failed to commit");
            var prior=db.CopyItems(work.SessionId).Single();
            Ensure(prior.State=="Committed" && prior.PhotoId==originalId,
                "A3-P1 fixture did not create exact committed predecessor");
            work.RetireCommitted(new[]{originalId});db.Save(work.CaptureSession());
            var resumed=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            resumed.RestoreSession(db.Load()!);resumed.JumpToPhoto(2);
            Ensure(resumed.Current!.Id!=prior.PhotoId,
                "A3-P1 next Move targets the existing Copy original");
            resumed.Assign('T');var secondPlan=resumed.ReviewPlan();
            var next=DurableCopyCoordinator.PrepareAsync(resumed,secondPlan,
                resumed.CurrentPlanId!.Value,resumed.Revision).GetAwaiter().GetResult().Single();
            resumed.ApprovePlan(resumed.CurrentPlanId!.Value,resumed.Revision);
            db.Save(resumed.CaptureSession());
            // A2 requires the exact reviewed destination parent to exist;
            // this newly generated fixture directory is empty and unowned.
            Directory.CreateDirectory(Path.GetDirectoryName(next.Destination)!);
            var id=Guid.NewGuid();
            var candidate=new M6JProspectiveCandidate(id,"Move",resumed.SessionId,
                next.PhotoId,next.Source,next.Destination,next.Identity,next.Hash,
                next.Length,next.WriteTicks);
            db.StageM6JProspectiveReview(candidate);
            db.ReviewM6JProspectiveCandidate(id,candidate,true);
            string parent=Path.GetDirectoryName(next.Destination)!;
            string archiveRoot=Path.Combine(Path.GetDirectoryName(next.Source)!,
                ".photosift-reborn-recovery",db.MoveInstallationId().ToString("N"),
                resumed.SessionId.ToString("N"),id.ToString("N"));
            var intent=new DurableMoveItem(id,db.MoveInstallationId(),next.SessionId,
                next.PhotoId,next.PlanId,next.Revision,DateTime.UtcNow.Ticks,
                next.Source,next.Destination,
                Path.Combine(parent,".photosift-move-"+id.ToString("N")+".part"),
                Path.Combine(archiveRoot,Path.GetFileName(next.Source)),
                Path.Combine(archiveRoot,"installed-"+Path.GetFileName(next.Destination)),
                next.OutputRoot,next.Hash,next.Identity,next.Length,next.WriteTicks,
                null,null,"Intent",null,next.Key);
            return (root,resumed,db,candidate,intent,prior);
        }
        Check("M6J_A3_P1_EXACT_COMMITTED_COPY_MOVE_BOUNDARY_IS_NONAUTHORITY",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var result=db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,f.Intent);
            Ensure(result.IsExactPreparedShape && result.PredecessorDigest?.Length==64 &&
                result.ReviewedCandidateDigest?.Length==64 &&
                !result.AuthorizesNativeOperation && !result.CanWriteMoveIntent &&
                !result.AuthorizesSelectiveUndo && db.AllMoveHistoryCount()==0 &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination) &&
                File.Exists(f.Prior.Destination),
                "An exact, native-proven disjoint predecessor was not observed safely");
        });
        Check("M6J_A3_P1_DIFFERENT_REVIEW_ID_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(!db.InspectM6JMoveAfterCopyIntent(Guid.NewGuid(),f.Intent)
                .IsExactPreparedShape && db.AllMoveHistoryCount()==0,
                "Unrelated review ID became a Move Intent authority");
        });
        Check("M6J_A3_P1_CHANGED_DESTINATION_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,
                f.Intent with {Destination=Path.Combine(f.Root,"output","foreign.png")})
                .IsExactPreparedShape,"Reviewed destination was mutable");
        });
        Check("M6J_A3_P1_TAMPERED_TEMP_PATH_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,
                f.Intent with {Temp=f.Prior.Destination}).IsExactPreparedShape,
                "Move could reuse committed Copy output as temporary path");
        });
        Check("M6J_A3_P1_ARCHIVE_PATH_REPLACEMENT_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,
                f.Intent with {OriginalArchive=Path.Combine(f.Root,"wrong-owner")})
                .IsExactPreparedShape,"Move recovery owner path could be replaced");
        });
        Check("M6J_A3_P1_SOURCE_IDENTITY_REPLACEMENT_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            File.Delete(f.Candidate.Source);
            File.WriteAllText(f.Candidate.Source,"foreign generated replacement");
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,f.Intent)
                .IsExactPreparedShape && File.Exists(f.Prior.Destination),
                "Altered next source retained Move readiness or changed Copy");
        });
        Check("M6J_A3_P1_COMMITTED_COPY_OUTPUT_TAMPER_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            File.AppendAllText(f.Prior.Destination,"synthetic evidence tamper");
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,f.Intent)
                .IsExactPreparedShape && db.AllMoveHistoryCount()==0,
                "Tampered committed Copy became a trusted predecessor");
        });
        Check("M6J_A3_P1_SAVED_CHECKPOINT_DRIFT_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.Save(f.Work.CaptureSession() with {Revision=f.Work.Revision+1});
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,f.Intent)
                .IsExactPreparedShape,"Changed saved checkpoint retained exact reviewed readiness");
        });
        Check("M6J_A3_P1_UNAPPROVED_TARGET_ROUTE_REFUSED",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(!db.InspectM6JMoveAfterCopyIntent(f.Candidate.Id,
                f.Intent with {RouteKey='Z'}).IsExactPreparedShape,
                "Route was not bound to the saved approved photo assignment");
        });
        // M6J-A3-P2A: actual conditional SQLite single-use consumption and
        // Move Intent insert share one transaction, but there is NO native
        // coordinator/Undo authorization or filesystem write in this phase.
        Check("M6J_A3_P2A_ATOMIC_CONSUMED_AND_NATIVE_MOVE_INTENT",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            string before=SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult();
            var receipt=db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Ensure(receipt.JournalCommitted && receipt.State=="ConsumedWithIntent" &&
                !receipt.AuthorizesNativeFileMutation && !receipt.AuthorizesCrossFamilyUndo &&
                db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                db.AllMoveHistoryCount()==1 &&
                SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult()==before &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination) &&
                !File.Exists(f.Intent.OriginalArchive),
                "A3-P2A atomic review/Intent or no-filesystem-change contract failed");
        });
        Check("M6J_A3_P2A_REPLAY_CANNOT_CREATE_SECOND_INTENT",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent));
            Ensure(db.AllMoveHistoryCount()==1,"Reviewed grant was reusable or journal duplicated");
        });
        Check("M6J_A3_P2A_TEST_CUT_ROLLS_BACK_BOTH_SQL_ROWS",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<InvalidOperationException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=>throw new InvalidOperationException("synthetic txn cut")));
            Ensure(db.AllMoveHistoryCount()==0 &&
                db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "Synthetic transaction cut left a consumed review or Intent after rollback");
        });
        Check("M6J_A3_P2A_GENERIC_MOVE_WRITER_STILL_REFUSES_COPY_HISTORY",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.PutMoveItem(f.Intent));
            Ensure(db.AllMoveHistoryCount()==0 &&
                db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "Generic Move path bypassed the existing cross-family safety veto");
        });
        Check("M6J_A3_P2A_DIFFERENT_REVIEW_ID_REFUSES_NO_SQL_CHANGE",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(Guid.NewGuid(),f.Intent));
            Ensure(db.AllMoveHistoryCount()==0 &&
                db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "Unrelated review ID created cross-family Intent");
        });
        Check("M6J_A3_P2A_COPY_OUTPUT_TAMPER_REFUSES",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            File.AppendAllText(f.Prior.Destination,"synthetic predecessor tamper");
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent));
            Ensure(db.AllMoveHistoryCount()==0,"Changed Copy predecessor produced a Move Intent");
        });
        Check("M6J_A3_P2A_SAVED_CHECKPOINT_DRIFT_REFUSES",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.Save(f.Work.CaptureSession() with {Revision=f.Work.Revision+1});
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent));
            Ensure(db.AllMoveHistoryCount()==0,"Changed plan checkpoint produced a Move Intent");
        });
        Check("M6J_A3_P2A_CANCELLED_REVIEW_REFUSES",()=>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Ensure(db.CancelM6JProspectiveReview(f.Candidate.Id).State=="Cancelled",
                "A2 cancellation setup failed");
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent));
            Ensure(db.AllMoveHistoryCount()==0,"Cancelled review created native Intent");
        });
        // M6J-A3-P2A-R1: schema v19 repairs the genuine SQLite CHECK
        // mismatch. The original failed 496/3 run remains preserved; these
        // are additional independent migration/integrity regressions.
        Check("M6J_A3_P2A_R1_FRESH_V19_CHECK_REJECTS_UNKNOWN_STATE",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
            con.Open();using var cmd=con.CreateCommand();
            cmd.CommandText="PRAGMA user_version";
            Ensure(Convert.ToInt32(cmd.ExecuteScalar())==19,"Fresh DB version must be 19.");
            cmd.CommandText="UPDATE m6j_review_proposals SET state='UnreviewedBypass' WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Refuse<SqliteException>(()=>cmd.ExecuteNonQuery());
            Ensure(db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "New CHECK failed to protect Reviewed state after invalid update.");
        });
        Check("M6J_A3_P2A_R1_CONSUMED_REVIEW_READONLY_RESTART_NO_GRANT",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var consumed=db.InspectM6JProspectiveReview(f.Candidate.Id);
            Ensure(consumed.State=="Consumed" && !consumed.BasisStillMatches &&
                !consumed.AuthorizesNativeOperation && !consumed.CanConsumeIntoIntent,
                "Consumed review was classified as fresh native authority.");
            Refuse<IOException>(()=>db.CancelM6JProspectiveReview(f.Candidate.Id));
            db.Dispose();
            using var cold=new WorkspaceSessionStore(folder);
            consumed=cold.InspectM6JProspectiveReview(f.Candidate.Id);
            Ensure(consumed.State=="Consumed" && !consumed.AuthorizesNativeOperation &&
                cold.AllMoveHistoryCount()==1,
                "Consumed state/journal were lost or permission was revived after restart.");
            Refuse<IOException>(()=>cold.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent));
            Ensure(cold.AllMoveHistoryCount()==1,"Restart replay duplicated native Intent.");
        });
        Check("M6J_A3_P2A_R1_TRANSACTION_CUT_DURABLE_RESTART_ROLLBACK",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<InvalidOperationException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=>throw new InvalidOperationException("Synthetic interrupted txn")));
            db.Dispose();
            using var cold=new WorkspaceSessionStore(folder);
            Ensure(cold.AllMoveHistoryCount()==0 &&
                cold.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "Cold restart found partial consumption or Intent after test cut.");
            var completed=cold.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Ensure(completed.JournalCommitted && cold.AllMoveHistoryCount()==1,
                "Recovered transaction could not atomically commit after restart.");
        });
        // Restore a true v18 contract, with real historical Reviewed row, to
        // exercise the exact migration path that triggered the original fail.
        void DowngradeGeneratedV19ToExactV18(string database)
        {
            using var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
            con.Open();using var tx=con.BeginTransaction();using var cmd=con.CreateCommand();
            cmd.Transaction=tx;
            // Make an authentic original v18 sqlite_master SQL statement.
            // ALTER TABLE RENAME would add quotes around the table name,
            // producing a different historical schema than the pinned A2.
            const string cols="id,installation,session,family,candidate_json,candidate_digest,"+
                "history_digest,checkpoint_digest,state,review_digest";
            cmd.CommandText="CREATE TEMP TABLE m6j_review_proposals_v18_snapshot AS SELECT "+
                cols+" FROM m6j_review_proposals";
            cmd.ExecuteNonQuery();
            cmd.CommandText="DROP TABLE m6j_review_proposals";cmd.ExecuteNonQuery();
            cmd.CommandText="CREATE TABLE m6j_review_proposals ("+
                "id TEXT PRIMARY KEY, installation TEXT NOT NULL, session TEXT NOT NULL,"+
                "family TEXT NOT NULL, candidate_json TEXT NOT NULL, candidate_digest TEXT NOT NULL,"+
                "history_digest TEXT NOT NULL, checkpoint_digest TEXT NOT NULL,"+
                "state TEXT NOT NULL CHECK(state IN ('Staged','Reviewed','Cancelled')),"+
                "review_digest TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText="INSERT INTO m6j_review_proposals("+cols+") SELECT "+cols+
                " FROM m6j_review_proposals_v18_snapshot";
            cmd.ExecuteNonQuery();
            cmd.CommandText="DROP TABLE m6j_review_proposals_v18_snapshot";
            cmd.ExecuteNonQuery();
            cmd.CommandText="CREATE UNIQUE INDEX m6j_only_one_open_review "+
                "ON m6j_review_proposals(installation) WHERE state IN ('Staged','Reviewed')";
            cmd.ExecuteNonQuery();
            cmd.CommandText="PRAGMA user_version=18";cmd.ExecuteNonQuery();tx.Commit();
        }
        Check("M6J_A3_P2A_R1_V18_UPGRADE_RETAINS_REVIEW_BACKUP_JOURNAL",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            string database=db.DatabasePath;
            db.Dispose();
            DowngradeGeneratedV19ToExactV18(database);
            using(var upgraded=new WorkspaceSessionStore(folder)) {
                using var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false
                }.ToString());
                con.Open();using var cmd=con.CreateCommand();
                cmd.CommandText="PRAGMA user_version";
                Ensure(Convert.ToInt32(cmd.ExecuteScalar())==19,"v18->v19 schema upgrade not durable.");
                var result=upgraded.InspectM6JProspectiveReview(f.Candidate.Id);
                Ensure(result.State=="Reviewed" && result.BasisStillMatches &&
                    upgraded.CopyItems(f.Intent.SessionId).Count==1 &&
                    upgraded.AllMoveHistoryCount()==0,
                    "v19 migration lost native predecessor or existing Reviewed proposal.");
                // Prove migrated review is still legally consumable once.
                Ensure(upgraded.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent)
                    .JournalCommitted,"Reviewed v18 proposal was not consumable after v19 migration.");
            }
            string[] backups=Directory.GetFiles(folder,"workspaces-v1.sqlite.before-v19-*.sqlite");
            Ensure(backups.Length==1,"Expected one consistent v18 backup before v19 swap.");
            using var backup=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=backups[0],Mode=SqliteOpenMode.ReadOnly,Pooling=false
            }.ToString());
            backup.Open();using var query=backup.CreateCommand();
            query.CommandText="PRAGMA user_version";
            Ensure(Convert.ToInt32(query.ExecuteScalar())==18,"Backup is not original v18.");
            query.CommandText="SELECT state FROM m6j_review_proposals WHERE id=$id";
            query.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Ensure(query.ExecuteScalar() as string=="Reviewed",
                "v19 pre-migration backup lost historical Reviewed state.");
            query.CommandText="SELECT COUNT(*) FROM copy_items";
            Ensure(Convert.ToInt64(query.ExecuteScalar())==1,"v19 backup lost Copy predecessor.");
        });
        Check("M6J_A3_P2A_R1_V18_CORRUPTED_SEAL_REFUSES_MIGRATION",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            string database=db.DatabasePath;
            db.Dispose();
            DowngradeGeneratedV19ToExactV18(database);
            using(var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                con.Open();using var bad=con.CreateCommand();
                bad.CommandText="UPDATE m6j_review_proposals SET review_digest='BAD' WHERE id=$id";
                bad.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
                Ensure(bad.ExecuteNonQuery()==1,"Corrupt seal setup did not affect row.");
            }
            Refuse<InvalidDataException>(()=>{using var ignored=new WorkspaceSessionStore(folder);});
            using var check=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false
            }.ToString());
            check.Open();using var query=check.CreateCommand();
            query.CommandText="PRAGMA user_version";
            Ensure(Convert.ToInt32(query.ExecuteScalar())==18,"Rejected migration changed DB version.");
            query.CommandText="SELECT review_digest FROM m6j_review_proposals WHERE id=$id";
            query.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Ensure(query.ExecuteScalar() as string=="BAD","Rejected migration rewrote corrupted source.");
            Ensure(Directory.GetFiles(folder,"workspaces-v1.sqlite.before-v19-*.sqlite").Length==0,
                "Bad reviewed seal should refuse migration before backup or schema writes.");
        });
        Check("M6J_A3_P2A_R1_V19_PARTIAL_UNIQUE_OPEN_REVIEW_SURVIVES",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            string database=db.DatabasePath;
            db.Dispose();
            DowngradeGeneratedV19ToExactV18(database);
            using var upgraded=new WorkspaceSessionStore(folder);
            using var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
            con.Open();using var cmd=con.CreateCommand();
            cmd.CommandText="INSERT INTO m6j_review_proposals ("+
                "id,installation,session,family,candidate_json,candidate_digest,"+
                "history_digest,checkpoint_digest,state,review_digest) "+
                "SELECT $newid,installation,session,family,candidate_json,candidate_digest,"+
                "history_digest,checkpoint_digest,'Staged',NULL "+
                "FROM m6j_review_proposals WHERE id=$id";
            cmd.Parameters.AddWithValue("$newid",Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Refuse<SqliteException>(()=>cmd.ExecuteNonQuery());
            Ensure(upgraded.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed" &&
                upgraded.AllMoveHistoryCount()==0,
                "One-open-review index failed after v19 migration.");
        });
        // M6J-A3-P2B: generated-only cross-family filesystem execution and
        // selective Undo. The ordinary Move APIs remain unchanged and locked.
        PreparedCopy P2BPrepared(DurableMoveItem row) => new(row.PlanId,
            row.PlanRevision,row.SessionId,row.PhotoId,row.RouteKey,row.Source,
            row.Destination,row.SourceLength,row.SourceWriteTicks,row.SourceHash,
            row.SourceIdentity,row.OutputRoot);
        Check("M6J_A3_P2B_COMMITTED_MOVE_AFTER_COPY_PRESERVES_COPY",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var applied=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(applied.State=="Committed" && db.AllMoveHistoryCount()==1 &&
                db.InspectM6JProspectiveReview(f.Intent.Id).State=="Consumed" &&
                !File.Exists(applied.Source) && File.Exists(applied.Destination) &&
                File.Exists(applied.OriginalArchive) && !File.Exists(applied.Temp) &&
                NativeFileCapability.Identity(applied.OriginalArchive)==applied.SourceIdentity &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult()==
                    f.Prior.SourceHash && f.Prior.State=="Committed" &&
                db.CopyItems(f.Prior.SessionId).Single().State=="Committed",
                "Cross-family Move did not preserve both exact native ownership chains.");
        });
        Check("M6J_A3_P2B_COLD_RESTART_UNDO_MOVE_ONLY_COPY_INTACT",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var restarted=new DurableMoveCoordinator(cold);
            var undone=restarted.UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                .GetAwaiter().GetResult();
            Ensure(undone.State=="Undone" && !File.Exists(undone.Destination) &&
                File.Exists(undone.Source) && !File.Exists(undone.OriginalArchive) &&
                File.Exists(undone.OutputArchive) &&
                NativeFileCapability.Identity(undone.Source)==f.Intent.SourceIdentity &&
                SortingWorkspace.ComputeSha256Async(undone.Source).GetAwaiter().GetResult()==
                    f.Intent.SourceHash &&
                DurableMoveCoordinator.ProveTerminalUndoneEvidence(undone) &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed" &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult()==
                    f.Prior.SourceHash &&
                cold.InspectM6JProspectiveReview(f.Intent.Id).State=="Consumed",
                "Restart/selective Undo failed or disturbed committed Copy A.");
        });
        Check("M6J_A3_P2B_GENERIC_MOVE_UNDO_STILL_REFUSES_COPY_HISTORY",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Refuse<IOException>(()=>mover.UndoAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.Destination) && File.Exists(f.Prior.Destination),
                "Generic Undo bypassed its historical mixed-family lock.");
        });
        Check("M6J_A3_P2B_REPLAY_REFUSES_SECOND_FILESYSTEM_MOVE",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Refuse<IOException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==1 && File.Exists(moved.Destination) &&
                File.Exists(f.Prior.Destination),"Replay duplicated or harmed committed operations.");
        });
        Check("M6J_A3_P2B_INTENT_CUT_PRESERVES_COPY_AND_UNMOVED_SOURCE",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var cut=new DurableMoveCoordinator(db,phase=>{
                if (phase=="MOVE_INTENT_RECORDED") throw new SimulatedInterruptionException(phase);
            });
            Refuse<SimulatedInterruptionException>(()=>cut.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==1 &&
                db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                db.InspectM6JProspectiveReview(f.Intent.Id).State=="Consumed" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination) &&
                File.Exists(f.Prior.Destination) &&
                !File.Exists(f.Intent.OriginalArchive),
                "Synthetic cut after atomic Intent mutated native files.");
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(folder);
            var restarted=new DurableMoveCoordinator(cold);
            Refuse<IOException>(()=>restarted.UndoM6JReviewedGeneratedCopyMoveAsync(
                f.Intent.SessionId).GetAwaiter().GetResult());
            Ensure(cold.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && File.Exists(f.Prior.Destination),
                "Cold restart guessed an uncommitted Move was safe to undo.");
        });
        // M6J-A3-P2B-R1: the READ-ONLY inspector returns NeedsAttention when
        // native Copy/target proof becomes stale. That observation is NOT the
        // persisted review's durable state. Compare the sealed SQLite row before
        // and after refusal to prove no Intent/consumption occurred.
        string P2BImmutableReviewRow(WorkspaceSessionStore store, Guid id)
        {
            using var connection=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=store.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false
            }.ToString());
            connection.Open();
            using var cmd=connection.CreateCommand();
            cmd.CommandText="SELECT state,review_digest,candidate_digest,history_digest,"+
                "checkpoint_digest,candidate_json FROM m6j_review_proposals WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",id.ToString("N"));
            using var reader=cmd.ExecuteReader();
            Ensure(reader.Read(),"Original sealed review was not found.");
            var columns=new string[6];
            for(int i=0;i<columns.Length;i++)
                columns[i]=reader.IsDBNull(i) ? "<NULL>" : reader.GetString(i);
            Ensure(!reader.Read(),"Duplicate reviewed grant rows.");
            return System.Text.Json.JsonSerializer.Serialize(columns);
        }
        Check("M6J_A3_P2B_CHANGED_COPY_OUTPUT_BEFORE_INTENT_REFUSED",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            string frozenRow=P2BImmutableReviewRow(db,f.Intent.Id);
            Ensure(db.InspectM6JProspectiveReview(f.Intent.Id).State=="Reviewed",
                "Fixture review was not initially fresh.");
            File.AppendAllText(f.Prior.Destination,"synthetic changed Copy output");
            var mover=new DurableMoveCoordinator(db);
            Refuse<IOException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 &&
                P2BImmutableReviewRow(db,f.Intent.Id)==frozenRow &&
                db.InspectM6JProspectiveReview(f.Intent.Id).State=="NeedsAttention" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination) &&
                !File.Exists(f.Intent.OriginalArchive) && !File.Exists(f.Intent.Temp) &&
                File.Exists(f.Prior.Destination),
                "Tampered Copy preceded unauthorized filesystem Move or durable review mutation.");
        });
        Check("M6J_A3_P2B_CHANGED_COPY_OUTPUT_BLOCKS_SELECTIVE_UNDO",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            File.AppendAllText(f.Prior.Destination,"synthetic changed Copy output");
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(
                moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.OriginalArchive) && File.Exists(moved.Destination) &&
                !File.Exists(moved.Source),
                "Changed Copy predecessor allowed an unsafe selective Move Undo.");
        });
        Check("M6J_A3_P2B_CORRUPTED_CONSUMED_SEAL_BLOCKS_UNDO",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                connection.Open(); using var cmd=connection.CreateCommand();
                cmd.CommandText="UPDATE m6j_review_proposals SET review_digest='CORRUPTED' WHERE id=$id";
                cmd.Parameters.AddWithValue("$id",moved.Id.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Fixture seal corruption did not apply.");
            }
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(
                moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.OriginalArchive) && File.Exists(moved.Destination),
                "Bad consumed seal allowed file relocation during selective Undo.");
        });
        Check("M6J_A3_P2B_OCCUPIED_REVIEWED_TARGET_BLOCKS_INTENT",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            string frozenRow=P2BImmutableReviewRow(db,f.Intent.Id);
            Ensure(db.InspectM6JProspectiveReview(f.Intent.Id).State=="Reviewed",
                "Fixture review was not initially fresh.");
            File.WriteAllText(f.Intent.Destination,"synthetic foreign occupant");
            var mover=new DurableMoveCoordinator(db);
            Refuse<IOException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 &&
                P2BImmutableReviewRow(db,f.Intent.Id)==frozenRow &&
                db.InspectM6JProspectiveReview(f.Intent.Id).State=="NeedsAttention" &&
                File.ReadAllText(f.Intent.Destination)=="synthetic foreign occupant" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.OriginalArchive) &&
                !File.Exists(f.Intent.Temp) && File.Exists(f.Prior.Destination),
                "Occupied destination led to Intent, review consumption or replacement.");
        });
        // M6J-A3-P2C-P0: read-only cold-restart interrupted mixed-family
        // observations. No journal transition, file movement or automatic
        // recovery occurs in the inspector; each cut remains manual-only.
        void P2CMoveCut(string phase, string expectedState, string expectedSource,
            string expectedTarget, string expectedTemp, string expectedArchive)
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut==phase)throw new SimulatedInterruptionException(cut);
            });
            Refuse<SimulatedInterruptionException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var before=cold.MoveItems(f.Intent.SessionId).Single();
            var report=cold.InspectM6JInterruptedCopyMoveReadOnly(f.Intent.SessionId);
            var after=cold.MoveItems(f.Intent.SessionId).Single();
            Ensure(before==after && report.MoveId==f.Intent.Id &&
                report.RecordedMoveState==expectedState &&
                report.MoveSource==expectedSource && report.MoveTarget==expectedTarget &&
                report.MoveTemp==expectedTemp && report.MoveOriginalArchive==expectedArchive &&
                report.ConsumedReviewSealAndCandidateMatch &&
                report.OriginalCopyHistoryDigestMatches &&
                report.CommittedCopyNativeOwnershipMatches &&
                report.NativeClaimsAreDisjoint &&
                report.RecoveryVerdict=="MANUAL_RECONCILIATION_REQUIRED" &&
                !report.AuthorizesFilesystemMutation &&
                !report.AuthorizesAutomaticReplay && !report.AuthorizesAutomaticUndo &&
                !report.AuthorizesJournalStateRepair &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed" &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult()==
                    f.Prior.SourceHash,
                "Mixed-family interruption observation incorrectly changed or authorized the ledger.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(cold)
                .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                .GetAwaiter().GetResult());
            Ensure(cold.MoveItems(f.Intent.SessionId).Single()==before,
                "Interrupted Move selective Undo modified a nonterminal journal.");
        }
        void P2CUndoCut(string phase,string expectedState,string expectedSource,
            string expectedTarget,string expectedArchive,string expectedOutputArchive)
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var normal=new DurableMoveCoordinator(db);
            normal.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,P2BPrepared(f.Intent),f.Intent)
                .GetAwaiter().GetResult();
            var interrupted=new DurableMoveCoordinator(db,cut=>{
                if(cut==phase)throw new SimulatedInterruptionException(cut);
            });
            Refuse<SimulatedInterruptionException>(()=>interrupted
                .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                .GetAwaiter().GetResult());
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var before=cold.MoveItems(f.Intent.SessionId).Single();
            var report=cold.InspectM6JInterruptedCopyMoveReadOnly(f.Intent.SessionId);
            Ensure(report.RecordedMoveState==expectedState &&
                report.MoveSource==expectedSource && report.MoveTarget==expectedTarget &&
                report.MoveOriginalArchive==expectedArchive &&
                report.MoveOutputArchive==expectedOutputArchive &&
                report.ConsumedReviewSealAndCandidateMatch &&
                report.OriginalCopyHistoryDigestMatches &&
                report.CommittedCopyNativeOwnershipMatches &&
                report.NativeClaimsAreDisjoint &&
                !report.AuthorizesAutomaticReplay && !report.AuthorizesAutomaticUndo &&
                !report.AuthorizesFilesystemMutation &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed" &&
                SortingWorkspace.ComputeSha256Async(f.Prior.Destination).GetAwaiter().GetResult()==
                    f.Prior.SourceHash,
                "Interrupted Undo evidence was misleading or changed Copy predecessor.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(cold)
                .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                .GetAwaiter().GetResult());
            Ensure(cold.MoveItems(f.Intent.SessionId).Single()==before,
                "Interrupted Undo retry overwrote pending journal without independent authority.");
        }
        Check("M6J_A3_P2C_MOVE_INTENT_COLD_READONLY",()=>
            P2CMoveCut("MOVE_INTENT_RECORDED","Intent","OWNED","ABSENT","ABSENT","ABSENT"));
        Check("M6J_A3_P2C_MOVE_TEMP_UNJOURNALLED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_TEMP_FLUSHED_UNRECORDED","Intent","OWNED","ABSENT","UNKNOWN","ABSENT"));
        Check("M6J_A3_P2C_MOVE_TEMP_RECORDED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_TEMP_RECORDED","TempReady","OWNED","ABSENT","OWNED","ABSENT"));
        Check("M6J_A3_P2C_MOVE_PUBLISHED_UNJOURNALLED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_OUTPUT_PUBLISHED_UNJOURNALLED","TempReady","OWNED","OWNED","OWNED","ABSENT"));
        Check("M6J_A3_P2C_MOVE_PUBLISHED_RECORDED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_OUTPUT_PUBLISHED_RECORDED","TargetPublished","OWNED","OWNED","OWNED","ABSENT"));
        Check("M6J_A3_P2C_MOVE_ARCHIVE_INTENT_COLD_READONLY",()=>
            P2CMoveCut("MOVE_SOURCE_ARCHIVE_INTENT","ArchiveIntent","OWNED","OWNED","OWNED","ABSENT"));
        Check("M6J_A3_P2C_MOVE_ARCHIVED_UNJOURNALLED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_SOURCE_RELOCATED_UNJOURNALLED","ArchiveIntent","ABSENT","OWNED","OWNED","OWNED"));
        Check("M6J_A3_P2C_MOVE_ARCHIVED_RECORDED_COLD_READONLY",()=>
            P2CMoveCut("MOVE_SOURCE_ARCHIVED_RECORDED","SourceArchived","ABSENT","OWNED","OWNED","OWNED"));
        Check("M6J_A3_P2C_UNDO_RESTORED_UNJOURNALLED_COLD_READONLY",()=>
            P2CUndoCut("MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED","UndoRestoreIntent",
                "OWNED","OWNED","ABSENT","ABSENT"));
        Check("M6J_A3_P2C_UNDO_ARCHIVED_UNJOURNALLED_COLD_READONLY",()=>
            P2CUndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED","UndoOutputArchiveIntent",
                "OWNED","ABSENT","ABSENT","OWNED"));
        Check("M6J_A3_P2C_CHANGED_COPY_PRESERVED_AS_UNTRUSTED",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_INTENT_RECORDED")throw new SimulatedInterruptionException(cut);
            });
            Refuse<SimulatedInterruptionException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            File.AppendAllText(f.Prior.Destination,"P2C synthetic owned predecessor altered");
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var report=cold.InspectM6JInterruptedCopyMoveReadOnly(f.Intent.SessionId);
            Ensure(report.ConsumedReviewSealAndCandidateMatch &&
                report.OriginalCopyHistoryDigestMatches &&
                !report.CommittedCopyNativeOwnershipMatches &&
                !report.AuthorizesFilesystemMutation && !report.AuthorizesAutomaticReplay &&
                cold.MoveItems(f.Intent.SessionId).Single().State=="Intent",
                "Tampered Copy was not reported as unverifiable manual-recovery evidence.");
        });
        Check("M6J_A3_P2C_CORRUPTED_CONSUMED_SEAL_UNTRUSTED",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_INTENT_RECORDED")throw new SimulatedInterruptionException(cut);
            });
            Refuse<SimulatedInterruptionException>(()=>mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            using(var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString()))
            {
                con.Open();using var cmd=con.CreateCommand();
                cmd.CommandText="UPDATE m6j_review_proposals SET review_digest='BAD' WHERE id=$id";
                cmd.Parameters.AddWithValue("$id",f.Intent.Id.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Synthetic review corruptor did not apply.");
            }
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var before=cold.MoveItems(f.Intent.SessionId).Single();
            var report=cold.InspectM6JInterruptedCopyMoveReadOnly(f.Intent.SessionId);
            Ensure(!report.ConsumedReviewSealAndCandidateMatch &&
                report.OriginalCopyHistoryDigestMatches &&
                report.CommittedCopyNativeOwnershipMatches &&
                !report.AuthorizesFilesystemMutation && !report.AuthorizesAutomaticUndo &&
                cold.MoveItems(f.Intent.SessionId).Single()==before,
                "Corrupt consumed seal was misclassified or the inspector rewrote evidence.");
        });
        // M6J-A3-P2C-P1: a *single*, generated-only recovery action. The
        // selective Undo has already completed BOTH filesystem relocations;
        // only its terminal SQLite row was interrupted. No native mutation.
        void P2C1UndoCut(string boundary,
            Action<WorkspaceSessionStore,DurableMoveItem,DurableCopyItem> action)
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var interrupted=new DurableMoveCoordinator(db,point=>{
                if(point==boundary)throw new SimulatedInterruptionException(point);
            });
            Refuse<SimulatedInterruptionException>(()=>interrupted
                .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                .GetAwaiter().GetResult());
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(folder);
            action(cold,cold.MoveItems(f.Intent.SessionId).Single(),f.Prior);
        }
        Check("M6J_A3_P2C_P1_FINISHED_NATIVE_UNDO_JOURNAL_TERMINALIZATION",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                Ensure(before.State=="UndoOutputArchiveIntent" &&
                    DurableMoveCoordinator.ProveTerminalUndoneEvidence(before with {State="Undone"}),
                    "P2C-P1 positive fixture did not reach completely undone native state.");
                string originalHash=SortingWorkspace.ComputeSha256Async(before.Source)
                    .GetAwaiter().GetResult();
                string outputHash=SortingWorkspace.ComputeSha256Async(before.OutputArchive)
                    .GetAwaiter().GetResult();
                string copyHash=SortingWorkspace.ComputeSha256Async(copy.Destination)
                    .GetAwaiter().GetResult();
                var receipt=cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId);
                var after=cold.MoveItems(before.SessionId).Single();
                Ensure(receipt.JournalUpdated && receipt.MoveId==before.Id &&
                    receipt.FinalRecordedState=="Undone" &&
                    !receipt.AuthorizesFilesystemMutation &&
                    !receipt.AuthorizesAutomaticReplay &&
                    !receipt.AuthorizesNewMoveIntent &&
                    !receipt.AuthorizesOrdinaryPhotos &&
                    after==(before with {State="Undone"}) &&
                    DurableMoveCoordinator.ProveTerminalUndoneEvidence(after) &&
                    SortingWorkspace.ComputeSha256Async(before.Source).GetAwaiter().GetResult()==originalHash &&
                    SortingWorkspace.ComputeSha256Async(before.OutputArchive).GetAwaiter().GetResult()==outputHash &&
                    SortingWorkspace.ComputeSha256Async(copy.Destination).GetAwaiter().GetResult()==copyHash &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P1 journal-only Undo finalization altered native files or committed Copy.");
            }));
        Check("M6J_A3_P2C_P1_ALREADY_FINALIZED_SINGLE_USE_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId);
                var after=cold.MoveItems(before.SessionId).Single();
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==after &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P1 accepted a repeated terminal journal update.");
            }));
        Check("M6J_A3_P2C_P1_UNDO_BEFORE_OUTPUT_RELOCATION_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_ARCHIVE_INTENT",(cold,before,copy)=>
            {
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.Exists(before.Destination) && !File.Exists(before.OutputArchive),
                    "P2C-P1 falsely finalized undone output before native relocation.");
            }));
        Check("M6J_A3_P2C_P1_UNDO_SOURCE_RESTORED_CUT_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED",(cold,before,copy)=>
            {
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.Exists(before.Destination),
                    "P2C-P1 bypassed earlier Undo state despite installed output.");
            }));
        Check("M6J_A3_P2C_P1_TAMPERED_RESTORED_SOURCE_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                File.AppendAllText(before.Source,"P2C-P1 altered restored original");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P1 accepted altered restored source.");
            }));
        Check("M6J_A3_P2C_P1_TAMPERED_OUTPUT_ARCHIVE_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                File.AppendAllText(before.OutputArchive,"P2C-P1 changed installed archive");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.Exists(before.OutputArchive),
                    "P2C-P1 accepted altered output archive.");
            }));
        Check("M6J_A3_P2C_P1_TAMPERED_COPY_PREDECESSOR_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                File.AppendAllText(copy.Destination,"P2C-P1 changed committed Copy");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P1 ignored corrupted committed Copy native evidence.");
            }));
        Check("M6J_A3_P2C_P1_CORRUPT_CONSUMED_REVIEW_SEAL_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                using(var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource=cold.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
                }.ToString()))
                {
                    con.Open();using var cmd=con.CreateCommand();
                    cmd.CommandText="UPDATE m6j_review_proposals SET review_digest='BAD' WHERE id=$id";
                    cmd.Parameters.AddWithValue("$id",before.Id.ToString("N"));
                    Ensure(cmd.ExecuteNonQuery()==1,"Failed to corrupt P2C-P1 fixture review.");
                }
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P1 ignored a corrupted consumed review seal.");
            }));
        Check("M6J_A3_P2C_P1_OCCUPIED_DESTINATION_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                File.WriteAllText(before.Destination,"P2C-P1 foreign occupied destination");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.ReadAllText(before.Destination)=="P2C-P1 foreign occupied destination",
                    "P2C-P1 ignored destination occupant or modified foreign bytes.");
            }));
        Check("M6J_A3_P2C_P1_CORRUPT_RECOVERY_OWNER_MARKER_REFUSES",() =>
            P2C1UndoCut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED",(cold,before,copy)=>
            {
                string marker=Path.Combine(Path.GetDirectoryName(before.OriginalArchive)!,
                    ".photosift-operation-v1");
                File.WriteAllText(marker,"P2C-P1 foreign marker");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedUndoJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P1 accepted a foreign operation recovery marker.");
            }));

        // M6J-A3-P2C-P2: two major deliverables in one owner gate:
        // (a) completed-native Move journal finalization only; (b) the
        // installation-wide, four-family Recovery Command Center projection.
        // NO additional ordinary file-operation authority.
        void P2C2MoveCut(string boundary,
            Action<WorkspaceSessionStore,DurableMoveItem,DurableCopyItem> action)
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var mover=new DurableMoveCoordinator(db,point=>
            {
                if(point==boundary)throw new SimulatedInterruptionException(point);
            });
            Refuse<SimulatedInterruptionException>(()=>mover
                .ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            action(cold,cold.MoveItems(f.Intent.SessionId).Single(),f.Prior);
        }
        void P2C2FinishNativeTemp(DurableMoveItem before)
        {
            Ensure(before.State=="SourceArchived" && before.TempIdentity is not null &&
                before.TargetIdentity==before.TempIdentity &&
                NativeFileCapability.Identity(before.Temp)==before.TempIdentity &&
                SortingWorkspace.ComputeSha256Async(before.Temp).GetAwaiter().GetResult()==
                    before.SourceHash && File.Exists(before.OriginalArchive) &&
                File.Exists(before.Destination),
                "P2C-P2 test fixture is not a real native SourceArchived phase.");
            // Simulate completion of the EXISTING coordinator's temp unlink
            // after its last checkpoint but before the final SQLite write.
            // This test helper is the only writer; recovery API must not unlink.
            File.Delete(before.Temp);
            Ensure(!File.Exists(before.Temp) &&
                NativeFileCapability.IsSingleLinkRegularFile(before.Destination),
                "Test fixture did not reach exact post-unlink native state.");
        }
        Check("M6J_A3_P2C_P2_NATIVE_MOVE_FINISHED_JOURNAL_FINALIZES",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                string original=SortingWorkspace.ComputeSha256Async(before.OriginalArchive)
                    .GetAwaiter().GetResult();
                string output=SortingWorkspace.ComputeSha256Async(before.Destination)
                    .GetAwaiter().GetResult();
                string predecessor=SortingWorkspace.ComputeSha256Async(copy.Destination)
                    .GetAwaiter().GetResult();
                var result=cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId);
                var after=cold.MoveItems(before.SessionId).Single();
                Ensure(result.JournalUpdated && result.MoveId==before.Id &&
                    result.FinalRecordedState=="Committed" &&
                    !result.AuthorizesFilesystemMutation && !result.AuthorizesAutomaticReplay &&
                    !result.AuthorizesAdditionalEpoch && !result.AuthorizesOrdinaryPhotos &&
                    after==(before with {State="Committed"}) &&
                    SortingWorkspace.ComputeSha256Async(after.OriginalArchive).GetAwaiter().GetResult()==original &&
                    SortingWorkspace.ComputeSha256Async(after.Destination).GetAwaiter().GetResult()==output &&
                    SortingWorkspace.ComputeSha256Async(copy.Destination).GetAwaiter().GetResult()==predecessor &&
                    cold.CopyItems(copy.SessionId).Single()==copy && !File.Exists(after.Temp),
                    "Post-native Move journaling mutated bytes or damaged committed predecessor.");
            }));
        Check("M6J_A3_P2C_P2_REPLAY_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId);
                var committed=cold.MoveItems(before.SessionId).Single();
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==committed &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P2 replay was permitted.");
            }));
        Check("M6J_A3_P2C_P2_TEMP_HARDLINK_PRESENT_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.Exists(before.Temp),"P2C-P2 accepted incomplete native temp unlink.");
            }));
        Check("M6J_A3_P2C_P2_EARLIER_SOURCE_ARCHIVE_CUT_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVE_INTENT",(cold,before,copy)=>
            {
                Ensure(before.State=="ArchiveIntent","Expected earlier journal state.");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.Exists(before.Source),"Premature journal transition allowed.");
            }));
        Check("M6J_A3_P2C_P2_OCCUPIED_ORIGINAL_PATH_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.WriteAllText(before.Source,"foreign occupant");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    File.ReadAllText(before.Source)=="foreign occupant",
                    "P2C-P2 overlooked occupied original path.");
            }));
        Check("M6J_A3_P2C_P2_DESTINATION_BYTES_TAMPER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.AppendAllText(before.Destination,"foreign bytes");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 accepted changed destination bytes.");
            }));
        Check("M6J_A3_P2C_P2_ORIGINAL_ARCHIVE_TAMPER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.AppendAllText(before.OriginalArchive,"foreign archive bytes");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 accepted altered original recovery archive.");
            }));
        Check("M6J_A3_P2C_P2_COPY_PREDECESSOR_TAMPER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.AppendAllText(copy.Destination,"corrupt prior Copy output");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before &&
                    cold.CopyItems(copy.SessionId).Single()==copy,
                    "P2C-P2 overlooked changed Copy predecessor.");
            }));
        Check("M6J_A3_P2C_P2_CORRUPT_ROOT_OWNER_MARKER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(before.Source)!,
                    ".photosift-reborn-recovery",".photosift-move-owner-v1"),"foreign marker");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 overlooked altered root marker.");
            }));
        Check("M6J_A3_P2C_P2_CORRUPT_OPERATION_MARKER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(before.OriginalArchive)!,
                    ".photosift-operation-v1"),"foreign marker");
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 overlooked altered operation marker.");
            }));
        Check("M6J_A3_P2C_P2_CONSUMED_REVIEW_TAMPER_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                using(var con=new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource=cold.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
                }.ToString()))
                {
                    con.Open();using var cmd=con.CreateCommand();
                    cmd.CommandText="UPDATE m6j_review_proposals SET review_digest='INVALID' WHERE id=$id";
                    cmd.Parameters.AddWithValue("$id",before.Id.ToString("N"));
                    Ensure(cmd.ExecuteNonQuery()==1,"P2C-P2 review tamper did not run.");
                }
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 overlooked consumed review corruption.");
            }));
        Check("M6J_A3_P2C_P2_DISPOSABLE_MARKER_REMOVED_REFUSED",() =>
            P2C2MoveCut("MOVE_SOURCE_ARCHIVED_RECORDED",(cold,before,copy)=>
            {
                P2C2FinishNativeTemp(before);
                string root=Directory.GetParent(Path.GetDirectoryName(cold.DatabasePath)!)!.FullName;
                File.Delete(Path.Combine(root,"P3B2_TEST_ONLY.marker"));
                Refuse<IOException>(()=>cold.FinalizeM6JGeneratedOnlyCompletedMoveJournal(before.SessionId));
                Ensure(cold.MoveItems(before.SessionId).Single()==before,
                    "P2C-P2 lost fixture barrier.");
            }));

        // Recovery Command Center is an ACTUAL modal entry point in History
        // and Diagnostics. Presentation-level tests exercise failure, risks,
        // summary, session scope, saved proof, filter and clipboard content.
        UnifiedHistoryInventory RecoveryInventory(
            UnifiedHistoryKind kind,int copies,int moves,int renames,int rejects,
            bool complete,params UnifiedHistoryRowEvidence[] rows)
            =>new(new(kind,copies,moves,renames,rejects,0,false,"synthetic read-only"),
                rows,complete,"synthetic evidence");
        M6JDependencyAssessment RecoveryDependency(M6JDependencyKind kind,int count,
            params M6JDependencyEdge[] edges)
            =>new(kind,count,edges,"Synthetic path evidence; not operation authority.");
        UnifiedHistoryRowEvidence RecoveryRow(string family,int n,string state,
            UnifiedRowProof proof,string explanation="Exact native fixture observation")
            =>new(family,n.ToString("x32"),(n+500).ToString("x32"),state,proof,explanation);
        Check("M6J_A3_P2C_P2_DOSSIER_EMPTY_NO_AUTHORITY",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.Fresh,0,0,0,0,true),
                RecoveryDependency(M6JDependencyKind.EmptyObservation,0));
            Ensure(view.Cases.Count==0 && view.Conflicts.Count==0 &&
                !view.AuthorizesNativeOperation && !view.AuthorizesRepair &&
                !view.AuthorizesSelectiveUndo && view.Report.Contains("READ-ONLY") &&
                view.Risk.Contains("NEVER AN OPERATION GRANT"),
                "Empty dossier implied native permission.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_EXACT_FOUR_FAMILY_COUNTS",() =>
        {
            var rows=new[]{RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified),
                RecoveryRow("Move",2,"Undone",UnifiedRowProof.NativeTerminalVerified),
                RecoveryRow("Rename",3,"Committed",UnifiedRowProof.NativeActiveVerified),
                RecoveryRow("Reject",4,"Undone",UnifiedRowProof.NativeTerminalVerified)};
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.ForeignMixed,1,1,1,1,true,rows),
                RecoveryDependency(M6JDependencyKind.DependencyDetected,4));
            Ensure(view.Cases.Count==4 && view.Heading.Contains("4 journals") &&
                view.Report.Contains("Copy") && view.Report.Contains("Reject") &&
                !view.AuthorizesNativeOperation,"Four-family dossier lost rows.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_UNFINISHED_NATIVE_HOLD",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,0,1,0,0,true,
                    RecoveryRow("Move",1,"ArchiveIntent",UnifiedRowProof.NotVerified)),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,1));
            Ensure(view.Risk.Contains("INCOMPLETE") &&
                view.Heading.Contains("1 unfinished") &&
                view.Heading.Contains("1 unverified") &&
                view.Report.Contains("ArchiveIntent"),
                "Interrupted operation looked terminal or safe.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_INCOMPLETE_SCAN_HOLD",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,2,0,0,0,false,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified)),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,0));
            Ensure(view.Risk.Contains("INCOMPLETE") &&
                view.NextStep.Contains("Preserve"),
                "Partial journal inventory was promoted.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_CONFLICT_DETAILS",() =>
        {
            var edge=new M6JDependencyEdge("Copy",1.ToString("x32"),"Move",
                2.ToString("x32"),M6JConflictKind.SameRecordedPath,"Same underlying path");
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.ForeignMixed,1,1,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified),
                    RecoveryRow("Move",2,"Undone",UnifiedRowProof.NativeTerminalVerified)),
                RecoveryDependency(M6JDependencyKind.DependencyDetected,2,edge));
            Ensure(view.Conflicts.Count==1 && view.Risk.Contains("REVIEW REQUIRED") &&
                view.Report.Contains("SameRecordedPath") &&
                view.Report.Contains("Same underlying path"),
                "Important dependency not displayed in dossier.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_TERMINAL_STILL_NO_GRANT",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.AllTerminalVerified,0,1,0,0,true,
                    RecoveryRow("Move",1,"Undone",UnifiedRowProof.NativeTerminalVerified)),
                RecoveryDependency(M6JDependencyKind.LexicallyDisjointNotAuthorized,1));
            Ensure(view.Risk.Contains("NEVER AN OPERATION GRANT") &&
                !view.AuthorizesRepair &&
                view.Report.Contains("not a transaction"),
                "Terminal evidence promoted to authorization.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_HISTORY_COUNT_MISMATCH_HOLD",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.AllTerminalVerified,2,0,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified)),
                RecoveryDependency(M6JDependencyKind.LexicallyDisjointNotAuthorized,1));
            Ensure(view.Risk.Contains("INCOMPLETE"),
                "Torn/missing journal count became healthy.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_DEPENDENCY_COUNT_MISMATCH_HOLD",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.AllTerminalVerified,1,0,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified)),
                RecoveryDependency(M6JDependencyKind.LexicallyDisjointNotAuthorized,0));
            Ensure(view.Risk.Contains("INCOMPLETE"),
                "Independent audit disagreement was ignored.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_DUPLICATE_FAMILY_KEY_REFUSED",() =>
        {
            var a=RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified);
            Refuse<InvalidDataException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,2,0,0,0,true,a,a),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,2)));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_EMPTY_GUID_REFUSED",() =>
        {
            Refuse<InvalidDataException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,1,0,0,0,true,
                    new UnifiedHistoryRowEvidence("Copy",Guid.Empty.ToString("N"),
                        Guid.NewGuid().ToString("N"),"Committed",
                        UnifiedRowProof.NativeActiveVerified,"Bad ID")),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,1)));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_UNKNOWN_FAMILY_REFUSED",() =>
        {
            Refuse<InvalidDataException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,1,0,0,0,true,
                    RecoveryRow("Magic",1,"Committed",UnifiedRowProof.NativeActiveVerified)),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,1)));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_NO_NULL_EVIDENCE",() =>
        {
            Refuse<InvalidDataException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,1,0,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified,"")),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,1)));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_REPORT_INCIDENT_BOUNDARY",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,1,0,0,0,true,
                    RecoveryRow("Copy",1,"NeedsAttention",UnifiedRowProof.NotVerified)),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,1));
            Ensure(view.Report.Contains("RECOVERY & DEPENDENCY DOSSIER") &&
                view.Report.Contains("No automatic replay") &&
                view.Report.Contains("Observed evidence") &&
                view.Report.Contains("Installation history") &&
                view.Report.Contains("RECORDED DEPENDENCY CONFLICTS"),
                "Dossier lost current-proof boundaries.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_FILTER_BY_FAMILY",() =>
        {
            var c=new[]{new StudioRecoveryCase("1","Copy","Committed","3",
                    UnifiedRowProof.NativeActiveVerified,"owned"),
                new StudioRecoveryCase("2","Reject","Staged","4",
                    UnifiedRowProof.NotVerified,"unverified")};
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,1,0,null).Length==1 &&
                StudioRecoveryCommandCenterPresentation.Filter(c,4,0,null).Single().Family=="Reject" &&
                StudioRecoveryCommandCenterPresentation.Filter(c,3,0,null).Length==0,
                "Family filter hid or confused operation rows.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_FILTER_UNVERIFIED",() =>
        {
            var c=new[]{new StudioRecoveryCase("1","Copy","Committed","3",
                    UnifiedRowProof.NativeActiveVerified,"owned"),
                new StudioRecoveryCase("2","Move","Intent","4",
                    UnifiedRowProof.NotVerified,"unverified")};
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,1,null).Length==1 &&
                StudioRecoveryCommandCenterPresentation.Filter(c,0,1,null)[0].Key=="2",
                "Unverified filter failed.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_FILTER_UNFINISHED",() =>
        {
            var c=new[]{new StudioRecoveryCase("1","Copy","Committed","3",
                    UnifiedRowProof.NativeActiveVerified,"owned"),
                new StudioRecoveryCase("2","Move","UndoRestoreIntent","4",
                    UnifiedRowProof.NotVerified,"unsafe")};
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,2,null).Length==1 &&
                StudioRecoveryCommandCenterPresentation.Filter(c,0,2,null)[0].Key=="2",
                "Unfinished filter failed.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_FILTER_VERIFIED_TERMINAL",() =>
        {
            var c=new[]{new StudioRecoveryCase("1","Copy","Committed","3",
                    UnifiedRowProof.NativeActiveVerified,"owned"),
                new StudioRecoveryCase("2","Move","Undone","4",
                    UnifiedRowProof.NativeTerminalVerified,"verified"),
                new StudioRecoveryCase("3","Reject","Undone","5",
                    UnifiedRowProof.NotVerified,"broken")};
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,3,null).Length==2,
                "Verified terminal filter admitted unverified Undo.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_FILTER_CASE_INSENSITIVE_SEARCH",() =>
        {
            var c=new[]{new StudioRecoveryCase("1","Rename","Undone","3",
                    UnifiedRowProof.NativeTerminalVerified,"SENTINEL_HASH_OK"),
                new StudioRecoveryCase("2","Copy","Committed","4",
                    UnifiedRowProof.NativeActiveVerified,"other")};
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,0," sentinel_hash_ok ").Length==1 &&
                StudioRecoveryCommandCenterPresentation.Filter(c,0,0,"  ").Length==2,
                "Search normalization failed.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_INVALID_FILTER_REFUSED",() =>
        {
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryCommandCenterPresentation.Filter(
                Array.Empty<StudioRecoveryCase>(),5,0,null));
            Refuse<ArgumentOutOfRangeException>(()=>StudioRecoveryCommandCenterPresentation.Filter(
                Array.Empty<StudioRecoveryCase>(),0,4,null));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_SESSION_BOUNDARY_EXPLICIT",() =>
        {
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,2,0,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified),
                    RecoveryRow("Copy",2,"Undone",UnifiedRowProof.NativeTerminalVerified)),
                RecoveryDependency(M6JDependencyKind.IncompleteOrUnverified,2));
            Ensure(view.Cases.Select(x=>x.SessionId).Distinct().Count()==2 &&
                view.Report.Contains("Session:") && view.Heading.Contains("2 journals"),
                "Cross-session history disappeared from installation-wide dossier.");
        });
        Check("M6J_A3_P2C_P2_DOSSIER_NULL_ARGUMENTS_REFUSED",() =>
        {
            Refuse<ArgumentNullException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                null!,RecoveryDependency(M6JDependencyKind.EmptyObservation,0)));
            Refuse<ArgumentNullException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.Fresh,0,0,0,0,true),null!));
        });
        Check("M6J_A3_P2C_P2_DOSSIER_MALFORMED_CONFLICT_REFUSED",() =>
        {
            var bad=new M6JDependencyEdge("Copy","1","Move","2",
                M6JConflictKind.SamePhoto,"");
            Refuse<InvalidDataException>(()=>StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,0,0,0,0,true),
                RecoveryDependency(M6JDependencyKind.DependencyDetected,0,bad)));
        });

        // P2C-P2-R2: UI-specific regressions. Every example below is in-memory
        // ONLY, never a journal write, owner marker, grant, filesystem operation,
        // or simulation of actual native evidence.
        Check("M6J_A3_P2C_P2_R2_SAMPLE_ALL_FAMILIES_VISIBLE",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            Ensure(sample.Cases.Select(x=>x.Family).SequenceEqual(
                new[]{"Copy","Move","Rename","Reject"}),
                "Explicit family selectors cannot preview all families.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_NEVER_OPERATION_AUTHORITY",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            Ensure(!sample.AuthorizesNativeOperation && !sample.AuthorizesRepair &&
                !sample.AuthorizesSelectiveUndo && sample.Risk.Contains("SAMPLE DATA ONLY") &&
                sample.Report.Contains("NEVER A NATIVE RECOVERY REPORT"),
                "Sample report implied real native operation authority.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_MARKERS_EVERY_CASE",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            Ensure(sample.Cases.Count==4 &&
                sample.Cases.All(x=>x.Explanation.Contains("SAMPLE ONLY")) &&
                sample.Conflicts.Count==1 &&
                sample.Conflicts.All(x=>x.Explanation.Contains("SAMPLE ONLY")),
                "In-memory sample contained a realistic, unlabeled claim.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_GUIDS_AND_SESSIONS_VALID",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            Ensure(sample.Cases.All(x=>Guid.TryParseExact(x.Key,"N",out var id) && id!=Guid.Empty &&
                Guid.TryParseExact(x.SessionId,"N",out var sid) && sid!=Guid.Empty),
                "Sample rows malformed and misleading.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_FILTER_ALL_FAMILIES",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,0,null).Length==4,
                "All operation families no longer visible.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_FILTER_COPY",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,1,0,null).Single().Family=="Copy",
                "Copy chip did not select Copy.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_FILTER_MOVE",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,2,0,null).Single().Family=="Move",
                "Move chip did not select Move.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_FILTER_RENAME",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,3,0,null).Single().Family=="Rename",
                "Rename chip did not select Rename.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_FILTER_REJECT",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,4,0,null).Single().Family=="Reject",
                "Reject chip did not select Reject.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_UNVERIFIED",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            var filtered=StudioRecoveryCommandCenterPresentation.Filter(c,0,1,null);
            Ensure(filtered.Length==2 && filtered.All(x=>x.Proof==UnifiedRowProof.NotVerified),
                "Example's unverified filter cannot be inspected.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_UNFINISHED",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,2,null).Length==2,
                "Example's unfinished status no longer visible.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_VERIFIED_TERMINAL",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,3,null).Length==2,
                "Example's verified terminal status no longer visible.");
        });
        Check("M6J_A3_P2C_P2_R2_SAMPLE_SEARCH",() =>
        {
            var c=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases;
            Ensure(StudioRecoveryCommandCenterPresentation.Filter(c,0,0," interrupted ").Length==1,
                "Search did not find marked sample Move.");
        });
        Check("M6J_A3_P2C_P2_R2_EMPTY_REAL_HISTORY_EXPLAINED",() =>
        {
            var empty=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.Fresh,0,0,0,0,true),
                RecoveryDependency(M6JDependencyKind.EmptyObservation,0));
            Ensure(StudioRecoveryCommandCenterUiState.EmptyHeading(empty,0,0,0,null)
                    =="No saved operations yet" &&
                StudioRecoveryCommandCenterUiState.EmptyExplanation(empty,0,0,0,null)
                    .Contains("not proof"),
                "Empty state falsely conveyed safety or lacked guidance.");
        });
        Check("M6J_A3_P2C_P2_R2_FILTER_NO_MATCH_DISTINCT",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            Ensure(StudioRecoveryCommandCenterUiState.EmptyHeading(sample,0,1,3,"nomatch")
                    =="No operations match these filters" &&
                StudioRecoveryCommandCenterUiState.EmptyExplanation(sample,0,1,3,"nomatch")
                    .Contains("There are saved operations"),
                "Filtered zero incorrectly reported an empty database.");
        });
        Check("M6J_A3_P2C_P2_R2_NOT_INSPECTED_DISTINCT",() =>
        {
            Ensure(StudioRecoveryCommandCenterUiState.EmptyHeading(null,0,0,0,null)
                =="Inspection hasn't completed" &&
                StudioRecoveryCommandCenterUiState.EmptyExplanation(null,0,0,0,null)
                    .Contains("Nothing is changed"),
                "Pre-inspection state posed as observed empty history.");
        });
        Check("M6J_A3_P2C_P2_R2_SUMMARY_EXACT_COUNTS",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            var summary=StudioRecoveryCommandCenterUiState.Summary(sample,2);
            Ensure(summary.Contains("4 example operations") && summary.Contains("2 unverified") &&
                summary.Contains("2 unfinished") && summary.Contains("1 conflicts") &&
                summary.Contains("2 shown"),
                "UI counter misrepresented example evidence.");
        });
        Check("M6J_A3_P2C_P2_R2_ROW_LABEL_UNVERIFIED_VISIBLE",() =>
        {
            var sample=StudioRecoveryCommandCenterUiState.ExampleOnly();
            var label=StudioRecoveryCommandCenterUiState.RowLabel(sample.Cases[1]);
            Ensure(label.Contains("Move") && label.Contains("UndoRestoreIntent") &&
                label.Contains("NotVerified") && label.Contains(sample.Cases[1].Key),
                "List rows masked critical state or proof.");
        });


        // M6J-P3: Real, two-family native history, paired read-only recovery
        // observation, and a one-shot generated-only GUI fixture.
        Check("M6J_P3_PAIRED_EMPTY_DATABASE_READONLY",() =>
        {
            string root=Root();using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var observed=db.InspectM6JP3PairedRecoveryObservation();
            Ensure(observed.ConsistentDatabaseObservation &&
                observed.Inventory.Rows.Count==0 &&
                observed.Dependencies.Kind==M6JDependencyKind.EmptyObservation &&
                !observed.AuthorizesNativeOperation && !observed.AuthorizesSelectiveUndo &&
                !observed.AuthorizesJournalRepair,
                "Paired empty read-only observation was not bounded or seemed authoritative.");
        });
        Check("M6J_P3_PAIRED_NATIVE_COPY_JOURNAL",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var observed=db.InspectM6JP3PairedRecoveryObservation();
            Ensure(observed.ConsistentDatabaseObservation &&
                observed.Inventory.Rows.Count==1 &&
                observed.Inventory.Rows.Single().Family=="Copy" &&
                observed.Inventory.Rows.Single().Proof==UnifiedRowProof.NativeActiveVerified &&
                !observed.AuthorizesNativeOperation,
                "Paired recovery observation did not read real committed native Copy.");
        });
        Check("M6J_P3_PAIRED_REAL_TWO_FAMILY_HISTORY",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db)
                .ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,P2BPrepared(f.Intent),f.Intent)
                .GetAwaiter().GetResult();
            Ensure(moved.State=="Committed","Real generated-only Move fixture did not commit.");
            var observed=db.InspectM6JP3PairedRecoveryObservation();
            Ensure(observed.ConsistentDatabaseObservation &&
                observed.Inventory.Rows.Count==2 &&
                observed.Inventory.Rows.Count(x=>x.Family=="Copy")==1 &&
                observed.Inventory.Rows.Count(x=>x.Family=="Move")==1 &&
                !observed.AuthorizesNativeOperation,
                "Real mixed journals were not observed as a bounded pair.");
        });
        Check("M6J_P3_PAIRED_NATIVE_AFTER_COLD_UNDO",() =>
        {
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            new DurableMoveCoordinator(db)
                .ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,P2BPrepared(f.Intent),f.Intent)
                .GetAwaiter().GetResult();
            string folder=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using(var cold=new WorkspaceSessionStore(folder))
                new DurableMoveCoordinator(cold)
                    .UndoM6JReviewedGeneratedCopyMoveAsync(f.Intent.SessionId)
                    .GetAwaiter().GetResult();
            using var finallyCold=new WorkspaceSessionStore(folder);
            var observed=finallyCold.InspectM6JP3PairedRecoveryObservation();
            Ensure(observed.ConsistentDatabaseObservation &&
                observed.Inventory.Rows.Count==2 &&
                observed.Inventory.Rows.Any(x=>x.Family=="Copy" && x.Proof==UnifiedRowProof.NativeActiveVerified) &&
                observed.Inventory.Rows.Any(x=>x.Family=="Move" && x.State=="Undone" &&
                    x.Proof==UnifiedRowProof.NativeTerminalVerified),
                "Cross-family cold-restart native row proof was lost.");
        });
        Check("M6J_P3_PAIRED_STILL_NO_DEPENDENCY_GRANT",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var observed=db.InspectM6JP3PairedRecoveryObservation();
            Ensure(!observed.Dependencies.AuthorizesNativeOperation &&
                !observed.Dependencies.AuthorizesSelectiveUndo &&
                !observed.Dependencies.GrantsSuccessorEpoch,
                "Read-only dependency snapshot became write or Undo authority.");
        });
        Check("M6J_P3_PAIRED_DOSSIER_FROM_REAL_NATIVE_ROW",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var observed=db.InspectM6JP3PairedRecoveryObservation();
            var dossier=StudioRecoveryCommandCenterPresentation.Create(
                observed.Inventory,observed.Dependencies);
            Ensure(dossier.Cases.Count==1 && dossier.Cases[0].Key==f.Prior.Id.ToString("N") &&
                dossier.Report.Contains(f.Prior.Id.ToString("N")) &&
                !dossier.AuthorizesNativeOperation && !dossier.AuthorizesRepair,
                "Native SQLite row was not represented by the read-only dossier.");
        });
        Check("M6J_P3_OPERATION_ROW_EXPLICIT_VIEW_EVIDENCE",() =>
        {
            var item=StudioRecoveryCommandCenterUiState.ExampleOnly().Cases[1];
            var primary=StudioRecoveryCommandCenterUiState.OperationPrimary(item);
            var secondary=StudioRecoveryCommandCenterUiState.OperationSecondary(item);
            Ensure(primary.Contains("UndoRestoreIntent") &&
                secondary.Contains("View evidence") && secondary.Contains("NotVerified") &&
                !secondary.Contains(item.Key),
                "Operation row lacks a concise discoverable selection affordance.");
        });
        Check("M6J_P3_SAMPLE_COUNTERS_NOT_SAVED_HISTORY",() =>
        {
            string value=StudioRecoveryCommandCenterUiState.Summary(
                StudioRecoveryCommandCenterUiState.ExampleOnly(),4);
            Ensure(value.Contains("4 example operations") && !value.Contains("4 saved"),
                "Fictitious records were counted as saved history.");
        });
        Check("M6J_P3_GENERATED_NATIVE_LAB_REQUIRES_EXPLICIT_OPT_IN",() =>
        {
            string? original=Environment.GetEnvironmentVariable(
                GeneratedNativeRecoveryHistoryLab.OptInVariable);
            try
            {
                Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,null);
                Refuse<InvalidOperationException>(()=>GeneratedNativeRecoveryHistoryLab.Create());
            }
            finally {Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,original);}
        });
        Check("M6J_P3_GENERATED_GUI_LAB_HAS_TWO_TRUE_NATIVE_FAMILIES",() =>
        {
            string? original=Environment.GetEnvironmentVariable(
                GeneratedNativeRecoveryHistoryLab.OptInVariable);
            try
            {
                Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,
                    GeneratedNativeRecoveryHistoryLab.ExactOptIn);
                string state=GeneratedNativeRecoveryHistoryLab.Create();
                Ensure(File.Exists(Path.Combine(Path.GetDirectoryName(state)!,
                        "P3_GENERATED_NATIVE_HISTORY_RECEIPT.json")),
                    "Generated-only native GUI preview did not preserve its exact witness.");
                using var cold=new WorkspaceSessionStore(state);
                var report=cold.InspectM6JP3PairedRecoveryObservation();
                Ensure(report.ConsistentDatabaseObservation && report.Inventory.Rows.Count==2 &&
                    report.Inventory.Rows.Any(x=>x.Family=="Copy" && x.Proof==UnifiedRowProof.NativeActiveVerified) &&
                    report.Inventory.Rows.Any(x=>x.Family=="Move" && x.Proof==UnifiedRowProof.NativeTerminalVerified) &&
                    !report.AuthorizesNativeOperation,
                    "Generated-only GUI preview does not hold real native Copy/Move journals.");
            }
            finally {Environment.SetEnvironmentVariable(GeneratedNativeRecoveryHistoryLab.OptInVariable,original);}
        });

        // P3-R2: exact mixed-history failure may expose read-only inspection;
        // general startup/database failures and any active work remain fail-closed.
        Check("M6J_P3_R2_DISTINCT_MIXED_STARTUP_VETO",() =>
        {
            var known = new P3R2MixedCopyMoveStartupException("generated disjoint Copy/Move journal");
            Ensure(P3R2RecoveryInspectionGate.IsExactlyKnownMixedHistory(known,true) &&
                !P3R2RecoveryInspectionGate.IsExactlyKnownMixedHistory(known,false) &&
                !P3R2RecoveryInspectionGate.IsExactlyKnownMixedHistory(
                    new InvalidDataException(known.Message),true) &&
                !P3R2RecoveryInspectionGate.IsExactlyKnownMixedHistory(
                    new IOException(known.Message),true),
                "Generic error or absent SQLite store incorrectly unlocked inspection.");
        });
        Check("M6J_P3_R2_MIXED_STARTUP_READONLY_VISIBLE",() =>
        {
            Ensure(P3R2RecoveryInspectionGate.CanInspect(true,false,true,false,false,false,false) &&
                P3R2RecoveryInspectionGate.CanInspect(true,true,false,false,false,false,false),
                "A valid mixed-history read-only recovery inspection was incorrectly blocked.");
        });
        Check("M6J_P3_R2_UNKNOWN_FAILURES_FAIL_CLOSED",() =>
        {
            Ensure(!P3R2RecoveryInspectionGate.CanInspect(false,false,true,false,false,false,false) &&
                !P3R2RecoveryInspectionGate.CanInspect(true,false,false,false,false,false,false) &&
                !P3R2RecoveryInspectionGate.CanInspect(false,true,false,false,false,false,false),
                "Missing store or unclassified startup failure opened native inspection.");
        });
        Check("M6J_P3_R2_BUSY_AND_REVIEW_LEASES_BLOCK_INSPECTION",() =>
        {
            foreach(var state in new[]{ (true,false,false,false), (false,true,false,false),
                        (false,false,true,false), (false,false,false,true) })
                Ensure(!P3R2RecoveryInspectionGate.CanInspect(true,false,true,
                    state.Item1,state.Item2,state.Item3,state.Item4),
                    "Concurrent operation/review lease did not block inspection.");
        });
        Check("M6J_P3_R2_ACTUAL_MIXED_NATIVE_ROWS_INSPECT_LOCKED",() =>
        {
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db)
                .ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,P2BPrepared(f.Intent),f.Intent)
                .GetAwaiter().GetResult();
            Ensure(moved.State=="Committed", "Native generated Move did not commit.");
            var observation=db.InspectM6JP3PairedRecoveryObservation();
            var dossier=StudioRecoveryCommandCenterPresentation.Create(
                observation.Inventory,observation.Dependencies);
            Ensure(P3R2RecoveryInspectionGate.CanInspect(true,false,true,false,false,false,false) &&
                dossier.Cases.Count==2 && dossier.Cases.Any(c=>c.Family=="Copy") &&
                dossier.Cases.Any(c=>c.Family=="Move") &&
                !observation.AuthorizesNativeOperation && !observation.AuthorizesSelectiveUndo &&
                !dossier.AuthorizesNativeOperation && !dossier.AuthorizesRepair,
                "Read-only gate or genuine two-family evidence expanded operation authority.");
        });

        // P3-R4: historical native identity and path-hierarchy witnesses are
        // strictly READ-ONLY warning signals. They are never grants or current
        // native-handle proofs, even for disjoint or fully verified journals.
        M6JOperationFootprint P4Footprint(string family, M6JPathClaim[] claims,
            bool proven=true) => new(family,Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),Guid.NewGuid(),"Undone",proven,claims);
        string p4Root=Path.Combine(Path.GetTempPath(),"M6J_P4_WITNESS_ROOT");
        Check("M6J_P4_SAVED_NATIVE_ALIAS_DISTINCT_PATHS",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"a","x.png"),
                    RecordedNativeIdentity:"volume:file:9"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"a","y.png"))});
            var b=P4Footprint("Move",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"b","x.png"),
                    RecordedNativeIdentity:"volume:file:9"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"b","z.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedNativeIdentity) &&
                !report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedPath) &&
                !report.AuthorizesNativeOperation && !report.AuthorizesSelectiveUndo,
                "Saved native identity alias disappeared when recorded paths differed.");
        });
        Check("M6J_P4_RECORDED_ANCESTOR_CLAIM_BLOCKS",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"ancestor")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"other","b.png"))});
            var b=P4Footprint("Move",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"ancestor","child.png")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"elsewhere","child.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(e=>e.Kind==M6JConflictKind.PathAncestorOverlap) &&
                !report.GrantsSuccessorEpoch,
                "Parent/child recorded path collision lacked a warning edge.");
        });
        Check("M6J_P4_PREFIX_SIBLINGS_NOT_ANCESTORS",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"family")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"out1"))});
            var b=P4Footprint("Move",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"family2","photo.png")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"out2"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.Edges.Count==0 && !report.AuthorizesSelectiveUndo,
                "String prefix of a sibling path was treated as directory containment.");
        });
        Check("M6J_P4_SHARED_ROUTING_ROOT_NOT_FALSE_OWNERSHIP",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"sources","one.png")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"outputs","F","one.png"))});
            var b=P4Footprint("Move",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"sources","two.png")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"outputs","T","two.png")),
                new M6JPathClaim("OutputRoot",Path.Combine(p4Root,"outputs"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.Edges.Count==0 && !report.AuthorizesNativeOperation,
                "Shared route container incorrectly created a false file-ownership dependency.");
        });
        Check("M6J_P4_MISSING_IDENTITY_IS_NOT_INDEPENDENCE_PROOF",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"aa","s.png")),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"aa","d.png"))});
            var b=P4Footprint("Reject",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"bb","s.png")),
                new M6JPathClaim("Archive",Path.Combine(p4Root,"bb","r.png"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.Edges.Count==0 && !report.AuthorizesNativeOperation &&
                report.Explanation.Contains("unrecorded aliases"),
                "Missing saved identities falsely promoted lexical separation to permission.");
        });
        Check("M6J_P4_INTRA_JOURNAL_IDENTITY_NOT_CROSS_EDGE",() =>
        {
            var a=P4Footprint("Rename",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"one.png"),
                    RecordedNativeIdentity:"native:same"),
                new M6JPathClaim("Target",Path.Combine(p4Root,"two.png"),
                    RecordedNativeIdentity:"native:same")});
            var report=M6JDependencyInspector.Analyze(new[]{a});
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.Edges.Count==0 && !report.AuthorizesNativeOperation,
                "A single Rename journal's own historical file identity was treated as a cross-operation conflict.");
        });
        Check("M6J_P4_MALFORMED_NATIVE_IDENTITY_REFUSES",() =>
        {
            foreach(var bad in new[]{new string('N',257),"bad\0identity"})
            {
                var a=P4Footprint("Copy",new[]{
                    new M6JPathClaim("Source",Path.Combine(p4Root,"bad","one.png"),
                        RecordedNativeIdentity:bad),
                    new M6JPathClaim("Destination",Path.Combine(p4Root,"bad","two.png"))});
                Ensure(M6JDependencyInspector.Analyze(new[]{a}).Kind==
                    M6JDependencyKind.IncompleteOrUnverified,
                    "Malformed saved native identity survived conservative validation.");
            }
        });
        Check("M6J_P4_PATH_WARNING_PERSISTS_IF_IDENTITY_DIFFERS",() =>
        {
            string shared=Path.Combine(p4Root,"share","photo.png");
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",shared,RecordedNativeIdentity:"file:a"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"da"))});
            var b=P4Footprint("Reject",new[]{
                new M6JPathClaim("Source",shared,RecordedNativeIdentity:"file:b"),
                new M6JPathClaim("Archive",Path.Combine(p4Root,"db"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedPath) &&
                !report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedNativeIdentity),
                "Different recorded identities suppressed a true shared-path collision.");
        });
        Check("M6J_P4_WEAK_PROOF_STILL_REPORTS_IDENTITY_EDGE",() =>
        {
            var a=P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"weakA"),
                    RecordedNativeIdentity:"shared-witness"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"weakAd"))},false);
            var b=P4Footprint("Move",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"weakB"),
                    RecordedNativeIdentity:"shared-witness"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"weakBd"))});
            var report=M6JDependencyInspector.Analyze(new[]{a,b});
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedNativeIdentity) &&
                !report.AuthorizesSelectiveUndo,
                "Unverified native journal incorrectly hid saved identity-warning evidence.");
        });
        Check("M6J_P4_OVERSIZED_DEPENDENCIES_REFUSE_BOUNDED",() =>
        {
            var many=Enumerable.Range(0,66).Select(n=>P4Footprint("Copy",new[]{
                new M6JPathClaim("Source",Path.Combine(p4Root,"stress",$"s{n}.png"),
                    RecordedNativeIdentity:"one-shared-recorded-file"),
                new M6JPathClaim("Destination",Path.Combine(p4Root,"stress",$"d{n}.png"))
            })).ToArray();
            var report=M6JDependencyInspector.Analyze(many);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                report.Edges.Count==0 && !report.GrantsSuccessorEpoch,
                "Oversized native alias graph escaped the bounded fail-closed policy.");
        });
        Check("M6J_P4_DOSSIER_EXPLAINS_SAVED_IDENTITY_LIMITS",() =>
        {
            var edge=new M6JDependencyEdge("Copy",1.ToString("x32"),
                "Move",2.ToString("x32"),M6JConflictKind.SameRecordedNativeIdentity,
                "Saved identity warning only; current identity is not established.");
            var view=StudioRecoveryCommandCenterPresentation.Create(
                RecoveryInventory(UnifiedHistoryKind.NeedsRecovery,2,0,0,0,true,
                    RecoveryRow("Copy",1,"Committed",UnifiedRowProof.NativeActiveVerified),
                    RecoveryRow("Move",2,"Undone",UnifiedRowProof.NativeTerminalVerified)),
                new M6JDependencyAssessment(M6JDependencyKind.DependencyDetected,2,
                    new[]{edge},"Saved identity collision needs investigation."));
            Ensure(view.Report.Contains("Recorded native-identity correlations: 1") &&
                view.Report.Contains("historical journal values; not proof of current identity") &&
                view.Report.Contains("SameRecordedNativeIdentity") &&
                !view.AuthorizesNativeOperation && !view.AuthorizesSelectiveUndo,
                "Dossier lost historical-identity limitations or inferred permission.");
        });


        // P4-R2: owner-host native Windows handle observations on generated-only
        // disposable files. Nothing here writes journals or grants an operation.
        (string Root,M6JP4PinnedFileExpectation A,M6JP4PinnedFileExpectation B,
            M6JP4PinnedFileExpectation C) P4R2Fixture()
        {
            string root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P4R2_DISPOSABLE_"+
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string a=Path.Combine(root,"copy-source.dat");
            string b=Path.Combine(root,"copy-committed.dat");
            string c=Path.Combine(root,"move-source.dat");
            File.WriteAllBytes(a,System.Text.Encoding.ASCII.GetBytes("copy-bytes-01"));
            File.WriteAllBytes(b,System.Text.Encoding.ASCII.GetBytes("copy-bytes-01"));
            File.WriteAllBytes(c,System.Text.Encoding.ASCII.GetBytes("move-bytes-02"));
            M6JP4PinnedFileExpectation Make(string path)
            {
                var bytes=File.ReadAllBytes(path);
                return new(path,NativeFileCapability.Identity(path),
                    Convert.ToHexString(SHA256.HashData(bytes)),bytes.LongLength);
            }
            return (root,Make(a),Make(b),Make(c));
        }
        Check("M6J_P4R2_WINDOWS_PINNED_REAL_THREE_FILE_SUCCESS",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                var result=M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,fixture.C);
                Ensure(result.IsFreshWitness && result.ObservationFingerprint?.Length==64 &&
                    !result.IsDurableProof && !result.AuthorizesNativeOperation &&
                    !result.GrantsSuccessorEpoch && !result.CanWriteMoveIntent &&
                    !result.AuthorizesSelectiveUndo,"Real native three-handle witness did not pass or minted authority.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_CHANGED_COMMITTED_COPY_BYTES_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                File.WriteAllBytes(fixture.B.Path,System.Text.Encoding.ASCII.GetBytes("copy-bytes-XX"));
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,fixture.C).IsFreshWitness,
                    "Altered committed output survived pinned digest check.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_REPLACED_MOVE_SOURCE_NATIVE_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                File.Delete(fixture.C.Path);
                File.WriteAllBytes(fixture.C.Path,System.Text.Encoding.ASCII.GetBytes("move-bytes-XX"));
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,fixture.C).IsFreshWitness,
                    "Replacement move source was accepted as native predecessor.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_WIN_HARDLINK_SAME_FILE_ID_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                File.Delete(fixture.B.Path);
                NativeFileCapability.PublishLinkNoReplace(fixture.A.Path,fixture.B.Path);
                var bytes=File.ReadAllBytes(fixture.B.Path);
                var linked=new M6JP4PinnedFileExpectation(fixture.B.Path,
                    NativeFileCapability.Identity(fixture.B.Path),
                    Convert.ToHexString(SHA256.HashData(bytes)),bytes.LongLength);
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,linked,fixture.C).IsFreshWitness,
                    "Two hardlink paths incorrectly treated as independent native files.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_FORGED_IDENTITY_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                var forged=fixture.C with {Identity=fixture.A.Identity};
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,forged).IsFreshWitness,
                    "Forged Move identity passed native pinning.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_NONCANONICAL_SOURCE_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                var alias=fixture.C with {Path=Path.Combine(fixture.Root,"sub","..","move-source.dat")};
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,alias).IsFreshWitness,
                    "Dot-segment path alias survived fresh witness preflight.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_MISSING_CURRENT_COPY_SOURCE_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                File.Delete(fixture.A.Path);
                Ensure(!M6JP4FreshPinnedPredecessor.Inspect(fixture.A,fixture.B,fixture.C).IsFreshWitness,
                    "Missing committed predecessor input passed fresh verification.");
            } finally { Directory.Delete(fixture.Root,true); }
        });
        Check("M6J_P4R2_OVER_LIMIT_FILE_EVIDENCE_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var fixture=P4R2Fixture();
            try {
                var huge=fixture.A with {Length=512L*1024*1024+1};
                var result=M6JP4FreshPinnedPredecessor.Inspect(huge,fixture.B,fixture.C);
                Ensure(!result.IsFreshWitness && !result.CanWriteMoveIntent &&
                    !result.AuthorizesSelectiveUndo,"Oversized candidate minted native review proof.");
            } finally { Directory.Delete(fixture.Root,true); }
        });

        // P4-R4: the cut tests validate durable SQL rollback, not whether a
        // read-only live evidence inspector considers deliberately damaged
        // filesystem state safe.  A foreign target or altered file can
        // correctly make the inspector return NeedsAttention after rollback.
        string P4R4PersistedReviewState(WorkspaceSessionStore db, Guid id)
        {
            using var readOnly=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath, Mode=SqliteOpenMode.ReadOnly, Pooling=false
            }.ToString());
            readOnly.Open();
            using var command=readOnly.CreateCommand();
            command.CommandText="SELECT state FROM m6j_review_proposals WHERE id=$id";
            command.Parameters.AddWithValue("$id",id.ToString("N"));
            return command.ExecuteScalar() as string ?? "Missing";
        }

        // P4-R3: the actual generated-only SQLite mutation boundary reopens
        // native handles after review consumption and Move Intent insertion.
        // This is still NOT a Move executor or a production mixed-family grant.
        Check("M6J_P4R3_PRECOMMIT_POSITIVE_RESTART_DURABLE_BUT_NO_EXECUTION",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var receipt=db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Ensure(receipt.JournalCommitted && !receipt.AuthorizesNativeFileMutation &&
                !receipt.AuthorizesCrossFamilyUndo && db.AllMoveHistoryCount()==1 &&
                db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Consumed" &&
                File.Exists(f.Intent.Source) && File.Exists(f.Prior.Destination) &&
                !File.Exists(f.Intent.Destination) && !File.Exists(f.Intent.OriginalArchive),
                "P4-R3 generated-only transaction lost durable rows or wrote filesystem bytes.");
        });
        Check("M6J_P4R3_CUT_TAMPER_COPY_SOURCE_BYTES",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.AppendAllText(f.Prior.Source,"native-cut"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: altered Copy source");
        });
        Check("M6J_P4R3_CUT_TAMPER_COPY_OUTPUT_BYTES",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.AppendAllText(f.Prior.Destination,"native-cut"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: altered Copy output");
        });
        Check("M6J_P4R3_CUT_TAMPER_MOVE_SOURCE_BYTES",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.AppendAllText(f.Intent.Source,"native-cut"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: altered Move source");
        });
        Check("M6J_P4R3_CUT_REPLACE_MOVE_SOURCE_SAME_BYTES",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { var data=File.ReadAllBytes(f.Intent.Source);File.Delete(f.Intent.Source);File.WriteAllBytes(f.Intent.Source,data); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: replaced Move source same bytes");
        });
        Check("M6J_P4R3_CUT_DELETE_COPY_SOURCE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.Delete(f.Prior.Source); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: deleted Copy source");
        });
        Check("M6J_P4R3_CUT_FOREIGN_DESTINATION",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.WriteAllText(f.Intent.Destination,"foreign-target"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                File.ReadAllText(f.Intent.Destination)=="foreign-target",
                "P4-R3 atomic native revalidation/rollback failed: foreign Move destination");
        });
        Check("M6J_P4R3_CUT_FOREIGN_TEMP",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.WriteAllText(f.Intent.Temp,"foreign-temp"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: foreign Move temp");
        });
        Check("M6J_P4R3_CUT_FOREIGN_ORIGINAL_ARCHIVE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { Directory.CreateDirectory(Path.GetDirectoryName(f.Intent.OriginalArchive)!);File.WriteAllText(f.Intent.OriginalArchive,"foreign-archive"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: foreign native original archive");
        });
        Check("M6J_P4R3_CUT_FOREIGN_OUTPUT_ARCHIVE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { Directory.CreateDirectory(Path.GetDirectoryName(f.Intent.OutputArchive)!);File.WriteAllText(f.Intent.OutputArchive,"foreign-archive"); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: foreign native output archive");
        });
        Check("M6J_P4R3_CUT_HARDLINK_COPY_OUTPUT",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.Delete(f.Prior.Destination);NativeFileCapability.PublishLinkNoReplace(f.Prior.Source,f.Prior.Destination); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: committed output hardlink alias");
        });
        Check("M6J_P4R3_CUT_CHANGE_MOVE_WRITE_TICKS",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            Refuse<IOException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=> { File.SetLastWriteTimeUtc(f.Intent.Source,File.GetLastWriteTimeUtc(f.Intent.Source).AddHours(-2)); }));
            Ensure(db.AllMoveHistoryCount()==0 &&
                P4R4PersistedReviewState(db,f.Candidate.Id)=="Reviewed" &&
                !File.Exists(f.Intent.Destination),
                "P4-R3 atomic native revalidation/rollback failed: Move timestamp drift");
        });
        Check("M6J_P4R3_CUT_EXCEPTION_PRESERVES_RESTART_REVIEW",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<InvalidOperationException>(()=>db.RecordM6JGeneratedOnlyCopyMoveIntent(
                f.Candidate.Id,f.Intent,()=>throw new InvalidOperationException("generated test cut")));
            db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.AllMoveHistoryCount()==0 &&
                cold.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "P4-R3 injected transaction failure committed a partial journal after restart.");
        });

        // P4-R5: regenerated read-only lineage inspection AFTER atomic
        // Intent commitment and cold restart. No Move filesystem writes.
        Check("M6J_P4R5_EXACT_CONSUMED_INTENT_LINEAGE_MATCHES_NOW",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var report=db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id);
            Ensure(report.Verified && report.ObservationFingerprint?.Length==64 &&
                !report.AuthorizesFileOperation && !report.AuthorizesMoveExecution &&
                !report.AuthorizesSelectiveUndo && !report.CanRepairJournal &&
                db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "P4-R5 read-only exact lineage did not verify fresh native evidence.");
        });
        Check("M6J_P4R5_COLD_RESTART_LINEAGE_RECONSTRUCTS",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var before=db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id);
            string folder=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(folder);
            var after=cold.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id);
            Ensure(before.Verified && after.Verified &&
                before.ObservationFingerprint==after.ObservationFingerprint &&
                cold.AllCopyHistoryCount()==1 && cold.AllMoveHistoryCount()==1 &&
                !after.AuthorizesMoveExecution,
                "P4-R5 cold restart could not reconstruct same predecessor linkage.");
        });
        Check("M6J_P4R5_ALTERED_PREDECESSOR_ROW_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            using(var other=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                other.Open();using var cmd=other.CreateCommand();
                cmd.CommandText="UPDATE copy_items SET json=json || ' '";
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R5 could not alter test predecessor row.");
            }
            Ensure(!db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id).Verified,
                "P4-R5 accepted Copy predecessor whose raw journal changed.");
        });
        Check("M6J_P4R5_CORRUPT_CONSUMED_REVIEW_SEAL_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            using(var other=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                other.Open();using var cmd=other.CreateCommand();
                cmd.CommandText="UPDATE m6j_review_proposals SET review_digest='FORGED' WHERE id=$id";
                cmd.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R5 could not alter test seal.");
            }
            Ensure(!db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id).Verified,
                "P4-R5 accepted forged consumed review seal.");
        });
        Check("M6J_P4R5_UNEXPECTED_MOVE_JSON_BYTES_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            using(var other=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString())) {
                other.Open();using var cmd=other.CreateCommand();
                cmd.CommandText="UPDATE move_items SET json=json || ' '";
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R5 could not alter Move row bytes.");
            }
            Ensure(!db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id).Verified,
                "P4-R5 accepted noncanonical Move JSON journal bytes.");
        });
        Check("M6J_P4R5_CHANGED_COMMITTED_COPY_OUTPUT_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            File.AppendAllText(f.Prior.Destination,"P4-R5 external output mutation");
            Ensure(!db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id).Verified,
                "P4-R5 accepted altered committed Copy output after Intent.");
        });
        Check("M6J_P4R5_CHANGED_MOVE_SOURCE_IDENTITY_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var bytes=File.ReadAllBytes(f.Intent.Source);
            File.Move(f.Intent.Source,f.Intent.Source+".old");
            File.WriteAllBytes(f.Intent.Source,bytes);
            Ensure(!db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id).Verified,
                "P4-R5 accepted same bytes at replacement Move file identity.");
        });
        Check("M6J_P4R5_FOREIGN_DESTINATION_PRESERVED_AND_REFUSED",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            File.WriteAllText(f.Intent.Destination,"foreign-owner-must-survive");
            var report=db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id);
            Ensure(!report.Verified && !report.AuthorizesFileOperation &&
                File.ReadAllText(f.Intent.Destination)=="foreign-owner-must-survive" &&
                db.AllMoveHistoryCount()==1,
                "P4-R5 rewrote external occupied destination or granted authority.");
        });
        Check("M6J_P4R5_UNCONSUMED_PROPOSAL_NEVER_A_CHAIN",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var report=db.InspectM6JP4R5GeneratedConsumedLineage(f.Intent.Id);
            Ensure(!report.Verified && !report.AuthorizesSelectiveUndo &&
                db.AllMoveHistoryCount()==0 &&
                db.InspectM6JProspectiveReview(f.Candidate.Id).State=="Reviewed",
                "P4-R5 fabricated committed lineage from an unconsumed review.");
        });
        // P4-R6: execute one EXACT already-consumed native Intent through
        // the existing generated-only no-replace Move machinery. No generic
        // history waiver, no personal-photo authority and no automatic replay
        // of an interrupted partially executed filesystem state.
        Check("M6J_P4R6_PRECOMMITTED_INTENT_EXECUTES_ONE_REAL_GENERATED_MOVE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var moved=new DurableMoveCoordinator(db)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(moved.State=="Committed" && db.MoveItems(f.Intent.SessionId).Single()==moved &&
                !File.Exists(moved.Source) && File.Exists(moved.Destination) &&
                File.Exists(moved.OriginalArchive) && !File.Exists(moved.Temp) &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                db.CopyItems(f.Prior.SessionId).Single()==f.Prior,
                "P4-R6 previously committed Intent could not complete without disturbing Copy.");
        });
        Check("M6J_P4R6_COLD_RESTART_INTENT_EXECUTES_ONE_NATIVE_MOVE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            var moved=new DurableMoveCoordinator(cold)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(moved.State=="Committed" && cold.AllMoveHistoryCount()==1 &&
                File.Exists(moved.Destination) && File.Exists(moved.OriginalArchive) &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed",
                "Cold-start generated Intent did not execute exact native Move.");
        });
        Check("M6J_P4R6_COLD_RESTART_SELECTIVE_UNDO_PRESERVES_COPY",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var mover=new DurableMoveCoordinator(cold);
            var moved=mover.ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var undone=mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult();
            Ensure(undone.State=="Undone" && File.Exists(undone.Source) &&
                !File.Exists(undone.Destination) && File.Exists(undone.OutputArchive) &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed",
                "Cold resumed selective Move Undo damaged committed Copy predecessor.");
        });
        Check("M6J_P4R6_REPLAY_REJECTS_DUPLICATE_MOVE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Refuse<IOException>(()=>mover.ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==1 && File.Exists(moved.Destination),
                "Cold precommitted Move replay inserted duplicate or damaged installed output.");
        });
        Check("M6J_P4R6_FORGED_TICKET_REFUSES_WITHOUT_FS_CHANGES",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent with {SourceHash=new string('F',64)})
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "Mismatched ticket changed generated filesystem or consumed a second review.");
        });
        Check("M6J_P4R6_FOREIGN_DESTINATION_PRESERVED_BEFORE_EXECUTION",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            File.WriteAllText(f.Intent.Destination,"other-user-owner");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.ReadAllText(f.Intent.Destination)=="other-user-owner" &&
                File.Exists(f.Intent.Source),"Foreign destination overwritten or Intent advanced.");
        });
        Check("M6J_P4R6_TAMPERED_COPY_OUTPUT_REFUSES_EXECUTION",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            File.AppendAllText(f.Prior.Destination,"tamper-copy-output");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "Changed Copy predecessor still authorized a generated Move.");
        });
        Check("M6J_P4R6_REPLACED_SOURCE_IDENTITY_REFUSES_EXECUTION",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var original=File.ReadAllBytes(f.Intent.Source);
            File.Move(f.Intent.Source,f.Intent.Source+".prior");File.WriteAllBytes(f.Intent.Source,original);
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "Same-byte source replacement escaped native identity validation.");
        });
        Check("M6J_P4R6_GENERIC_EXECUTOR_CANNOT_USE_PRECOMMITTED_INTENT",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .ExecuteApprovedAsync(f.Work,P2BPrepared(f.Intent),reviewedOperationId:f.Intent.Id)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "Generic Move executor bypassed mixed-history global veto.");
        });
        Check("M6J_P4R6_CUT_AFTER_INTENT_RECORDED_REMAINS_RESTARTABLE",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var cut=new DurableMoveCoordinator(db,stage=>{
                if(stage=="MOVE_INTENT_RECORDED")throw new SimulatedInterruptionException(stage);
            });
            Refuse<SimulatedInterruptionException>(()=>cut
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(db.MoveItems(f.Intent.SessionId).Single().State=="Intent" &&
                File.Exists(f.Intent.Source) && !File.Exists(f.Intent.Destination),
                "Pre-filesystem interruption advanced the cold Intent.");
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var moved=new DurableMoveCoordinator(cold)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(moved.State=="Committed", "Cold Intent could not safely resume after zero-write interruption.");
        });
        Check("M6J_P4R6_CUT_DURING_FILE_MOVE_NEVER_AUTOREPLAYS",() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            db.RecordM6JGeneratedOnlyCopyMoveIntent(f.Candidate.Id,f.Intent);
            var cut=new DurableMoveCoordinator(db,stage=>{
                if(stage=="MOVE_TEMP_RECORDED")throw new SimulatedInterruptionException(stage);
            });
            Refuse<SimulatedInterruptionException>(()=>cut
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            Refuse<IOException>(()=>new DurableMoveCoordinator(cold)
                .ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(f.Work,
                    P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult());
            Ensure(cold.MoveItems(f.Intent.SessionId).Single().State=="TempReady" &&
                File.Exists(f.Intent.Source) && File.Exists(f.Intent.Temp) &&
                !File.Exists(f.Intent.Destination),
                "Partially executed Move was falsely replayed or erased after restart.");
        });
        // P4-R7: four simultaneous Windows handles recheck the owned Copy
        // predecessor and both committed Move artifacts before selective Undo
        // and again AFTER UndoRestoreIntent but BEFORE the first relocation.
        // All cases use generated disposable fixtures; no personal-photo grant.
        Check("M6J_P4R7_FOUR_PINNED_UNDO_POSITIVE",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var witness=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                new(f.Prior.Source,f.Prior.SourceIdentity,f.Prior.SourceHash,f.Prior.SourceLength,f.Prior.SourceWriteTicks),
                new(f.Prior.Destination,f.Prior.TargetIdentity!,f.Prior.SourceHash,f.Prior.SourceLength),
                new(moved.OriginalArchive,moved.SourceIdentity,moved.SourceHash,moved.SourceLength,moved.SourceWriteTicks),
                new(moved.Destination,moved.TargetIdentity!,moved.SourceHash,moved.SourceLength));
            Ensure(witness.IsFreshWitness && !witness.AuthorizesNativeOperation &&
                !witness.AuthorizesSelectiveUndo && !witness.IsDurableProof &&
                witness.ObservationFingerprint?.Length==64,"Four-file exact native Undo evidence was not non-authorizing.");
            var undone=mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult();
            Ensure(undone.State=="Undone" && File.Exists(undone.Source) &&
                File.Exists(undone.OutputArchive) && File.Exists(f.Prior.Destination) &&
                db.CopyItems(f.Prior.SessionId).Single().State=="Committed",
                "Pinned Undo harmed generated committed Copy ownership.");
        });
        Check("M6J_P4R7_COLD_PINNED_UNDO_COMPLETES",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string state=Path.GetDirectoryName(db.DatabasePath)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(state);
            var result=new DurableMoveCoordinator(cold)
                .UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult();
            Ensure(result.State=="Undone" && File.Exists(result.Source) &&
                cold.CopyItems(f.Prior.SessionId).Single().State=="Committed" &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity,
                "P4-R7 cold pinned selective Undo failed or modified Copy.");
        });
        Check("M6J_P4R7_REPLACED_COPY_SOURCE_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            byte[] bytes=File.ReadAllBytes(f.Prior.Source);
            File.Move(f.Prior.Source,f.Prior.Source+".old");File.WriteAllBytes(f.Prior.Source,bytes);
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.Destination) && File.Exists(moved.OriginalArchive),
                "Copy source identity replacement advanced Move Undo.");
        });
        Check("M6J_P4R7_COPY_OUTPUT_CONTENT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            File.AppendAllText(f.Prior.Destination,"changed-copy-output");
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.OriginalArchive) && File.Exists(moved.Destination),
                "Changed Copy output permitted selective Undo.");
        });
        Check("M6J_P4R7_MOVE_ARCHIVE_HARDLINK_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string alias=moved.OriginalArchive+".otherlink";
            NativeFileCapability.PublishLinkNoReplace(moved.OriginalArchive,alias);
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(alias) && File.Exists(moved.OriginalArchive),
                "Hardlinked original archive led to premature selective Undo.");
        });
        Check("M6J_P4R7_MOVE_DESTINATION_ID_REPLACED_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            byte[] bytes=File.ReadAllBytes(moved.Destination);
            File.Move(moved.Destination,moved.Destination+".foreign-preserved");
            File.WriteAllBytes(moved.Destination,bytes);
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.Destination+".foreign-preserved") &&
                File.Exists(moved.OriginalArchive),
                "Move output same-byte replacement led to selective Undo.");
        });
        Check("M6J_P4R7_POST_INTENT_COPY_TAMPER_FENCES_FIRST_RELOCATION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var interrupted=new DurableMoveCoordinator(db,phase=>{
                if(phase=="MOVE_UNDO_RESTORE_INTENT")
                    File.AppendAllText(f.Prior.Destination,"after-durable-undo-intent");
            });
            Refuse<IOException>(()=>interrupted.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                !File.Exists(moved.Source) && File.Exists(moved.OriginalArchive) &&
                File.Exists(moved.Destination) && File.Exists(f.Prior.Destination),
                "Post-UndoIntent Copy tamper did not halt before first relocation.");
        });
        Check("M6J_P4R7_POST_INTENT_FOREIGN_SOURCE_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var interrupted=new DurableMoveCoordinator(db,phase=>{
                if(phase=="MOVE_UNDO_RESTORE_INTENT")
                    File.WriteAllText(moved.Source,"foreign-user-source");
            });
            Refuse<IOException>(()=>interrupted.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.ReadAllText(moved.Source)=="foreign-user-source" &&
                File.Exists(moved.OriginalArchive) && File.Exists(moved.Destination),
                "Post-Intent foreign source was overwritten or original archive relocated.");
        });
        Check("M6J_P4R7_GENERIC_UNDO_STILL_BLOCKED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(f.Work,
                P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Refuse<IOException>(()=>mover.UndoAsync(moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(moved.Destination),"Generic Undo bypassed historical mixed-family veto.");
        });
        // P4-R8: P4-R7 protects the FIRST Undo relocation. These tests
        // defend the SECOND relocation (Move installed output -> owned archive)
        // by checking post-restore four-handle evidence on BOTH sides of the
        // durable UndoOutputArchiveIntent transition. Generated fixtures only.
        Check("M6J_P4R8_POST_RESTORE_POSITIVE_PRESERVES_COPY",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            int sourceRestored=0,outputIntent=0;
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_SOURCE_RESTORED_RECORDED") sourceRestored++;
                if(cut=="MOVE_UNDO_OUTPUT_ARCHIVE_INTENT") outputIntent++;
            });
            var undone=mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult();
            Ensure(sourceRestored==1 && outputIntent==1 && undone.State=="Undone" &&
                File.Exists(undone.Source) && !File.Exists(undone.Destination) &&
                File.Exists(undone.OutputArchive) && File.Exists(f.Prior.Source) &&
                NativeFileCapability.Identity(f.Prior.Destination)==f.Prior.TargetIdentity &&
                db.CopyItems(moved.SessionId).Single().State=="Committed",
                "P4-R8 second-stage pinned Undo failed or damaged Copy predecessor.");
        });
        Check("M6J_P4R8_COPY_SOURCE_TAMPER_AFTER_RESTORE_FENCES_OUTPUT",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_SOURCE_RESTORED_RECORDED")
                    File.AppendAllText(f.Prior.Source,"copy-source-tampered-after-restore");
            });
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(moved.Source) && File.Exists(moved.Destination) &&
                !File.Exists(moved.OutputArchive) && !File.Exists(moved.OriginalArchive),
                "Changed Copy source let second Undo relocation proceed.");
        });
        Check("M6J_P4R8_COPY_OUTPUT_TAMPER_AFTER_SECOND_INTENT_FENCES_OUTPUT",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_OUTPUT_ARCHIVE_INTENT")
                    File.AppendAllText(f.Prior.Destination,"copy-output-after-second-intent");
            });
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(moved.Source) && File.Exists(moved.Destination) &&
                !File.Exists(moved.OutputArchive),
                "Copy output modification after second durable Intent escaped fence.");
        });
        Check("M6J_P4R8_REPLACED_RESTORED_SOURCE_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string kept=moved.Source+".kept";
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_SOURCE_RESTORED_RECORDED")
                {
                    var bytes=File.ReadAllBytes(moved.Source);
                    File.Move(moved.Source,kept);
                    File.WriteAllBytes(moved.Source,bytes);
                }
            });
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(kept) && File.Exists(moved.Source) &&
                File.Exists(moved.Destination) && !File.Exists(moved.OutputArchive),
                "Same-byte replacement of restored source escaped P4-R8 identity fence.");
        });
        Check("M6J_P4R8_INSTALLED_OUTPUT_TAMPER_AFTER_SECOND_INTENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_OUTPUT_ARCHIVE_INTENT")
                    File.AppendAllText(moved.Destination,"changed-installed-after-intent");
            });
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(moved.Source) && File.Exists(moved.Destination) &&
                !File.Exists(moved.OutputArchive),
                "Modified installed output still archived after second Intent.");
        });
        Check("M6J_P4R8_FOREIGN_OUTPUT_ARCHIVE_PRESERVED_AFTER_RESTORE",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var mover=new DurableMoveCoordinator(db,cut=>{
                if(cut=="MOVE_UNDO_SOURCE_RESTORED_RECORDED")
                    File.WriteAllText(moved.OutputArchive,"foreign-owner-output-archive");
            });
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(moved.Source) && File.Exists(moved.Destination) &&
                File.ReadAllText(moved.OutputArchive)=="foreign-owner-output-archive",
                "Foreign output archive was deleted or overwritten during selective Undo.");
        });
        // P4-R9: first native THIRD family. Generated-only Copy A, native Move B,
        // then a genuinely relocated/SQLite-journalled Rename C of Move's
        // installed output. Move Undo MUST remain refused while C is active.
        Check("M6J_P4R9_THIRD_FAMILY_ACTUAL_RENAME_COMMITTED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "third-family"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Ensure(renamed.State=="Committed" &&
                db.CopyItems(moved.SessionId).Single()==f.Prior &&
                db.MoveItems(moved.SessionId).Single()==moved &&
                db.RenameItems(moved.SessionId).Single()==renamed &&
                File.Exists(renamed.Target) && !File.Exists(renamed.Source) &&
                File.Exists(moved.OriginalArchive) &&
                DurableRenameCoordinator.ProveState(renamed),
                "P4-R9 generated Copy Move Rename did not preserve exact native lineage.");
        });
        Check("M6J_P4R9_COLD_RESTART_RENAME_THIRD_FAMILY",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            var plan=M6JP4R9GeneratedMoveRename.Prepare(cold,moved.SessionId,
                "cold-third-family"+Path.GetExtension(moved.Destination));
            var committed=M6JP4R9GeneratedMoveRename.Apply(cold,plan);
            Ensure(committed.State=="Committed" &&
                cold.AllCopyHistoryCount()==1 && cold.AllMoveHistoryCount()==1 &&
                cold.AllRenameHistoryCount()==1 && File.Exists(committed.Target) &&
                DurableRenameCoordinator.ProveState(committed),
                "P4-R9 cold Move to Rename continuity failed.");
        });
        Check("M6J_P4R9_FOREIGN_RENAME_TARGET_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            string name="foreign-third-family"+Path.GetExtension(moved.Destination);
            string target=Path.Combine(Path.GetDirectoryName(moved.Destination)!,name);
            File.WriteAllText(target,"foreign-owned-file");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,name));
            Ensure(db.AllRenameHistoryCount()==0 && File.ReadAllText(target)=="foreign-owned-file" &&
                File.Exists(moved.Destination),"P4-R9 foreign target overwritten or unauthorized journal created.");
        });
        Check("M6J_P4R9_COPY_PREDECESSOR_CHANGED_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            File.AppendAllText(f.Prior.Destination,"not-original-copy-contents");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "not-owned"+Path.GetExtension(moved.Destination)));
            Ensure(db.AllRenameHistoryCount()==0 && File.Exists(moved.Destination),
                "P4-R9 altered Copy predecessor gained Rename authority.");
        });
        Check("M6J_P4R9_REPLACED_MOVE_OUTPUT_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            byte[] original=File.ReadAllBytes(moved.Destination);
            File.Move(moved.Destination,moved.Destination+".original-owned");
            File.WriteAllBytes(moved.Destination,original);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "replaced-third-family"+Path.GetExtension(moved.Destination)));
            Ensure(db.AllRenameHistoryCount()==0 && File.Exists(moved.Destination+".original-owned"),
                "P4-R9 same-bytes replacement of installed Move output accepted.");
        });
        Check("M6J_P4R9_AFTER_DURABLE_INTENT_CUT_IS_NOT_REPLAYABLE",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "cut-after-intent"+Path.GetExtension(moved.Destination));
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Apply(db,plan,stage=>{
                if(stage=="P4R9_RENAME_INTENT_DURABLE")
                    throw new IOException("synthetic-cut-after-intent");
            }));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="Intent" &&
                File.Exists(moved.Destination) && !File.Exists(plan.Target),
                "P4-R9 Intent cut unexpectedly relocated a file.");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Apply(db,plan));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="Intent",
                "P4-R9 interrupted Intent was improperly replayed.");
        });
        Check("M6J_P4R9_AFTER_RELOCATION_CUT_NEEDS_ATTENTION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "cut-after-relocate"+Path.GetExtension(moved.Destination));
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.Apply(db,plan,stage=>{
                if(stage=="P4R9_RENAME_RELOCATED_UNJOURNALLED")
                    throw new IOException("synthetic-cut-after-relocate");
            }));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="NeedsAttention" &&
                !File.Exists(moved.Destination) && File.Exists(plan.Target) &&
                NativeFileCapability.Identity(plan.Target)==plan.NativeIdentity &&
                File.Exists(moved.OriginalArchive),
                "P4-R9 post-mutation interruption was incorrectly finalized or overwritten.");
        });
        Check("M6J_P4R9_MOVE_UNDO_REFUSES_ACTIVE_RENAME_DEPENDENCY",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var mover=new DurableMoveCoordinator(db);
            var moved=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "dependent"+Path.GetExtension(moved.Destination));
            var third=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>mover.UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId)
                .GetAwaiter().GetResult());
            Ensure(db.MoveItems(moved.SessionId).Single().State=="Committed" &&
                db.RenameItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(third.Target) && File.Exists(moved.OriginalArchive),
                "P4-R9 active Rename did not block predecessor selective Undo.");
        });
        Check("M6J_P4R9_GENERIC_RENAME_UNDO_REFUSES_MIXED_CHAIN",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "not-generic"+Path.GetExtension(moved.Destination));
            var third=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>new DurableRenameCoordinator(db).Undo(moved.SessionId));
            Ensure(File.Exists(third.Target) && !File.Exists(third.Source) &&
                db.RenameItems(moved.SessionId).Single().State=="Committed",
                "P4-R9 generic Rename Undo improperly bypassed predecessor constraints.");
        });
        // P4-R10: exact ONE generated Copy A -> committed Move B -> committed
        // Rename C, selective native Undo of C only; Move and Copy preserved.
        // No ordinary/multi-Rename/general mixed-history authority is granted.
        Check("M6J_P4R10_THIRD_FAMILY_SELECTIVE_RENAME_UNDO_PRESERVES_PREDECESSORS",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "undo-third-family"+Path.GetExtension(moved.Destination));
            var committed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            var undone=M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId);
            Ensure(undone.State=="Undone" && committed.Id==undone.Id &&
                DurableRenameCoordinator.ProveState(undone) &&
                db.CopyItems(moved.SessionId).Single()==f.Prior &&
                db.MoveItems(moved.SessionId).Single()==moved &&
                File.Exists(undone.Source) && !File.Exists(undone.Target) &&
                File.Exists(moved.OriginalArchive),
                "P4-R10 selective Rename Undo failed exact Copy/Move preservation.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .UndoM6JReviewedGeneratedCopyMoveAsync(moved.SessionId).GetAwaiter().GetResult());
        });
        Check("M6J_P4R10_COLD_RESTART_SELECTIVE_RENAME_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "cold-undo-third-family"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            var state=Path.GetDirectoryName(db.DatabasePath)!;
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            var undone=M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(cold,moved.SessionId);
            Ensure(undone.State=="Undone" && undone.Id==renamed.Id &&
                cold.CopyItems(moved.SessionId).Single()==f.Prior &&
                cold.MoveItems(moved.SessionId).Single()==moved &&
                File.Exists(moved.Destination) && !File.Exists(renamed.Target),
                "P4-R10 cold-restart terminal Rename Undo failed.");
        });
        Check("M6J_P4R10_ALTERED_COPY_PREDECESSOR_BLOCKS_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "copy-fence"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            File.AppendAllText(f.Prior.Destination,"changed-copy");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId));
            Ensure(db.RenameItems(moved.SessionId).Single()==renamed &&
                File.Exists(renamed.Target) && !File.Exists(renamed.Source),
                "P4-R10 modified Copy predecessor granted undo.");
        });
        Check("M6J_P4R10_SAME_BYTES_REPLACED_RENAME_NATIVE_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "replace-fence"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            var bytes=File.ReadAllBytes(renamed.Target);
            File.Move(renamed.Target,renamed.Target+".old-owned");
            File.WriteAllBytes(renamed.Target,bytes);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="Committed" &&
                File.Exists(renamed.Target+".old-owned") && File.Exists(renamed.Target),
                "P4-R10 accepted substituted native identity.");
        });
        Check("M6J_P4R10_FOREIGN_RESTORATION_SOURCE_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "foreign-undo-source"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            File.WriteAllText(renamed.Source,"foreign-file-owns-original-name");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId));
            Ensure(db.RenameItems(moved.SessionId).Single()==renamed &&
                File.ReadAllText(renamed.Source)=="foreign-file-owns-original-name" &&
                File.Exists(renamed.Target),"P4-R10 foreign source overwritten by selective Undo.");
        });
        Check("M6J_P4R10_CUT_AFTER_UNDO_INTENT_NOT_AUTOREPLAYED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "undo-intent-cut"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId,
                stage=>{if(stage=="P4R10_RENAME_UNDO_INTENT_DURABLE")
                    throw new IOException("synthetic-cut-after-undo-intent");}));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="UndoIntent" &&
                File.Exists(renamed.Target) && !File.Exists(renamed.Source),
                "P4-R10 intent cut advanced native filesystem unexpectedly.");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId));
        });
        Check("M6J_P4R10_POST_INTENT_COPY_MUTATION_FENCES_RELOCATION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "copy-post-intent"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId,
                stage=>{if(stage=="P4R10_RENAME_UNDO_INTENT_DURABLE")
                    File.AppendAllText(f.Prior.Destination,"tamper-after-intent");}));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(renamed.Target) && !File.Exists(renamed.Source),
                "P4-R10 intervening Copy mutation did not fence the relocation.");
        });
        Check("M6J_P4R10_POST_INTENT_FOREIGN_SOURCE_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "foreign-post-intent"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId,
                stage=>{if(stage=="P4R10_RENAME_UNDO_INTENT_DURABLE")
                    File.WriteAllText(renamed.Source,"foreign-added-after-intent");}));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(renamed.Target) &&
                File.ReadAllText(renamed.Source)=="foreign-added-after-intent",
                "P4-R10 post-intent foreign original name overwritten.");
        });
        Check("M6J_P4R10_CUT_AFTER_UNDO_RELOCATION_NEEDS_ATTENTION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "after-undo-rename-cut"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId,
                stage=>{if(stage=="P4R10_RENAME_UNDO_RELOCATED_UNJOURNALLED")
                    throw new IOException("synthetic-cut-after-native-undo");}));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(renamed.Source) && !File.Exists(renamed.Target) &&
                NativeFileCapability.Identity(renamed.Source)==renamed.NativeIdentity,
                "P4-R10 interrupted post-relocation Rename Undo improperly finalized.");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId));
        });
        Check("M6J_P4R10_POST_INTENT_RENAMED_CONTENT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var plan=M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                "rename-tamper-after-intent"+Path.GetExtension(moved.Destination));
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,plan);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,moved.SessionId,
                stage=>{if(stage=="P4R10_RENAME_UNDO_INTENT_DURABLE")
                    File.AppendAllText(renamed.Target,"tampered-content-post-intent");}));
            Ensure(db.RenameItems(moved.SessionId).Single().State=="NeedsAttention" &&
                File.Exists(renamed.Target) && !File.Exists(renamed.Source),
                "P4-R10 post-intent renamed file corruption gained Undo permission.");
        });
        // P4-R12: actual fourth-family Reject is restricted to one disposable
        // generated Copy A -> Move B -> Rename C native predecessor chain.
        // The Reject is preserved as a separately journalled same-directory
        // owned file relocation; no permanent delete, no ordinary authority.
        Check("M6J_P4R12_FOURTH_FAMILY_NATIVE_REJECT_COMMITTED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"reject-fourth"+Path.GetExtension(moved.Destination)));
            var reject=M6JP4R12GeneratedRenameReject.Apply(db,
                M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            Ensure(reject.State=="Committed" && !File.Exists(renamed.Target) &&
                File.Exists(reject.Archive) &&
                NativeFileCapability.Identity(reject.Archive)==renamed.NativeIdentity &&
                db.CopyItems(moved.SessionId).Count==1 &&
                db.MoveItems(moved.SessionId).Single()==moved &&
                db.RenameItems(moved.SessionId).Single()==renamed &&
                db.RejectItems().Single()==reject,
                "P4-R12 generated fourth Reject did not preserve exact three predecessors.");
        });
        Check("M6J_P4R12_COLD_RESTART_FOURTH_REJECT_COMMITTED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"cold-fourth"+Path.GetExtension(moved.Destination)));
            // P4-R14: WorkspaceSessionStore accepts a DIRECTORY, not a
            // database filename. Preserve and prove the existing SQLite path
            // before closing its FileShare.None owner; then cold-open that
            // exact state directory without creating any second database.
            string originalDatabaseFile=db.DatabasePath;
            string stateDirectory=Path.GetDirectoryName(originalDatabaseFile)
                ?? throw new IOException("P4-R14 exact cold state directory missing.");
            Ensure(Path.GetFileName(originalDatabaseFile)=="workspaces-v1.sqlite" &&
                Directory.Exists(stateDirectory) && File.Exists(originalDatabaseFile),
                "P4-R14 cold preflight did not find the original persisted SQLite file.");
            db.Dispose();
            using var cold=new WorkspaceSessionStore(stateDirectory);
            Ensure(string.Equals(cold.DatabasePath,originalDatabaseFile,StringComparison.Ordinal) &&
                cold.CopyItems(moved.SessionId).Count==1 &&
                cold.MoveItems(moved.SessionId).Count==1 &&
                cold.RenameItems(moved.SessionId).Count==1 &&
                cold.RejectItems().Count==0,
                "P4-R14 cold owner reopened a different database or lost predecessor history.");
            var row=M6JP4R12GeneratedRenameReject.Apply(cold,
                M6JP4R12GeneratedRenameReject.Prepare(cold,moved.SessionId));
            Ensure(row.State=="Committed" && cold.RejectItems().Single()==row &&
                !File.Exists(row.Source) && File.Exists(row.Archive),
                "P4-R12 cold restart failed to commit one owned fourth Reject.");
        });
        Check("M6J_P4R12_FOREIGN_REJECT_ARCHIVE_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"foreign-fourth"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            File.WriteAllText(plan.Archive,"foreign fourth-family archive");
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan));
            Ensure(db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target) &&
                File.ReadAllText(plan.Archive)=="foreign fourth-family archive",
                "P4-R12 foreign Reject recovery location was overwritten.");
        });
        Check("M6J_P4R12_TAMPERED_COPY_PREDECESSOR_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"copy-guard-fourth"+Path.GetExtension(moved.Destination)));
            File.AppendAllText(f.Prior.Destination,"tampered-copy-before-reject");
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            Ensure(db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target),
                "P4-R12 changed Copy predecessor permitted fourth-family Reject.");
        });
        Check("M6J_P4R12_REPLACED_RENAME_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"identity-fourth"+Path.GetExtension(moved.Destination)));
            var bytes=File.ReadAllBytes(renamed.Target);
            File.Move(renamed.Target,renamed.Target+".original-owned");
            File.WriteAllBytes(renamed.Target,bytes);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            Ensure(db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target+".original-owned"),
                "P4-R12 replaced Rename native identity permitted Reject.");
        });
        Check("M6J_P4R12_CUT_AFTER_DURABLE_INTENT_NOT_REPLAYED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"intent-fourth"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan,stage=>{
                if(stage=="P4R12_REJECT_INTENT_DURABLE")throw new IOException("synthetic fourth Intent cut");}));
            Ensure(db.RejectItems().Single().State=="Intent" &&
                File.Exists(renamed.Target) && !File.Exists(plan.Archive),
                "P4-R12 durable Intent cut advanced a native relocation.");
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan));
        });
        Check("M6J_P4R12_POST_INTENT_COPY_TAMPER_FENCES_RELOCATION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"post-intent-fourth"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan,stage=>{
                if(stage=="P4R12_REJECT_INTENT_DURABLE")
                    File.AppendAllText(f.Prior.Destination,"tamper-after-reject-intent");}));
            Ensure(db.RejectItems().Single().State=="NeedsAttention" &&
                File.Exists(renamed.Target) && !File.Exists(plan.Archive),
                "P4-R12 post-Intent Copy change allowed native Reject relocation.");
        });
        Check("M6J_P4R12_POST_INTENT_FOREIGN_ARCHIVE_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"post-foreign-fourth"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan,stage=>{
                if(stage=="P4R12_REJECT_RELOCATE_INTENT_DURABLE")
                    File.WriteAllText(plan.Archive,"post-intent foreign archive");}));
            Ensure(db.RejectItems().Single().State=="NeedsAttention" &&
                File.Exists(renamed.Target) &&
                File.ReadAllText(plan.Archive)=="post-intent foreign archive",
                "P4-R12 post-Intent foreign recovery file was overwritten.");
        });
        Check("M6J_P4R12_CUT_AFTER_RELOCATION_NEEDS_ATTENTION",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,"cut-relocated-fourth"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan,stage=>{
                if(stage=="P4R12_REJECT_RELOCATED_UNJOURNALLED")
                    throw new IOException("synthetic fourth post-relocation cut");}));
            Ensure(db.RejectItems().Single().State=="NeedsAttention" &&
                !File.Exists(renamed.Target) && File.Exists(plan.Archive) &&
                NativeFileCapability.Identity(plan.Archive)==renamed.NativeIdentity,
                "P4-R12 cut after relocation improperly finalized fourth-family Reject.");
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan));
        });
        // P4-R13: prove that the two-key guard was NOT weakened to repair R12.
        Check("M6J_P4R13_UNMARKED_FOURTH_FAMILY_REFUSES",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root,authorize:false);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                    "unmarked-fourth"+Path.GetExtension(moved.Destination)));
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            Ensure(db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target) &&
                !File.Exists(Path.Combine(f.Root,"P5C_GUI_TEST_ONLY.marker")),
                "P4-R13 unmarked fixture obtained generated fourth-family authority.");
        });
        Check("M6J_P4R13_WRONG_ENV_FOURTH_FAMILY_REFUSES",() =>
        {
            if(!OperatingSystem.IsWindows()) return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                    "wrong-env-fourth"+Path.GetExtension(moved.Destination)));
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            Ensure(db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target),
                "P4-R13 mismatched fixture environment obtained native Reject authority.");
        });
        // P4-R15: separate read-only admission probe BEFORE any generated
        // fourth-family selective Undo design. It does not write a journal,
        // move a file, or grant native authority. The deliberate fixture
        // tampering in negative cases touches generated-only temp roots.
        (DurableMoveItem Moved,DurableRenameItem Renamed,DurableRejectItem Rejected)
            P4R15CommitFour(WorkspaceSessionStore db,SortingWorkspace work,
                M6JProspectiveCandidate candidate,DurableMoveItem intent,string suffix)
        {
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                work,P2BPrepared(intent),intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                    suffix+Path.GetExtension(moved.Destination)));
            var rejected=M6JP4R12GeneratedRenameReject.Apply(db,
                M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId));
            return (moved,renamed,rejected);
        }
        Check("M6J_P4R15_FOUR_FAMILY_READONLY_FRESH_NATIVE_WITNESS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-fresh");
            var result=db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId);
            Ensure(result.IsFreshWitness && result.RejectId==chain.Rejected.Id &&
                result.Fingerprint?.Length==64 && !result.IsDurableProof &&
                !result.AuthorizesNativeOperation && !result.AuthorizesSelectiveUndo &&
                !result.CanReplayReject && !result.CanModifyJournal &&
                db.CopyItems(chain.Moved.SessionId).Count==1 &&
                db.MoveItems(chain.Moved.SessionId).Single()==chain.Moved &&
                db.RenameItems(chain.Moved.SessionId).Single()==chain.Renamed &&
                db.RejectItems().Single()==chain.Rejected &&
                File.Exists(chain.Rejected.Archive) && !File.Exists(chain.Rejected.Source),
                "P4-R15 native four-family witness granted authority or altered journals.");
        });
        Check("M6J_P4R15_FOUR_FAMILY_COLD_SQLITE_SAME_DATABASE_WITNESS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-cold");
            string savedPath=db.DatabasePath;
            string savedDir=Path.GetDirectoryName(savedPath)!;
            db.Dispose();
            using var cold=new WorkspaceSessionStore(savedDir);
            var result=cold.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId);
            Ensure(cold.DatabasePath==savedPath && result.IsFreshWitness &&
                result.RejectId==chain.Rejected.Id &&
                cold.RejectItems().Single()==chain.Rejected &&
                cold.RenameItems(chain.Moved.SessionId).Single()==chain.Renamed &&
                !File.Exists(chain.Rejected.Source) && File.Exists(chain.Rejected.Archive),
                "P4-R15 cold read-only witness did not reopen identical persisted SQLite.");
        });
        Check("M6J_P4R15_FOUR_FAMILY_REPEAT_READONLY_NO_NEW_HISTORY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-repeat");
            var first=db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId);
            var second=db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId);
            Ensure(first.IsFreshWitness && second.IsFreshWitness &&
                first.Fingerprint==second.Fingerprint && db.AllCopyHistoryCount()==1 &&
                db.AllMoveHistoryCount()==1 && db.AllRenameHistoryCount()==1 &&
                db.AllRejectHistoryCount()==1 && db.RejectItems().Single()==chain.Rejected,
                "P4-R15 repeated read-only inspection wrote history or lost identity.");
        });
        Check("M6J_P4R15_PRE_REJECT_CHAIN_NOT_A_FOURTH_FAMILY_WITNESS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                    "p4r15-before"+Path.GetExtension(moved.Destination)));
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(moved.SessionId).IsFreshWitness &&
                db.AllRejectHistoryCount()==0 && File.Exists(renamed.Target),
                "P4-R15 missing fourth family was accepted as committed.");
        });
        Check("M6J_P4R15_REJECT_ARCHIVE_CONTENT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-archive-bytes");
            File.AppendAllText(chain.Rejected.Archive,"p4r15 test-only archive tamper");
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                db.RejectItems().Single()==chain.Rejected && !File.Exists(chain.Rejected.Source),
                "P4-R15 archive content changed but was still treated as owned.");
        });
        Check("M6J_P4R15_REPLACED_REJECT_ARCHIVE_NATIVE_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-archive-id");
            string preserved=chain.Rejected.Archive+".original-owned";
            File.Move(chain.Rejected.Archive,preserved);
            File.Copy(preserved,chain.Rejected.Archive);
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                File.Exists(preserved) && db.RejectItems().Single()==chain.Rejected,
                "P4-R15 replaced fourth-family archive native identity was trusted.");
        });
        Check("M6J_P4R15_COMMITTED_COPY_PREDECESSOR_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-copy-tamper");
            File.AppendAllText(f.Prior.Destination,"p4r15 copy predecessor tamper");
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                db.RejectItems().Single()==chain.Rejected && File.Exists(chain.Rejected.Archive),
                "P4-R15 changed historical Copy output escaped four-family fence.");
        });
        Check("M6J_P4R15_COMMITTED_MOVE_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-move-archive");
            File.AppendAllText(chain.Moved.OriginalArchive,"p4r15 archive provenance tamper");
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                db.RejectItems().Single()==chain.Rejected && File.Exists(chain.Rejected.Archive),
                "P4-R15 moved-original recovery was no longer owned.");
        });
        Check("M6J_P4R15_FOREIGN_RENAME_TARGET_REFUSED_NO_OVERWRITE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-foreign-target");
            File.WriteAllText(chain.Renamed.Target,"synthetic foreign replacement");
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                File.ReadAllText(chain.Renamed.Target)=="synthetic foreign replacement" &&
                db.RejectItems().Single()==chain.Rejected,
                "P4-R15 foreign source collision was accepted or overwritten.");
        });
        Check("M6J_P4R15_REMOVED_GENERATED_MARKER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-unmark");
            File.Delete(Path.Combine(f.Root,"P5C_GUI_TEST_ONLY.marker"));
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                db.RejectItems().Single()==chain.Rejected,
                "P4-R15 missing second generated marker was accepted.");
        });
        Check("M6J_P4R15_WRONG_FIXTURE_ENVIRONMENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r15-wrong-env");
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
            Ensure(!db.InspectM6JP4R15CommittedGeneratedFourth(chain.Moved.SessionId).IsFreshWitness &&
                db.RejectItems().Single()==chain.Rejected,
                "P4-R15 wrong P5C environment was accepted.");
        });
        Check("M6J_P4R15_INTERRUPTED_DURABLE_REJECT_INTENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var renamed=M6JP4R9GeneratedMoveRename.Apply(db,
                M6JP4R9GeneratedMoveRename.Prepare(db,moved.SessionId,
                    "p4r15-cut"+Path.GetExtension(moved.Destination)));
            var plan=M6JP4R12GeneratedRenameReject.Prepare(db,moved.SessionId);
            Refuse<IOException>(()=>M6JP4R12GeneratedRenameReject.Apply(db,plan,stage=>{
                if(stage=="P4R12_REJECT_INTENT_DURABLE")
                    throw new IOException("P4-R15 generated-only interruption");}));
            Ensure(db.RejectItems().Single().State=="Intent" &&
                !db.InspectM6JP4R15CommittedGeneratedFourth(moved.SessionId).IsFreshWitness &&
                File.Exists(renamed.Target) && !File.Exists(plan.Archive),
                "P4-R15 unfinished fourth-family Intent was treated as committed.");
        });
        // P4-R16: only exact generated Copy A -> Move B -> Rename C ->
        // Reject D D-only Undo. No GUI and no ordinary-photo authority.
        Check("M6J_P4R16_FOURTH_REJECT_SELECTIVE_UNDO_PRESERVES_ABC",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-positive");
            var id=chain.Rejected.SourceIdentity;
            var undone=M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId);
            Ensure(undone.State=="Undone" && undone.Id==chain.Rejected.Id &&
                db.RejectItems().Single()==undone &&
                db.CopyItems(chain.Moved.SessionId).Single()==f.Prior &&
                db.MoveItems(chain.Moved.SessionId).Single()==chain.Moved &&
                db.RenameItems(chain.Moved.SessionId).Single()==chain.Renamed &&
                db.AllCopyHistoryCount()==1 && db.AllMoveHistoryCount()==1 &&
                db.AllRenameHistoryCount()==1 && db.AllRejectHistoryCount()==1 &&
                File.Exists(chain.Rejected.Source) && !File.Exists(chain.Rejected.Archive) &&
                NativeFileCapability.Identity(chain.Rejected.Source)==id &&
                File.Exists(chain.Moved.OriginalArchive),
                "P4-R16 D Undo did not restore exact file while retaining all A/B/C journals.");
        });
        Check("M6J_P4R16_COLD_SQLITE_D_UNDO_AND_SECOND_COLD_TERMINAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-cold");
            string saved=db.DatabasePath;string dir=Path.GetDirectoryName(saved)!;
            db.Dispose();
            using(var cold=new WorkspaceSessionStore(dir))
            {
                Ensure(cold.DatabasePath==saved,"P4-R16 reopened another SQLite database.");
                var undone=M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                    cold,chain.Moved.SessionId);
                Ensure(undone.State=="Undone" && File.Exists(undone.Source),
                    "P4-R16 cold D undo failed.");
            }
            using var second=new WorkspaceSessionStore(dir);
            Ensure(second.DatabasePath==saved &&
                second.RejectItems().Single().State=="Undone" &&
                second.CopyItems(chain.Moved.SessionId).Single()==f.Prior &&
                second.MoveItems(chain.Moved.SessionId).Single()==chain.Moved &&
                second.RenameItems(chain.Moved.SessionId).Single()==chain.Renamed &&
                File.Exists(chain.Rejected.Source) && !File.Exists(chain.Rejected.Archive),
                "P4-R16 second cold reopen changed history or recovered incorrectly.");
        });
        Check("M6J_P4R16_REPEAT_D_UNDO_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-repeat");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Undone" &&
                File.Exists(chain.Rejected.Source) && !File.Exists(chain.Rejected.Archive),
                "P4-R16 repeated Undo altered terminal state.");
        });
        Check("M6J_P4R16_FOREIGN_OCCUPIED_TARGET_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-occupied");
            File.WriteAllText(chain.Rejected.Source,"foreign-owned-target");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(File.ReadAllText(chain.Rejected.Source)=="foreign-owned-target" &&
                File.Exists(chain.Rejected.Archive) && db.RejectItems().Single().State=="Committed",
                "P4-R16 foreign target was overwritten or durable intent improperly recorded.");
        });
        Check("M6J_P4R16_ARCHIVE_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-archive-tamper");
            File.AppendAllText(chain.Rejected.Archive,"tampered-generated-archive");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed" &&
                !File.Exists(chain.Rejected.Source),"P4-R16 tampered D archive escaped preflight.");
        });
        Check("M6J_P4R16_ARCHIVE_SAME_BYTES_DIFFERENT_NATIVE_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-archive-id");
            string keep=chain.Rejected.Archive+".preserved";
            File.Move(chain.Rejected.Archive,keep);
            File.Copy(keep,chain.Rejected.Archive);
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(File.Exists(keep) && db.RejectItems().Single().State=="Committed",
                "P4-R16 same-byte archive replacement gained native authority.");
        });
        Check("M6J_P4R16_CHANGED_COPY_OUTPUT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-copy-tamper");
            File.AppendAllText(f.Prior.Destination,"changed-Copy-A");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed" && File.Exists(chain.Rejected.Archive),
                "P4-R16 damaged A was silently ignored.");
        });
        Check("M6J_P4R16_CHANGED_MOVE_RECOVERY_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-move-tamper");
            File.AppendAllText(chain.Moved.OriginalArchive,"changed-Move-B");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed" && File.Exists(chain.Rejected.Archive),
                "P4-R16 damaged B was silently ignored.");
        });
        Check("M6J_P4R16_RENAME_ROW_SQL_IDENTITY_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-rename-sql");
            using(var other=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString()))
            {
                other.Open();using var cmd=other.CreateCommand();
                cmd.CommandText="UPDATE rename_items SET id=$wrong";
                cmd.Parameters.AddWithValue("$wrong",Guid.NewGuid().ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R16 failed to corrupt synthetic Rename ID.");
            }
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed" && File.Exists(chain.Rejected.Archive),
                "P4-R16 changed C journal id gained fourth-family Undo authority.");
        });
        Check("M6J_P4R16_MISSING_SECOND_MARKER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-marker");
            File.Delete(Path.Combine(f.Root,"P5C_GUI_TEST_ONLY.marker"));
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed", "P4-R16 marker bypassed.");
        });
        Check("M6J_P4R16_WRONG_ENVIRONMENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-env");
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="Committed", "P4-R16 wrong environment accepted.");
        });
        Check("M6J_P4R16_DURABLE_UNDO_INTENT_CUT_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-intent-cut");
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R16_REJECT_UNDO_INTENT_DURABLE")
                        throw new IOException("generated interruption after D UndoIntent");}));
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.RejectItems().Single().State=="UndoIntent" &&
                !File.Exists(chain.Rejected.Source) && File.Exists(chain.Rejected.Archive) &&
                cold.CopyItems(chain.Moved.SessionId).Single()==f.Prior,
                "P4-R16 post-intent cold state changed unexpectedly.");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                cold,chain.Moved.SessionId));
            Ensure(cold.RejectItems().Single().State=="UndoIntent", "P4-R16 replayed pending D.");
        });
        Check("M6J_P4R16_POST_RELOCATION_CUT_COLD_NO_AUTOFINALIZE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-relocate-cut");
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R16_REJECT_UNDO_RELOCATED_UNJOURNALLED")
                        throw new IOException("generated interruption after D physical Undo");}));
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.RejectItems().Single().State=="UndoIntent" &&
                File.Exists(chain.Rejected.Source) && !File.Exists(chain.Rejected.Archive) &&
                NativeFileCapability.Identity(chain.Rejected.Source)==chain.Rejected.SourceIdentity,
                "P4-R16 post-relocation cold state was replayed or auto-finalized.");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                cold,chain.Moved.SessionId));
            Ensure(cold.RejectItems().Single().State=="UndoIntent", "P4-R16 replayed moved D.");
        });
        Check("M6J_P4R16_POST_INTENT_FOREIGN_DESTINATION_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-post-intent-foreign");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R16_REJECT_UNDO_INTENT_DURABLE")
                        File.WriteAllText(chain.Rejected.Source,"foreign-after-durable-intent");}));
            Ensure(db.RejectItems().Single().State=="UndoIntent" &&
                File.ReadAllText(chain.Rejected.Source)=="foreign-after-durable-intent" &&
                File.Exists(chain.Rejected.Archive),
                "P4-R16 post-intent collision was overwritten or replayed.");
        });
        Check("M6J_P4R16_POST_INTENT_COPY_TAMPER_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-post-intent-copy");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R16_REJECT_UNDO_INTENT_DURABLE")
                        File.AppendAllText(f.Prior.Destination,"copy-tamper-after-intent");}));
            Ensure(db.RejectItems().Single().State=="UndoIntent" &&
                File.Exists(chain.Rejected.Archive) && !File.Exists(chain.Rejected.Source),
                "P4-R16 post-intent predecessor mutation escaped ownership check.");
        });
        Check("M6J_P4R16_PREDECESSOR_MOVE_RENAME_UNDO_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r16-dependency-veto");
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(
                db,chain.Moved.SessionId));
            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .UndoM6JReviewedGeneratedCopyMoveAsync(chain.Moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed" &&
                db.MoveItems(chain.Moved.SessionId).Single().State=="Committed" &&
                db.RejectItems().Single().State=="Committed", "P4-R16 altered B/C before D Undo.");
        });
        // P4-R17: strictly generated Copy A -> Move B -> Rename C -> Reject D,
        // D durably Undone, then C-only Undo; NEVER ordinary or generic Undo.
        Check("M6J_P4R17_D_UNDONE_C_SELECTIVE_UNDO_PRESERVES_A_B_D",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-positive");
            var d=M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            var c=M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            Ensure(c.State=="Undone" && d.State=="Undone" &&
                File.Exists(c.Source) && !File.Exists(c.Target) && !File.Exists(d.Archive) &&
                NativeFileCapability.Identity(c.Source)==c.NativeIdentity &&
                db.RejectItems().Single()==d && db.MoveItems(chain.Moved.SessionId).Single()==chain.Moved &&
                db.CopyItems(chain.Moved.SessionId).Single()==f.Prior,
                "P4-R17 selective C Undo lost historical A/B/D ownership.");
        });
        Check("M6J_P4R17_SAME_SQLITE_C_COLD_REOPEN_AND_TERMINAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-cold");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            string path=db.DatabasePath,dir=Path.GetDirectoryName(path)!;db.Dispose();
            using(var cold=new WorkspaceSessionStore(dir))
            {
                Ensure(cold.DatabasePath==path,"P4-R17 did not reopen exact same DB.");
                var c=M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(cold,chain.Moved.SessionId);
                Ensure(c.State=="Undone" && cold.RejectItems().Single().State=="Undone",
                    "P4-R17 cold reopened C Undo did not retain terminal D.");
            }
            using var second=new WorkspaceSessionStore(dir);
            Ensure(second.DatabasePath==path && second.RenameItems(chain.Moved.SessionId).Single().State=="Undone" &&
                second.RejectItems().Single().State=="Undone" &&
                File.Exists(chain.Renamed.Source) && !File.Exists(chain.Renamed.Target),
                "P4-R17 final cold restart changed C/D histories.");
        });
        Check("M6J_P4R17_C_UNDO_BEFORE_D_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-before-d");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed" &&
                File.Exists(chain.Rejected.Archive),"P4-R17 Undo C before D escaped dependency fence.");
        });
        Check("M6J_P4R17_C_UNDO_WITH_UNFINISHED_D_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-unfinished-d");
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R16_REJECT_UNDO_INTENT_DURABLE")
                        throw new IOException("generated D interrupted");}));
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RejectItems().Single().State=="UndoIntent" &&
                db.RenameItems(chain.Moved.SessionId).Single().State=="Committed",
                "P4-R17 unresolved D granted C authority.");
        });
        Check("M6J_P4R17_REPEAT_C_UNDO_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-repeat");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            var c=M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single()==c && File.Exists(c.Source),
                "P4-R17 repeated C Undo changed terminal history.");
        });
        Check("M6J_P4R17_FOREIGN_C_ORIGINAL_PATH_COLLISION_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-foreign");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            File.WriteAllText(chain.Renamed.Source,"synthetic-foreign-C");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(File.ReadAllText(chain.Renamed.Source)=="synthetic-foreign-C" &&
                db.RenameItems(chain.Moved.SessionId).Single().State=="Committed" && File.Exists(chain.Renamed.Target),
                "P4-R17 C Undo overwrote foreign original occupant.");
        });
        Check("M6J_P4R17_RESTORED_D_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-tamper-c");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            File.AppendAllText(chain.Renamed.Target,"tamper-after-D");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed", "P4-R17 changed C data gained authority.");
        });
        Check("M6J_P4R17_RESTORED_D_NATIVE_ID_SUBSTITUTION_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-id-substitution");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            string original=chain.Renamed.Target+".original-generated-owned";
            File.Move(chain.Renamed.Target,original); File.Copy(original,chain.Renamed.Target);
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(File.Exists(original) && db.RenameItems(chain.Moved.SessionId).Single().State=="Committed",
                "P4-R17 same-hash foreign ID gained Undo authority.");
        });
        Check("M6J_P4R17_PREDECESSOR_COPY_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-copy-tamper");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            File.AppendAllText(f.Prior.Destination,"change-committed-A");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed", "P4-R17 Copy predecessor bypassed.");
        });
        Check("M6J_P4R17_PREDECESSOR_MOVE_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-move-tamper");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            File.AppendAllText(chain.Moved.OriginalArchive,"change-committed-B");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed", "P4-R17 Move predecessor bypassed.");
        });
        Check("M6J_P4R17_MISSING_SECOND_MARKER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-no-marker");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            File.Delete(Path.Combine(f.Root,"P5C_GUI_TEST_ONLY.marker"));
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed", "P4-R17 missing marker bypassed.");
        });
        Check("M6J_P4R17_WRONG_GENERATED_ENV_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-env");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="Committed", "P4-R17 wrong environment bypassed.");
        });
        Check("M6J_P4R17_C_UNDO_INTENT_COLD_CUT_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-intent-cut");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R17_C_UNDO_INTENT_DURABLE")
                        throw new IOException("generated C interruption after intent");}));
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                cold.RejectItems().Single().State=="Undone" &&
                File.Exists(chain.Renamed.Target) && !File.Exists(chain.Renamed.Source),
                "P4-R17 cold durable intent mismatch.");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(cold,chain.Moved.SessionId));
            Ensure(cold.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent", "P4-R17 restarted C Undo replayed.");
        });
        Check("M6J_P4R17_C_UNDO_AFTER_RELOCATION_COLD_CUT_NO_FINALIZE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-physical-cut");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R17_C_UNDO_RELOCATED_UNJOURNALLED")
                        throw new IOException("generated C interruption after native rename");}));
            db.Dispose();using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                cold.RejectItems().Single().State=="Undone" &&
                File.Exists(chain.Renamed.Source) && !File.Exists(chain.Renamed.Target),
                "P4-R17 cold physical relocation unexpectedly finalized.");
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(cold,chain.Moved.SessionId));
            Ensure(cold.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent", "P4-R17 physical restart replayed.");
        });
        Check("M6J_P4R17_POST_INTENT_FOREIGN_C_SOURCE_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-post-intent-foreign");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R17_C_UNDO_INTENT_DURABLE")
                        File.WriteAllText(chain.Renamed.Source,"foreign-after-C-intent");}));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                File.ReadAllText(chain.Renamed.Source)=="foreign-after-C-intent" &&
                File.Exists(chain.Renamed.Target),"P4-R17 post-intent collision overwritten.");
        });
        Check("M6J_P4R17_POST_INTENT_COPY_TAMPER_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-post-intent-copy");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R17_C_UNDO_INTENT_DURABLE")
                        File.AppendAllText(f.Prior.Destination,"A-tampered-after-C-intent");}));
            Ensure(db.RenameItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                File.Exists(chain.Renamed.Target),"P4-R17 post-intent predecessor mutation escaped.");
        });
        Check("M6J_P4R17_D_UNDONE_OLD_GENERIC_RENAME_UNDO_STILL_REFUSES",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r17-old-veto");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R9GeneratedMoveRename.UndoGeneratedThirdFamily(db,chain.Moved.SessionId));
            Ensure(db.AllRejectHistoryCount()==1 && db.RenameItems(chain.Moved.SessionId).Single().State=="Committed",
                "P4-R17 accidentally weakened existing generic Rename veto.");
        });
        // P4-R18: strictly generated only; B Move Undo after durable D and C Undo.
        // Four-stage journal CAS, two physical relocations, no recovery replay.
        Check("M6J_P4R18_B_UNDO_D_AND_C_TERMINAL_RETAINS_A_C_D",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-positive");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            var priorA=db.CopyItems(chain.Moved.SessionId).Single();
            var priorC=db.RenameItems(chain.Moved.SessionId).Single();
            var priorD=db.RejectItems().Single();
            var b=M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Ensure(b.State=="Undone" && db.CopyItems(chain.Moved.SessionId).Single()==priorA &&
                db.RenameItems(chain.Moved.SessionId).Single()==priorC &&
                db.RejectItems().Single()==priorD &&
                File.Exists(b.Source) && File.Exists(b.OutputArchive) &&
                !File.Exists(b.Destination) && !File.Exists(b.OriginalArchive) &&
                NativeFileCapability.Identity(b.Source)==b.SourceIdentity &&
                NativeFileCapability.Identity(b.OutputArchive)==b.TargetIdentity,
                "P4-R18 four-family B-only Undo altered protected predecessors.");
        });
        Check("M6J_P4R18_SAME_DB_COLD_REOPEN_AND_TERMINAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-cold");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using(var cold=new WorkspaceSessionStore(dir))
            {
                Ensure(cold.DatabasePath==file,"P4-R18 cold opened the wrong SQLite file.");
                var b=M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId);
                Ensure(b.State=="Undone" && File.Exists(b.OutputArchive),"P4-R18 B cold finish failed.");
            }
            using var second=new WorkspaceSessionStore(dir);
            Ensure(second.DatabasePath==file && second.MoveItems(chain.Moved.SessionId).Single().State=="Undone" &&
                second.RenameItems(chain.Moved.SessionId).Single().State=="Undone" &&
                second.RejectItems().Single().State=="Undone",
                "P4-R18 terminal journal failed the same-DB cold restart.");
        });
        Check("M6J_P4R18_BEFORE_D_UNDO_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-before-d");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed", "B advanced before D.");
        });
        Check("M6J_P4R18_AFTER_D_BEFORE_C_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-before-c");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed", "B advanced before C.");
        });
        Check("M6J_P4R18_REPEAT_UNDO_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-repeat");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Undone", "Repeated B Undo resumed.");
        });
        Check("M6J_P4R18_FOREIGN_SOURCE_COLLISION_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-foreign-source");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            File.WriteAllText(chain.Moved.Source,"foreign-b-source");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(File.ReadAllText(chain.Moved.Source)=="foreign-b-source" &&
                File.Exists(chain.Moved.OriginalArchive),"Foreign B source overwritten.");
        });
        Check("M6J_P4R18_FOREIGN_OUTPUT_ARCHIVE_COLLISION_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-foreign-output");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            File.WriteAllText(chain.Moved.OutputArchive,"foreign-b-archive");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(File.ReadAllText(chain.Moved.OutputArchive)=="foreign-b-archive" &&
                File.Exists(chain.Moved.Destination),"Foreign output archive overwritten.");
        });
        Check("M6J_P4R18_COPY_PREDECESSOR_TAMPER_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-tamper-a");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            File.AppendAllText(f.Prior.Destination,"tamper-a");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed", "Copy tamper escaped.");
        });
        Check("M6J_P4R18_MOVE_ORIGINAL_ARCHIVE_TAMPER_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-tamper-b");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            File.AppendAllText(chain.Moved.OriginalArchive,"tamper-b");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed", "Original archive tamper escaped.");
        });
        Check("M6J_P4R18_MOVE_OUTPUT_SAME_BYTES_FOREIGN_ID_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-native-id");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            string saved=chain.Moved.Destination+".saved-generated-b";
            File.Move(chain.Moved.Destination,saved);File.Copy(saved,chain.Moved.Destination);
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(File.Exists(saved) && db.MoveItems(chain.Moved.SessionId).Single().State=="Committed",
                "Same-hash foreign native identity escaped B Undo.");
        });
        Check("M6J_P4R18_MISSING_OPERATION_MARKER_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-marker");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            File.Move(Path.Combine(Path.GetDirectoryName(chain.Moved.OriginalArchive)!,".photosift-operation-v1"),
                Path.Combine(Path.GetDirectoryName(chain.Moved.OriginalArchive)!,"marker.saved"));
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed", "Marker removed, B Undo advanced.");
        });
        Check("M6J_P4R18_INTENT_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-intent_cold_no_replay");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R18_B_RESTORE_INTENT_DURABLE")
                    throw new IOException("P4-R18 generated interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoRestoreIntent" &&
                File.Exists(chain.Moved.OriginalArchive) && !File.Exists(chain.Moved.Source),"P4-R18 cold interruption evidence did not persist.");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId));
            Ensure(cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoRestoreIntent",
                "P4-R18 automatically replayed interrupted Move Undo.");
        });
        Check("M6J_P4R18_SOURCE_RELOCATED_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-source_relocated_cold_no_replay");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R18_B_SOURCE_RELOCATED_UNJOURNALLED")
                    throw new IOException("P4-R18 generated interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoRestoreIntent" &&
                !File.Exists(chain.Moved.OriginalArchive) && File.Exists(chain.Moved.Source),"P4-R18 cold interruption evidence did not persist.");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId));
            Ensure(cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoRestoreIntent",
                "P4-R18 automatically replayed interrupted Move Undo.");
        });
        Check("M6J_P4R18_RESTORE_RECORD_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-restore_record_cold_no_replay");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R18_B_SOURCE_RESTORED_DURABLE")
                    throw new IOException("P4-R18 generated interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.MoveItems(chain.Moved.SessionId).Single().State=="SourceRestored" &&
                File.Exists(chain.Moved.Source) && File.Exists(chain.Moved.Destination),"P4-R18 cold interruption evidence did not persist.");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId));
            Ensure(cold.MoveItems(chain.Moved.SessionId).Single().State=="SourceRestored",
                "P4-R18 automatically replayed interrupted Move Undo.");
        });
        Check("M6J_P4R18_OUTPUT_INTENT_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-output_intent_cold_no_replay");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R18_B_OUTPUT_ARCHIVE_INTENT_DURABLE")
                    throw new IOException("P4-R18 generated interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoOutputArchiveIntent" &&
                File.Exists(chain.Moved.Destination) && !File.Exists(chain.Moved.OutputArchive),"P4-R18 cold interruption evidence did not persist.");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId));
            Ensure(cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoOutputArchiveIntent",
                "P4-R18 automatically replayed interrupted Move Undo.");
        });
        Check("M6J_P4R18_OUTPUT_RELOCATED_COLD_NO_FINALIZE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-output_relocated_cold_no_finalize");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R18_B_OUTPUT_RELOCATED_UNJOURNALLED")
                    throw new IOException("P4-R18 generated interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoOutputArchiveIntent" &&
                !File.Exists(chain.Moved.Destination) && File.Exists(chain.Moved.OutputArchive),"P4-R18 cold interruption evidence did not persist.");
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(cold,chain.Moved.SessionId));
            Ensure(cold.MoveItems(chain.Moved.SessionId).Single().State=="UndoOutputArchiveIntent",
                "P4-R18 automatically replayed interrupted Move Undo.");
        });
        Check("M6J_P4R18_POST_INTENT_FOREIGN_SOURCE_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-post-intent-source");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R18_B_RESTORE_INTENT_DURABLE")
                        File.WriteAllText(chain.Moved.Source,"foreign-after-b-intent");}));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="UndoRestoreIntent" &&
                File.ReadAllText(chain.Moved.Source)=="foreign-after-b-intent" &&
                File.Exists(chain.Moved.OriginalArchive),"P4-R18 post-intent collision escaped.");
        });
        Check("M6J_P4R18_POST_OUTPUT_INTENT_FOREIGN_ARCHIVE_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-post-intent-archive");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R18_B_OUTPUT_ARCHIVE_INTENT_DURABLE")
                        File.WriteAllText(chain.Moved.OutputArchive,"foreign-after-output-intent");}));
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="UndoOutputArchiveIntent" &&
                File.ReadAllText(chain.Moved.OutputArchive)=="foreign-after-output-intent" &&
                File.Exists(chain.Moved.Destination),"P4-R18 output collision escaped.");
        });
        Check("M6J_P4R18_OLD_GENERIC_MOVE_UNDO_VETO_RETAINED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r18-old-veto");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);            Refuse<IOException>(()=>new DurableMoveCoordinator(db)
                .UndoM6JReviewedGeneratedCopyMoveAsync(chain.Moved.SessionId).GetAwaiter().GetResult());
            Ensure(db.MoveItems(chain.Moved.SessionId).Single().State=="Committed" &&
                db.AllRejectHistoryCount()==1,"P4-R18 weakened historical Move Undo veto.");
        });
        // P4-R19: strictly marked generated only; A Copy selective Undo
        // after durable B, C and D terminal Undone. No ordinary authority.
        Check("M6J_P4R19_A_UNDO_PRESERVES_B_C_D",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-positive");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            var b=db.MoveItems(chain.Moved.SessionId).Single();
            var c=db.RenameItems(chain.Moved.SessionId).Single();
            var d=db.RejectItems().Single();
            var a=M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId);
            Ensure(a.State=="Undone" && db.MoveItems(chain.Moved.SessionId).Single()==b &&
                db.RenameItems(chain.Moved.SessionId).Single()==c &&
                db.RejectItems().Single()==d &&
                File.Exists(a.Source) && File.Exists(a.Recovery) && !File.Exists(a.Destination) &&
                NativeFileCapability.Identity(a.Recovery)==a.TargetIdentity &&
                NativeFileCapability.Identity(a.Source)==a.SourceIdentity &&
                File.Exists(b.Source) && File.Exists(b.OutputArchive),
                "P4-R19 four-family completed reverse chain changed predecessors.");
        });
        Check("M6J_P4R19_EXACT_SAME_DB_COLD_REOPEN",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-cold");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using(var cold=new WorkspaceSessionStore(dir))
            {
                Ensure(cold.DatabasePath==file,"P4-R19 switched SQLite database.");
                var a=M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(cold,chain.Moved.SessionId);
                Ensure(a.State=="Undone" && File.Exists(a.Recovery),"Cold A Undo was not complete.");
            }
            using var final=new WorkspaceSessionStore(dir);
            Ensure(final.DatabasePath==file && final.CopyItems(chain.Moved.SessionId).Single().State=="Undone" &&
                final.MoveItems(chain.Moved.SessionId).Single().State=="Undone" &&
                final.RenameItems(chain.Moved.SessionId).Single().State=="Undone" &&
                final.RejectItems().Single().State=="Undone", "P4-R19 terminal same-DB cold check failed.");
        });
        Check("M6J_P4R19_BEFORE_D_UNDO_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-before-d");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","A Undo bypassed D.");
        });
        Check("M6J_P4R19_AFTER_D_BEFORE_C_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-before-c");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","A Undo bypassed C.");
        });
        Check("M6J_P4R19_AFTER_D_C_BEFORE_B_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-before-b");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","A Undo bypassed B.");
        });
        Check("M6J_P4R19_REPEATED_A_UNDO_VETO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-repeat");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Undone","Repeated A Undo replayed.");
        });
        Check("M6J_P4R19_FOREIGN_OUTPUT_RECOVERY_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-foreign-recovery");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(f.Prior.Recovery)!);
            File.WriteAllText(f.Prior.Recovery,"synthetic-foreign-output");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(File.ReadAllText(f.Prior.Recovery)=="synthetic-foreign-output" &&
                File.Exists(f.Prior.Destination),"Foreign A recovery occupant overwritten.");
        });
        Check("M6J_P4R19_COPY_OUTPUT_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-bytes");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            File.AppendAllText(f.Prior.Destination,"tamper-A-output");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","Tampered A output authorized.");
        });
        Check("M6J_P4R19_COPY_SOURCE_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-source-bytes");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            File.AppendAllText(f.Prior.Source,"tamper-A-source");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(File.Exists(f.Prior.Destination),"Tampered A source caused output relocation.");
        });
        Check("M6J_P4R19_COPY_OUTPUT_IDENTITY_SUBSTITUTION_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-id");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            string saved=f.Prior.Destination+".saved-generated-A";
            File.Move(f.Prior.Destination,saved);File.Copy(saved,f.Prior.Destination);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(File.Exists(saved) && db.CopyItems(chain.Moved.SessionId).Single().State=="Committed",
                "Same-byte Copy output replacement authorized A Undo.");
        });
        Check("M6J_P4R19_B_SOURCE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-b-tamper");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            File.AppendAllText(chain.Moved.Source,"tamper-restored-B");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(File.Exists(f.Prior.Destination),"B source tamper escaped A proof.");
        });
        Check("M6J_P4R19_B_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-b-archive");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            File.AppendAllText(chain.Moved.OutputArchive,"tamper-B-output-archive");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","B archived output tamper escaped A proof.");
        });
        Check("M6J_P4R19_MISSING_MOVE_OWNER_MARKER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-marker");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            string mark=Path.Combine(Path.GetDirectoryName(chain.Moved.OriginalArchive)!,".photosift-operation-v1");
            File.Move(mark,mark+".saved");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(File.Exists(f.Prior.Destination),"Missing B owner marker authorized A Undo.");
        });
        Check("M6J_P4R19_WRONG_GENERATED_ENV_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-env");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Environment.SetEnvironmentVariable(P5CGuiFixturePolicy.EnvName,Path.GetTempPath());
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Committed","Wrong environment authorized A Undo.");
        });
        Check("M6J_P4R19_A_INTENT_COLD_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-a_intent_cold_no_replay");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R19_A_UNDO_INTENT_DURABLE")
                    throw new IOException("generated Copy interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.CopyItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                File.Exists(f.Prior.Destination) && !File.Exists(f.Prior.Recovery),"P4-R19 Before interruption cold evidence failed.");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(cold,chain.Moved.SessionId));
            Ensure(cold.CopyItems(chain.Moved.SessionId).Single().State=="UndoIntent",
                "P4-R19 automatically replayed interrupted A Undo.");
        });
        Check("M6J_P4R19_A_PHYSICAL_COLD_NO_FINALIZE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-a_physical_cold_no_finalize");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(
                db,chain.Moved.SessionId,stage=>{ if(stage=="P4R19_A_OUTPUT_RELOCATED_UNJOURNALLED")
                    throw new IOException("generated Copy interruption"); }));
            string file=db.DatabasePath,dir=Path.GetDirectoryName(file)!;db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            Ensure(cold.DatabasePath==file && cold.CopyItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                !File.Exists(f.Prior.Destination) && File.Exists(f.Prior.Recovery),"P4-R19 After interruption cold evidence failed.");
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(cold,chain.Moved.SessionId));
            Ensure(cold.CopyItems(chain.Moved.SessionId).Single().State=="UndoIntent",
                "P4-R19 automatically replayed interrupted A Undo.");
        });
        Check("M6J_P4R19_POST_INTENT_FOREIGN_RECOVERY_FENCED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-post-intent");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(
                db,chain.Moved.SessionId,stage=>{
                    if(stage=="P4R19_A_UNDO_INTENT_DURABLE")
                        File.WriteAllText(f.Prior.Recovery,"foreign-after-A-intent");}));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="UndoIntent" &&
                File.ReadAllText(f.Prior.Recovery)=="foreign-after-A-intent" &&
                File.Exists(f.Prior.Destination),"Post-intent Copy archive collision escaped.");
        });
        Check("M6J_P4R19_GENERIC_COPY_WRITER_REJECT_VETO_RETAINED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r19-generic");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            var old=db.CopyItems(chain.Moved.SessionId).Single();
            Refuse<IOException>(()=>db.PutCopyItem(old with {State="UndoIntent"}));
            Ensure(db.AllRejectHistoryCount()==1 &&
                db.CopyItems(chain.Moved.SessionId).Single()==old,
                "R19 weakened historical generic Copy/Reject veto.");
        });
        // P4-R20 P0 hostile repeatability/topology FALSIFICATION ONLY.
        // No repetition, interleaving, ordinary photos, or new undo permission
        // is granted by any of these disposable tests or diagnostic results.
        string r20Base=Path.Combine(Path.GetTempPath(),"P4R20_TOPOLOGY_"+Guid.NewGuid().ToString("N"));
        M6JOperationFootprint R20Row(string family,int n,Guid? photo=null,
            string? session=null,string state="Undone",bool proven=true,
            string? src=null,string? dst=null,string? identity=null) =>
            new(family,Guid.NewGuid().ToString("N"),session??Guid.NewGuid().ToString("N"),
                photo??Guid.NewGuid(),state,proven,new[]{
                    new M6JPathClaim("Source",src??Path.Combine(r20Base,"source",$"s{n}.png"),
                        RecordedNativeIdentity:identity),
                    new M6JPathClaim("Destination",dst??Path.Combine(r20Base,"output",$"d{n}.png"))});
        void R20NoGrant(M6JDependencyAssessment report) =>
            Ensure(!report.AuthorizesNativeOperation && !report.AuthorizesSelectiveUndo &&
                !report.GrantsSuccessorEpoch && report.IsReadOnlyDiagnostic,
                "P4-R20 diagnostic crossed an authorization boundary.");
        Check("M6J_P4R20_EIGHT_TERMINAL_DISJOINT_ROWS_NO_GRANT",() =>
        {
            var rows=new[]{"Copy","Move","Rename","Reject","Copy","Move","Rename","Reject"}
                .Select((fam,n)=>R20Row(fam,n)).ToArray();
            var report=M6JDependencyInspector.Analyze(rows);
            R20NoGrant(report);
            Ensure(report.ExaminedRows==8 && report.Edges.Count==0 &&
                report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized,
                "Eight lexically distinct historical rows became operation authority.");
        });
        Check("M6J_P4R20_INTERLEAVED_SAME_PHOTO_DETECTED",() =>
        {
            Guid photo=Guid.NewGuid();
            var rows=new[]{R20Row("Copy",1,photo),R20Row("Move",2),
                R20Row("Rename",3,photo),R20Row("Reject",4),R20Row("Copy",5,photo)};
            var report=M6JDependencyInspector.Analyze(rows);R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Count(e=>e.Kind==M6JConflictKind.SamePhoto)==3,
                "Interleaved same-photo history lost an edge.");
        });
        Check("M6J_P4R20_SHARED_TARGET_ACROSS_SESSIONS_DETECTED",() =>
        {
            string shared=Path.Combine(r20Base,"target","one.png");
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1,dst:shared),R20Row("Copy",2,dst:shared)});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(x=>x.Kind==M6JConflictKind.SameRecordedPath),
                "Different-session same-target collision went unnoticed.");
        });
        Check("M6J_P4R20_SAME_NATIVE_IDENTITY_DIFFERENT_PATHS_DETECTED",() =>
        {
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1,identity:"recorded-native-ambiguous"),
                R20Row("Reject",2,identity:"recorded-native-ambiguous")});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(x=>x.Kind==M6JConflictKind.SameRecordedNativeIdentity) &&
                !report.Edges.Any(x=>x.Kind==M6JConflictKind.SameRecordedPath),
                "Recorded native alias lost when paths were distinct.");
        });
        Check("M6J_P4R20_HISTORICAL_RENAME_CHAIN_PATH_OVERLAP",() =>
        {
            string target=Path.Combine(r20Base,"output","renamed.png");
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Move",1,dst:target),R20Row("Rename",2,src:target)});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(x=>x.Kind==M6JConflictKind.SameRecordedPath),
                "Recorded Move to Rename edge silently disappeared.");
        });
        Check("M6J_P4R20_RECOVERY_ROOT_CONTAINS_SUCCESSOR",() =>
        {
            string owned=Path.Combine(r20Base,"archive","own");
            var reject=R20Row("Reject",1);
            reject=reject with {Claims=new[]{
                new M6JPathClaim("Source",Path.Combine(r20Base,"source","r.png")),
                new M6JPathClaim("OwnedRecoveryRoot",owned,true),
                new M6JPathClaim("Archive",Path.Combine(owned,"r.png"))}};
            var move=R20Row("Move",2,src:Path.Combine(owned,"nested","s.png"));
            var report=M6JDependencyInspector.Analyze(new[]{reject,move});R20NoGrant(report);
            Ensure(report.Edges.Any(e=>e.Kind==M6JConflictKind.RecoveryRootContainsPath),
                "Recovery-root containment was ignored for an interleaved successor.");
        });
        Check("M6J_P4R20_INTERRUPT_WITH_EDGES_REMAINS_UNVERIFIED",() =>
        {
            string same=Path.Combine(r20Base,"mixed","same.png");
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1,dst:same),
                R20Row("Move",2,state:"UndoRestoreIntent",proven:false,src:same)});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                report.Edges.Any(e=>e.Kind==M6JConflictKind.SameRecordedPath),
                "Interrupted overlapping history suppressed its diagnostic edge.");
        });
        Check("M6J_P4R20_DUPLICATE_HISTORY_KEY_REFUSED",() =>
        {
            var row=R20Row("Copy",1);
            var cloned=row with {Claims=new[]{
                new M6JPathClaim("Source",Path.Combine(r20Base,"another","source.png")),
                new M6JPathClaim("Destination",Path.Combine(r20Base,"another","dest.png"))}};
            var report=M6JDependencyInspector.Analyze(new[]{row,cloned});R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified,
                "Duplicate same-family journal key created a successor grant.");
        });
        Check("M6J_P4R20_HISTORY_ROW_LIMIT_REFUSES",() =>
        {
            var rows=Enumerable.Range(0,513).Select(i=>R20Row("Copy",i)).ToArray();
            var report=M6JDependencyInspector.Analyze(rows);R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified &&
                report.ExaminedRows==0,"An oversized historical ledger obtained authority.");
        });
        Check("M6J_P4R20_PREFIX_NEIGHBORS_NOT_NATIVE_INDEPENDENCE",() =>
        {
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1,src:Path.Combine(r20Base,"album","pic.png")),
                R20Row("Copy",2,src:Path.Combine(r20Base,"album-2","pic.png"))});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.LexicallyDisjointNotAuthorized &&
                report.Edges.Count==0,"Lexical absence of edges became a permission token.");
        });
        Check("M6J_P4R20_SAME_PHOTO_CROSS_SESSION_REUSE_DETECTED",() =>
        {
            Guid p=Guid.NewGuid();
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1,p),R20Row("Reject",2,p)});
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.DependencyDetected &&
                report.Edges.Any(x=>x.Kind==M6JConflictKind.SamePhoto),
                "Same photo reused across sessions was mistaken for independent.");
        });
        Check("M6J_P4R20_INCOMPLETE_OBSERVATION_REFUSES_EVEN_DISJOINT",() =>
        {
            var report=M6JDependencyInspector.Analyze(new[]{
                R20Row("Copy",1),R20Row("Rename",2)},false);
            R20NoGrant(report);
            Ensure(report.Kind==M6JDependencyKind.IncompleteOrUnverified,
                "Incomplete eight-family snapshot obtained an approval.");
        });
        Check("M6J_P4R20_REAL_FOUR_REVERSED_JOURNALS_STILL_LOCKED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r20-final-readonly");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId);
            var report=db.InspectM6JReadOnlyDependencies();R20NoGrant(report);
            Ensure(db.AllCopyHistoryCount()==1 && db.AllMoveHistoryCount()==1 &&
                db.AllRenameHistoryCount()==1 && db.AllRejectHistoryCount()==1 &&
                db.CopyItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.MoveItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.RenameItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.RejectItems().Single().State=="Undone" &&
                File.Exists(f.Prior.Recovery) && !File.Exists(f.Prior.Destination),
                "Four terminal journals were deleted or history was promoted.");
        });
        Check("M6J_P4R20_REAL_TERMINAL_REENTRY_REFUSES_ALL_FAMILIES",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var chain=P4R15CommitFour(db,f.Work,f.Candidate,f.Intent,"p4r20-no-repeat");
            M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId);
            M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId);
            M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId);
            M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId);
            Refuse<IOException>(()=>M6JP4R16GeneratedSelectiveRejectUndo.UndoExactlyOneGeneratedFourth(db,chain.Moved.SessionId));
            Refuse<IOException>(()=>M6JP4R17GeneratedRenameUndoAfterReject.UndoExactlyOneGeneratedC(db,chain.Moved.SessionId));
            Refuse<IOException>(()=>M6JP4R18GeneratedMoveUndoAfterCD.UndoExactlyOneGeneratedB(db,chain.Moved.SessionId));
            Refuse<IOException>(()=>M6JP4R19GeneratedCopyUndoAfterBCD.UndoExactlyOneGeneratedA(db,chain.Moved.SessionId));
            Ensure(db.CopyItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.MoveItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.RenameItems(chain.Moved.SessionId).Single().State=="Undone" &&
                db.RejectItems().Single().State=="Undone", "R20 repeat attempt modified terminal history.");
        });
        // P4-R21 P1: real two-operation Copy A + Move B positive CURRENT native
        // ownership after a consumed generated Move review. These are NOT two
        // independent mixed-family cycles; no approval, replay, Undo, or third
        // operation capability is granted by the read-only observation.
        (WorkspaceSessionStore Db, P4R13FourthFamilyFixtureScope Scope,
            DurableCopyItem Copy, DurableMoveItem Move) R21Pair()
        {
            var f=A3DisjointCopyMoveFixture();
            var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var moved=new DurableMoveCoordinator(f.Db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(moved.State=="Committed" && f.Db.AllCopyHistoryCount()==1 &&
                f.Db.AllMoveHistoryCount()==1 && f.Db.AllRenameHistoryCount()==0 &&
                f.Db.AllRejectHistoryCount()==0,
                "P4-R21 setup did not establish two real committed journals.");
            return (f.Db,scope,f.Prior,moved);
        }
        void R21NoAuthority(M6JP4R21IndependentPairObservation x) =>
            Ensure(!x.AuthorizesNativeOperation && !x.AuthorizesSelectiveUndo &&
                !x.GrantsSuccessorEpoch && !x.IsDurableProof && !x.CanReplay &&
                x.IsReadOnlyDiagnostic,"P4-R21 observation mistakenly grants authority.");
        Check("M6J_P4R21_TWO_REAL_DISJOINT_FAMILIES_CURRENT_NATIVE_POSITIVE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            var beforeCopy=db.CopyItems(f.Move.SessionId).Single();
            var beforeMove=db.MoveItems(f.Move.SessionId).Single();
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(x.IsFreshWitness && x.CopyId==f.Copy.Id && x.MoveId==f.Move.Id &&
                db.CopyItems(f.Move.SessionId).Single()==beforeCopy &&
                db.MoveItems(f.Move.SessionId).Single()==beforeMove &&
                File.Exists(f.Copy.Source) && File.Exists(f.Copy.Destination) &&
                !File.Exists(f.Move.Source) && File.Exists(f.Move.OriginalArchive) &&
                File.Exists(f.Move.Destination),
                "P4-R21 real independently owned two-family snapshot failed or modified history.");
        });
        Check("M6J_P4R21_COLD_REOPEN_SAME_DB_POSITIVE_NO_GRANT",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var scope=f.Scope;
            string path=f.Db.DatabasePath,dir=Path.GetDirectoryName(path)!;
            var a=f.Db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);
            Ensure(a.IsFreshWitness,"P4-R21 warm pair must be positive before cold reopen.");
            f.Db.Dispose();
            using var cold=new WorkspaceSessionStore(dir);
            var b=cold.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(b);
            Ensure(cold.DatabasePath==path && b.IsFreshWitness &&
                b.CopyId==a.CopyId && b.MoveId==a.MoveId &&
                cold.AllCopyHistoryCount()==1 && cold.AllMoveHistoryCount()==1,
                "P4-R21 independent pair lost durable native evidence across cold restart.");
        });
        Check("M6J_P4R21_REPEAT_READONLY_NO_JOURNAL_TRANSITIONS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            var copies=db.CopyItems(f.Move.SessionId).ToArray();
            var moves=db.MoveItems(f.Move.SessionId).ToArray();
            for(int i=0;i<3;i++) {
                var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
                Ensure(x.IsFreshWitness,"P4-R21 repeat read unexpectedly failed.");
            }
            Ensure(copies.SequenceEqual(db.CopyItems(f.Move.SessionId)) &&
                moves.SequenceEqual(db.MoveItems(f.Move.SessionId)) &&
                db.AllRenameHistoryCount()==0 && db.AllRejectHistoryCount()==0,
                "P4-R21 read-only observation wrote native history.");
        });
        Check("M6J_P4R21_COPY_SOURCE_MUTATION_FENCES_PAIR",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.Copy.Source,"R21-test-only-changed-original");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness && db.AllCopyHistoryCount()==1 && db.AllMoveHistoryCount()==1,
                "P4-R21 changed Copy source passed pair witness.");
        });
        Check("M6J_P4R21_COPY_OUTPUT_MUTATION_FENCES_PAIR",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.Copy.Destination,"R21-test-only-changed-copy-output");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness,"P4-R21 changed Copy output passed pair witness.");
        });
        Check("M6J_P4R21_MOVE_ORIGINAL_ARCHIVE_MUTATION_FENCES_PAIR",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.Move.OriginalArchive,"R21-test-only-changed-archive");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness,"P4-R21 changed owned Move archive passed pair witness.");
        });
        Check("M6J_P4R21_MOVE_OUTPUT_SAME_BYTES_FOREIGN_NATIVE_ID_FENCES",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            string old=f.Move.Destination+".owned-native-test-only";
            File.Move(f.Move.Destination,old);
            File.Copy(old,f.Move.Destination,false);
            Ensure(NativeFileCapability.Identity(old)!=NativeFileCapability.Identity(f.Move.Destination),
                "P4-R21 test failed to install genuinely foreign native id.");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness,"P4-R21 same bytes with new native ID passed pair witness.");
        });
        Check("M6J_P4R21_MOVE_RECOVERY_OWNER_MARKER_TAMPER_FENCES",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(f.Move.OriginalArchive)!,
                ".photosift-operation-v1"),"R21-test-only-owner-tamper");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness,"P4-R21 tampered Move ownership marker passed pair witness.");
        });
        Check("M6J_P4R21_COPY_HARDLINK_ALIAS_FENCES_PAIR",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            NativeFileCapability.PublishLinkNoReplace(f.Copy.Destination,
                f.Copy.Destination+".r21-test-only-hardlink");
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness,"P4-R21 multi-link Copy output passed single-link witness.");
        });
        Check("M6J_P4R21_EXTRA_COPY_HISTORY_REJECTED_NOT_SILENTLY_APPROVED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            using(var sql=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                sql.Open();using var cmd=sql.CreateCommand();
                cmd.CommandText="INSERT INTO copy_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.Move.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R21 synthetic extra journal insertion failed.");
            }
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness && db.AllCopyHistoryCount()==2,
                "P4-R21 positive pair accidentally authorized repeat Copy history.");
        });
        Check("M6J_P4R21_FOREIGN_RENAME_ROW_REJECTED_WITHOUT_WRITING",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R21Pair();using var db=f.Db;using var scope=f.Scope;
            using(var sql=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                sql.Open();using var cmd=sql.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.Move.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"P4-R21 synthetic Rename insertion failed.");
            }
            var x=db.InspectM6JP4R21GeneratedCommittedPair(f.Move.SessionId);R21NoAuthority(x);
            Ensure(!x.IsFreshWitness && db.AllRenameHistoryCount()==1,
                "P4-R21 positive pair accidentally accepted unapproved third family.");
        });
        // P4-R22 P2: exercise EXISTING P6G3C separately reviewed two-Copy
        // native authorization, then hostilely verify its EXACT two-row ceiling.
        // This creates only disposable generated photo fixtures. No new grants,
        // schema, production coordinator, or third-Copy permission are added.
        (string Root,WorkspaceSessionStore Db,DurableCopyCoordinator Engine,
            SortingWorkspace Resumed,PreparedCopy Next,CopyCommittedSuccessorGrant Grant,
            DurableCopyItem A,DurableCopyItem B) R22TwoCopies()
        {
            string root=Root();var initial=NativeCopyUndoFixture(root);
            var ids=initial.Photos.Select(x=>x.Id).ToArray();
            Ensure(ids.Length>=3,"R22 needs three generated source images for the ceiling test.");
            var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            try
            {
                var engine=new DurableCopyCoordinator(db);
                initial.Assign('F');var plan=initial.ReviewPlan();
                var prepared=DurableCopyCoordinator.PrepareAsync(initial,plan,
                    initial.CurrentPlanId!.Value,initial.Revision).GetAwaiter().GetResult();
                initial.ApprovePlan(initial.CurrentPlanId!.Value,initial.Revision);
                db.Save(initial.CaptureSession());
                Ensure(engine.ApplyAsync(prepared).GetAwaiter().GetResult().Completed==1,
                    "R22 first independent native Copy was not committed.");
                var first=db.CopyItems(initial.SessionId).Single();
                Ensure(first.State=="Committed" && first.PhotoId==ids[0],
                    "R22 native Copy predecessor is not the reviewed first photo.");
                initial.RetireCommitted(new[]{ids[0]});db.Save(initial.CaptureSession());
                var nextWork=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
                nextWork.RestoreSession(db.Load()!);nextWork.JumpToPhoto(2);
                Ensure(nextWork.Current!.Id==ids[1],"R22 successor is not generated photo 2.");
                nextWork.Assign('T');var secondPlan=nextWork.ReviewPlan();
                var next=DurableCopyCoordinator.PrepareAsync(nextWork,secondPlan,
                    nextWork.CurrentPlanId!.Value,nextWork.Revision).GetAwaiter().GetResult().Single();
                nextWork.ApprovePlan(nextWork.CurrentPlanId!.Value,nextWork.Revision);
                db.Save(nextWork.CaptureSession());
                var permit=db.ApproveCommittedCopySuccessor(nextWork,next);
                Ensure(permit.SessionId==initial.SessionId && db.AllCopyHistoryCount()==1,
                    "R22 Copy successor approval wrote a journal row prematurely.");
                Ensure(engine.ApplyAsync(new[]{next},successor:permit).GetAwaiter().GetResult().Completed==1,
                    "R22 second independently authorized native Copy did not commit.");
                var rows=db.CopyItems(initial.SessionId);
                Ensure(rows.Count==2 && rows[0].Id==first.Id &&
                    rows.All(x=>x.State=="Committed") && rows[1].PhotoId==ids[1],
                    "R22 native Copy successor rows differ from exact separately reviewed grants.");
                return (root,db,engine,nextWork,next,permit,rows[0],rows[1]);
            }
            catch {db.Dispose();throw;}
        }
        Check("M6J_P4R22_TWO_REAL_COPY_COMMITS_HAVE_DISTINCT_NATIVE_OUTPUTS",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            Ensure(db.AllCopyHistoryCount()==2 && db.HasAnyCopySuccessorGrant &&
                f.A.SessionId==f.B.SessionId && f.A.Id!=f.B.Id &&
                f.A.PhotoId!=f.B.PhotoId &&
                File.Exists(f.A.Destination) && File.Exists(f.B.Destination) &&
                NativeFileCapability.Identity(f.A.Destination)==f.A.TargetIdentity &&
                NativeFileCapability.Identity(f.B.Destination)==f.B.TargetIdentity &&
                f.A.TargetIdentity!=f.B.TargetIdentity &&
                f.A.SourceIdentity!=f.B.SourceIdentity,
                "Two separately reviewed native Copies did not retain independent identities.");
            db.RequireIndependentLatestCopyUndo(f.A.SessionId);
        });
        Check("M6J_P4R22_EXACT_DB_COLD_REOPEN_REVALIDATES_BOTH",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();string state=Path.Combine(f.Root,"state");
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(state);
            Ensure(cold.AllCopyHistoryCount()==2 && cold.HasAnyCopySuccessorGrant &&
                cold.CopyItems(f.A.SessionId).All(x=>x.State=="Committed"),
                "Copy pair changed after cold reopening the original SQLite DB.");
            cold.RequireIndependentLatestCopyUndo(f.A.SessionId);
        });
        Check("M6J_P4R22_SELECTED_B_UNDO_PRESERVES_NATIVE_A",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            var result=f.Engine.UndoLatestIndependentCopyAsync(f.A.SessionId).GetAwaiter().GetResult();
            var rows=db.CopyItems(f.A.SessionId);
            Ensure(result.Completed==1 && rows.Count==2 &&
                rows[0].Id==f.A.Id && rows[0].State=="Committed" &&
                rows[1].Id==f.B.Id && rows[1].State=="Undone" &&
                File.Exists(f.A.Destination) && File.Exists(f.B.Recovery) &&
                !File.Exists(f.B.Destination) &&
                NativeFileCapability.Identity(f.A.Destination)==f.A.TargetIdentity &&
                NativeFileCapability.Identity(f.B.Recovery)==f.B.TargetIdentity,
                "Selective Copy successor Undo affected its committed predecessor.");
        });
        Check("M6J_P4R22_REPEAT_B_UNDO_AFTER_TERMINAL_REFUSED",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            Ensure(f.Engine.UndoLatestIndependentCopyAsync(f.A.SessionId).GetAwaiter().GetResult().Completed==1,
                "First B Undo did not complete.");
            Refuse<IOException>(()=>f.Engine.UndoLatestIndependentCopyAsync(f.A.SessionId).GetAwaiter().GetResult());
            Ensure(db.CopyItems(f.A.SessionId).Count==2 &&
                db.CopyItems(f.A.SessionId)[0].State=="Committed" &&
                db.CopyItems(f.A.SessionId)[1].State=="Undone",
                "Repeated B Undo modified original journal history.");
        });
        Check("M6J_P4R22_CONSUMED_GRANT_CANNOT_INSERT_THIRD_INTENT",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            Refuse<IOException>(()=>f.Engine.ApplyAsync(new[]{f.Next},successor:f.Grant)
                .GetAwaiter().GetResult());
            Ensure(db.AllCopyHistoryCount()==2 &&
                db.CopyItems(f.A.SessionId).All(x=>x.State=="Committed"),
                "Replayed Copy successor permit introduced another native journal row.");
        });
        Check("M6J_P4R22_NO_FURTHER_UNREVIEWED_COPY_BEYOND_TWO_ROWS",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            Refuse<IOException>(()=>f.Engine.ApplyAsync(new[]{f.Next}).GetAwaiter().GetResult());
            Ensure(db.AllCopyHistoryCount()==2 && db.HasAnyCopySuccessorGrant,
                "Third Copy bypassed the two-row authority ceiling.");
        });
        Check("M6J_P4R22_THIRD_GRANT_REJECTED_AFTER_SECOND_COMMIT",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            Refuse<IOException>(()=>db.ApproveCommittedCopySuccessor(f.Resumed,f.Next));
            Ensure(db.AllCopyHistoryCount()==2,"Third Copy approval mutated committed history.");
        });
        Check("M6J_P4R22_PRIOR_OUTPUT_CHANGED_NATIVE_ID_BLOCKS_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            string moved=f.A.Destination+".r22-foreign";
            File.Move(f.A.Destination,moved);File.Copy(moved,f.A.Destination,false);
            Ensure(NativeFileCapability.Identity(f.A.Destination)!=f.A.TargetIdentity,
                "Foreign replacement did not change native identity.");
            Refuse<IOException>(()=>db.RequireIndependentLatestCopyUndo(f.A.SessionId));
            Ensure(db.CopyItems(f.A.SessionId).All(x=>x.State=="Committed"),
                "Predecessor output substitution modified the Copy journals.");
        });
        Check("M6J_P4R22_SUCCESSOR_OUTPUT_ALIAS_BLOCKS_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            NativeFileCapability.PublishLinkNoReplace(f.B.Destination,
                f.B.Destination+".r22-hardlink");
            Refuse<IOException>(()=>db.RequireIndependentLatestCopyUndo(f.A.SessionId));
            Ensure(db.CopyItems(f.A.SessionId).All(x=>x.State=="Committed"),
                "Hard-linked Copy successor was incorrectly authorized for Undo.");
        });
        Check("M6J_P4R22_SUCCESSOR_SOURCE_TAMPER_BLOCKS_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            File.AppendAllText(f.B.Source,"r22-generated-only-source-tamper");
            Refuse<IOException>(()=>db.RequireIndependentLatestCopyUndo(f.A.SessionId));
            Ensure(db.AllCopyHistoryCount()==2,"Tamper refusal lost a journal row.");
        });
        Check("M6J_P4R22_FOREIGN_MOVE_ROW_BLOCKS_LATEST_COPY_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO move_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.A.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Generated foreign Move row injection failed.");
            }
            Refuse<IOException>(()=>db.RequireIndependentLatestCopyUndo(f.A.SessionId));
            Ensure(db.AllCopyHistoryCount()==2 && db.AllMoveHistoryCount()==1,
                "Unreviewed Move row was ignored or Copy history erased.");
        });
        Check("M6J_P4R22_DURABLE_GRANT_ROW_TAMPER_BLOCKS_LATEST_UNDO",()=>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="UPDATE copy_successor_grants SET predecessor_digest='r22-tamper' WHERE id=$id";
                cmd.Parameters.AddWithValue("$id",f.Grant.Id.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"Generated successor grant tamper failed.");
            }
            Refuse<IOException>(()=>db.RequireIndependentLatestCopyUndo(f.A.SessionId));
            Ensure(db.AllCopyHistoryCount()==2,"Tampered grant changed existing native Copy rows.");
        });
        // P4-R22-R2: a P1 legacy file may be externally rewritten with the
        // original bytes AND original LastWriteTimeUtc. An earlier detected
        // conflict must remain latched, not silently gain Undo permission.
        Check("M6J_P4R22R2_CONFLICT_LATCH_SURVIVES_IDENTICAL_BYTES_AND_MTIME",()=>
        {
            var root=Root();var w=Create(root);
            w.Assign('f');w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult().Copied==1,
                "Legacy generated Copy fixture was not committed.");
            var a=w.Applied.Single();
            File.AppendAllText(a.Destination,"r22-r2-external-conflict");
            Ensure(w.UndoAppliedCopiesAsync().GetAwaiter().GetResult().Error is not null,
                "Observed external overwrite was not refused.");
            File.WriteAllBytes(a.Destination,File.ReadAllBytes(a.Source));
            File.SetLastWriteTimeUtc(a.Destination,new DateTime(a.InstalledWriteUtcTicks,DateTimeKind.Utc));
            var info=new FileInfo(a.Destination);
            Ensure(info.Length==a.Length && info.LastWriteTimeUtc.Ticks==a.InstalledWriteUtcTicks &&
                SortingWorkspace.ComputeSha256Async(a.Destination).GetAwaiter().GetResult()==a.Sha256,
                "Adversarial same-metadata generated fixture not fully restored.");
            var refused=w.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(refused.Error is not null && refused.Copied==0 && File.Exists(a.Destination) &&
                w.Applied.Count==1,"Restored bytes and timestamps improperly cleared a previous ownership conflict.");
        });
        Check("M6J_P4R22R2_PARTIAL_BATCH_CONFLICT_LATCH_PRESERVES_UNCERTAIN_OUTPUT",()=>
        {
            var root=Root();var w=Create(root);
            w.Assign('f');w.Assign('t');w.ReviewPlan();
            w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
            Ensure(w.ApplyReviewedCopiesAsync().GetAwaiter().GetResult().Copied==2,
                "Legacy generated two-Copy batch was not committed.");
            var first=w.Applied[0];var second=w.Applied[1];
            File.AppendAllText(first.Destination,"r22-r2-first-copy-tamper");
            var partial=w.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(partial.Copied==1 && partial.Error is not null &&
                !File.Exists(second.Destination) && File.Exists(first.Destination) &&
                w.Applied.Count==1,"Partial legacy Undo did not preserve its unresolved earlier Copy.");
            File.WriteAllBytes(first.Destination,File.ReadAllBytes(first.Source));
            File.SetLastWriteTimeUtc(first.Destination,new DateTime(first.InstalledWriteUtcTicks,DateTimeKind.Utc));
            var refused=w.UndoAppliedCopiesAsync().GetAwaiter().GetResult();
            Ensure(refused.Error is not null && refused.Copied==0 &&
                w.Applied.Count==1 && File.Exists(first.Destination) &&
                File.Exists(first.Source) && File.Exists(second.Source),
                "Previously uncertain earlier Copy was deleted after bytes and metadata restoration.");
        });
        // P4-R23: the first complete, narrowly reviewed TWO-family reverse
        // topology: Copy A -> independently reviewed Move B -> Undo B -> Undo A.
        // Strictly one row per family, NO third/fourth family rows, marked
        // disposable Windows fixtures, new isolated Copy journal CAS.
        (WorkspaceSessionStore Db,P4R13FourthFamilyFixtureScope Scope,
            DurableCopyItem A,DurableMoveItem B) R23Ready()
        {
            var f=A3DisjointCopyMoveFixture();
            var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var mover=new DurableMoveCoordinator(f.Db);
            var committed=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            var undone=mover.UndoM6JReviewedGeneratedCopyMoveAsync(committed.SessionId)
                .GetAwaiter().GetResult();
            Ensure(undone.State=="Undone" && f.Db.CopyItems(undone.SessionId).Single().State=="Committed" &&
                f.Db.InspectM6JProspectiveReview(undone.Id).State=="Consumed" &&
                File.Exists(undone.Source) && File.Exists(undone.OutputArchive) &&
                !File.Exists(undone.Destination) && !File.Exists(undone.OriginalArchive),
                "R23 prerequisite is not a real reviewed Move B terminal Undo.");
            return(f.Db,scope,f.Prior,undone);
        }
        Check("M6J_P4R23_TWO_FAMILY_FULL_NATIVE_REVERSE_POSITIVE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            var beforeB=db.MoveItems(f.B.SessionId).Single();
            var a=M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId);
            Ensure(a.State=="Undone" && db.CopyItems(f.B.SessionId).Single()==a &&
                db.MoveItems(f.B.SessionId).Single()==beforeB &&
                !File.Exists(a.Destination) && File.Exists(a.Recovery) &&
                File.Exists(a.Source) && File.Exists(f.B.Source) &&
                File.Exists(f.B.OutputArchive) && !File.Exists(f.B.Destination) &&
                NativeFileCapability.Identity(a.Recovery)==f.A.TargetIdentity &&
                NativeFileCapability.Identity(f.B.Source)==f.B.SourceIdentity &&
                db.AllRenameHistoryCount()==0 && db.AllRejectHistoryCount()==0,
                "R23 two-family terminal reversal changed predecessor or native ownership.");
        });
        Check("M6J_P4R23_COLD_REOPEN_THEN_UNDO_A_PRESERVES_B",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var scope=f.Scope;
            var file=f.Db.DatabasePath;var folder=Path.GetDirectoryName(file)!;
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(folder);
            var a=M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(cold,f.B.SessionId);
            Ensure(cold.DatabasePath==file && a.State=="Undone" &&
                cold.MoveItems(f.B.SessionId).Single()==f.B &&
                File.Exists(a.Recovery) && !File.Exists(a.Destination) &&
                File.Exists(f.B.Source) && File.Exists(f.B.OutputArchive),
                "R23 cold exact database and Move predecessor were not preserved.");
        });
        Check("M6J_P4R23_COPY_UNDO_BEFORE_B_UNDONE_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var committed=new DurableMoveCoordinator(db).ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,committed.SessionId));
            Ensure(db.CopyItems(committed.SessionId).Single().State=="Committed" &&
                db.MoveItems(committed.SessionId).Single().State=="Committed" &&
                File.Exists(f.Prior.Destination),"R23 early A Undo bypassed B dependency.");
        });
        Check("M6J_P4R23_REPEATED_A_UNDO_REFUSED_AFTER_TERMINAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId);
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="Undone" &&
                File.Exists(f.A.Recovery),"R23 terminal Copy Undo replayed.");
        });
        Check("M6J_P4R23_COPY_RECOVERY_DESTINATION_FOREIGN_COLLISION_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            Directory.CreateDirectory(Path.GetDirectoryName(f.A.Recovery)!);
            File.WriteAllText(f.A.Recovery,"foreign-owner-r23-generated-only");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(File.ReadAllText(f.A.Recovery)=="foreign-owner-r23-generated-only" &&
                File.Exists(f.A.Destination) && db.CopyItems(f.B.SessionId).Single().State=="Committed",
                "R23 foreign recovery file was overwritten or A was consumed.");
        });
        Check("M6J_P4R23_COPY_OUTPUT_BYTES_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.A.Destination,"r23-generated-external-output");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="Committed" &&
                File.Exists(f.A.Destination),"R23 changed A output was removed.");
        });
        Check("M6J_P4R23_COPY_OUTPUT_NATIVE_ID_SUBSTITUTION_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            var old=f.A.Destination+".r23-preserve-foreign-replaced";
            File.Move(f.A.Destination,old);File.Copy(old,f.A.Destination,false);
            Ensure(NativeFileCapability.Identity(f.A.Destination)!=f.A.TargetIdentity,
                "R23 test did not create a new native identity.");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(File.Exists(old) && File.Exists(f.A.Destination),
                "R23 same-content identity substitution was destructively consumed.");
        });
        Check("M6J_P4R23_COPY_SOURCE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.A.Source,"r23-generated-changed-source");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="Committed" &&
                File.Exists(f.A.Destination),"R23 changed Copy source was ignored.");
        });
        Check("M6J_P4R23_RESTORED_MOVE_SOURCE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.B.Source,"r23-generated-changed-restored-b");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="Committed" &&
                File.Exists(f.B.OutputArchive),"R23 damaged restored B source passed.");
        });
        Check("M6J_P4R23_ARCHIVED_MOVE_OUTPUT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.B.OutputArchive,"r23-generated-changed-b-output");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="Committed" &&
                File.Exists(f.A.Destination),"R23 changed archived B output passed.");
        });
        Check("M6J_P4R23_MOVE_OPERATION_MARKER_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            var marker=Path.Combine(Path.GetDirectoryName(f.B.OutputArchive)!,".photosift-operation-v1");
            File.AppendAllText(marker,"r23-generated-foreign-owner");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(File.Exists(f.A.Destination) && db.CopyItems(f.B.SessionId).Single().State=="Committed",
                "R23 changed Move marker authorized Copy Undo.");
        });
        Check("M6J_P4R23_FOREIGN_RENAME_ROW_REFUSED_NO_WRITE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            using(var sql=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                sql.Open();using var cmd=sql.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.B.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R23 synthetic foreign Rename insert failed.");
            }
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
            Ensure(db.AllRenameHistoryCount()==1 && File.Exists(f.A.Destination),
                "R23 foreign Rename bypassed exact two-family fence.");
        });
        Check("M6J_P4R23_DURABLE_A_INTENT_INTERRUPTION_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            Refuse<InvalidOperationException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(
                db,f.B.SessionId,cut=>{
                    if(cut=="P4R23_A_UNDO_INTENT_DURABLE")
                        throw new InvalidOperationException("r23-test-only-exact-cut");
                }));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="UndoIntent" &&
                File.Exists(f.A.Destination) && !File.Exists(f.A.Recovery),
                "R23 exact intent interruption state not preserved.");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
        });
        Check("M6J_P4R23_AFTER_NATIVE_RELOCATION_INTERRUPTION_NO_FINALIZE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            Refuse<InvalidOperationException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(
                db,f.B.SessionId,cut=>{
                    if(cut=="P4R23_A_OUTPUT_RELOCATED_UNJOURNALLED")
                        throw new InvalidOperationException("r23-test-only-physical-cut");
                }));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="UndoIntent" &&
                File.Exists(f.A.Recovery) && !File.Exists(f.A.Destination),
                "R23 physical relocation cut was not preserved.");
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(db,f.B.SessionId));
        });
        Check("M6J_P4R23_POST_INTENT_FOREIGN_RECOVERY_COLLISION_PRESERVED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R23Ready();using var db=f.Db;using var scope=f.Scope;
            Refuse<IOException>(()=>M6JP4R23GeneratedCopyUndoAfterB.UndoExactlyOneGeneratedA(
                db,f.B.SessionId,cut=>{
                    if(cut=="P4R23_A_UNDO_INTENT_DURABLE")
                        File.WriteAllText(f.A.Recovery,"r23-synthetic-postintent-foreign-occupant");
                }));
            Ensure(db.CopyItems(f.B.SessionId).Single().State=="UndoIntent" &&
                File.Exists(f.A.Destination) &&
                File.ReadAllText(f.A.Recovery)=="r23-synthetic-postintent-foreign-occupant",
                "R23 foreign recovery file was overwritten after durable Undo Intent.");
        });
        // P4-R24 P0: operation-scoped multi-row classification is NEVER
        // a filesystem mutation authority or a replacement for reviewed grants.
        void R24NoAuthority(M6JP4R24ScopedTopologyObservation x) =>
            Ensure(!x.AuthorizesNativeOperation && !x.AuthorizesSelectiveUndo &&
                !x.GrantsSuccessorEpoch && !x.AuthorizesOrdinaryMixedFamily &&
                !x.IsDurableProof && x.IsReadOnlyDiagnostic,
                "R24 operation-scoped observation crossed a mutation authority boundary.");
        M6JP4R24ScopedTopologyObservation R24Scope(
            M6JOperationFootprint[] rows, M6JOperationFootprint target,
            bool complete = true) =>
            M6JP4R24OperationScopedTopology.Inspect(rows,target.Family,target.JournalId,complete);
        Check("M6J_P4R24_EIGHT_DISJOINT_ROWS_SELECT_EXACT_OPERATION_NO_GRANT",() =>
        {
            var rows=Enumerable.Range(0,8).Select(i=>R20Row(
                new[]{"Copy","Move","Rename","Reject"}[i%4],200+i)).ToArray();
            var x=R24Scope(rows,rows[5]);R24NoAuthority(x);
            Ensure(x.TargetFound && x.ExaminedRows==8 && x.HistoryFullyVerified &&
                x.NoRecordedTargetEdges && x.TargetEdgeCount==0 && x.OtherEdgeCount==0 &&
                x.HistoryKind==M6JDependencyKind.LexicallyDisjointNotAuthorized,
                "R24 disjoint multi-row set was mistaken for native permission.");
        });
        Check("M6J_P4R24_THREE_SAME_FAMILY_ROWS_DISJOINT_NO_GRANT",() =>
        {
            var rows=Enumerable.Range(0,3).Select(i=>R20Row("Copy",220+i)).ToArray();
            var x=R24Scope(rows,rows[1]);R24NoAuthority(x);
            Ensure(x.TargetFound && x.NoRecordedTargetEdges && x.ExaminedRows==3,
                "Three historical Copy rows were incorrectly assumed authorized.");
        });
        Check("M6J_P4R24_TARGET_ISOLATED_BUT_UNRELATED_CONFLICT_RETAINED",() =>
        {
            var first=R20Row("Copy",230);var second=R20Row("Move",231);
            var third=R20Row("Rename",232,photo:second.PhotoId);
            var x=R24Scope(new[]{first,second,third},first);R24NoAuthority(x);
            Ensure(x.TargetFound && x.NoRecordedTargetEdges && x.TargetEdgeCount==0 &&
                x.OtherEdgeCount>0 && x.HistoryKind==M6JDependencyKind.DependencyDetected,
                "Unrelated conflicting journals were hidden by selected operation scope.");
        });
        Check("M6J_P4R24_SHARED_PHOTO_SELECTIVE_DEPENDENCY_DETECTED",() =>
        {
            var photo=Guid.NewGuid();var first=R20Row("Copy",240,photo);
            var second=R20Row("Reject",241,photo);
            var x=R24Scope(new[]{first,second},second);R24NoAuthority(x);
            Ensure(x.TargetFound && !x.NoRecordedTargetEdges && x.TargetEdgeCount>=1,
                "Same-photo historical dependence was dropped from exact row view.");
        });
        Check("M6J_P4R24_SAME_OUTPUT_ACROSS_SESSIONS_NOT_DISJOINT",() =>
        {
            string shared=Path.Combine(r20Base,"r24","shared.png");
            var first=R20Row("Copy",250,dst:shared);var second=R20Row("Move",251,dst:shared);
            var x=R24Scope(new[]{first,second},first);R24NoAuthority(x);
            Ensure(x.TargetFound && x.TargetEdgeCount>=1 && !x.NoRecordedTargetEdges,
                "Cross-session destination alias was ignored.");
        });
        Check("M6J_P4R24_RECORDED_NATIVE_IDENTITY_ALIAS_NOT_DISJOINT",() =>
        {
            var first=R20Row("Copy",260,identity:"r24-shared-recorded-id");
            var second=R20Row("Rename",261,identity:"r24-shared-recorded-id");
            var x=R24Scope(new[]{first,second},second);R24NoAuthority(x);
            Ensure(x.TargetEdgeCount>=1 && !x.NoRecordedTargetEdges,
                "Saved native identity collision went unreported.");
        });
        Check("M6J_P4R24_RECOVERY_ROOT_CHILD_COLLISION_BLOCKED",() =>
        {
            string root=Path.Combine(r20Base,"r24","owned-recovery");
            var first=R20Row("Copy",270);
            first=first with {Claims=new[]{
                new M6JPathClaim("Source",Path.Combine(r20Base,"src","r24a.png")),
                new M6JPathClaim("OwnedRecoveryRoot",root,true)}};
            var second=R20Row("Move",271,src:Path.Combine(root,"child.png"));
            var x=R24Scope(new[]{first,second},second);R24NoAuthority(x);
            Ensure(x.TargetEdgeCount>=1,"Owned recovery root hierarchy was ignored.");
        });
        Check("M6J_P4R24_CROSS_FAMILY_SAME_JOURNAL_ID_BLOCKED",() =>
        {
            var first=R20Row("Copy",280);
            var second=R20Row("Move",281) with {JournalId=first.JournalId};
            var x=R24Scope(new[]{first,second},first);R24NoAuthority(x);
            Ensure(x.TargetEdgeCount>=1,"Same operation GUID across families was accepted.");
        });
        Check("M6J_P4R24_INCOMPLETE_HISTORY_NO_SELECTED_INDEPENDENCE",() =>
        {
            var first=R20Row("Copy",290);
            var second=R20Row("Move",291,state:"UndoIntent");
            var x=R24Scope(new[]{first,second},first);R24NoAuthority(x);
            Ensure(!x.TargetFound && !x.HistoryFullyVerified && !x.NoRecordedTargetEdges,
                "An unresolved unrelated operation was ignored.");
        });
        Check("M6J_P4R24_NATIVE_UNVERIFIED_ROW_BLOCKS_ALL",() =>
        {
            var first=R20Row("Copy",300);
            var second=R20Row("Move",301,proven:false);
            var x=R24Scope(new[]{first,second},first);R24NoAuthority(x);
            Ensure(!x.TargetFound && !x.HistoryFullyVerified,
                "An unproved row became permission for another row.");
        });
        Check("M6J_P4R24_EXTERNAL_INCOMPLETE_SNAPSHOT_BLOCKS_ALL",() =>
        {
            var first=R20Row("Copy",310);var second=R20Row("Move",311);
            var x=R24Scope(new[]{first,second},first,complete:false);R24NoAuthority(x);
            Ensure(!x.TargetFound && !x.HistoryFullyVerified,
                "Incomplete historical observation was treated as fresh.");
        });
        Check("M6J_P4R24_UNKNOWN_TARGET_ROW_FAILS_CLOSED",() =>
        {
            var first=R20Row("Copy",320);
            var x=M6JP4R24OperationScopedTopology.Inspect(new[]{first},"Copy",Guid.NewGuid().ToString("N"));
            R24NoAuthority(x);Ensure(!x.TargetFound && !x.NoRecordedTargetEdges,
                "Unknown operation key inherited a predecessor's proof.");
        });
        Check("M6J_P4R24_DUPLICATE_SAME_FAMILY_KEY_REFUSED",() =>
        {
            var first=R20Row("Copy",330);
            var second=R20Row("Copy",331) with {JournalId=first.JournalId};
            var x=R24Scope(new[]{first,second},first);R24NoAuthority(x);
            Ensure(!x.TargetFound && !x.HistoryFullyVerified,
                "Duplicate family journal key appeared authoritative.");
        });
        Check("M6J_P4R24_OVERSIZE_AND_INVALID_TARGETS_REFUSED",() =>
        {
            var first=R20Row("Copy",340);
            var giant=Enumerable.Range(0,513).Select(i=>R20Row("Copy",350+i)).ToArray();
            var x=R24Scope(giant,giant[0]);R24NoAuthority(x);
            var bad=M6JP4R24OperationScopedTopology.Inspect(new[]{first},"Copy","not-a-guid");
            R24NoAuthority(bad);
            Ensure(!x.TargetFound && !bad.TargetFound && !x.HistoryFullyVerified,
                "Over-limit or malformed requested identities were admitted.");
        });
        Check("M6J_P4R24_REAL_TWO_REVIEWED_COPY_HISTORY_NO_AUTHORITY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R22TwoCopies();using var db=f.Db;
            db.RequireIndependentLatestCopyUndo(f.A.SessionId);
            // A real consumed one-use grant proves ONLY the existing two-Copy
            // capability. It cannot authorize Move/Rename/Reject or third Copy.
            var synthetic=new[]{
                new M6JOperationFootprint("Copy",f.A.Id.ToString("N"),f.A.SessionId.ToString("N"),
                    f.A.PhotoId,"Committed",true,new[]{
                        new M6JPathClaim("Source",f.A.Source,RecordedNativeIdentity:f.A.SourceIdentity),
                        new M6JPathClaim("Destination",f.A.Destination,RecordedNativeIdentity:f.A.TargetIdentity)}),
                new M6JOperationFootprint("Copy",f.B.Id.ToString("N"),f.B.SessionId.ToString("N"),
                    f.B.PhotoId,"Committed",true,new[]{
                        new M6JPathClaim("Source",f.B.Source,RecordedNativeIdentity:f.B.SourceIdentity),
                        new M6JPathClaim("Destination",f.B.Destination,RecordedNativeIdentity:f.B.TargetIdentity)})};
            var x=R24Scope(synthetic,synthetic[1]);R24NoAuthority(x);
            Ensure(x.TargetFound && x.ExaminedRows==2 && x.NoRecordedTargetEdges &&
                db.AllCopyHistoryCount()==2 && db.HasAnyCopySuccessorGrant &&
                db.AllMoveHistoryCount()==0,
                "Real two-Copy current native proof leaked unsupported mixed-family authority.");
        });
        Check("M6J_P4R24_REAL_CONSUMED_COPY_MOVE_REVIEW_REMAINS_SCOPED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=A3DisjointCopyMoveFixture();using var db=f.Db;
            using var scope=new P4R13FourthFamilyFixtureScope(f.Root);
            var mover=new DurableMoveCoordinator(db);
            var b=mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(
                f.Work,P2BPrepared(f.Intent),f.Intent).GetAwaiter().GetResult();
            Ensure(b.State=="Committed" && db.InspectM6JProspectiveReview(b.Id).State=="Consumed",
                "Real Copy->Move approval was not consumed.");
            var ar=db.CopyItems(b.SessionId).Single();
            var rows=new[]{
                new M6JOperationFootprint("Copy",ar.Id.ToString("N"),ar.SessionId.ToString("N"),
                    ar.PhotoId,"Committed",true,new[]{
                        new M6JPathClaim("Source",ar.Source,RecordedNativeIdentity:ar.SourceIdentity),
                        new M6JPathClaim("Destination",ar.Destination,RecordedNativeIdentity:ar.TargetIdentity)}),
                new M6JOperationFootprint("Move",b.Id.ToString("N"),b.SessionId.ToString("N"),
                    b.PhotoId,"Committed",true,new[]{
                        new M6JPathClaim("Source",b.Source,RecordedNativeIdentity:b.SourceIdentity),
                        new M6JPathClaim("Destination",b.Destination,RecordedNativeIdentity:b.TargetIdentity)})};
            var x=R24Scope(rows,rows[1]);R24NoAuthority(x);
            Ensure(x.TargetFound && x.NoRecordedTargetEdges && x.ExaminedRows==2 &&
                db.AllCopyHistoryCount()==1 && db.AllMoveHistoryCount()==1,
                "Real consumed reviewed Copy/Move histories were promoted to generic authority.");
        });
        // R25 P0: first real durable per-operation reviewed seal in a real
        // two-committed-Copy native history. This is NOT Move Intent/native Move.
        (string Root,WorkspaceSessionStore Db,P4R13FourthFamilyFixtureScope Scope,
            SortingWorkspace Work,PreparedCopy Prepared,M6JProspectiveCandidate Candidate,
            DurableCopyItem A,DurableCopyItem B) R25ThirdReviewFixture()
        {
            var f=R22TwoCopies();
            try
            {
                var scope=new P4R13FourthFamilyFixtureScope(f.Root);
                f.Resumed.RetireCommitted(new[]{f.B.PhotoId});f.Db.Save(f.Resumed.CaptureSession());
                var w=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
                w.RestoreSession(f.Db.Load()!);
                Ensure(w.Photos.Count>=3,"R25 third generated photo is absent.");
                w.JumpToPhoto(3);
                Ensure(w.Current!.Id!=f.A.PhotoId && w.Current.Id!=f.B.PhotoId,
                    "R25 third candidate reused one of the already committed native photos.");
                w.Assign('F');var plan=w.ReviewPlan();
                var p=DurableCopyCoordinator.PrepareAsync(w,plan,
                    w.CurrentPlanId!.Value,w.Revision).GetAwaiter().GetResult().Single();
                w.ApprovePlan(w.CurrentPlanId!.Value,w.Revision);
                f.Db.Save(w.CaptureSession());
                Directory.CreateDirectory(Path.GetDirectoryName(p.Destination)!);
                var c=new M6JProspectiveCandidate(Guid.NewGuid(),"Move",p.SessionId,
                    p.PhotoId,p.Source,p.Destination,p.Identity,p.Hash,p.Length,p.WriteTicks);
                return(f.Root,f.Db,scope,w,p,c,f.A,f.B);
            }
            catch {f.Db.Dispose();throw;}
        }
        Check("M6J_P4R25_EXACT_THIRD_MOVE_REVIEW_SEAL_DURABLY_RECORDED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            var receipt=db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true);
            Ensure(receipt.ReviewId==f.Candidate.Id && receipt.ReviewedDigest.Length==64 &&
                receipt.CopyHistoryDigest.Length==64 && receipt.SavedCheckpointDigest.Length==64 &&
                receipt.IsDurablyReviewedOnly && !receipt.AuthorizesNativeOperation &&
                !receipt.AuthorizesMoveIntent && !receipt.AuthorizesSelectiveUndo &&
                !receipt.AuthorizesOrdinaryMixedFamily &&
                db.AllCopyHistoryCount()==2 && db.AllMoveHistoryCount()==0 &&
                File.Exists(f.Candidate.Source) && !File.Exists(f.Candidate.Destination),
                "R25 reviewed third-family boundary leaked native Move execution authority.");
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            c.Open();using var sql=c.CreateCommand();
            sql.CommandText="SELECT state,review_digest FROM m6j_review_proposals WHERE id=$id";
            sql.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            using var rows=sql.ExecuteReader();
            Ensure(rows.Read() && rows.GetString(0)=="Reviewed" &&
                rows.GetString(1)==receipt.ReviewedDigest && !rows.Read(),
                "R25 durable operation-specific reviewed seal missing.");
        });
        Check("M6J_P4R25_COLD_SQLITE_REOPEN_PRESERVES_SEAL_WITHOUT_INTENT",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var scope=f.Scope;
            var receipt=f.Db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true);
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
            Ensure(cold.AllCopyHistoryCount()==2 && cold.AllMoveHistoryCount()==0 &&
                File.Exists(f.Candidate.Source) && !File.Exists(f.Candidate.Destination) &&
                !cold.InspectM6JProspectiveReview(receipt.ReviewId).AuthorizesNativeOperation,
                "R25 reviewed-only SQLite seal unexpectedly granted native Move after cold restart.");
        });
        Check("M6J_P4R25_OWNER_ACKNOWLEDGEMENT_IS_REQUIRED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,false));
            Ensure(db.AllMoveHistoryCount()==0 && !db.InspectM6JProspectiveReview(f.Candidate.Id).AuthorizesNativeOperation,
                "R25 unacknowledged Move review bypassed owner consent.");
        });
        Check("M6J_P4R25_THIRD_SOURCE_TAMPER_REFUSED_BEFORE_REVIEW",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.Candidate.Source,"r25-foreign-source-change");
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true));
            Ensure(db.AllMoveHistoryCount()==0 && db.AllCopyHistoryCount()==2,
                "R25 changed original was approved for third family.");
        });
        Check("M6J_P4R25_PREDECESSOR_OUTPUT_REPLACEMENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            File.Move(f.A.Destination,f.A.Destination+".r25-saved");
            File.Copy(f.A.Destination+".r25-saved",f.A.Destination,false);
            Ensure(NativeFileCapability.Identity(f.A.Destination)!=f.A.TargetIdentity,
                "R25 tamper fixture did not replace the native predecessor identity.");
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true));
        });
        Check("M6J_P4R25_CROSS_FAMILY_HISTORY_ROW_BLOCKS_REVIEW",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            using(var sql=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                sql.Open();using var cmd=sql.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.Candidate.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R25 foreign journal fixture insert failed.");
            }
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true));
            Ensure(db.AllMoveHistoryCount()==0 && db.AllRenameHistoryCount()==1,
                "R25 third-family authorization silently ignored unrelated journal history.");
        });
        Check("M6J_P4R25_REPEATED_REVIEW_CANNOT_REISSUE_OPEN_SEAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            var originalReceipt=db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true);
            // An existing Reviewed seal is rejected by the durable history gate
            // with IOException BEFORE a duplicate SQLite insert is attempted.
            // Require both the exact original persisted seal and no Move Intent.
            string ReadExistingSeal()
            {
                using var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                    DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="SELECT id,state,review_digest,candidate_json FROM m6j_review_proposals";
                using var row=cmd.ExecuteReader();
                Ensure(row.Read(),"R25 approved seal unexpectedly disappeared.");
                string snapshot=row.GetString(0)+"|"+row.GetString(1)+"|"+
                    row.GetString(2)+"|"+row.GetString(3);
                Ensure(!row.Read(),"R25 duplicate operation seal row appeared.");
                return snapshot;
            }
            string beforeReplay=ReadExistingSeal();
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,f.Candidate,true));
            Ensure(beforeReplay==ReadExistingSeal() &&
                originalReceipt.ReviewId==f.Candidate.Id &&
                originalReceipt.ReviewedDigest.Length==64 &&
                db.AllCopyHistoryCount()==2 && db.AllMoveHistoryCount()==0 &&
                File.Exists(f.Candidate.Source) && !File.Exists(f.Candidate.Destination),
                "R25 replay changed reviewed seal, created Move Intent or modified native files.");
        });
        Check("M6J_P4R25_MISMATCHED_APPROVED_PLAN_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R25ThirdReviewFixture();using var db=f.Db;using var scope=f.Scope;
            var bad=f.Candidate with {Destination=f.Candidate.Destination+".foreign"};
            Refuse<IOException>(()=>db.ApproveM6JP4R25ThirdGeneratedMoveReview(f.Work,f.Prepared,bad,true));
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(f.Candidate.Source),
                "R25 different reviewed destination was silently approved.");
        });

        // P4-R26: full third generated native Move after TWO separately
        // approved committed Copy predecessors. Own one operation-specific
        // R25 review; preserve all generic Move and ordinary-photo vetoes.
        (string Root,WorkspaceSessionStore Db,P4R13FourthFamilyFixtureScope Scope,
            SortingWorkspace Work,PreparedCopy Prepared,M6JProspectiveCandidate Candidate,
            DurableCopyItem A,DurableCopyItem B,
            M6JP4R25ThirdMoveReviewReceipt Review,DurableMoveItem Ticket) R26Third()
        {
            var f=R25ThirdReviewFixture();
            try
            {
                var reviewed=f.Db.ApproveM6JP4R25ThirdGeneratedMoveReview(
                    f.Work,f.Prepared,f.Candidate,true);
                var p=f.Prepared;var c=f.Candidate;
                var install=f.Db.MoveInstallationId();
                var recovery=Path.Combine(Path.GetDirectoryName(p.Source)!,
                    ".photosift-reborn-recovery",install.ToString("N"),
                    p.SessionId.ToString("N"),c.Id.ToString("N"));
                var ticket=new DurableMoveItem(c.Id,install,p.SessionId,p.PhotoId,p.PlanId,
                    p.Revision,DateTime.UtcNow.Ticks,p.Source,p.Destination,
                    Path.Combine(Path.GetDirectoryName(p.Destination)!,
                        ".photosift-move-"+c.Id.ToString("N")+".part"),
                    Path.Combine(recovery,Path.GetFileName(p.Source)),
                    Path.Combine(recovery,"installed-"+Path.GetFileName(p.Destination)),
                    p.OutputRoot,p.Hash,p.Identity,p.Length,p.WriteTicks,
                    null,null,"Intent",null,p.Key);
                return(f.Root,f.Db,f.Scope,f.Work,p,c,f.A,f.B,reviewed,ticket);
            }
            catch {f.Db.Dispose();f.Scope.Dispose();throw;}
        }
        Check("M6J_P4R26_TWO_COPIES_THIRD_MOVE_ACTUALLY_COMMITTED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var move=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            Ensure(move.State=="Committed" && db.AllCopyHistoryCount()==2 &&
                db.AllMoveHistoryCount()==1 && db.MoveItems(f.Candidate.SessionId).Single()==move &&
                db.CopyItems(f.Candidate.SessionId).Count==2 &&
                db.CopyItems(f.Candidate.SessionId).All(c=>c.State=="Committed") &&
                File.Exists(move.OriginalArchive) && File.Exists(move.Destination) &&
                !File.Exists(move.Source) && !File.Exists(move.Temp) &&
                NativeFileCapability.Identity(move.OriginalArchive)==move.SourceIdentity &&
                NativeFileCapability.Identity(move.Destination)==move.TargetIdentity &&
                SortingWorkspace.ComputeSha256Async(move.Destination).GetAwaiter().GetResult()==move.SourceHash &&
                SortingWorkspace.ComputeSha256Async(move.OriginalArchive).GetAwaiter().GetResult()==move.SourceHash,
                "R26 third generated native Move did not fully commit with two intact Copies.");
            using var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            conn.Open();using var cmd=conn.CreateCommand();
            cmd.CommandText="SELECT state FROM m6j_review_proposals WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Ensure((cmd.ExecuteScalar() as string)=="Consumed",
                "R26 actual native Move did not consume its own R25 reviewed seal.");
        });
        Check("M6J_P4R26_COLD_RESTART_PRESERVES_TERMINAL_JOURNALS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var scope=f.Scope;
            var move=new DurableMoveCoordinator(f.Db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
            Ensure(cold.CopyItems(f.Candidate.SessionId).Count==2 &&
                cold.MoveItems(f.Candidate.SessionId).Single().State=="Committed" &&
                File.Exists(move.OriginalArchive) && File.Exists(move.Destination) &&
                !File.Exists(move.Source),
                "R26 cold SQLite reopening lost durable third generated Move.");
        });
        Check("M6J_P4R26_REVIEW_CONSUMPTION_AND_INTENT_ATOMIC_ROLLBACK",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            Refuse<InvalidOperationException>(()=>db.RecordM6JP4R26ThirdMoveIntent(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket,
                ()=>throw new InvalidOperationException("r26-synthetic-before-intent-cut")));
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(f.Candidate.Source) &&
                !File.Exists(f.Candidate.Destination),"R26 atomic cut leaked Move intent or filesystem effect.");
            using var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            conn.Open();using var cmd=conn.CreateCommand();
            cmd.CommandText="SELECT state FROM m6j_review_proposals WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",f.Candidate.Id.ToString("N"));
            Ensure((cmd.ExecuteScalar() as string)=="Reviewed",
                "R26 exception between consume/insert failed to roll back review consumption.");
        });
        Check("M6J_P4R26_REVIEWED_NATIVE_SOURCE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            File.AppendAllText(f.Candidate.Source,"r26-generated-third-source-tamper");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 && !File.Exists(f.Candidate.Destination),
                "R26 source tamper was not stopped before durable Intent.");
        });
        Check("M6J_P4R26_PREDECESSOR_OUTPUT_IDENTITY_REPLACEMENT_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            File.Move(f.A.Destination,f.A.Destination+".r26-foreign-original");
            File.Copy(f.A.Destination+".r26-foreign-original",f.A.Destination,false);
            Ensure(NativeFileCapability.Identity(f.A.Destination)!=f.A.TargetIdentity,
                "R26 adversarial predecessor identity was not changed.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0,"R26 foreign predecessor consumed third Move authority.");
        });
        Check("M6J_P4R26_FOREIGN_FAMILY_HISTORY_REFUSES_THIRD_MOVE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",f.Candidate.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R26 hostile foreign Rename injection did not persist.");
            }
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0,"R26 ignored an unrelated family history row.");
        });
        Check("M6J_P4R26_WRONG_REVIEW_OR_OPERATION_ID_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var wrong=f.Ticket with {Id=Guid.NewGuid()};
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,wrong)
                    .GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(f.Candidate.Source),
                "R26 counterfeit Move ID crossed the sealed review boundary.");
        });
        Check("M6J_P4R26_NATIVE_INTENT_CUT_NO_AUTOMATIC_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var executor=new DurableMoveCoordinator(db,stage=>{
                if(stage=="MOVE_INTENT_RECORDED")throw new InvalidOperationException("r26-exact-intent-cut");
            });
            string observedCut="";
            try
            {
                executor.ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                    f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            }
            catch(InvalidOperationException ex)
            {
                observedCut=ex.Message;
            }
            Ensure(observedCut=="r26-exact-intent-cut",
                "R26 injected cut was not reached at the required durable boundary.");
            Ensure(db.MoveItems(f.Candidate.SessionId).Single().State=="Intent" &&
                File.Exists(f.Candidate.Source) && !File.Exists(f.Candidate.Destination),
                "R26 durable Intent cut was unexpectedly replayed or files were moved.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
        });
        Check("M6J_P4R26_PUBLISHED_OUTPUT_CUT_NO_AUTOMATIC_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var executor=new DurableMoveCoordinator(db,stage=>{
                if(stage=="MOVE_OUTPUT_PUBLISHED_UNJOURNALLED")
                    throw new InvalidOperationException("r26-exact-publish-cut");
            });
            string observedCut="";
            try
            {
                executor.ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                    f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            }
            catch(InvalidOperationException ex)
            {
                observedCut=ex.Message;
            }
            Ensure(observedCut=="r26-exact-publish-cut",
                "R26 injected cut was not reached at the required durable boundary.");
            Ensure(db.MoveItems(f.Candidate.SessionId).Single().State=="TempReady" &&
                File.Exists(f.Candidate.Source) && File.Exists(f.Candidate.Destination),
                "R26 native publish interruption was not preserved as unresolved.");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
        });
        Check("M6J_P4R26_FOREIGN_TEMP_BLOCKS_REVIEW_CONSUMPTION",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            File.WriteAllText(f.Ticket.Temp,"r26-unowned-foreign-temporary-file");
            Refuse<IOException>(()=>new DurableMoveCoordinator(db).
                ExecuteM6JP4R26ThirdGeneratedMoveAsync(f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket)
                    .GetAwaiter().GetResult());
            Ensure(db.AllMoveHistoryCount()==0 &&
                File.ReadAllText(f.Ticket.Temp)=="r26-unowned-foreign-temporary-file",
                "R26 foreign Temp occupant was erased or received Move Intent.");
        });

        // P4-R27: exact third Move C Undo in two independent committed Copy history.
        // Unlike the old single-Copy B Undo, it preserves BOTH separately
        // reviewed Copy rows and the consumed R25 operation-specific review.
        Check("M6J_P4R27_TWO_COPIES_THIRD_MOVE_NATIVE_UNDO_TERMINAL",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var committed=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var originals=db.CopyItems(f.Candidate.SessionId).ToArray();
            var undone=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(
                db,f.Candidate.SessionId);
            Ensure(undone.State=="Undone" && db.MoveItems(undone.SessionId).Single()==undone &&
                db.CopyItems(undone.SessionId).SequenceEqual(originals) &&
                File.Exists(undone.Source) && !File.Exists(undone.OriginalArchive) &&
                !File.Exists(undone.Destination) && !File.Exists(undone.Temp) &&
                File.Exists(undone.OutputArchive) &&
                NativeFileCapability.Identity(undone.Source)==committed.SourceIdentity &&
                NativeFileCapability.Identity(undone.OutputArchive)==committed.TargetIdentity &&
                originals.All(x=>File.Exists(x.Source) && File.Exists(x.Destination)),
                "R27 exact third Move Undo failed to preserve both Copy predecessors.");
        });
        Check("M6J_P4R27_COLD_SQLITE_REOPEN_PRESERVES_TERMINAL_THIRD_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var scope=f.Scope;
            var c=new DurableMoveCoordinator(f.Db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var terminal=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(
                f.Db,c.SessionId);
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
            cold.RequireM6JP4R27ThirdMoveUndoStage(terminal,"AfterOutputArchive");
            Ensure(cold.MoveItems(c.SessionId).Single()==terminal &&
                cold.CopyItems(c.SessionId).Count==2 &&
                cold.CopyItems(c.SessionId).All(x=>x.State=="Committed"),
                "R27 cold restart lost final Undo state or independent Copies.");
        });
        Check("M6J_P4R27_REVIEWED_ONLY_MOVE_HAS_NO_UNDO_AUTHORITY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.
                UndoExactlyReviewedGeneratedThirdMoveC(db,f.Candidate.SessionId));
            Ensure(db.AllMoveHistoryCount()==0 && File.Exists(f.Candidate.Source),
                "R27 precommitted or unexecuted Move was undone.");
        });
        Check("M6J_P4R27_INSTALLED_OUTPUT_TAMPER_REFUSES_BEFORE_UNDO_INTENT",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            File.AppendAllText(c.Destination,"r27-foreign-target-change");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(db.MoveItems(c.SessionId).Single()==c && File.Exists(c.OriginalArchive),
                "R27 changed installed output received Undo Intent.");
        });
        Check("M6J_P4R27_SOURCE_ARCHIVE_TAMPER_REFUSES_BEFORE_UNDO_INTENT",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            File.AppendAllText(c.OriginalArchive,"r27-foreign-archive-change");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(db.MoveItems(c.SessionId).Single()==c && File.Exists(c.Destination),
                "R27 changed original archive was relocated.");
        });
        Check("M6J_P4R27_FOREIGN_SOURCE_OCCUPANT_NOT_OVERWRITTEN",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            File.WriteAllText(c.Source,"r27-foreign-source-occupant");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(File.ReadAllText(c.Source)=="r27-foreign-source-occupant" &&
                db.MoveItems(c.SessionId).Single()==c,"R27 replaced an unowned source occupant.");
        });
        Check("M6J_P4R27_FOREIGN_OUTPUT_ARCHIVE_NOT_OVERWRITTEN",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            File.WriteAllText(c.OutputArchive,"r27-foreign-output-archive");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(File.ReadAllText(c.OutputArchive)=="r27-foreign-output-archive" &&
                db.MoveItems(c.SessionId).Single()==c,"R27 overwrote an unowned recovery output.");
        });
        Check("M6J_P4R27_CHANGED_COPY_PREDECESSOR_BLOCKS_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            File.Move(f.A.Destination,f.A.Destination+".r27-foreign");
            File.Copy(f.A.Destination+".r27-foreign",f.A.Destination,false);
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(db.MoveItems(c.SessionId).Single()==c && File.Exists(c.OriginalArchive),
                "R27 silently ignored changed Copy A native identity.");
        });
        Check("M6J_P4R27_FOREIGN_RENAME_ROW_REFUSES_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,98,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",c.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R27 hostile Rename injection did not persist.");
            }
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(db.MoveItems(c.SessionId).Single()==c,"R27 foreign Rename history authorized Undo.");
        });
        Check("M6J_P4R27_DURABLE_RESTORE_INTENT_CUT_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            string observed="";
            try {M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId,
                stage=>{if(stage=="P4R27_C_RESTORE_INTENT_DURABLE")throw new InvalidOperationException("r27-exact-restore-intent");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r27-exact-restore-intent" &&
                db.MoveItems(c.SessionId).Single().State=="UndoRestoreIntent" &&
                File.Exists(c.OriginalArchive) && !File.Exists(c.Source),
                "R27 intended durable cut did not leave an unresolved Undo intent.");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R27_SOURCE_RELOCATED_CUT_NO_AUTOMATIC_CONTINUATION",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            string observed="";
            try {M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId,
                stage=>{if(stage=="P4R27_C_SOURCE_RELOCATED_UNJOURNALLED")throw new InvalidOperationException("r27-exact-source-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r27-exact-source-cut" &&
                db.MoveItems(c.SessionId).Single().State=="UndoRestoreIntent" &&
                File.Exists(c.Source) && !File.Exists(c.OriginalArchive),
                "R27 physical source cut was not preserved as unresolved.");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R27_OUTPUT_ARCHIVE_INTENT_CUT_NO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            string observed="";
            try {M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId,
                stage=>{if(stage=="P4R27_C_OUTPUT_ARCHIVE_INTENT_DURABLE")throw new InvalidOperationException("r27-exact-output-intent");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r27-exact-output-intent" &&
                db.MoveItems(c.SessionId).Single().State=="UndoOutputArchiveIntent" &&
                File.Exists(c.Source) && File.Exists(c.Destination),
                "R27 durable output archive cut was not left unresolved.");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R27_OUTPUT_RELOCATED_CUT_NO_AUTOMATIC_CONTINUATION",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            string observed="";
            try {M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId,
                stage=>{if(stage=="P4R27_C_OUTPUT_RELOCATED_UNJOURNALLED")throw new InvalidOperationException("r27-exact-output-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r27-exact-output-cut" &&
                db.MoveItems(c.SessionId).Single().State=="UndoOutputArchiveIntent" &&
                File.Exists(c.OutputArchive) && !File.Exists(c.Destination),
                "R27 installed output relocation cut was not left unresolved.");
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R27_REPEATED_UNDO_REQUIRES_NEW_AUTHORITY_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var undone=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            Refuse<IOException>(()=>M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId));
            Ensure(db.MoveItems(c.SessionId).Single()==undone &&
                db.CopyItems(c.SessionId).Count==2,
                "R27 repeated Undo duplicated authority or altered history.");
        });

        // P4-R28: second independently reviewed Copy B Undo AFTER durable C Undo.
        // Native files, consumed R25 seal and original first Copy A stay unchanged.
        Check("M6J_P4R28_TWO_COPIES_THIRD_MOVE_THEN_COPY_B_NATIVE_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var terminalC=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            var a=db.CopyItems(c.SessionId)[0];var b=db.CopyItems(c.SessionId)[1];
            var undone=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            var rows=db.CopyItems(c.SessionId);
            Ensure(undone.State=="Undone" && rows.Count==2 && rows[0]==a && rows[1]==undone &&
                db.MoveItems(c.SessionId).Single()==terminalC &&
                File.Exists(a.Destination) && File.Exists(b.Source) &&
                !File.Exists(b.Destination) && File.Exists(b.Recovery) &&
                NativeFileCapability.Identity(b.Recovery)==b.TargetIdentity &&
                File.Exists(terminalC.Source) && File.Exists(terminalC.OutputArchive),
                "R28 second Copy native Undo did not protect Copy A/Move C.");
        });
        Check("M6J_P4R28_COLD_RESTART_PRESERVES_B_UNDONE_A_AND_C",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var scope=f.Scope;
            var c=new DurableMoveCoordinator(f.Db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var terminalC=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(f.Db,c.SessionId);
            var b=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(f.Db,c.SessionId);
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
            cold.RequireM6JP4R28SecondCopyUndoStage(b,"AfterArchive");
            Ensure(cold.CopyItems(c.SessionId).Count==2 &&
                cold.CopyItems(c.SessionId)[0].State=="Committed" &&
                cold.CopyItems(c.SessionId)[1]==b &&
                cold.MoveItems(c.SessionId).Single()==terminalC,
                "R28 cold restart lost native independent Copy lineage.");
        });
        Check("M6J_P4R28_COMMITTED_THIRD_MOVE_BLOCKS_PREMATURE_B_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId).All(x=>x.State=="Committed") &&
                db.MoveItems(c.SessionId).Single()==c,"R28 B Undo ignored unresolved Move C.");
        });
        Check("M6J_P4R28_SECOND_COPY_INSTALLED_OUTPUT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            File.AppendAllText(f.B.Destination,"r28-foreign-installed-output");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed" && File.Exists(f.B.Destination),
                "R28 tampered Copy B output was removed.");
        });
        Check("M6J_P4R28_FIRST_COPY_NATIVE_REPLACEMENT_REFUSES_B_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            File.Move(f.A.Destination,f.A.Destination+".r28-foreign");
            File.Copy(f.A.Destination+".r28-foreign",f.A.Destination,false);
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed" &&
                File.Exists(f.B.Destination),"R28 ignored foreign Copy A identity.");
        });
        Check("M6J_P4R28_TERMINAL_MOVE_OUTPUT_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var undone=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            File.AppendAllText(undone.OutputArchive,"r28-foreign-move-archive");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed" &&
                File.Exists(f.B.Destination),"R28 ignored changed Move C archive.");
        });
        Check("M6J_P4R28_FOREIGN_RECOVERY_OCCUPANT_NOT_OVERWRITTEN",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(f.B.Recovery)!);
            File.WriteAllText(f.B.Recovery,"r28-owned-by-foreign");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(File.ReadAllText(f.B.Recovery)=="r28-owned-by-foreign" &&
                db.CopyItems(c.SessionId)[1].State=="Committed", "R28 overwrote foreign recovery.");
        });
        Check("M6J_P4R28_FOREIGN_RENAME_HISTORY_REFUSES_B_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",c.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R28 foreign Rename row not inserted.");
            }
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed", "R28 ignored foreign Rename.");
        });
        Check("M6J_P4R28_CONSUMED_COPY_SUCCESSOR_GRANT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="UPDATE copy_successor_grants SET used_intent=$id";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R28 grant mutation did not persist.");
            }
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed", "R28 ignored changed one-use grant.");
        });
        Check("M6J_P4R28_DURABLE_B_UNDO_INTENT_CUT_NO_AUTO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            string observed="";
            try {M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId,
                cut=>{if(cut=="P4R28_B_UNDO_INTENT_DURABLE")throw new InvalidOperationException("r28-exact-intent-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r28-exact-intent-cut" && db.CopyItems(c.SessionId)[1].State=="UndoIntent" &&
                File.Exists(f.B.Destination) && !File.Exists(f.B.Recovery),
                "R28 intentional durable Undo cut missing.");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R28_B_OUTPUT_RELOCATION_CUT_NO_AUTO_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            string observed="";
            try {M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId,
                cut=>{if(cut=="P4R28_B_OUTPUT_RELOCATED_UNJOURNALLED")throw new InvalidOperationException("r28-exact-move-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r28-exact-move-cut" && db.CopyItems(c.SessionId)[1].State=="UndoIntent" &&
                !File.Exists(f.B.Destination) && File.Exists(f.B.Recovery),
                "R28 native output was not preserved as unresolved after cut.");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
        });
        Check("M6J_P4R28_B_SOURCE_TAMPER_BLOCKS_SELECTIVE_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            File.AppendAllText(f.B.Source,"r28-foreign-source-append");
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1].State=="Committed", "R28 ignored Copy B source tamper.");
        });
        Check("M6J_P4R28_REPEATED_SECOND_COPY_UNDO_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var terminalC=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            var terminalB=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            Refuse<IOException>(()=>M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[1]==terminalB && db.MoveItems(c.SessionId).Single()==terminalC,
                "R28 duplicate Undo altered terminal journal or Move C.");
        });

        // R29: independently approved two-Copy, third-Move history fully reversed.
        // These tests use generated fixtures ONLY. General mixed operation authority remains locked.
        Check("M6J_P4R29_TWO_COPIES_THIRD_MOVE_FULL_REVERSE_NATIVE",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var tc=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            var tb=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            var ta=M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId);
            Ensure(ta.State=="Undone" && db.CopyItems(c.SessionId).Count==2 &&
                db.CopyItems(c.SessionId)[0]==ta && db.CopyItems(c.SessionId)[1]==tb &&
                db.MoveItems(c.SessionId).Single()==tc &&
                !File.Exists(f.A.Destination) && File.Exists(f.A.Recovery) &&
                NativeFileCapability.Identity(f.A.Recovery)==f.A.TargetIdentity &&
                !File.Exists(f.B.Destination) && File.Exists(f.B.Recovery) &&
                File.Exists(tc.Source) && File.Exists(tc.OutputArchive),
                "R29 complete reverse changed terminal operation ownership.");
        });
        Check("M6J_P4R29_COLD_RESTART_THREE_TERMINAL_JOURNALS",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var scope=f.Scope;
            var c=new DurableMoveCoordinator(f.Db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var tc=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(f.Db,c.SessionId);
            var tb=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(f.Db,c.SessionId);
            var ta=M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(f.Db,c.SessionId);
            f.Db.Dispose();using var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
            cold.RequireM6JP4R29FirstCopyUndoStage(ta,"AfterArchive");
            Ensure(cold.CopyItems(c.SessionId)[0]==ta && cold.CopyItems(c.SessionId)[1]==tb &&
                cold.MoveItems(c.SessionId).Single()==tc, "R29 cold restart lost terminal journals.");
        });
        Check("M6J_P4R29_COMMITTED_SECOND_COPY_PREMATURE_A_UNDO_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0].State=="Committed" && File.Exists(f.A.Destination),
                "R29 ignored committed Copy B dependency.");
        });
        Check("M6J_P4R29_COMMITTED_MOVE_PREMATURE_A_UNDO_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0].State=="Committed","R29 ignored nonterminal C.");
        });
        Check("M6J_P4R29_FIRST_COPY_INSTALLED_OUTPUT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            File.AppendAllText(f.A.Destination,"r29-foreign-output");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0].State=="Committed" && File.Exists(f.A.Destination),
                "R29 touched changed first Copy output.");
        });
        Check("M6J_P4R29_FIRST_COPY_ORIGINAL_SOURCE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            File.AppendAllText(f.A.Source,"r29-untrusted-original");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(File.Exists(f.A.Destination),"R29 removed first Copy after source tamper.");
        });
        Check("M6J_P4R29_SECOND_COPY_TERMINAL_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            File.AppendAllText(f.B.Recovery,"r29-foreign-b-archive");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(File.Exists(f.A.Destination),"R29 ignored B terminal archive mutation.");
        });
        Check("M6J_P4R29_THIRD_MOVE_TERMINAL_OUTPUT_ARCHIVE_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            var tc=M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            File.AppendAllText(tc.OutputArchive,"r29-foreign-c-output-archive");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(File.Exists(f.A.Destination),"R29 ignored terminal C archive mutation.");
        });
        Check("M6J_P4R29_FOREIGN_FIRST_COPY_RECOVERY_NOT_OVERWRITTEN",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(f.A.Recovery)!);
            File.WriteAllText(f.A.Recovery,"r29-foreign-recovery-occupant");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(File.ReadAllText(f.A.Recovery)=="r29-foreign-recovery-occupant" &&
                db.CopyItems(c.SessionId)[0].State=="Committed", "R29 overwrote unknown recovery.");
        });
        Check("M6J_P4R29_FOREIGN_RENAME_ROW_REFUSES_A_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$s,99,'{}')";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$s",c.SessionId.ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R29 foreign Rename insertion missing.");
            }
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0].State=="Committed","R29 ignored injected Rename.");
        });
        Check("M6J_P4R29_CONSUMED_SUCCESSOR_GRANT_TAMPER_REFUSED",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="UPDATE copy_successor_grants SET used_intent=$id";
                cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
                Ensure(cmd.ExecuteNonQuery()==1,"R29 grant mutation did not persist.");
            }
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0].State=="Committed","R29 ignored stale grant.");
        });
        Check("M6J_P4R29_DURABLE_A_UNDO_INTENT_CUT_NO_AUTOMATIC_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            string observed="";
            try{M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId,
                cut=>{if(cut=="P4R29_A_UNDO_INTENT_DURABLE")throw new InvalidOperationException("r29-exact-intent-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r29-exact-intent-cut" && db.CopyItems(c.SessionId)[0].State=="UndoIntent" &&
                File.Exists(f.A.Destination) && !File.Exists(f.A.Recovery),"R29 cut was not the exact durable Intent.");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
        });
        Check("M6J_P4R29_A_OUTPUT_RELOCATION_CUT_NO_AUTOMATIC_REPLAY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            string observed="";
            try{M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId,
                cut=>{if(cut=="P4R29_A_OUTPUT_RELOCATED_UNJOURNALLED")throw new InvalidOperationException("r29-exact-move-cut");});}
            catch(InvalidOperationException ex){observed=ex.Message;}
            Ensure(observed=="r29-exact-move-cut" && db.CopyItems(c.SessionId)[0].State=="UndoIntent" &&
                !File.Exists(f.A.Destination) && File.Exists(f.A.Recovery),"R29 relocation cut did not occur.");
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
        });
        Check("M6J_P4R29_REPEATED_A_UNDO_REQUIRES_NEW_AUTHORITY",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            var tb=M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            var ta=M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId);
            Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(db.CopyItems(c.SessionId)[0]==ta && db.CopyItems(c.SessionId)[1]==tb,
                "R29 attempted repeated A Undo mutated terminal Copy journals.");
        });
        Check("M6J_P4R29_EXTRA_COPY_ROW_REFUSES_TERMINAL_A_UNDO",() =>
        {
            if(!OperatingSystem.IsWindows())return;
            var f=R26Third();using var db=f.Db;using var scope=f.Scope;
            var c=new DurableMoveCoordinator(db).ExecuteM6JP4R26ThirdGeneratedMoveAsync(
                f.Work,f.Prepared,f.Candidate,f.Review,f.Ticket).GetAwaiter().GetResult();
            M6JP4R27GeneratedThirdMoveUndo.UndoExactlyReviewedGeneratedThirdMoveC(db,c.SessionId);
            M6JP4R28GeneratedSecondCopyUndo.UndoExactlySecondCopyBAfterThirdMoveC(db,c.SessionId);
            // A VALID additional Copy row is required. The old negative fixture
            // wrote '{}', so journal parsing failed before the count/ownership
            // veto was actually exercised. This row has coherent DB/JSON identity.
            var original=db.CopyItems(c.SessionId)[0];
            var extra=original with {Id=Guid.NewGuid(),Sequence=99};
            using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
            {
                conn.Open();using var cmd=conn.CreateCommand();
                cmd.CommandText="INSERT INTO copy_items(id,session,sequence,json) VALUES($id,$s,99,$json)";
                cmd.Parameters.AddWithValue("$id",extra.Id.ToString("N"));
                cmd.Parameters.AddWithValue("$s",extra.SessionId.ToString("N"));
                cmd.Parameters.AddWithValue("$json",System.Text.Json.JsonSerializer.Serialize(extra));
                Ensure(cmd.ExecuteNonQuery()==1,"R29 additional valid Copy insertion missing.");
            }
            var refusal=Refuse<IOException>(()=>M6JP4R29GeneratedFirstCopyUndo.UndoExactlyFirstCopyAAfterBAndC(db,c.SessionId));
            Ensure(refusal.Message=="R29 exact terminal Copy B and committed Copy A required.",
                "R29 extra Copy row did not reach exact three-operation cardinality veto.");
            Ensure(File.Exists(f.A.Destination) && !File.Exists(f.A.Recovery),
                "R29 accepted unsolicited extra Copy history.");
        });
        RunR30Tests();
        RunR30R3Tests();
        RunR30R2Tests();
        Console.WriteLine($"P2_TESTS_PASSED={_passed}");
        Console.WriteLine($"P2_TESTS_FAILED={_failed}");
        Console.WriteLine($"P2_ENGINE_TEST_VERDICT={(_failed==0 ? "PASS_BOUNDED_NATIVE_JOURNAL_TESTS" : "FAIL")}");
        Console.WriteLine("P2_PLATFORM_OWNER_EVIDENCE=SEE_ACTUAL_WINDOWS_AND_UBUNTU_RUN_LOGS");
        return _failed==0?0:1;
    }
}
