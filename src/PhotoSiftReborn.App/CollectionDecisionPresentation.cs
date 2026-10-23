namespace PhotoSiftReborn;

// Pure read-only summary of a saved workspace. No native journal authority and
// no filesystem probes, mutations, automatic grants or data-dependent IO.
public sealed record CollectionDecisionRow(Guid PhotoId, string Path, int Position,
    string State, char? RouteKey, string RouteLabel)
{
    public string Display => $"{Position,4}.  {System.IO.Path.GetFileName(Path)}   —   " +
        (State == "Assigned" ? $"{RouteKey} · {RouteLabel} (staged, not copied)" :
            State == "RejectReview" ? "Reject review ONLY (no file move)" : "Needs a decision");
}

public sealed record CollectionDecisionOverview(
    IReadOnlyList<CollectionDecisionRow> Rows, int Assigned, int RejectReview, int Unassigned,
    int Removed)
{
    public string Headline => $"{Unassigned} to decide · {Assigned} assigned · " +
        $"{RejectReview} for review · {Removed} removed from session";
}

public static class CollectionDecisionPresentation
{
    public static CollectionDecisionOverview FromSnapshot(WorkspaceSessionSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Photos is null || snapshot.Assignments is null ||
            snapshot.Routes is null || snapshot.SessionId == Guid.Empty)
            throw new InvalidDataException("Collection snapshot unavailable.");
        var photoIds = new HashSet<Guid>();
        foreach (var photo in snapshot.Photos)
            if (photo is null || photo.Id == Guid.Empty || !photoIds.Add(photo.Id))
                throw new InvalidDataException("Collection contains duplicate or missing photograph identities.");
        var assignments = new Dictionary<Guid,char>();
        foreach (var assignment in snapshot.Assignments)
            if (!photoIds.Contains(assignment.PhotoId) || !assignments.TryAdd(assignment.PhotoId,assignment.Key))
                throw new InvalidDataException("Collection assignments contain a missing or duplicate photograph.");
        var routeMap = new Dictionary<char,string>();
        foreach (var route in snapshot.Routes)
            if (route is null || !routeMap.TryAdd(route.Key,route.Label))
                throw new InvalidDataException("Collection routes are missing or duplicated.");
        if (assignments.Values.Any(key => !routeMap.ContainsKey(key)))
            throw new InvalidDataException("Collection assignment refers to an unavailable route.");
        var pending = new HashSet<Guid>();
        foreach (var id in snapshot.PendingRejectIds ?? Array.Empty<Guid>())
            if (!photoIds.Contains(id) || assignments.ContainsKey(id) || !pending.Add(id))
                throw new InvalidDataException("Collection Reject-review state is inconsistent.");
        var rows = snapshot.Photos.Select((photo,index) =>
        {
            var assigned = assignments.TryGetValue(photo.Id,out var key);
            var rejected = pending.Contains(photo.Id);
            return new CollectionDecisionRow(photo.Id,photo.Path,index+1,
                assigned ? "Assigned" : rejected ? "RejectReview" : "Unassigned",
                assigned ? key : null, assigned ? routeMap[key] : "");
        }).ToArray();
        return new CollectionDecisionOverview(rows,assignments.Count,pending.Count,
            rows.Length - assignments.Count - pending.Count,
            snapshot.RemovedPhotos?.Length ?? 0);
    }
}
