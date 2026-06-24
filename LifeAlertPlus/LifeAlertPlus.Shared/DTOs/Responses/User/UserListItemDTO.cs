namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    // Server: returnat de UserController (GET, listă) — folosit de Admin pentru a vedea toți utilizatorii
    // (fără PasswordHash sau alte câmpuri sensibile din entitatea User).
    // Client: consumat de UsersPage.razor.cs (tabelul de administrare a conturilor).
    public class UserListItemDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Provider { get; set; } = "Local";
        public string Role { get; set; } = "User";
        public string? ProfilePictureUrl { get; set; }
        public bool IsEmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? LastChangedPasswordAt { get; set; }
    }
}
