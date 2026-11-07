using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.POS;
using NexaConnect.POS;

internal static partial class Program
{
    private static ManagerRefundFixture? managerRefundFixture;

    private static void VerifyManagerManualRefund(MainWindow window)
    {
        var fixture = new ManagerRefundFixture();
        managerRefundFixture = fixture;
        var store = (LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!;
        try
        {
            ((TabItem)window.FindName("ReceiptsTab")).IsSelected = true;
            ((TabItem)window.FindName("ManualRefundSection")).IsSelected = true;
            typeof(MainWindow).GetField("managerRefundOrder", Private)!.SetValue(window, fixture.OrderId);
            Invoke(window, "UpdateOperationalState");
            Invoke(window, "LoadManagerRefund_Click", window, new RoutedEventArgs());
            Pump(window);
            var cashier = (ComboBox)window.FindName("ManagerRefundCashier");
            var approve = (Button)window.FindName("ApproveManagerRefundButton");
            Require(cashier.Items.Count == 1 && !approve.IsEnabled, "approval requires a listed cashier and amount");
            cashier.SelectedIndex = 0;
            ((TextBox)window.FindName("ManagerRefundAmount")).Text = 30m.ToString(CultureInfo.CurrentCulture);
            Require(approve.IsEnabled, "verified cashier selection makes approval available");
            approve.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            var saved = store.LoadManualRefundApproval();
            Require(saved?.Request == fixture.Approval && !approve.IsEnabled, "lost approval response retains the exact request");
            ((Button)window.FindName("VerifyManagerRefundButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Require(store.LoadManualRefundApproval() is null && fixture.ApprovalWrites == 1, "read-only verification recovers approval without resending");
            ((ListBox)window.FindName("ManagedRefundList")).SelectedIndex = 0;
            var release = (Button)window.FindName("ReleaseManagerRefundButton");
            Require(release.IsEnabled, "untouched approval can be released");
            release.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Require(fixture.Released && !release.IsEnabled, "released approval is terminal in the manager workspace");
            Console.WriteLine("PASS: manager cashier selection, lost approval recovery and release; synthetic fixture only.");
        }
        finally
        {
            managerRefundFixture = null;
            Invoke(window, "ClearManualRefundReview");
            ((TabItem)window.FindName("CheckoutTab")).IsSelected = true;
            Invoke(window, "UpdateOperationalState");
        }
    }

    private sealed class ManagerRefundFixture
    {
        public Guid OrderId { get; } = Guid.NewGuid();
        private Guid ShiftId { get; } = Guid.NewGuid();
        private DateTimeOffset ApprovedAt { get; } = DateTimeOffset.UtcNow;
        public ApproveManualRefundV1? Approval { get; private set; }
        public int ApprovalWrites { get; private set; }
        public bool Released { get; private set; }

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            Require(path.Contains(OrderId.ToString("D")), "manager requests use only the reviewed order");
            if (path.EndsWith("/preview")) return Reply(new ManualRefundPreviewV1(OrderId, "cash", "THB", 100, 100));
            if (path.EndsWith("/cashiers")) return Reply(new ManualRefundCashiersV1([new(ShiftId, "approved-cashier", "POS-01", "SHIFT-01", true)]));
            if (path.EndsWith("/managed")) return Reply(new AssignedManualRefundsV1([], false));
            if (path.EndsWith("/approve"))
            {
                ApprovalWrites++;
                Approval = await request.Content!.ReadFromJsonAsync<ApproveManualRefundV1>(ct);
                Require(Approval is { Amount: 30, CashierSubject: "approved-cashier" } && Approval.CashierShiftId == ShiftId,
                    "approval binds the verified cashier and shift");
                throw new HttpRequestException("Synthetic lost approval response");
            }
            Require(Approval is not null && path.Contains(Approval.OperationId.ToString("D")), "manager recovers the exact operation");
            if (path.EndsWith("/release")) Released = true;
            else Require(request.Method == HttpMethod.Get, "verification is read-only");
            return Reply(new ManualRefundStatusV1(Approval!.OperationId, OrderId, "cash", "THB", 30, "customer_request",
                Released ? "released" : "approved", ApprovedAt, null, null, true, Released ? ApprovedAt.AddSeconds(1) : null));
        }

        private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
