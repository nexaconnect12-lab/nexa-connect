using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyDisplayPlacement()
    {
        var staffMonitor=new PosMonitor("staff",new(0,0,1366,768),new(0,0,1366,728),true,"staff-interface");
        var customerMonitor=new PosMonitor("customer",new(1366,0,2732,768),new(1366,0,2732,728),false,"customer-interface");
        var fake=new PlacementPlatform{Monitors=[staffMonitor,customerMonitor]};
        var staff=new Window{Width=1100,Height=700,Opacity=0,ShowActivated=false,ShowInTaskbar=false,
            WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize};
        var handle=new WindowInteropHelper(staff).EnsureHandle();fake.Rectangles[handle]=new(0,0,1100,700);
        bool blocked=false;
        using var coordinator=new PosDisplayCoordinator(staff,()=>blocked=true,fake);
        coordinator.InitializeStaffFullscreen();
        Require(fake.WindowBounds(handle)==staffMonitor.Bounds&&!blocked,"startup fills the primary staff monitor");
        var saved = new PosDisplayRoles("staff", "customer", DisplayTopology.LayoutHash(fake.Monitors)!);
        fake.Monitors = [staffMonitor, customerMonitor with { MonitorId = "replacement-interface" }];
        Require(!coordinator.Restore(saved) && !coordinator.Active && !blocked
            && fake.WindowBounds(handle) == staffMonitor.Bounds, "a monitor replacement is never restored from saved roles");
        fake.Monitors = [staffMonitor, customerMonitor];
        var topology=(DisplayTopology)typeof(PosDisplayCoordinator).GetField("topology",Private)!.GetValue(coordinator)!;
        Require(topology.Assign(fake.Monitors,"staff","customer"),"fixture display roles assign");
        try
        {
            var message=typeof(PosDisplayCoordinator).GetMethod("WindowMessage",Private)!;
            nint buffer=Marshal.AllocHGlobal(Marshal.SizeOf<WindowsDisplays.WindowPosition>());
            try
            {
                foreach(var input in new[]{new WindowsDisplays.WindowPosition{X=-8,Y=-8,Width=1382,Height=784},new WindowsDisplays.WindowPosition{X=1500,Y=100,Width=1000,Height=680}})
                {
                    Marshal.StructureToPtr(input,buffer,false);object[] args=[handle,0x0046,(nint)0,buffer,false];message.Invoke(coordinator,args);
                    var output=Marshal.PtrToStructure<WindowsDisplays.WindowPosition>(buffer);
                    Require(staffMonitor.Bounds==new DisplayBounds(output.X,output.Y,output.X+output.Width,output.Y+output.Height),"native move retains exact fullscreen bounds before painting");
                    Require(!blocked&&!topology.Blocked,"ordinary constrained movement does not lock staff");
                }
            }finally{Marshal.FreeHGlobal(buffer);}
            foreach(int command in new[]{0xF000,0xF010,0xF020,0xF030,0xF120,0xF060})
            {
                object[] args=[handle,0x0112,(nint)command,(nint)0,false];message.Invoke(coordinator,args);
                Require((bool)args[4]==(command!=0xF060),"resize/minimize commands are blocked and normal close remains available");
            }
            var customer=new CustomerDisplayWindow(fake){Opacity=0,ShowActivated=false,ShowInTaskbar=false,WindowState=WindowState.Maximized};
            var customerHandle=new WindowInteropHelper(customer).EnsureHandle();fake.Rectangles[customerHandle]=customerMonitor.WorkArea;
            typeof(PosDisplayCoordinator).GetField("customer",Private)!.SetValue(coordinator,customer);
            typeof(PosDisplayCoordinator).GetMethod("CustomerGeometryChanged",Private)!.Invoke(coordinator,[customer,EventArgs.Empty]);
            staff.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            Require(fake.WindowBounds(customerHandle)==customerMonitor.Bounds&&customer.WindowState==WindowState.Normal&&!blocked,"customer maximize/state/DPI refits public view without locking staff");
            Require(!customer.IsVisible&&!staff.IsVisible,"display geometry fixture never shows real windows");
            fake.Monitors=[staffMonitor];
            typeof(PosDisplayCoordinator).GetMethod("CustomerGeometryChanged",Private)!.Invoke(coordinator,[customer,EventArgs.Empty]);
            staff.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            Require(blocked&&topology.Blocked,"actual monitor removal still fails closed");
        }
        finally{coordinator.Dispose();staff.Close();}
    }
    private sealed class PlacementPlatform:IPosDisplayPlatform
    {
        public IReadOnlyList<PosMonitor> Monitors=[];public Dictionary<nint,DisplayBounds> Rectangles=[];
        public IReadOnlyList<PosMonitor> Read()=>Monitors;
        public DisplayBounds WindowBounds(nint handle)=>Rectangles[handle];
        public void Place(nint handle,DisplayBounds bounds)=>Rectangles[handle]=bounds;
        public void MakeCustomerReadOnly(nint handle,Action changed){}
    }
}
