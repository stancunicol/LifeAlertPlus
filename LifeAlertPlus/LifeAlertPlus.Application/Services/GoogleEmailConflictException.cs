namespace LifeAlertPlus.Application.Services
{
    // Excepție aruncată când un utilizator încearcă să se autentifice cu Google
    // folosind un email deja înregistrat prin altă metodă (email + parolă clasică).
    // MOTIVARE SECURITATE: dacă am permite schimbarea automată a provider-ului,
    // oricine cu acces la contul Google aferent emailului ar putea prelua
    // un cont protejat cu parolă — deci respingem cererea explicit.
    // Prinsă în GoogleAuthController → returnează 409 Conflict cu mesaj clar utilizatorului.
    public class GoogleEmailConflictException : Exception
    {
        public string Email { get; } // Emailul în conflict (folosit în mesajul de eroare din controller)

        public GoogleEmailConflictException(string email)
            : base($"Email '{email}' is already registered with a different sign-in method.")
        {
            Email = email;
        }
    }
}
