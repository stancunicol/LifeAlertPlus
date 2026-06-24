namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.ForgotPassword (POST /api/authentication/forgot-password) —
    // răspunde mereu cu 200 generic, indiferent dacă emailul există, ca să nu permită enumerarea conturilor
    // (vezi AuthenticationControllerTests.cs: ForgotPassword_Returns200_WhenUserNotFound).
    // Client: LoginPage.razor.cs nu instanțiază această clasă direct — trimite un obiect anonim
    // { Email = ... } prin Http.PostAsJsonAsync, care se serializează în același format JSON.
    public class UserForgotPasswordRequestDTO
    {
        public string Email { get; set; } = string.Empty;
    }
}
