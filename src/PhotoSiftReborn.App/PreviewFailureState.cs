namespace PhotoSiftReborn;

// Terminal photo preview failures must survive unrelated Refresh() calls until a new
// decode request starts. This is presentation state, not recovery/Undo authority.
internal sealed class PreviewFailureState
{
    private Guid? _photoId;
    private long _generation;
    private string? _heading;
    private string? _description;

    public void Clear()
    {
        _photoId = null;
        _heading = null;
        _description = null;
    }

    public void Record(Guid photoId, long generation, string heading, string description)
    {
        if (photoId == Guid.Empty || string.IsNullOrWhiteSpace(heading))
            throw new ArgumentException("Preview failure requires photo identity and explanation.");
        _photoId = photoId;
        _generation = generation;
        _heading = heading;
        _description = description;
    }

    public bool TryGet(Guid currentPhotoId, long generation, out string heading, out string description)
    {
        if (_photoId == currentPhotoId && _generation == generation && _heading is not null)
        {
            heading = _heading;
            description = _description ?? "";
            return true;
        }
        heading = "";
        description = "";
        return false;
    }
}
