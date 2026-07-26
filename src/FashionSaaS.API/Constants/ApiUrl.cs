namespace FashionSaaS.API.Constants;

internal static class ApiUrl
{
    internal static class Auth
    {
        public const string Login = "api/auth/login";
        public const string LoginMfa = "api/auth/login/mfa";
        public const string Refresh = "api/auth/refresh";
        public const string Logout = "api/auth/logout";
        public const string ForgotPassword = "api/auth/forgot-password";
        public const string ResetPassword = "api/auth/reset-password";
        public const string ChangePassword = "api/auth/change-password";
    }

    internal static class AdminMfa
    {
        public const string Setup = "api/admin/mfa/setup";
        public const string VerifySetup = "api/admin/mfa/verify-setup";
        public const string BackupCodes = "api/admin/mfa/backup-codes";
        public const string RegenerateBackupCodes = "api/admin/mfa/regenerate-backup-codes";
    }


    internal static class TenantMfa
    {
        public const string Setup = "api/tenant/mfa/setup";
        public const string VerifySetup = "api/tenant/mfa/verify-setup";
        public const string RegenerateBackupCodes = "api/tenant/mfa/regenerate-backup-codes";
    }

    internal static class AdminTenants
    {
        public const string GetAll = "api/admin/tenants";
        public const string GetById = "api/admin/tenants/{id}";
        public const string Create = "api/admin/tenants";
        public const string Update = "api/admin/tenants/{id}";
        public const string Suspend = "api/admin/tenants/{id}/suspend";
        public const string Activate = "api/admin/tenants/{id}/activate";
        public const string Delete = "api/admin/tenants/{id}";
    }

    internal static class AdminUsers
    {
        public const string GetAll = "api/admin/users";
        public const string GetById = "api/admin/users/{id}";
        public const string Create = "api/admin/users";
        public const string Update = "api/admin/users/{id}";
        public const string Delete = "api/admin/users/{id}";
        public const string Unlock = "api/admin/users/{id}/unlock";
    }

    internal static class AdminSubscriptionPlans
    {
        public const string GetAll = "api/admin/subscription-plans";
        public const string GetById = "api/admin/subscription-plans/{id}";
        public const string Create = "api/admin/subscription-plans";
        public const string Update = "api/admin/subscription-plans/{id}";
        public const string Delete = "api/admin/subscription-plans/{id}";
    }

    internal static class AdminSubscriptions
    {
        public const string GetAll = "api/admin/subscriptions";
        public const string GetById = "api/admin/subscriptions/{id}";
        public const string Assign = "api/admin/subscriptions";
        public const string ChangePlan = "api/admin/subscriptions/{id}/change-plan";
        public const string Suspend = "api/admin/subscriptions/{id}/suspend";
        public const string Reactivate = "api/admin/subscriptions/{id}/reactivate";
    }

    internal static class AdminPayments
    {
        public const string GetAll = "api/admin/payments";
        public const string GetById = "api/admin/payments/{id}";
        public const string Confirm = "api/admin/payments/{id}/confirm";
    }

    internal static class AdminBankAccount
    {
        public const string Get = "api/admin/bank-account";
        public const string GetFull = "api/admin/bank-account/full";
        public const string Create = "api/admin/bank-account";
        public const string Update = "api/admin/bank-account";
    }

    internal static class AdminAuditLogs
    {
        public const string GetAll = "api/admin/audit-logs";
        public const string GetById = "api/admin/audit-logs/{id}";
    }

    internal static class AdminLoginAttempts
    {
        public const string GetAll = "api/admin/login-attempts";
    }

    internal static class TenantProfile
    {
        public const string Get = "api/tenant/profile";
        public const string Update = "api/tenant/profile";
    }

    internal static class TenantUsers
    {
        public const string GetAll = "api/tenant/users";
        public const string GetById = "api/tenant/users/{id}";
        public const string Create = "api/tenant/users";
        public const string Update = "api/tenant/users/{id}";
        public const string AssignRole = "api/tenant/users/{id}/assign-role";
        public const string Delete = "api/tenant/users/{id}";
    }

    internal static class TenantSubscription
    {
        public const string Get = "api/tenant/subscription";
        public const string GetPayments = "api/tenant/subscription/payments";
    }

    internal static class TenantBankAccount
    {
        public const string Get = "api/tenant/bank-account";
        public const string GetFull = "api/tenant/bank-account/full";
        public const string Create = "api/tenant/bank-account";
        public const string Update = "api/tenant/bank-account";
    }

    internal static class TenantCategories
    {
        public const string GetAll = "api/tenant/categories";
        public const string GetTree = "api/tenant/categories/tree";
        public const string GetById = "api/tenant/categories/{id}";
        public const string Create = "api/tenant/categories";
        public const string Update = "api/tenant/categories/{id}";
        public const string Move = "api/tenant/categories/{id}/move";
        public const string Reorder = "api/tenant/categories/reorder";
        public const string Delete = "api/tenant/categories/{id}";
    }

    internal static class TenantProducts
    {
        public const string GetAll = "api/tenant/products";
        public const string GetById = "api/tenant/products/{id}";
        public const string GetBySlug = "api/tenant/products/slug/{slug}";
        public const string Create = "api/tenant/products";
        public const string Update = "api/tenant/products/{id}";
        public const string Publish = "api/tenant/products/{id}/publish";
        public const string Archive = "api/tenant/products/{id}/archive";
        public const string Delete = "api/tenant/products/{id}";
    }

    internal static class TenantProductVariants
    {
        public const string GetByProduct = "api/tenant/products/{productId}/variants";
        public const string Add = "api/tenant/variants";
        public const string Update = "api/tenant/variants/{id}";
        public const string Deactivate = "api/tenant/variants/{id}/deactivate";
        public const string Delete = "api/tenant/variants/{id}";
    }

    internal static class TenantProductImages
    {
        public const string GetByProduct = "api/tenant/products/{productId}/images";
        public const string Upload = "api/tenant/products/images";
        public const string Reorder = "api/tenant/products/{productId}/images/reorder";
        public const string SetPrimary = "api/tenant/products/images/{id}/set-primary";
        public const string Delete = "api/tenant/products/images/{id}";
    }

    internal static class TenantInventory
    {
        public const string AdjustStock = "api/tenant/inventory/adjust";
        public const string GetLowStock = "api/tenant/inventory/low-stock";
        public const string GetStockHistory = "api/tenant/inventory/variants/{variantId}/history";
    }

    internal static class TenantCustomers
    {
        public const string GetAll = "api/tenant/customers";
        public const string GetById = "api/tenant/customers/{id}";
        public const string Create = "api/tenant/customers";
        public const string Update = "api/tenant/customers/{id}";
        public const string Deactivate = "api/tenant/customers/{id}/deactivate";
    }

    internal static class TenantDiscounts
    {
        public const string GetAll = "api/tenant/discounts";
        public const string GetById = "api/tenant/discounts/{id}";
        public const string GetByCode = "api/tenant/discounts/code/{code}";
        public const string Create = "api/tenant/discounts";
        public const string Update = "api/tenant/discounts/{id}";
        public const string Deactivate = "api/tenant/discounts/{id}/deactivate";
        public const string Delete = "api/tenant/discounts/{id}";
    }

    internal static class TenantReviews
    {
        public const string GetAll = "api/tenant/reviews";
        public const string GetById = "api/tenant/reviews/{id}";
        public const string Approve = "api/tenant/reviews/{id}/approve";
        public const string Reject = "api/tenant/reviews/{id}/reject";
        public const string Delete = "api/tenant/reviews/{id}";
    }


    internal static class TenantNotifications
    {
        public const string GetAll = "api/tenant/notifications";
        public const string GetUnreadCount = "api/tenant/notifications/unread-count";
        public const string MarkRead = "api/tenant/notifications/{id}/mark-read";
        public const string MarkAllRead = "api/tenant/notifications/mark-all-read";
    }

    internal static class TenantWishlists
    {
        public const string GetByCustomer = "api/tenant/customers/{customerId}/wishlist";
        public const string RemoveItem = "api/tenant/wishlist/items/{itemId}";
    }

    internal static class AccountWishlist
    {
        public const string GetMine = "api/account/wishlist";
        public const string Add = "api/account/wishlist";
        public const string Remove = "api/account/wishlist/{itemId}";
    }

    internal static class StoreOrders
    {
        public const string Create = "api/store/orders";
        public const string GetMine = "api/store/orders";
        public const string GetById = "api/store/orders/{id}";
        public const string Cancel = "api/store/orders/{id}/cancel";
        public const string GetPaymentProof = "api/store/orders/{id}/payment-proof";
    }

    internal static class StoreReviews
    {
        public const string Submit = "api/store/reviews";
    }

    internal static class TenantOrders
    {
        public const string GetAll = "api/tenant/orders";
        public const string GetById = "api/tenant/orders/{id}";
        public const string Confirm = "api/tenant/orders/{id}/confirm";
        public const string Ship = "api/tenant/orders/{id}/ship";
        public const string Deliver = "api/tenant/orders/{id}/deliver";
        public const string Cancel = "api/tenant/orders/{id}/cancel";
        public const string GetPaymentProof = "api/tenant/orders/{id}/payment-proof";
    }

    internal static class TenantReports
    {
        public const string Summary = "api/tenant/reports/summary";
        public const string SalesOverTime = "api/tenant/reports/sales-over-time";
        public const string TopProducts = "api/tenant/reports/top-products";
        public const string StatusBreakdown = "api/tenant/reports/order-status-breakdown";
        public const string CustomerAnalytics = "api/tenant/reports/customer-analytics";
        public const string InventoryTrends = "api/tenant/reports/inventory-trends";
        public const string CategorySales = "api/tenant/reports/category-sales";
    }

    // Public, unauthenticated storefront catalog-browsing routes. The leading {slug}
    // segment is resolved by TenantResolutionMiddleware (context.GetRouteValue("slug"))
    // before these actions run — it must stay the literal first URL segment for that to work.
    internal static class PublicCatalog
    {
        public const string GetCategories = "api/{slug}/categories";
        public const string GetProducts = "api/{slug}/products";
        public const string GetProductById = "api/{slug}/products/{id}";
        public const string GetProductVariants = "api/{slug}/products/{id}/variants";
        public const string GetPaymentInstructions = "api/{slug}/payment-instructions";
    }
}
