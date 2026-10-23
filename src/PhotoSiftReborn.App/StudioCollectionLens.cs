using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// Read-only photographer-facing collection lens. These are saved decisions,
// NOT file/route verification or permission to approve a native operation.
internal static class StudioCollectionLens
{
    internal static string Progress(CollectionDecisionOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        RequireCounts(overview);
        int total = overview.Rows.Count;
        return total == 0
            ? "Add photographs to begin. Nothing has been reviewed or copied."
            : $"{overview.Unassigned} awaiting choice  ·  {overview.Assigned} saved folder choices  ·  " +
              $"{overview.RejectReview} marked for review  ·  {overview.Removed} removed from this session. " +
              "Saved choices only; no file changes or current recovery verification.";
    }

    internal static string Routes(CollectionDecisionOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        RequireCounts(overview);
        var groups=overview.Rows.Where(x=>x.State=="Assigned")
            .GroupBy(x=>(x.RouteKey,x.RouteLabel))
            .OrderBy(x=>x.Key.RouteKey)
            .Select(x=>$"{x.Key.RouteKey} · {x.Key.RouteLabel}: {x.Count()} chosen").ToArray();
        return groups.Length == 0 ? "No destination choices saved yet." :
            "Saved destination choices · " + string.Join("   |   ",groups) +
            ". Not an output inventory.";
    }

    internal static string ExactSelection(CollectionDecisionRow row)
    {
        ValidateRow(row);
        var choice = row.State switch
        {
            "Assigned" => $"Saved folder choice: {row.RouteKey} · {row.RouteLabel}. Original unchanged by this choice.",
            "RejectReview" => "Marked for Reject review only. No native Reject move or Trash deletion authorized.",
            _ => "Needs a folder decision. No destination approved."
        };
        return $"PHOTO {row.Position} · {Path.GetFileName(row.Path)}" + Environment.NewLine +
            choice + Environment.NewLine +
            "Exact imported original:" + Environment.NewLine + row.Path + Environment.NewLine +
            "Stable photo ID: " + row.PhotoId + Environment.NewLine +
            "Saved collection state only · no current disk verification, file change or Undo authority.";
    }

    internal static CollectionDecisionRow RequireFresh(CollectionDecisionRow selected,
        IReadOnlyList<CollectionDecisionRow> fresh)
    {
        ValidateRow(selected);
        ArgumentNullException.ThrowIfNull(fresh);
        // A list index is never sufficient to identify a photograph after
        // filtering, import, removal, reassignment, or an asynchronous refresh.
        // Refuse duplicates and any changed saved decision before navigation.
        var matches=fresh.Where(x=>x is not null && x.PhotoId==selected.PhotoId).ToArray();
        if (matches.Length!=1) throw new InvalidDataException("Selected photo identity changed; select again.");
        var now=matches[0];
        ValidateRow(now);
        if (now.Position!=selected.Position ||
            !SortingWorkspace.PathComparer.Equals(now.Path,selected.Path) ||
            now.State!=selected.State || now.RouteKey!=selected.RouteKey ||
            !string.Equals(now.RouteLabel,selected.RouteLabel,StringComparison.Ordinal))
            throw new InvalidDataException("Selected saved decision changed; select again.");
        return now;
    }

    internal static CollectionDecisionRow? NextUndecided(IReadOnlyList<CollectionDecisionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var seen = new HashSet<Guid>();
        CollectionDecisionRow? next=null;
        foreach(var row in rows)
        {
            ValidateRow(row);
            if(!seen.Add(row.PhotoId)) throw new InvalidDataException("Duplicate collection identity.");
            if(row.State=="Unassigned" && (next is null || row.Position<next.Position)) next=row;
        }
        return next;
    }

    private static void RequireCounts(CollectionDecisionOverview overview)
    {
        if(overview.Assigned<0 || overview.RejectReview<0 || overview.Unassigned<0 ||
            overview.Removed<0 ||
            (long)overview.Assigned+overview.RejectReview+overview.Unassigned!=overview.Rows.Count)
            throw new InvalidDataException("Collection summary counts are inconsistent.");
        var ids=new HashSet<Guid>();
        int assigned=0,review=0,unassigned=0;
        foreach(var row in overview.Rows)
        {
            ValidateRow(row);
            if(!ids.Add(row.PhotoId)) throw new InvalidDataException("Duplicate photo in collection summary.");
            if(row.State=="Assigned") ++assigned;
            else if(row.State=="RejectReview") ++review;
            else ++unassigned;
        }
        if(assigned!=overview.Assigned || review!=overview.RejectReview ||
            unassigned!=overview.Unassigned)
            throw new InvalidDataException("Collection summary disagrees with saved rows.");
    }
    private static void ValidateRow(CollectionDecisionRow? row)
    {
        if(row is null || row.PhotoId==Guid.Empty || row.Position<1 ||
            string.IsNullOrWhiteSpace(row.Path) ||
            row.State is not ("Assigned" or "RejectReview" or "Unassigned") ||
            (row.State=="Assigned" && (row.RouteKey is null || string.IsNullOrWhiteSpace(row.RouteLabel))) ||
            (row.State!="Assigned" && (row.RouteKey is not null || !string.IsNullOrEmpty(row.RouteLabel))))
            throw new InvalidDataException("Untrustworthy saved collection row.");
    }
}
