namespace NexaConnect.Contracts.Platform;

public static class ProductPermissions
{
    public const string CatalogMenuRead = "catalog.menu.read";
    public const string CatalogMenuWrite = "catalog.menu.write";
    public const string InventoryStockRead = "inventory.stock.read";
    public const string InventoryStockWrite = "inventory.stock.write";
    public const string InventoryReservationCreate = "inventory.reservation.create";
    public const string InventoryReservationRelease = "inventory.reservation.release";
    public const string OrderCreate = "order.create";
    public const string OrderRead = "order.read";
    public const string OrderPlace = "order.place";
    public const string PaymentIntentCreate = "payment.intent.create";
    public const string PaymentIntentRead = "payment.intent.read";
    public const string CustomerProfileCreate = "customer.profile.create";
    public const string CustomerProfileRead = "customer.profile.read";
    public const string RestaurantBranchRead = "restaurant.branch.read";
    public const string RestaurantBranchManage = "restaurant.branch.manage";
    public const string RestaurantConfigurationRead = "restaurant.configuration.read";
    public const string RestaurantConfigurationManage = "restaurant.configuration.manage";
    public const string ReportingDashboardRead = "reporting.dashboard.read";
    public const string ReportingSalesRead = "reporting.sales.read";
    public const string MediaAssetRead = "media.asset.read";
    public const string MediaAssetManage = "media.asset.manage";
    public const string ReportingActivityRead = "reporting.activity.read";
    public const string NotificationSend = "notification.send";
    public const string NotificationRead = "notification.read";

    public static IReadOnlyCollection<string> CustomerTenantApiPermissions { get; } =
    [
        CatalogMenuRead, CatalogMenuWrite,
        InventoryStockRead, InventoryStockWrite, InventoryReservationCreate, InventoryReservationRelease,
        OrderCreate, OrderRead, OrderPlace,
        PaymentIntentCreate, PaymentIntentRead,
        CustomerProfileCreate, CustomerProfileRead,
        RestaurantBranchRead, RestaurantBranchManage,
        RestaurantConfigurationRead, RestaurantConfigurationManage,
        ReportingDashboardRead, ReportingSalesRead, ReportingActivityRead, MediaAssetRead, MediaAssetManage,
        NotificationSend, NotificationRead
    ];
}
