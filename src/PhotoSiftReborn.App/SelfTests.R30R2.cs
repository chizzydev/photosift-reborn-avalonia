using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Reflection;
using System.Text.Json;

namespace PhotoSiftReborn;

internal static partial class SelfTests
{
    // Explicit inventory: every public/internal legacy writer, including wrappers
    // and journal-only repair APIs. Reflection invokes the REAL boundary before
    // argument validation; this is not a replacement filesystem implementation.
    private static readonly string[] R30R2LegacyWriters =
    [
        "Save", "SetCopyBarrier", "PutCopyItem", "PutMoveItem", "PutRenameItem", "PutRejectItem",
        "StageAdditionalEpochAuthorization", "ApproveAdditionalEpochAuthorization",
        "ApproveCommittedCopySuccessor", "PutCopySuccessorIntent",
        "RecordM6JGeneratedOnlyCopyMoveIntent", "FinalizeM6JGeneratedOnlyCompletedMoveJournal",
        "FinalizeM6JGeneratedOnlyCompletedUndoJournal", "PutM6JP4R12GeneratedReject",
        "PutM6JP4R16GeneratedRejectUndo", "PutM6JP4R17GeneratedRenameUndoAfterReject",
        "PutM6JP4R18GeneratedBUndo", "PutM6JP4R19GeneratedAUndo", "PutM6JP4R23GeneratedAUndo",
        "ApproveM6JP4R25ThirdGeneratedMoveReview", "RecordM6JP4R26ThirdMoveIntent",
        "PutM6JP4R26ThirdMoveState", "PutM6JP4R27ThirdMoveUndo", "PutM6JP4R28SecondCopyUndo",
        "PutM6JP4R29FirstCopyUndo", "StageM6JProspectiveReview", "ReviewM6JProspectiveCandidate",
        "CancelM6JProspectiveReview", "SealTerminalMoveHistory", "ApproveMoveEpochGrant",
        "SealMoveEpochLedger", "StageNextMoveEpochProposal", "BeginFirstGuiSuccessor",
        "SealRejectTerminalHistory", "SealOrdinaryRejectTerminalHistory", "GrantNextRejectStage",
        "BeginSecondOrdinaryRejectGuiSession", "BeginSecondRejectGuiSession", "SealFullyUndoneRename",
        "BeginThirdGuiSession"
    ];

    internal static int RunR30R2Development()
    {
        RunR30R2Tests(defectProofOnly:true);
        Console.WriteLine($"P2_R30_R2_TARGETED_PASSED={_passed}");
        Console.WriteLine($"P2_R30_R2_TARGETED_FAILED={_failed}");
        return _failed==0?0:1;
    }

    private static string R30R2DatabaseImage(WorkspaceSessionStore db)
    {
        using var bytes=new FileStream(db.DatabasePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static void RunR30R2Tests(bool defectProofOnly=false)
    {
        foreach(string shape in new[]{"ENROLLED", "MISSING_ANCHOR", "ORPHAN_TABLES"})
        foreach(string writer in R30R2LegacyWriters.Where(x=>!defectProofOnly || x is "Save" or "SetCopyBarrier"))
        {
            Check("R30_R2_LEGACY_"+writer.ToUpperInvariant()+"_"+shape+"_BEFORE_AND_AFTER_REOPEN",()=>
            {
                using var f=new R30TestFixture(4);f.Apply("Copy");
                if(shape!="ENROLLED")f.Sql("DROP TABLE r30_meta");
                if(shape=="ORPHAN_TABLES")
                {
                    File.Delete(Path.Combine(f.Root,"R30_GENERATED_ONLY.marker"));
                    f.Sql("PRAGMA user_version=19");
                }
                for(int reopen=0;reopen<2;reopen++)
                {
                    if(reopen==1)
                    {
                        f.Db.Dispose();f.Db=new WorkspaceSessionStore(Path.Combine(f.Root,"state"));
                        // A damaged anchor permits inspection, never an engine.
                    }
                    string sql=R30R2DatabaseImage(f.Db),files=f.Files();bool risk=f.Db.HasUnreconciledCopy;
                    var method=typeof(WorkspaceSessionStore).GetMethod(writer,
                        BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        ?? throw new Exception("Missing inventoried writer: "+writer);
                    var snapshot=f.Work.CaptureSession() with {Revision=f.Work.Revision+100};
                    object?[] args=method.GetParameters().Select(p=>p.ParameterType==typeof(WorkspaceSessionSnapshot)
                        ?(object)snapshot:p.ParameterType==typeof(SortingWorkspace)?f.Work:
                        p.ParameterType==typeof(bool)?true:p.ParameterType.IsValueType?
                        Activator.CreateInstance(p.ParameterType):null).ToArray();
                    Exception? refusal=null;
                    try {method.Invoke(f.Db,args);}
                    catch(TargetInvocationException ex){refusal=ex.InnerException;}
                    // Check the full database bytes (checkpoint/risk/auxiliaries,
                    // schema/reviews/audit too), native IDs and bytes, AND memory.
                    Ensure(sql==R30R2DatabaseImage(f.Db),writer+" altered SQLite bytes");
                    Ensure(files==f.Files(),writer+" altered native files/identities");
                    Ensure(risk==f.Db.HasUnreconciledCopy,writer+" altered in-memory copy risk");
                    Ensure(refusal is IOException && refusal.Message==
                        "R30 enrolled history requires exact operation-scoped authority.",
                        writer+" did not refuse at the R30 boundary: "+(refusal?.Message??"returned normally"));
                }
            });
        }
        Check("R30_R2_V19_CHECKPOINT_AND_COPY_BARRIER_SEMANTICS_UNCHANGED",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P1_DISPOSABLE_"+Guid.NewGuid().ToString("N"));
            using var db=new WorkspaceSessionStore(Path.Combine(root,"state"));
            var work=new SortingWorkspace();var first=work.CaptureSession();
            db.Save(first);db.SetCopyBarrier(true,first with {Revision=first.Revision+1});
            Ensure(db.HasUnreconciledCopy && db.Load()!.Revision==first.Revision+1,"v19 save/barrier changed");
            db.SetCopyBarrier(false,first);Ensure(!db.HasUnreconciledCopy,"v19 barrier reset changed");
        });
        Check("R30_R2_COLD_R30_CONNECTION_PRESERVES_EXTRA_SYNC_AND_SAFETY",()=>
        {
            using var f=new R30TestFixture(4);f.Apply("Copy");f.Reopen();
            var connection=(Microsoft.Data.Sqlite.SqliteConnection)typeof(WorkspaceSessionStore)
                .GetField("_connection",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Db)!;
            string before=R30R2DatabaseImage(f.Db),files=f.Files();
            foreach(var setting in new[]{("synchronous",3L),("foreign_keys",1L),("busy_timeout",5000L)})
            {
                using var command=connection.CreateCommand();command.CommandText="PRAGMA "+setting.Item1;
                Ensure(Convert.ToInt64(command.ExecuteScalar())==setting.Item2,
                    "Cold R30 connection lost "+setting.Item1+" safety setting");
            }
            Ensure(before==R30R2DatabaseImage(f.Db) && files==f.Files(),"Connection inspection changed durable state");
        });
        foreach(string shape in new[]{"MARKER", "ORPHAN_TABLES"})
        Check("R30_R2_DOWNGRADED_"+shape+"_NEVER_MIGRATED_ON_COLD_OPEN",()=>
        {
            using var f=new R30TestFixture(4);f.Apply("Copy");
            f.Sql("DROP TABLE r30_meta; PRAGMA user_version=18");
            if(shape=="ORPHAN_TABLES")File.Delete(Path.Combine(f.Root,"R30_GENERATED_ONLY.marker"));
            string sql=R30R2DatabaseImage(f.Db),files=f.Files();f.Db.Dispose();
            using(var cold=new WorkspaceSessionStore(Path.Combine(f.Root,"state")))
            {
                Ensure(cold.InspectR30History() is {Present:true,Complete:false},"Downgrade lost read-only HOLD");
                Refuse<IOException>(()=>cold.Save(f.Work.CaptureSession()));
            }
            Ensure(sql==R30R2DatabaseImage(f.Db) && files==f.Files(),"Cold open migrated damaged R30 state");
            Ensure(!Directory.EnumerateFiles(Path.Combine(f.Root,"state"),"*.before-*").Any(),"Cold open created migration backup");
        });

        foreach(string shape in new[]{"ENROLLED", "MISSING_ANCHOR", "CORRUPT", "CORRUPT_CHECKPOINT", "V19"})
        Check("R30_R2_ACTUAL_STUDIO_HISTORY_NAVIGATION_"+shape,()=>R30R2GuiNavigation(shape));
    }

    private static T R30R2Field<T>(MainWindow window,string name) =>
        (T)(typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(window)
            ?? throw new Exception("Missing actual Studio control/state: "+name));

    private static void R30R2Pump(Func<bool> done)
    {
        var timeout=System.Diagnostics.Stopwatch.StartNew();
        while(!done() && timeout.Elapsed<TimeSpan.FromSeconds(20))
        {R30R2NativeFrame();}
        Dispatcher.UIThread.RunJobs();Ensure(done(),"Actual UI inspection timed out");
    }

    private static void R30R2NativeFrame()
    {
        // Process native window resize/input/layout messages as well as managed
        // dispatcher work; RunJobs alone does not prove actual desktop bounds.
        var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};
        timer.Tick+=(_,_)=>frame.Continue=false;timer.Start();
        try {Dispatcher.UIThread.PushFrame(frame);} finally {timer.Stop();}
    }

    private static void R30R2GuiNavigation(string shape)
    {
        if(Application.Current is null)
            AppBuilder.Configure<App>().UsePlatformDetect().With(new Win32PlatformOptions {
                RenderingMode=new[]{Win32RenderingMode.Software}}).SetupWithoutStarting();
        using var f=new R30TestFixture(4);f.Apply("Copy");
        if(shape=="MISSING_ANCHOR")f.Sql("DROP TABLE r30_meta");
        if(shape=="CORRUPT")f.Sql("UPDATE r30_meta SET seal='corrupted'");
        if(shape=="CORRUPT_CHECKPOINT")f.Sql("UPDATE workspace_checkpoint SET json='{' WHERE id=1");
        string state=Path.Combine(f.Root,"state");
        if(shape=="V19")
        {
            // Separate generated v19 root: the R30 fixture's parent marker must
            // never be ignored merely because a sibling directory says ordinary.
            state=Path.Combine(Path.GetTempPath(),"PHOTOSIFT_REBORN_P1_DISPOSABLE_"+Guid.NewGuid().ToString("N"),"state");
            using var ordinary=new WorkspaceSessionStore(state);ordinary.Save(new SortingWorkspace().CaptureSession());
        }
        f.Db.Dispose();
        string file=Path.Combine(state,"workspaces-v1.sqlite");
        string before=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        string files=f.Files();
        string? previous=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
        MainWindow? window=null;
        try
        {
            Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",state);
            window=new MainWindow();window.Show();R30R2NativeFrame();
            bool ordinary=shape=="V19";
            Ensure(R30R2Field<bool>(window,"_storeHealthy")==ordinary,
                "Startup health/authority changed: "+R30R2Field<TextBlock>(window,"_status").Text);
            var history=R30R2Field<Button>(window,"_studioHistory");
            Ensure(history.IsEffectivelyEnabled,"History navigation disabled");
            history.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var host=R30R2Field<ContentControl>(window,"_studioHost");
            var box=R30R2Field<TextBox>(window,"_r30Readiness");
            var inspect=((Control)host.Content!).GetLogicalDescendants().OfType<Button>()
                .SingleOrDefault(b=>Equals(b.Content,"Inspect operation readiness"));
            Console.WriteLine($"R30_R2_GUI_TREE={shape};page={typeof(MainWindow).GetField("_studioActivePage",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)};host={host.Content!.GetType().Name};boxParent={box.Parent?.GetType().Name};buttons="+
                string.Join("|",((Control)host.Content!).GetLogicalDescendants().OfType<Button>().Select(b=>b.Content?.ToString())));
            Ensure(inspect is not null && ((Control)host.Content!).GetLogicalDescendants().Contains(box),
                "R30 inspector is not reachable through the actual History navigation");
            Ensure(inspect!.IsEffectivelyEnabled && box.IsReadOnly,"Inspector unavailable or editable");
            inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            R30R2Pump(()=>!R30R2Field<bool>(window,"_r30Inspecting"));
            Ensure(box.Text?.Contains("Ordinary mixed-family operations remain locked.")==true,"Authority boundary omitted");
            if(shape is "MISSING_ANCHOR" or "CORRUPT" or "CORRUPT_CHECKPOINT")
                Ensure(box.Text!.Contains("HOLD"),"Corrupt v20 HOLD was not reachable/displayed");
            if(!ordinary)
            foreach(string name in new[]{"_studioChanges","_studioAdd","_studioChooseOutput","_apply",
                "_applyRename","_undoRename","_markReject","_shuffle","_removeSession"})
                Ensure(!R30R2Field<Button>(window,name).IsEffectivelyEnabled,"Locked mutation control enabled: "+name);
            if(ordinary)Ensure(R30R2Field<Button>(window,"_studioChanges").IsEnabled,"v19 Changes disabled");
            foreach(int width in new[]{700,1360})
            {
                window.Width=width;window.Height=760;R30R2NativeFrame();window.UpdateLayout();
                Ensure(history.IsEffectivelyEnabled && inspect.IsEffectivelyEnabled && inspect.IsEffectivelyVisible,
                    "History inspector unavailable after resize");
                if(width==700)Ensure(window.Bounds.Width<=716,"Native compact resize was not processed");
                var position=inspect.TranslatePoint(default,window) ?? throw new Exception("Inspector is not in the actual window");
                Ensure(position.X>=0 && position.Y>=0 && position.X+inspect.Bounds.Width<=window.Bounds.Width &&
                    position.Y+inspect.Bounds.Height<=window.Bounds.Height,"Inspector clipped outside the actual viewport");
                Console.WriteLine($"R30_R2_GUI_NAVIGATION={shape};requestedWidth={width};actualBounds={window.Bounds};history={history.IsEffectivelyEnabled};inspect={inspect.IsEffectivelyEnabled};health={R30R2Field<bool>(window,"_storeHealthy")};readonly={box.IsReadOnly};inspectPosition={position};inspectBounds={inspect.Bounds}");
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));
                bitmap.Render(window);string capture=Path.Combine(f.Root,"state",$"r30-r2-gui-{shape}-{width}.png");
                bitmap.Save(capture,new PngBitmapEncoderOptions());Console.WriteLine("R30_R2_GUI_CAPTURE="+capture);
            }
        }
        finally
        {
            window?.Close();Dispatcher.UIThread.RunJobs();
            Environment.SetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME",previous);
        }
        Ensure(before==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
            "Read-only GUI startup/navigation/inspection changed SQL bytes");
        Ensure(files==f.Files(),"Read-only GUI changed native fixture files");
    }
}
