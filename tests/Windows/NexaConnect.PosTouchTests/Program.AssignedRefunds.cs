using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.Contracts.POS;
using NexaConnect.POS;

internal static partial class Program
{
    private static AssignedRefundFixture? assignedRefundFixture;

    private static void VerifyAssignedManualRefund(MainWindow window, PosClientConfiguration configuration, bool tamperedTerms = false)
    {
        var shiftField = typeof(MainWindow).GetField("_activeShift", Private)!;
        var previousShift = shiftField.GetValue(window);
        Guid shift = Guid.NewGuid();
        var fixture = new AssignedRefundFixture(configuration, shift, tamperedTerms);
        assignedRefundFixture = fixture;
        var local = (LocalPosStore)typeof(MainWindow).GetField("_localStore", Private)!.GetValue(window)!;
        var list = (ListBox)window.FindName("AssignedRefundList");
        var confirm = (CheckBox)window.FindName("ManualRefundPayoutConfirmed");
        var proof = (TextBox)window.FindName("ManualRefundTransferReference");
        var status = (TextBlock)window.FindName("AssignedRefundStatus");
        var start = (Button)window.FindName("StartAssignedRefundButton");
        var complete = (Button)window.FindName("CompleteAssignedRefundButton");
        var verify = (Button)window.FindName("VerifySavedManualRefundButton");
        try
        {
            shiftField.SetValue(window, new LocalShiftState(shift, "REFUND-FIXTURE", DateTimeOffset.UtcNow));
            ((TabItem)window.FindName("ReceiptsTab")).IsSelected = true;
            ((TabItem)window.FindName("ManualRefundSection")).IsSelected = true;
            Invoke(window, "UpdateOperationalState");
            Invoke(window, "LoadAssignedRefunds_Click", window, new RoutedEventArgs());
            Pump(window);
            Require(list.Items.Count == 2 && fixture.Writes == 0, "queue loading sends only a read");
            list.SelectedIndex = 1;
            Require(!start.IsEnabled && status.Text.Contains(45.1234m.ToString("N4")) && status.Text.Contains("fraction of one satang"),
                "sub-satang approval displays the exact amount and blocks a new payout");
            list.SelectedIndex = 0;
            Require(start.IsEnabled && !complete.IsEnabled, "approved refund requires explicit start");
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Require(verify.IsEnabled && local.LoadManualRefund()?.OperationId == fixture.OperationId
                && !start.IsEnabled && !complete.IsEnabled && fixture.Writes == 1, "lost start response retains original recovery and blocks payout controls");
            Invoke(window, "ClearManualRefundReview");
            Require(local.LoadManualRefund() is not null && proof.Text.Length == 0, "privacy reset preserves protected recovery");
            verify.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Require(status.Text.Contains("already started") && !status.Text.Contains("Return the approved amount once"),
                "recovered start never directs another payout");
            confirm.IsChecked = true;
            Require(!complete.IsEnabled, "PromptPay completion requires transfer reference");
            proof.Text = "synthetic-proof";
            Require(complete.IsEnabled, "confirmed PromptPay payout can be recorded");
            complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Require(fixture.Writes == 2 && status.Text.Contains("Refund completed") && !verify.IsEnabled
                && local.LoadManualRefund() is null && proof.Text.Length == 0, "completion clears exact recovery and private proof");
            Console.WriteLine(tamperedTerms
                ? "PASS: changed approval terms cannot produce payout instructions; synthetic fixture only."
                : "PASS: assigned refund selection, lost-start recovery and explicit PromptPay evidence; synthetic fixture only.");
        }
        finally
        {
            assignedRefundFixture = null;
            shiftField.SetValue(window, previousShift);
            Invoke(window, "ClearManualRefundReview");
            ((TabItem)window.FindName("CheckoutTab")).IsSelected = true;
            Invoke(window, "UpdateOperationalState");
        }
    }

    private sealed class AssignedRefundFixture(PosClientConfiguration configuration, Guid shift, bool tamperedTerms)
    {
        public Guid OperationId { get; } = Guid.NewGuid();
        private Guid OrderId { get; } = Guid.NewGuid();
        private DateTimeOffset ApprovedAt { get; } = DateTimeOffset.UtcNow.AddMinutes(-5);
        public int Writes { get; private set; }
        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            Require(path.Contains(configuration.BranchId.ToString("D")), "refund operation uses configured branch");
            if (path.EndsWith("/assigned") && request.Method == HttpMethod.Get)
                return Response(new AssignedManualRefundsV1([State("approved"),
                    State("approved") with { OperationId = Guid.NewGuid(), Amount = 45.1234m }], false));
            Require(path.Contains(OrderId.ToString("D")) && path.Contains(OperationId.ToString("D")), "only original refund is accessed");
            if (request.Method == HttpMethod.Get) return Response(State("started"));
            Writes++;
            if (path.EndsWith("/start"))
            {
                var custody = await request.Content!.ReadFromJsonAsync<ManualRefundCustodyV1>(ct);
                Require(custody == new ManualRefundCustodyV1(configuration.TerminalId, shift, null), "PromptPay binds the shift without a cash drawer");
                if (tamperedTerms) return Response(State("started") with { Amount = 46, Replayed = false });
                throw new HttpRequestException("Synthetic lost response");
            }
            var completion = await request.Content!.ReadFromJsonAsync<CompleteManualRefundV1>(ct);
            Require(path.EndsWith("/complete") && completion is { TransferVerified: true, TransferReference: "synthetic-proof" },
                "completion sends explicit proof only for the original operation");
            return Response(State("completed"));
        }

        private ManualRefundStatusV1 State(string status) => new(OperationId, OrderId, "promptpay_manual", "THB",
            45, "customer_request", status, ApprovedAt, status == "approved" ? null : ApprovedAt.AddMinutes(1),
            status == "completed" ? ApprovedAt.AddMinutes(2) : null, status != "approved");
        private static HttpResponseMessage Response<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
