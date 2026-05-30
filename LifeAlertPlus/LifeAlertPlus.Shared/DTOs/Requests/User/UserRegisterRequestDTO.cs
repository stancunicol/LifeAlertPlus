namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserRegisterRequestDTO
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool DataProcessingConsent { get; set; }
    }
}
