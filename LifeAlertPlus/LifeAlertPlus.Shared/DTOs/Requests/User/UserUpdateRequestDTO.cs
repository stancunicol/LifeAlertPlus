namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserUpdateRequestDTO
    {
        public string? FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; } = string.Empty;
        public string? Telephone { get; set; } = string.Empty;
        public string? FirstDayOfTheWeek { get; set; } = string.Empty;
    }
}