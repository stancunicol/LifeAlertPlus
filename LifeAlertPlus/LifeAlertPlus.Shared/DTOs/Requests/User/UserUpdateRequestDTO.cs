namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de UserController.UpdateUser (PUT /api/user/{id}) — toate câmpurile sunt nullable,
    // null = nu se modifică valoarea curentă (update parțial, nu un PUT complet care suprascrie totul).
    // Client: folosit în 2 contexte distincte — ProfilePage.razor.cs (date personale + praguri vitale proprii)
    // și SettingsPage.razor.cs (preferințe: limbă, temă, notificări), ambele prin UserApiClient.UpdateUserAsync.
    public class UserUpdateRequestDTO
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FirstDayOfTheWeek { get; set; }
        public string? Language { get; set; }
        public string? ThemeColor { get; set; }
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public float? MinTemperature { get; set; }
        public float? MaxTemperature { get; set; }
        public int? MinSpO2 { get; set; }
        public int? MaxSpO2 { get; set; }
        public int? UpdateFrequency { get; set; }
        public bool? NotifyByEmail { get; set; }
        public bool? NotifyByPush { get; set; }
        public bool? NotifyBySms { get; set; }
        public bool? EnableDailyReport { get; set; }
    }
}