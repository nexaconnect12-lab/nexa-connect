using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexaConnect.POS;

public sealed record TerminalSupportReport(int Version,DateTimeOffset GeneratedAtUtc,string ApplicationVersion,string RuntimeVersion,string WindowsVersion,
    bool TouchFirst,PosDisplayDensity Density,ReceiptPaper Paper,bool HasSavedPrinter,int? PrinterCount,bool? SavedPrinterDetected,
    bool PrinterDiscoveryFailed,int? DisplayCount,bool DisplayDiscoveryFailed,bool CustomerDisplayActive,bool DisplaySafetyBlocked,bool ScannerMode,
    TerminalServiceCheck[] Services,DateTimeOffset? PrinterCheckedAtUtc,DateTimeOffset? DisplayCheckedAtUtc)
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    public static TerminalSupportReport Create(TerminalSetupFacts f,IEnumerable<TerminalServiceCheck> services,Version application,Version runtime,Version windows)=>
        new(1,DateTimeOffset.UtcNow,application.ToString(),runtime.ToString(),windows.ToString(),f.TouchFirst,f.Density,f.Paper,f.HasSavedPrinter,
            f.PrinterCount,f.SavedPrinterDetected,f.PrinterDiscoveryFailed,f.DisplayCount,f.DisplayDiscoveryFailed,f.CustomerDisplayActive,f.DisplaySafetyBlocked,f.ScannerMode,services.ToArray(),f.PrinterCheckedAtUtc,f.DisplayCheckedAtUtc);
    public byte[] Serialize()
    {
        bool NumericVersion(string value)=>value.Length<=64&&System.Version.TryParse(value,out _);
        if(Version!=1||!Enum.IsDefined(Density)||!Enum.IsDefined(Paper)||!NumericVersion(ApplicationVersion)||!NumericVersion(RuntimeVersion)||!NumericVersion(WindowsVersion)
            ||PrinterCount is <0 or >256||DisplayCount is <0 or >32||Services is null||Services.Length!=6||Services.Any(s=>s is null)||Services.Select(s=>s.Service).Distinct().Count()!=6
            ||Services.Any(s=>!Enum.IsDefined(s.Service)||!Enum.IsDefined(s.Status)||s.HttpStatus is <100 or >599))throw new InvalidDataException("Invalid support report.");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(this,Json);
        if(bytes.Length>16384)throw new InvalidDataException("Support report exceeds its limit.");
        return bytes;
    }
    public void Save(string path)
    {
        byte[] bytes=Serialize();path=Path.GetFullPath(path);PosInstallationPolicy.AssertNoLinks(path);
        if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException();
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes);
            PosInstallationPolicy.AssertNoLinks(path);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
