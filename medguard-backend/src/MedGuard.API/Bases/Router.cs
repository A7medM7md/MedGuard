namespace MedGuard.Api.Bases
{
    public static class Router
    {
        public const string Root = "api";
        public const string Version = "v1";
        public const string Base = Root + "/" + Version;

        public const string ByIdRoute = "/{id}";

        // ================= User Routes =================
        public static class UserRouting
        {
            public const string Prefix = Base + "/users";
            public const string Create = Prefix;                  // POST: api/v1/users
            public const string AddUser = Base + "/users";                  // POST: api/v1/users
            public const string GetAll = Prefix;      // GET: api/v1/users
            public const string GetById = Prefix + ByIdRoute;     // GET: api/v1/users/{id}
            public const string GetProfile = Prefix + "/profile";
            public const string EditProfile = Prefix + "/profile";
            public const string Update = Prefix + ByIdRoute;      // PUT: api/v1/users/{id}
            public const string Delete = Prefix + ByIdRoute;      // DELETE: api/v1/users/{id}
            public const string ChangePassword = Prefix + "/changePassword" + ByIdRoute;      // PUT: api/v1/users/changePassword/{id}
            public const string AssignRole = Prefix + ByIdRoute + "/assign-role";      // POST: api/v1/users/{id}/assign-role
        }

        // ================= AuthN Routes =================
        public static class AuthenticationRouting
        {
            public const string Prefix = Base + "/authentication";
            public const string SignIn = Prefix + "/signin";
            public const string Register = Prefix + "/register";
            public const string RefreshToken = Prefix + "/refresh";
            public const string ValidateToken = Prefix + "/validate";
            public const string ConfirmEmail = Prefix + "/confirm-email";
            public const string SendResetPasswordCode = Prefix + "/send-reset-password-code";
            public const string VerifyResetPasswordCode = Prefix + "/verify-reset-password-code";
            public const string ResetPassword = Prefix + "/reset-password";
        }

        // ================= Email Routes =================
        public static class EmailRouting
        {
            public const string Prefix = Base + "/emails";

            public const string Send = Prefix; // POST: api/v1/emails

        }

        // ================= Notification Routes =================
        public static class NotificationRouting
        {
            public const string Prefix = Base + "/notifications";
            public const string GetAll = Prefix;                              // GET: api/v1/notifications
            public const string MarkRead = Prefix + ByIdRoute + "/mark-read";  // POST: api/v1/notifications/{id}/mark-read
            public const string MarkAllRead = Prefix + "/mark-all-read";       // POST: api/v1/notifications/mark-all-read
            public const string GetPreferences = Prefix + "/preferences";
            public const string UpdatePreferences = Prefix + "/preferences";
        }
    }
}
