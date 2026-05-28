namespace LifeAlertPlus.Application.Services
{
    /// <summary>
    /// Raised when a user tries to sign in with Google using an email that already
    /// exists in the system but was registered through a different provider (e.g. the
    /// classic email/password flow). Forcing the existing account to switch provider
    /// would allow anyone with Google access to that email to hijack a password-protected
    /// account, so we reject the request instead.
    /// </summary>
    public class GoogleEmailConflictException : Exception
    {
        public string Email { get; }

        public GoogleEmailConflictException(string email)
            : base($"Email '{email}' is already registered with a different sign-in method.")
        {
            Email = email;
        }
    }
}
