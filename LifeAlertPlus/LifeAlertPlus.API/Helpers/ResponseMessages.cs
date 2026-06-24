namespace LifeAlertPlus.API.Helpers
{
    // Constante pentru mesajele de eroare returnate de API
    // Centralizarea mesajelor asigură consistența textelor în toată aplicația
    // și facilitează traducerea sau modificarea lor ulterioară
    internal static class ResponseMessages
    {
        internal const string InvalidToken = "Invalid token."; // Token invalid sau expirat
        internal const string MonitoredPersonNotFound = "Monitored person not found."; // Persoana monitorizată nu există
        internal const string UserNotFound = "User not found."; // Utilizatorul nu există în DB
        internal const string InvitationNotFound = "Invitation not found."; // Invitația nu există sau a expirat
    }
}
