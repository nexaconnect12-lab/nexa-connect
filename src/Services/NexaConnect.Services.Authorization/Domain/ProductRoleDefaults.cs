namespace NexaConnect.Services.Authorization.Domain;

public static class ProductRoleDefaults
{
    public static string[] PermissionsFor(string roleCode) => roleCode switch
    {
        "tenant-admin" or "store-manager" =>
        [
            "catalog.menu.read", "catalog.menu.write", "inventory.stock.read", "inventory.stock.write",
            "inventory.reservation.create", "inventory.reservation.release", "order.create", "order.read",
            "order.place", "payment.intent.create", "payment.intent.read", "customer.profile.create",
            "customer.profile.read", "restaurant.branch.read", "restaurant.branch.manage",
            "restaurant.configuration.read", "restaurant.configuration.manage", "reporting.dashboard.read", "reporting.sales.read", "reporting.activity.read", "media.asset.read", "media.asset.manage", "notification.send", "notification.read",
            "pos.shift.open", "pos.shift.close", "kitchen.ticket.read", "kitchen.ticket.transition",
            "order.payment-review.read", "order.payment-review.resolve", "order.manual-payment.confirm",
            "pos.cash-review.read", "pos.cash-review.resolve", "pos.day-close.read", "pos.day-close.prepare", "pos.day-close.approve", "payment.refund.create", "payment.refund.read"
        ],
        "cashier" => ["catalog.menu.read", "inventory.stock.read", "inventory.reservation.create", "order.create", "order.read", "order.place", "order.manual-payment.confirm", "payment.intent.create", "payment.intent.read", "payment.refund.read", "customer.profile.read", "pos.shift.open", "pos.shift.close"],
        "inventory-controller" => ["inventory.stock.read", "inventory.stock.write", "inventory.reservation.create", "inventory.reservation.release"],
        "accountant" => ["order.read", "payment.intent.read", "payment.refund.read", "reporting.dashboard.read", "reporting.sales.read", "reporting.activity.read", "order.payment-review.read", "pos.cash-review.read", "pos.day-close.read"],
        "report-viewer" => ["catalog.menu.read", "inventory.stock.read", "order.read", "payment.intent.read", "customer.profile.read", "reporting.dashboard.read", "reporting.sales.read", "reporting.activity.read", "media.asset.read"],
        _ => throw new ArgumentException($"Unsupported product role '{roleCode}'.")
    };
}
