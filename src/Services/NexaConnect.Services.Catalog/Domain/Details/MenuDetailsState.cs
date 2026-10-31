namespace NexaConnect.Services.Catalog.Domain.Details;

public sealed record MenuDetailsState
{
    public string Name { get; }
    public decimal UnitPrice { get; }
    public string Currency { get; }
    public string PreparationStation { get; }
    public long Version { get; }
    public MenuDetailsState(string name, decimal price, string currency, string station, long version)
    {
        if(version<0)throw new ArgumentException("Invalid menu version.");
        Name=name;UnitPrice=price;Currency=currency;PreparationStation=station;Version=version;
    }
    public MenuDetailsState Change(string? name, decimal? price, string? station, long? expectedVersion)
    {
        if(expectedVersion is null or <0)throw new ArgumentException("Expected version required.");
        if(expectedVersion!=Version)throw new MenuDetailsConflictException();
        if(Currency!="THB")throw new ArgumentException("Menu editing supports the existing THB checkout profile.");
        string nextName=Text(name,120), nextStation=Text(station,80);
        if(price is null or <0 or >99999999m || decimal.Round(price.Value,2)!=price.Value)
            throw new ArgumentException("Price must have at most two decimal places and fit the checkout range.");
        if(nextName==Name&&price.Value==UnitPrice&&nextStation==PreparationStation)return this;
        if(Version==long.MaxValue)throw new MenuDetailsConflictException();
        return new(nextName,price.Value,Currency,nextStation,Version+1);
    }
    private static string Text(string? value,int maximum)
    {
        if(value is null||value.Any(char.IsControl))throw new ArgumentException("Invalid menu text.");
        var result=value.Trim();if(result.Length is 0||result.Length>maximum)throw new ArgumentException("Invalid menu text.");return result;
    }
}
public sealed class MenuDetailsConflictException : Exception;
