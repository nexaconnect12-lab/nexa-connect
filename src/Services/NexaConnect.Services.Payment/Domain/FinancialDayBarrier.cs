namespace NexaConnect.Services.Payment.Domain;

public static class FinancialDayBarrier
{
    public static void ValidateTransition(string? previous, string next, bool liveLease, bool hasDecision)
    {
        if (next is not ("armed" or "committed" or "aborted") || (next == "armed") == hasDecision)
            throw new ArgumentException("Invalid barrier decision.");
        if (next == "armed" && (previous == "aborted" || previous is null && !liveLease)
            || next == "committed" && previous is not ("armed" or "committed")
            || previous == "committed" && next == "aborted" || previous == "aborted" && next != "aborted")
            throw new InvalidOperationException("Barrier transition conflicts with retained evidence.");
    }
}
