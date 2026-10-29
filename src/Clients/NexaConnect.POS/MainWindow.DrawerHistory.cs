using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS.Hardware.Drawers;
namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly DrawerHistoryReader drawerHistory;
    private readonly ObservableCollection<DrawerHistoryRow> drawerHistoryRows=[];
    private CancellationTokenSource? drawerHistoryCancellation;
    private int drawerHistoryGeneration,drawerHistoryPages;
    private bool drawerHistoryReading;
    private string? drawerHistoryCursor;
    private DateTimeOffset drawerHistoryFrom,drawerHistoryTo;
    private void ClearDrawerHistory()
    {
        drawerHistoryGeneration++;drawerHistoryCancellation?.Cancel();drawerHistoryCancellation?.Dispose();drawerHistoryCancellation=null;
        drawerHistoryRows.Clear();DrawerHistoryList.SelectedItem=null;drawerHistoryCursor=null;drawerHistoryPages=0;drawerHistoryReading=false;
        DrawerHistoryStatusText.Text="History cleared. Sign in and load your authorized terminal history.";
        DrawerHistoryGuidanceText.Text="Review cannot open, reset or retry a drawer, or change payment.";
        RenderDrawerHistoryControls();
    }
    private void RenderDrawerHistoryControls()
    {
        if(!viewInitialized)return;
        bool available=IsSignedIn()&&!displaySafetyBlocked&&!busy&&!drawerHistoryReading;
        DrawerHistoryRefreshButton.IsEnabled=available;DrawerHistoryPeriodPicker.IsEnabled=available;
        DrawerHistoryMoreButton.IsEnabled=available&&drawerHistoryCursor is not null&&drawerHistoryPages<8;
        DrawerHistoryCheckButton.IsEnabled=available&&DrawerHistoryList.SelectedItem is DrawerHistoryRow;
    }
    private void DrawerHistoryPeriod_Changed(object sender,SelectionChangedEventArgs e){if(viewInitialized)ClearDrawerHistory();}
    private void DrawerHistorySelection_Changed(object sender,SelectionChangedEventArgs e)
    {if(viewInitialized){DrawerHistoryGuidanceText.Text=(DrawerHistoryList.SelectedItem as DrawerHistoryRow)?.Guidance??"Select an attempt to review its original record. Viewing sends no opening or payment command.";RenderDrawerHistoryControls();}}
    private async void DrawerHistoryRefresh_Click(object sender,RoutedEventArgs e)=>await LoadDrawerHistoryAsync(false);
    private async void DrawerHistoryMore_Click(object sender,RoutedEventArgs e)=>await LoadDrawerHistoryAsync(true);
    private bool DrawerHistoryCurrent(int history,int session,string subject)=>history==drawerHistoryGeneration&&PrintSessionCurrent(session,subject);
    private async Task LoadDrawerHistoryAsync(bool more)
    {
        if(!IsSignedIn()||displaySafetyBlocked||busy||drawerHistoryReading||_authentication.CurrentToken?.Subject is not {} subject)return;
        if(more&&(drawerHistoryCursor is null||drawerHistoryPages>=8))return;
        if(!more){ClearDrawerHistory();drawerHistoryTo=DateTimeOffset.UtcNow;drawerHistoryFrom=drawerHistoryTo.AddDays(DrawerHistoryPeriodPicker.SelectedIndex switch{0=>-1,2=>-30,_=>-7});}
        int generation=drawerHistoryGeneration,session=receiptSessionGeneration;
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);drawerHistoryCancellation=cancel;drawerHistoryReading=true;RenderDrawerHistoryControls();
        DrawerHistoryStatusText.Text="Loading authorized history…";
        try{
            var page=await drawerHistory.LoadAsync(subject,drawerHistoryFrom,drawerHistoryTo,more?drawerHistoryCursor:null,cancel.Token);
            if(!DrawerHistoryCurrent(generation,session,subject))return;
            var merged=DrawerHistoryReader.Merge(drawerHistoryRows,page.Rows);
            drawerHistoryRows.Clear();foreach(var row in merged)drawerHistoryRows.Add(row);drawerHistoryCursor=page.NextCursor;drawerHistoryPages++;
            DrawerHistoryStatusText.Text=drawerHistoryRows.Count==0?"No attempts found in this period. No opening command was sent.":$"Showing {drawerHistoryRows.Count} attempts (up to 200 server / 50 local). Select a row to check the original record.";
            if(!page.LocalAvailable)DrawerHistoryStatusText.Text+=" Local history could not be read; server observations are shown. Preserve the local journal.";
            if(drawerHistoryPages>=8&&drawerHistoryCursor is not null)DrawerHistoryStatusText.Text+=" Page limit reached; choose a shorter period.";
        }catch{if(DrawerHistoryCurrent(generation,session,subject)){ClearDrawerHistory();DrawerHistoryStatusText.Text="History unavailable or access denied. Check your account, terminal and service availability. No cached history or opening action is used.";}}
        finally{if(generation==drawerHistoryGeneration){drawerHistoryReading=false;drawerHistoryCancellation=null;RenderDrawerHistoryControls();}cancel.Dispose();}
    }
    private async void DrawerHistoryCheck_Click(object sender,RoutedEventArgs e)
    {
        if(!DrawerHistoryCheckButton.IsEnabled||DrawerHistoryList.SelectedItem is not DrawerHistoryRow row||_authentication.CurrentToken?.Subject is not {} subject)return;
        int generation=drawerHistoryGeneration,session=receiptSessionGeneration;var cancel=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);drawerHistoryCancellation=cancel;drawerHistoryReading=true;RenderDrawerHistoryControls();
        try{var checkedRow=await drawerHistory.CheckAsync(row,cancel.Token);if(!DrawerHistoryCurrent(generation,session,subject))return;
            int index=drawerHistoryRows.IndexOf(row);if(index<0)return;drawerHistoryRows[index]=checkedRow;DrawerHistoryList.SelectedItem=checkedRow;DrawerHistoryGuidanceText.Text=checkedRow.Guidance;DrawerHistoryStatusText.Text="Original record checked. These are observations; no intent, drawer or payment was changed.";
        }catch{if(DrawerHistoryCurrent(generation,session,subject)){ClearDrawerHistory();DrawerHistoryStatusText.Text="Original record unavailable or access denied. Reload authorized history; do not repeat the device command.";}}
        finally{if(generation==drawerHistoryGeneration){drawerHistoryReading=false;drawerHistoryCancellation=null;RenderDrawerHistoryControls();}cancel.Dispose();}
    }
}
