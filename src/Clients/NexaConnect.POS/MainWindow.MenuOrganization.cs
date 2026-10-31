using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.Catalog;
namespace NexaConnect.POS;
public sealed record MenuCategoryChoice(Guid? Id,bool All,string Label);
public partial class MainWindow
{
    private CancellationTokenSource? menuOrganizationCancellation;
    private long menuOrganizationGeneration;
    private BranchMenuOrganization? menuOrganization;
    private void InitializeMenuOrganization()=>ClearMenuOrganization();
    private void ClearMenuOrganization()
    {
        menuOrganizationGeneration++;menuOrganizationCancellation?.Cancel();menuOrganizationCancellation?.Dispose();menuOrganizationCancellation=null;menuOrganization=null;
        foreach(var tile in menuTiles)tile.CategoryId=null;
        CategoryComboBox.ItemsSource=new[]{new MenuCategoryChoice(null,true,"All categories"),new MenuCategoryChoice(null,false,"Uncategorized")};CategoryComboBox.SelectedIndex=0;
        MenuCategoryStatusText.Text="Refresh menu to load categories.";
    }
    private bool MatchesMenuCategory(MenuTile tile)=>CategoryComboBox.SelectedItem is not MenuCategoryChoice choice||choice.All||choice.Id==tile.CategoryId;
    private void MenuCategory_Changed(object sender,SelectionChangedEventArgs e)=>RefreshMenuFilter();
    private async Task LoadMenuOrganizationAsync()
    {
        if(!IsSignedIn()||_authentication.CurrentToken is not {} token)return;
        ClearMenuOrganization();var generation=menuOrganizationGeneration;var privacy=receiptSessionGeneration;var subject=token.Subject;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);menuOrganizationCancellation=cancellation;var ct=cancellation.Token;
        try{
            var layout=await _api.GetMenuOrganizationAsync(token,ct);
            if(generation!=menuOrganizationGeneration||!PrintSessionCurrent(privacy,subject))return;
            ApplyMenuOrganization(layout);MenuCategoryStatusText.Text="Categories and item order loaded.";
        }catch(Exception e)when(e is PosApiException or HttpRequestException or InvalidDataException or IOException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException){
            if(generation==menuOrganizationGeneration&&PrintSessionCurrent(privacy,subject))MenuCategoryStatusText.Text="Categories unavailable — showing all menu items.";
        }
    }
    private void ApplyMenuOrganization(BranchMenuOrganization layout)
    {
        PosMenuOrganizationView.Validate(layout,_configuration.OrganizationId,_configuration.BranchId);menuOrganization=layout;
        foreach(var tile in menuTiles)tile.CategoryId=PosMenuOrganizationView.CategoryFor(tile.Item.ProductId,layout);
        var ordered=PosMenuOrganizationView.Order(menu.ToArray(),layout);
        for(int index=0;index<ordered.Count;index++){
            var tile=menuTiles.FirstOrDefault(t=>t.Item.ProductId==ordered[index].ProductId);if(tile is not null){int previous=menuTiles.IndexOf(tile);if(previous!=index)menuTiles.Move(previous,index);}
        }
        CategoryComboBox.ItemsSource=new[]{new MenuCategoryChoice(null,true,"All categories")}.Concat(layout.Categories.Select(c=>new MenuCategoryChoice(c.Id,false,c.Name))).Append(new MenuCategoryChoice(null,false,"Uncategorized")).ToArray();CategoryComboBox.SelectedIndex=0;RefreshMenuFilter();
    }
}
