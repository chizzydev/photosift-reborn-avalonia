using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private readonly Button _ordinaryOperationsSafety = new()
    {
        Content="Move / Rename / Reject — safety & recovery center…",
        MinHeight=42, HorizontalAlignment=HorizontalAlignment.Stretch
    };

    private async Task ShowOrdinaryOperationsSafetyAsync()
    {
        if(_busy || !_storeHealthy || _sessionStore is null || _work.Current is not { } selected)
        {
            Message("Open a saved photograph before inspecting ordinary operation safety. No changes.");
            return;
        }
        Guid session=_work.SessionId, id=selected.Id;
        long revision=_work.Revision;
        var dialog=new Window {
            Title="PhotoSift — Move / Rename / Reject safety center (NO FILE CHANGES)",
            Width=970,Height=700,MinWidth=680,MinHeight=460,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White
        };
        var shell=new Grid{Margin=new Thickness(18),RowDefinitions=new RowDefinitions("Auto,Auto,Auto,*,Auto")};
        shell.Children.Add(new TextBlock {
            Text="One photograph · three operations · no native authority",
            FontSize=22,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap
        });
        var instruction=new TextBlock {
            Text="This evaluates one saved source and its recovery restrictions. Move and Reject still require their own separately approved native workflows; ordinary Rename is not yet enabled. No operation is staged or executed here.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)
        };Grid.SetRow(instruction,1);shell.Children.Add(instruction);
        var nameRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};
        nameRow.Children.Add(new TextBlock{Text="Proposed same-folder Rename:",VerticalAlignment=VerticalAlignment.Center});
        var proposed=new TextBox{Width=280,Text=Path.GetFileNameWithoutExtension(selected.Path)+"_sorted"+Path.GetExtension(selected.Path)};
        nameRow.Children.Add(proposed);
        var inspect=new Button{Content="Recheck exact current photograph (read only)",MinHeight=36};
        nameRow.Children.Add(inspect);
        Grid.SetRow(nameRow,2);shell.Children.Add(nameRow);
        var evidence=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            Background=new SolidColorBrush(Color.Parse("#172A39")),Foreground=Brushes.White,
            Text="Inspection not yet run; no native operation authorized.",Margin=new Thickness(0,12,0,8)};
        Grid.SetRow(evidence,3);shell.Children.Add(evidence);
        var finish=new Button{Content="Close — preserve photographs and recovery history",MinHeight=42,
            HorizontalAlignment=HorizontalAlignment.Right};
        finish.Click+=(_,_)=>dialog.Close(); Grid.SetRow(finish,4);shell.Children.Add(finish);
        dialog.Content=shell;
        async Task InspectNow()
        {
            if(_sessionStore is null || session!=_work.SessionId || id!=_work.Current?.Id ||
               revision!=_work.Revision)
            {
                evidence.Text="REFUSED: selected photo or workspace changed. Close and start a new inspection.";
                inspect.IsEnabled=false;return;
            }
            string candidate=proposed.Text??"";
            inspect.IsEnabled=false;proposed.IsEnabled=false;
            evidence.Text="Checking the saved checkpoint and original identity without changing files…";
            try
            {
                var result=await Task.Run(()=>OrdinaryOperationsSafetyAudit.Inspect(
                    _sessionStore,_work,selected,candidate));
                // Stale async completion must NEVER be presented as fresh proof.
                evidence.Text=(session==_work.SessionId && id==_work.Current?.Id && revision==_work.Revision)
                   ? result.HumanReadable
                   : "STALE INSPECTION REFUSED: selection or workspace changed.";
            }
            catch(Exception ex)
            {
                evidence.Text="READ-ONLY INSPECTION REFUSED: "+ex.Message+
                    "\nNo file operation was authorized. Preserve existing recovery evidence.";
            }
            finally{inspect.IsEnabled=true;proposed.IsEnabled=true;}
        }
        inspect.Click+=async (_,_)=>await InspectNow();
        await InspectNow();
        await dialog.ShowDialog(this);
        Message("Ordinary operation safety inspected. No native journal, approval, or file mutation was created.");
    }
}
