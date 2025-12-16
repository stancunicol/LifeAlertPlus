using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Client.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace LifeAlertPlus.Client.Pages.Profile
{
    public partial class ProfilePage : ComponentBase
    {
        [Inject]
        private AuthentificationService AuthentificationService { get; set; } = null!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = null!;

        [Inject]
        private NavigationManager Navigation { get; set; } = null!;

        private User CurrentUser { get; set; } = new User();

        private UserUpdateRequestDTO EditUser { get; set; } = new UserUpdateRequestDTO();
        private bool IsEditingPersonal { get; set; } = false;

        private int MonitoredCount { get; set; } = 8;
        private int AlertsCount { get; set; } = 4;
        private int DaysActive { get; set; } = 127;

        private NotificationPreferences Preferences { get; set; } = new NotificationPreferences
        {
            EmailNotifications = true,
            PushNotifications = true,
            CriticalAlerts = true,
            DailySummary = false
        };

        private bool ShowChangePasswordModal { get; set; } = false;
        private UserChangePasswordRequestDTO PasswordChange { get; set; } = new UserChangePasswordRequestDTO();
        private string PasswordError { get; set; } = string.Empty;

        private bool ShowChangeEmailModal { get; set; } = false;
        private UserChangeEmailRequestDTO EmailChange { get; set; } = new UserChangeEmailRequestDTO();
        private string EmailError { get; set; } = string.Empty;

        private bool ShowEmailChangeSuccess { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadCurrentUserAsync();
        }

        private async Task LoadCurrentUserAsync()
        {
            try
            {
                // Încercăm să obținem token-ul din localStorage
                var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
                
                if (!string.IsNullOrEmpty(token))
                {
                    // Extragem informațiile din JWT token
                    var handler = new JwtSecurityTokenHandler();
                    var jsonToken = handler.ReadJwtToken(token);
                    
                    var emailClaim = jsonToken?.Claims?.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Email || x.Type == "email");
                    var firstNameClaim = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName");
                    var lastNameClaim = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName");
                    var userIdClaim = jsonToken?.Claims?.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub);
                    
                    if (emailClaim != null)
                    {
                        CurrentUser.Email = emailClaim.Value;
                        
                        if (firstNameClaim != null)
                        {
                            CurrentUser.FirstName = firstNameClaim.Value;
                        }
                        
                        if (lastNameClaim != null)
                        {
                            CurrentUser.LastName = lastNameClaim.Value;
                        }
                        
                        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                        {
                            CurrentUser.Id = userId;
                        }
                        
                        // Telephone nu e în JWT, folosim valoare default pentru moment
                        if (string.IsNullOrEmpty(CurrentUser.Telephone))
                        {
                            CurrentUser.Telephone = "+40 700 000 000";
                        }
                    }
                }
                
                // Dacă nu avem un token valid sau datele sunt incomplete, folosim valori default
                if (string.IsNullOrEmpty(CurrentUser.Email))
                {
                    // Pentru debugging, setăm valori temporare
                    CurrentUser.FirstName = "User";
                    CurrentUser.LastName = "Test";
                    CurrentUser.Email = "test@email.com";
                    CurrentUser.Telephone = "+40 700 000 000";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading user data: {ex.Message}");
                // Setăm valori default în caz de eroare
                CurrentUser.FirstName = "User";
                CurrentUser.LastName = "Test";
                CurrentUser.Email = "test@email.com";
                CurrentUser.Telephone = "+40 700 000 000";
            }
        }

        private string GetUserInitials(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "?";

            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            return parts[0][0].ToString().ToUpper();
        }

        private void EnableEditPersonal()
        {
            EditUser = new UserUpdateRequestDTO
            {
                FirstName = CurrentUser.FirstName,
                LastName = CurrentUser.LastName,
                Telephone = CurrentUser.Telephone,
            };
            IsEditingPersonal = true;
        }

        private void SavePersonalInfo()
        {
            if(!string.IsNullOrWhiteSpace(EditUser.FirstName))
                CurrentUser.FirstName = EditUser.FirstName;

            if(!string.IsNullOrWhiteSpace(EditUser.FirstName))
                CurrentUser.FirstName = EditUser.FirstName;

            if(!string.IsNullOrWhiteSpace(EditUser.LastName))
                CurrentUser.LastName = EditUser.LastName;

            if(!string.IsNullOrWhiteSpace(EditUser.Telephone))
                CurrentUser.Telephone = EditUser.Telephone;

            IsEditingPersonal = false;
        }

        private void CancelEditPersonal()
        {
            IsEditingPersonal = false;
        }

        private void OpenChangePasswordModal()
        {
            ShowChangePasswordModal = true;
            PasswordChange = new UserChangePasswordRequestDTO();
            PasswordError = string.Empty;
        }

        private void CloseChangePasswordModal()
        {
            ShowChangePasswordModal = false;
            PasswordChange = new UserChangePasswordRequestDTO();
            PasswordError = string.Empty;
        }

        private void ChangePassword()
        {
            PasswordError = string.Empty;

            if (string.IsNullOrWhiteSpace(PasswordChange.CurrentPassword))
            {
                PasswordError = "Type your current password";
                return;
            }

            if (string.IsNullOrWhiteSpace(PasswordChange.NewPassword))
            {
                PasswordError = "Type your new password";
                return;
            }

            if (PasswordChange.NewPassword.Length < 8)
            {
                PasswordError = "Password must be at least 8 characters long";
                return;
            }

            if (PasswordChange.NewPassword != PasswordChange.ConfirmPassword)
            {
                PasswordError = "Passwords do not match";
                return;
            }

            CloseChangePasswordModal();
        }

        private void OpenChangeEmailModal()
        {
            ShowChangeEmailModal = true;
            EmailChange = new UserChangeEmailRequestDTO();
            EmailError = string.Empty;
        }

        private void CloseChangeEmailModal()
        {
            ShowChangeEmailModal = false;
            EmailChange = new UserChangeEmailRequestDTO();
            EmailError = string.Empty;
            ShowEmailChangeSuccess = true;
        }

        private async Task ChangeEmail()
        {
            EmailError = string.Empty;

            if (string.IsNullOrWhiteSpace(EmailChange.NewEmail))
            {
                EmailError = "Enter your new email address";
                return;
            }

            if (!IsValidEmail(EmailChange.NewEmail))
            {
                EmailError = "Invalid email format";
                return;
            }

            if (string.IsNullOrWhiteSpace(EmailChange.ConfirmEmail))
            {
                EmailError = "Confirm your new email address";
                return;
            }

            if (EmailChange.NewEmail != EmailChange.ConfirmEmail)
            {
                EmailError = "Email addresses do not match";
                return;
            }

            if (string.IsNullOrWhiteSpace(EmailChange.CurrentPassword))
            {
                EmailError = "Enter your current password to confirm";
                return;
            }

            UserChangeEmailRequestDTO request = new UserChangeEmailRequestDTO
            {
                CurrentEmail = CurrentUser.Email,
                NewEmail = EmailChange.NewEmail,
                ConfirmEmail = EmailChange.ConfirmEmail,
                CurrentPassword = EmailChange.CurrentPassword
            };

            var result = await AuthentificationService.UpdateEmailAsync(request);

            if (result != null)
            {
                if (result.Success == true)
                {
                    if (result.RequiresLogout)
                    {
                        // Afișez modal-ul de succes înainte de delogare
                        CloseChangeEmailModal();
                        ShowEmailChangeSuccess = true;
                        return;
                    }
                    
                    CurrentUser.Email = EmailChange.NewEmail;
                    CloseChangeEmailModal();
                }
                else
                {
                    EmailError = result.Message ?? "Failed to update email. Please try again.";
                }
            }
            else
            {
                EmailError = "Failed to update email. Please try again.";
            }
        }

        private async Task CloseEmailChangeSuccessModal()
        {
            ShowEmailChangeSuccess = false;
            // Delogez utilizatorul și îl redirect la login
            await AuthentificationService.LogoutAsync();
            Navigation.NavigateTo("/login");
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private class NotificationPreferences
        {
            public bool EmailNotifications { get; set; }
            public bool PushNotifications { get; set; }
            public bool CriticalAlerts { get; set; }
            public bool DailySummary { get; set; }
        }
    }
}
