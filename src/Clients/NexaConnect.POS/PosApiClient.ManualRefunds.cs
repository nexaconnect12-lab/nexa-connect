using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using NexaConnect.Contracts.POS;

namespace NexaConnect.POS;

public sealed partial class PosApiClient
{
    private string ManualRefundBranchPath =>
        $"api/pos/v1/customer/organizations/{_configuration.OrganizationId:D}/branches/{_configuration.BranchId:D}";
    private string ManualRefundOrderPath(Guid orderId) => $"{ManualRefundBranchPath}/orders/{orderId:D}/manual-refunds";

    public async Task RegisterManualRefundAvailabilityAsync(PosTokenSet token, ManualRefundAvailabilityV1 availability, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<ManualRefundAvailabilityResultV1>(token, HttpMethod.Post,
            $"{ManualRefundBranchPath}/manual-refunds/available", availability, "pos.manual-refund.available", ct);
        if (!result.Available) throw new InvalidDataException("Cashier availability could not be verified.");
    }

    public async Task<ManualRefundPreviewV1> ManualRefundPreviewAsync(PosTokenSet token, Guid orderId, CancellationToken ct)
    {
        var preview = await SendManualRefundAsync<ManualRefundPreviewV1>(token, HttpMethod.Get, $"{ManualRefundOrderPath(orderId)}/preview",
            null, "pos.manual-refund.preview", ct);
        if (preview.OrderId != orderId || preview.Currency != _configuration.Currency || preview.Method is not ("cash" or "promptpay_manual")
            || preview.PaidAmount <= 0 || preview.AvailableAmount < 0 || preview.AvailableAmount > preview.PaidAmount)
            throw new InvalidDataException("Invalid original refund preview.");
        return preview;
    }

    public async Task<ManualRefundCashiersV1> ManualRefundCashiersAsync(PosTokenSet token, Guid orderId, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<ManualRefundCashiersV1>(token, HttpMethod.Get, $"{ManualRefundOrderPath(orderId)}/cashiers",
            null, "pos.manual-refund.cashiers", ct);
        if (result.Items is null || result.Items.Count > 50 || result.Items.Any(row => row is null || row.ShiftId == Guid.Empty
            || !SafeText(row.Subject, 200) || !SafeText(row.TerminalCode, 64) || !SafeText(row.ShiftNumber, 128)))
            throw new InvalidDataException("Invalid cashier availability.");
        return result;

        static bool SafeText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    }

    public async Task<ManualRefundStatusV1> ApproveManualRefundAsync(PosTokenSet token, Guid orderId, ApproveManualRefundV1 request, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<ManualRefundStatusV1>(token, HttpMethod.Post, $"{ManualRefundOrderPath(orderId)}/approve",
            request, "pos.manual-refund.approve", ct);
        ValidateManualRefundStatus(result, orderId, request.OperationId);
        if (result.Amount != request.Amount || result.Reason != request.Reason)
            throw new InvalidDataException("Approval response differs from the requested refund.");
        return result;
    }

    public async Task<ManualRefundStatusV1> ReleaseManualRefundAsync(PosTokenSet token, Guid orderId, Guid operationId, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<ManualRefundStatusV1>(token, HttpMethod.Post, $"{ManualRefundOrderPath(orderId)}/{operationId:D}/release",
            null, "pos.manual-refund.release", ct);
        ValidateManualRefundStatus(result, orderId, operationId);
        if (result.Status != "released") throw new InvalidDataException("Release could not be verified.");
        return result;
    }

    public async Task<AssignedManualRefundsV1> ManagedManualRefundsAsync(PosTokenSet token, Guid orderId, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<AssignedManualRefundsV1>(token, HttpMethod.Get,
            $"{ManualRefundOrderPath(orderId)}/managed", null, "pos.manual-refund.managed", ct);
        if (result.Items is null || result.Items.Count > 25 || result.Items.Select(item => item?.OperationId).Distinct().Count() != result.Items.Count)
            throw new InvalidDataException("Invalid manager refund history.");
        foreach (var item in result.Items) ValidateManualRefundStatus(item, orderId, item?.OperationId ?? Guid.Empty);
        return result;
    }

    public async Task<AssignedManualRefundsV1> AssignedManualRefundsAsync(PosTokenSet token, CancellationToken ct)
    {
        var result = await SendManualRefundAsync<AssignedManualRefundsV1>(token, HttpMethod.Get,
            $"{ManualRefundBranchPath}/manual-refunds/assigned", null, "pos.manual-refund.assigned", ct);
        if (result.Items is null || result.Items.Count > 25 || result.Items.Select(item => item?.OperationId).Distinct().Count() != result.Items.Count)
            throw new InvalidDataException("Invalid refund queue.");
        foreach (var item in result.Items)
        {
            ValidateManualRefundStatus(item, item?.OrderId ?? Guid.Empty, item?.OperationId ?? Guid.Empty);
            if (item!.Status is "completed" or "released") throw new InvalidDataException("Invalid outstanding refund status.");
        }
        return result;
    }

    public async Task<ManualRefundStatusV1> ManualRefundOperationAsync(PosTokenSet token, Guid orderId, Guid operationId,
        ManualRefundCustodyV1? start, CompleteManualRefundV1? completion, CancellationToken ct)
    {
        if (orderId == Guid.Empty || operationId == Guid.Empty || start is not null && completion is not null)
            throw new ArgumentException("The original refund operation is required.");
        string action = completion is not null ? "complete" : start is not null ? "start" : "read";
        string path = $"{ManualRefundBranchPath}/orders/{orderId:D}/manual-refunds/{operationId:D}";
        if (action != "read") path += "/" + action;
        var result = await SendManualRefundAsync<ManualRefundStatusV1>(token, action == "read" ? HttpMethod.Get : HttpMethod.Post,
            path, (object?)completion ?? start, "pos.manual-refund." + action, ct);
        ValidateManualRefundStatus(result, orderId, operationId);
        if (start is not null && result.Status == "approved" || completion is not null && result.Status != "completed")
            throw new InvalidDataException("The original refund status could not be confirmed.");
        return result;
    }

    private async Task<T> SendManualRefundAsync<T>(PosTokenSet token, HttpMethod method, string path, object? body,
        string operation, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = CreateRequest(method, path, token);
        AddTenantContext(request, _configuration.OrganizationId);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await SendCheckoutAsync(_httpClient, request, operation, Guid.NewGuid(), deadline.Token, true);
        await EnsureSuccessAsync(response, "Manual refund status unavailable. Verify the original operation before taking further action.");
        await response.Content.LoadIntoBufferAsync(32768, deadline.Token);
        return await response.Content.ReadFromJsonAsync<T>(deadline.Token) ?? throw new InvalidDataException("Empty manual refund response.");
    }

    private void ValidateManualRefundStatus(ManualRefundStatusV1? status, Guid orderId, Guid operationId)
    {
        if (status is null || orderId == Guid.Empty || operationId == Guid.Empty
            || status.OrderId != orderId || status.OperationId != operationId
            || status.Method is not ("cash" or "promptpay_manual") || status.Currency != _configuration.Currency
            || status.Amount <= 0 || decimal.Round(status.Amount, 4) != status.Amount
            || status.Reason is not ("customer_request" or "duplicate_charge" or "item_unavailable" or "service_issue" or "other")
            || status.ApprovedAtUtc == default
            || status.Status is not ("approved" or "started" or "completed" or "released")
            || (status.Status is "approved" or "released") != (status.StartedAtUtc is null)
            || (status.Status == "completed") != (status.CompletedAtUtc is not null)
            || (status.Status == "released") != (status.ReleasedAtUtc is not null)
            || status.StartedAtUtc < status.ApprovedAtUtc || status.CompletedAtUtc < status.StartedAtUtc
            || status.ReleasedAtUtc < status.ApprovedAtUtc)
            throw new InvalidDataException("The refund status does not match the requested operation.");
    }
}
