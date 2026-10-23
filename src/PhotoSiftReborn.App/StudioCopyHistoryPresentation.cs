using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// Studio is a read-only observer of the durable Copy journal. In particular,
// Undone is a historical state, NOT proof that archived output is still owned
// or unmodified. Only the existing native recovery inspector gives fresh proof.
public sealed record StudioCopyHistoryLine(string Title, string Subtitle, string HistoricalStatus);
public sealed record StudioCopyHistoryView(string HistorySummary, string ChangesSummary,
    IReadOnlyList<StudioCopyHistoryLine> Recent);

public static class StudioCopyHistoryPresentation
{
    public static StudioCopyHistoryView Create(IReadOnlyList<DurableCopyItem> rows)
    {
        int committed=rows.Count(x=>x.State=="Committed");
        int undone=rows.Count(x=>x.State=="Undone");
        int unresolved=rows.Count-committed-undone;
        var summary=$"Recorded Copy history: {committed} completed, {undone} undone, " +
            $"{unresolved} awaiting inspection. These are saved states, NOT a current " +
            "verification of destination or recovery files. Select Inspect recovery journal " +
            "for fresh evidence and exact paths.";
        var changes=rows.Count==0
            ? "No recorded Copy operations yet. Saving a folder choice never copies a file."
            : $"{rows.Count} recorded Copy operation(s) · {committed} completed · " +
              $"{undone} undone · {unresolved} needing inspection. " +
              "Historical status only; inspect current file evidence in History.";
        var recent=rows.Reverse().Take(6).Select(row=>
        {
            var name=Path.GetFileName(row.Source);
            var folder=Path.GetFileName(Path.GetDirectoryName(row.Destination));
            var destination=Path.GetFileName(row.Destination);
            var status=row.State switch
            {
                "Committed"=>"Copy completed (recorded). Verify current output in History.",
                "Undone"=>"Copy undone (recorded). Verify recovery archive in History.",
                _=>"Operation needs inspection (recorded: "+row.State+"). No new write authorization."
            };
            return new StudioCopyHistoryLine(name+"  →  "+folder+"/"+destination,
                "Copy destination key "+row.Key+" · exact paths in recovery inspection",status);
        }).ToArray();
        return new StudioCopyHistoryView(summary,changes,recent);
    }
}
