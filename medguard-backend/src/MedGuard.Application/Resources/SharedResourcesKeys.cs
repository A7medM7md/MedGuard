namespace MedGuard.Application.Resources
{
    public static class SharedResourcesKeys
    {

        #region General
        // Common response messages returned from any endpoint

        public const string Required = "Required";                               // Field is required
        public const string NotFound = "NotFound";                               // Generic not found
        public const string Deleted = "Deleted";                                 // Resource deleted successfully
        public const string Created = "Created";                                 // Resource created successfully
        public const string Success = "Success";                                 // Operation succeeded
        public const string NotEmpty = "NotEmpty";                               // Field must not be empty
        public const string Updated = "Updated";                                 // Resource updated successfully
        public const string UnAuthorized = "UnAuthorized";                       // 401 – not authenticated
        public const string UnprocessableEntity = "UnprocessableEntity";         // 422 – validation failed
        public const string BadRequest = "BadRequest";                           // 400 – bad request
        public const string InternalServerError = "InternalServerError";         // 500 – server error
        public const string MaxLengthis100 = "MaxLengthis100";                   // Max length is 100 characters
        public const string IsExist = "IsExist";                                 // Resource already exists
        public const string IsNotExist = "IsNotExist";                           // Resource does not exist
        public const string UnexpectedError = "UnexpectedError";                 // Unexpected/unhandled error occurred

        #endregion

        #region Validation
        // Input validation error messages

        public const string ValidationError = "ValidationError";                 // Validation error occurred
        public const string MinLengthis3 = "MinLengthis3";                       // Minimum length is 3 characters
        public const string MinLengthis8 = "MinLengthis8";                       // Minimum length is 8 characters
        public const string InvalidEmail = "InvalidEmail";                       // Email format is invalid
        public const string InvalidHoursRange = "InvalidHoursRange";             // Hours value must be greater than 0

        #endregion

        #region Users
        // User management: register, edit, delete, password, roles

        public const string DepartmentId = "DepartmentId";                           // Department ID validation
        public const string PasswordNotEqualConfirmPass = "PasswordNotEqualConfirmPass"; // Password and confirm password do not match
        public const string EmailIsExist = "EmailIsExist";                           // Email is already registered
        public const string UserNameIsExist = "UserNameIsExist";                     // Username is already taken
        public const string FaildToAddUser = "FaildToAddUser";                       // Failed to create user
        public const string UpdateFailed = "UpdateFailed";                           // Update operation failed
        public const string DeletedFailed = "DeletedFailed";                         // Delete operation failed
        public const string ChangePassFailed = "ChangePassFailed";                   // Change password failed
        public const string UserName = "UserName";                                   // Username field label
        public const string Password = "Password";                                   // Password field label
        public const string UserNameIsNotExist = "UserNameIsNotExist";               // Username not found
        public const string PasswordNotCorrect = "PasswordNotCorrect";               // Password is incorrect
        public const string UserIsNotFound = "UserIsNotFound";                       // User not found by ID or email
        public const string RoleDoesNotExist = "RoleDoesNotExist";                   // Specified role does not exist
        public const string FailedToRemoveRoles = "FailedToRemoveRoles";             // Failed to remove user's current roles
        public const string FailedToAssignRoleToUser = "FailedToAssignRoleToUser";   // Failed to assign new role to user
        public const string PasswordChangedSuccessfully = "PasswordChangedSuccessfully"; // Password changed successfully
        public const string UserCreatedSuccessfully = "UserCreatedSuccessfully"; // User account created and email sent
        public const string CannotDeleteOwnAccount = "CannotDeleteOwnAccount"; // User cannot delete their own account

        #endregion

        #region Authentication
        // Sign in, tokens, lockout, refresh

        public const string AccountLocked = "AccountLocked";                         // Account is locked out
        public const string InvalidEmailOrPassword = "InvalidEmailOrPassword";       // Email or password is incorrect
        public const string AlgorithmIsWrong = "AlgorithmIsWrong";                   // JWT algorithm mismatch
        public const string TokenIsNotExpired = "TokenIsNotExpired";                 // Access token has not expired yet
        public const string TokenIsExpired = "TokenIsExpired";                       // Access token has expired
        public const string InvalidTokenFormat = "InvalidTokenFormat";               // Token format cannot be decoded
        public const string InvalidAccessToken = "InvalidAccessToken";               // Access token is invalid
        public const string InvalidRefreshToken = "InvalidRefreshToken";             // Refresh token is invalid
        public const string RefreshTokenIsNotFound = "RefreshTokenIsNotFound";       // Refresh token not found
        public const string RefreshTokenIsExpired = "RefreshTokenIsExpired";         // Refresh token has expired
        public const string UserIdInAccessTokenNotFound = "UserIdInAccessTokenNotFound"; // User ID claim missing in token
        public const string Forbidden = "Forbidden";

        #endregion

        #region Email & Confirmation
        // Email sending, account confirmation, password reset codes

        public const string Email = "Email";                                         // Email field label
        public const string Message = "Message";                                     // Message field label
        public const string SendEmailFailed = "SendEmailFailed";                     // Failed to send email
        public const string EmailNotConfirmed = "EmailNotConfirmed";                 // Email address is not confirmed
        public const string EmailAlreadyConfirmed = "EmailAlreadyConfirmed";         // Email is already confirmed
        public const string ConfirmEmailDone = "ConfirmEmailDone";                   // Email confirmed successfully
        public const string ErrorWhenConfirmEmail = "ErrorWhenConfirmEmail";         // Error occurred during email confirmation
        public const string ConfirmationEmailSent = "ConfirmationEmailSent";         // A new confirmation email has been sent
        public const string TryToRegisterAgain = "TryToRegisterAgain";               // Please try to register again
        public const string TryAgainInAnotherTime = "TryAgainInAnotherTime";         // Please try again later
        public const string TryRequestCodeAgain = "TryRequestCodeAgain";             // Please request the code again

        #endregion

    }
}
