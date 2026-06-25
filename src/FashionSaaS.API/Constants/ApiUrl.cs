namespace FashionSaaS.API.Constants;

public static class ApiUrl
{
    public static class Auth
    {
        public const string Login = "api/auth/login";
        public const string LoginMfa = "api/auth/login/mfa";
        public const string Refresh = "api/auth/refresh";
        public const string Logout = "api/auth/logout";
        public const string ForgotPassword = "api/auth/forgot-password";
        public const string ResetPassword = "api/auth/reset-password";
        public const string ChangePassword = "api/auth/change-password";
    }

    public static class AdminMfa
    {
        public const string Setup = "api/admin/mfa/setup";
        public const string VerifySetup = "api/admin/mfa/verify-setup";
        public const string BackupCodes = "api/admin/mfa/backup-codes";
        public const string RegenerateBackupCodes = "api/admin/mfa/regenerate-backup-codes";
    }

    public static class AdminTenants
    {
        public const string GetAll = "api/admin/tenants";
        public const string GetById = "api/admin/tenants/{id}";
        public const string Create = "api/admin/tenants";
        public const string Update = "api/admin/tenants/{id}";
        public const string Suspend = "api/admin/tenants/{id}/suspend";
        public const string Activate = "api/admin/tenants/{id}/activate";
        public const string Delete = "api/admin/tenants/{id}";
    }

    public static class AdminUsers
    {
        public const string GetAll = "api/admin/users";
        public const string GetById = "api/admin/users/{id}";
        public const string Create = "api/admin/users";
        public const string Update = "api/admin/users/{id}";
        public const string Delete = "api/admin/users/{id}";
        public const string Unlock = "api/admin/users/{id}/unlock";
    }

    public static class AdminSubscriptionPlans
    {
        public const string GetAll = "api/admin/subscription-plans";
        public const string GetById = "api/admin/subscription-plans/{id}";
        public const string Create = "api/admin/subscription-plans";
        public const string Update = "api/admin/subscription-plans/{id}";
        public const string Delete = "api/admin/subscription-plans/{id}";
    }

    public static class AdminSubscriptions
    {
        public const string GetAll = "api/admin/subscriptions";
        public const string GetById = "api/admin/subscriptions/{id}";
        public const string Assign = "api/admin/subscriptions";
        public const string ChangePlan = "api/admin/subscriptions/{id}/change-plan";
        public const string Suspend = "api/admin/subscriptions/{id}/suspend";
        public const string Reactivate = "api/admin/subscriptions/{id}/reactivate";
    }

    public static class AdminPayments
    {
        public const string GetAll = "api/admin/payments";
        public const string GetById = "api/admin/payments/{id}";
        public const string Confirm = "api/admin/payments/{id}/confirm";
    }

    public static class AdminBankAccount
    {
        public const string Get = "api/admin/bank-account";
        public const string GetFull = "api/admin/bank-account/full";
        public const string Create = "api/admin/bank-account";
        public const string Update = "api/admin/bank-account";
    }

    public static class AdminAuditLogs
    {
        public const string GetAll = "api/admin/audit-logs";
        public const string GetById = "api/admin/audit-logs/{id}";
    }

    public static class AdminLoginAttempts
    {
        public const string GetAll = "api/admin/login-attempts";
    }

    public static class TenantProfile
    {
        public const string Get = "api/tenant/profile";
        public const string Update = "api/tenant/profile";
    }

    public static class TenantUsers
    {
        public const string GetAll = "api/tenant/users";
        public const string GetById = "api/tenant/users/{id}";
        public const string Create = "api/tenant/users";
        public const string Update = "api/tenant/users/{id}";
        public const string AssignRole = "api/tenant/users/{id}/assign-role";
        public const string Delete = "api/tenant/users/{id}";
    }

    public static class TenantSubscription
    {
        public const string Get = "api/tenant/subscription";
        public const string GetPayments = "api/tenant/subscription/payments";
    }

    public static class TenantBankAccount
    {
        public const string Get = "api/tenant/bank-account";
        public const string GetFull = "api/tenant/bank-account/full";
        public const string Create = "api/tenant/bank-account";
        public const string Update = "api/tenant/bank-account";
    }
}
