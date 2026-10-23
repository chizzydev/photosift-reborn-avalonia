using Avalonia.Controls;
using Avalonia.Media;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private readonly TextBlock _guidedHeadline = new()
    {
        FontSize = 19, FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _guidedNextStep = Muted("Open or add a photo folder to begin.");
    private readonly Expander _recoveryAndFileTools = new()
    {
        Header = "Recovery and file tools",
        IsExpanded = false
    };
    private readonly Button _showMarkedRecovery = new()
    {
        Content = "Review marked photographs / recovery…",
        MinHeight = 38, IsVisible = false
    };
    private bool _guidedAttentionShown;

    private void ShowRecoveryAndFileTools()
    {
        _recoveryAndFileTools.IsExpanded = true;
        if (_anyRejectHistory && (_moveFixtureEnabled || OrdinaryRejectGuiProbePolicy.IsRequested))
            _fixtureRecoveryExpander.IsExpanded = true;
        if (_work.PendingRejectCount > 0 || (_anyRejectHistory && !_moveFixtureEnabled &&
            !OrdinaryRejectGuiProbePolicy.IsRequested))
            _ordinaryFirstRejectExpander.IsExpanded = true;
    }

    private void RefreshGuidedWorkspace(int committed)
    {
        bool copyHistoryNeedsReview = _sessionStore is not null &&
            _sessionStore.AllCopyHistoryCount() != 0 && !_copyContinuationApproved;
        bool attention = _copyBarrier || JournalNeedsAttention || _anyMoveHistory ||
            _anyRejectHistory || RenameLocksWrites || copyHistoryNeedsReview;
        try
        {
            var message = GuidedWorkspacePresentation.Describe(_work.Photos.Count,
                _work.AssignmentCount, _work.PendingRejectCount, committed,
                !string.IsNullOrWhiteSpace(_work.DestinationRoot), attention,
                _storeHealthy, _busy);
            _guidedHeadline.Text = message.Heading;
            _guidedNextStep.Text = message.NextStep;
            if (message.Attention && !_guidedAttentionShown)
                ShowRecoveryAndFileTools();
            _guidedAttentionShown = message.Attention;
        }
        catch (InvalidDataException)
        {
            _guidedHeadline.Text = "Collection requires attention";
            _guidedNextStep.Text = "Saved choices cannot be summarized. Do not apply changes; inspect the workspace.";
            if (!_guidedAttentionShown) ShowRecoveryAndFileTools();
            _guidedAttentionShown = true;
        }
        _showMarkedRecovery.IsVisible = _work.PendingRejectCount > 0 || _anyRejectHistory ||
            _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention || _copyBarrier;
        _showMarkedRecovery.IsEnabled = !_busy && _storeHealthy;
    }
}
