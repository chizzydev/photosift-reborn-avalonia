using Avalonia.Controls;
using System.Text;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private TextBox? _r30Readiness;
    private bool _r30Inspecting;
    private async Task InspectR30ReadinessAsync()
    {
        if(_r30Inspecting || _r30Readiness is null)return;
        var store=_sessionStore;
        if(store is null){_r30Readiness.Text="HOLD: saved operation history is unavailable. Preserve all files and journals; no mutation authority.";return;}
        _r30Inspecting=true;_r30Readiness.Text="Inspecting saved history and current native evidence…";
        try
        {
            var view=await Task.Run(store.InspectR30History);
            if(!ReferenceEquals(store,_sessionStore))return;
            _r30Readiness.Text=StudioR30ReadinessPresentation.Describe(view);
        }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {_r30Readiness.Text="HOLD: operation inspection unavailable. Preserve all files and journals. "+ex.Message;}
        finally{_r30Inspecting=false;}
    }
}

internal static class StudioR30ReadinessPresentation
{
    internal static string Describe(R30HistoryView view)
    {
        var text=new StringBuilder("Ordinary mixed-family operations remain locked.\n");
        text.AppendLine(view.Reason);
        if(!view.Present)
            text.AppendLine("No R30 generated fixture ledger. Use the recovery command center for earlier operation histories.");
        foreach(var row in view.Rows)
        {
            text.AppendLine($"{row.Epoch}. {row.Family} / {row.Id:N} / session {row.Session:N}: {row.State}");
            text.AppendLine("  Source: "+row.Source);text.AppendLine("  Destination: "+row.Destination);
            if(row.Consumes is {} parent)text.AppendLine($"  Consumes output of {parent:N}; active consumers block predecessor Undo.");
            if(row.State is not ("Committed" or "Undone"))
                text.AppendLine("  HOLD: interrupted or unresolved. No automatic mutation replay. Exact recovery review required.");
        }
        foreach(var edge in view.Conflicts)
            text.AppendLine($"Dependency {edge.FirstFamily}/{edge.FirstJournalId} ↔ {edge.SecondFamily}/{edge.SecondJournalId}: {edge.Kind}. {edge.Explanation}");
        if(!view.Complete)text.AppendLine("All displayed rows are recorded status only; current complete native ownership is unverified.");
        text.Append("Read-only inspection grants no Apply, Undo, recovery continuation or ordinary-photo permission.");
        return text.ToString();
    }
}
