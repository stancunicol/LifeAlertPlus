using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;

namespace LifeAlertPlus.Client.Pages.Profile
{
    public partial class ProfilePage : ComponentBase
    {
        [Inject]
        private AuthenticationService AuthenticationService { get; set; } = null!;

        [Inject]
        private UserService UserService { get; set; } = null!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = null!;

        [Inject]
        private ProfilePictureService ProfilePictureService { get; set; } = null!;

        [Inject]
        private NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private TokenParserService TokenParser { get; set; } = null!;

        [Inject]
        private LanguageService Lang { get; set; } = null!;

        private string T(string key) => Lang.T(key);

        [Inject]
        private UserMonitoredService UserMonitoredService { get; set; } = null!;

        [Inject]
        private MonitoredService MonitoredService { get; set; } = null!;

        private UserProfileDTO CurrentUser { get; set; } = new UserProfileDTO();

        private UserUpdateRequestDTO EditUser { get; set; } = new UserUpdateRequestDTO();
        private bool IsEditingPersonal { get; set; } = false;

        private int MonitoredCount { get; set; } = 0;
        private int AlertsCount { get; set; } = 0;

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
        // Password visibility toggles (for mobile/desktop)
        private string CurrentPasswordFieldType { get; set; } = "password";
        private string NewPasswordFieldType { get; set; } = "password";
        private string ConfirmPasswordFieldType { get; set; } = "password";
        private string EmailCurrentPasswordFieldType { get; set; } = "password";

        private bool ShowChangeEmailModal { get; set; } = false;
        private UserChangeEmailRequestDTO EmailChange { get; set; } = new UserChangeEmailRequestDTO();
        private string EmailError { get; set; } = string.Empty;

        private bool ShowEmailChangeSuccess { get; set; } = false;
        private bool ShowPasswordChangeSuccess { get; set; } = false;

        private bool ShowDeleteConfirmModalBool { get; set; } = false;
        private bool ShowDeleteInfoModal { get; set; } = false;

        private void ClosePasswordChangeSuccessModal()
        {
            ShowPasswordChangeSuccess = false;
        }

        protected override async Task OnInitializedAsync()
        {
            await LoadCurrentUserAsync();

            if (CurrentUser.Id == Guid.Empty)
            {
                await AuthenticationService.LogoutAsync();
                Navigation.NavigateTo("/login");
                return;
            }

            var userFromApi = await UserService.GetUserByIdAsync(CurrentUser.Id);
            if (userFromApi == null)
            {
                // User no longer exists in DB (e.g. after DB reset) — clear all client state
                await AuthenticationService.LogoutAsync();
                Navigation.NavigateTo("/login");
                return;
            }

            CurrentUser = userFromApi;
            if (string.IsNullOrEmpty(CurrentUser.Provider))
                CurrentUser.Provider = "Local";
            if (!string.IsNullOrEmpty(CurrentUser.ProfilePictureUrl))
            {
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "profilePictureUrl", CurrentUser.ProfilePictureUrl);
                ProfilePictureService.SetUrl(CurrentUser.ProfilePictureUrl);
            }
            else
            {
                await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
                ProfilePictureService.SetUrl(null);
            }

            // Sync notification preferences from backend
            Preferences.EmailNotifications = CurrentUser.NotifyByEmail;
            Preferences.PushNotifications = CurrentUser.NotifyByPush;

            // Load monitored people count
            await LoadMonitoredDataAsync();
        }

        private async Task SaveNotificationPreferencesAsync()
        {
            var updateRequest = new UserUpdateRequestDTO
            {
                NotifyByEmail = Preferences.EmailNotifications,
                NotifyByPush = Preferences.PushNotifications
            };
            await UserService.UpdateUserAsync(CurrentUser.Id, updateRequest);
        }

        private async Task OnEmailNotifChanged(ChangeEventArgs e)
        {
            Preferences.EmailNotifications = e.Value is bool b && b;
            await SaveNotificationPreferencesAsync();
        }

        private async Task OnPushNotifChanged(ChangeEventArgs e)
        {
            Preferences.PushNotifications = e.Value is bool b && b;
            await SaveNotificationPreferencesAsync();
        }

        private async Task LoadMonitoredDataAsync()
        {
            try
            {
                // Get monitored people count
                var monitoredPeople = await UserMonitoredService.GetMonitoredPeopleAsync(CurrentUser.Id);
                MonitoredCount = monitoredPeople.Count;

                // Count alerts (people with critical status)
                AlertsCount = 0;
                foreach (var person in monitoredPeople)
                {
                    try
                    {
                        var espData = await MonitoredService.GetEspDataAsync(person.DeviceSerialNumber);
                        if (espData?.IsAvailable == true && espData.Max30100 != null && espData.Max30100.Count >= 2)
                        {
                            var pulse = espData.Max30100.ElementAtOrDefault(0);
                            var spo2 = espData.Max30100.ElementAtOrDefault(1);

                            // Critical conditions
                            if (pulse > 100 || pulse < 50 || spo2 < 90)
                            {
                                AlertsCount++;
                            }
                        }
                    }
                    catch
                    {
                        // Skip if ESP data is unavailable
                    }
                }

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading monitored data: {ex.Message}");
            }
        }

        private async Task LoadCurrentUserAsync()
        {
            var claims = await TokenParser.GetClaimsAsync();
            if (claims == null)
                return;

            CurrentUser.Id = claims.UserId;
            CurrentUser.Email = claims.Email;
            CurrentUser.FirstName = claims.FirstName;
            CurrentUser.LastName = claims.LastName;
            CurrentUser.Provider = string.IsNullOrEmpty(claims.Provider) ? "Local" : claims.Provider;
            CurrentUser.ProfilePictureUrl = claims.ProfilePictureUrl;
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
                LastName = CurrentUser.LastName
            };
            IsEditingPersonal = true;
        }

        private async Task SavePersonalInfo()
        {
            if(!string.IsNullOrWhiteSpace(EditUser.FirstName))
                CurrentUser.FirstName = EditUser.FirstName;

            if(!string.IsNullOrWhiteSpace(EditUser.LastName))
                CurrentUser.LastName = EditUser.LastName;

            var updateRequest = new UserUpdateRequestDTO
            {
                FirstName = CurrentUser.FirstName,
                LastName = CurrentUser.LastName
            };

            var request = await UserService.UpdateUserAsync(CurrentUser.Id, updateRequest);

            if(request == false)
            {
                return;
            }

            EditUser = new UserUpdateRequestDTO();
            IsEditingPersonal = false;
        }

        private void CancelEditPersonal()
        {
            EditUser = new UserUpdateRequestDTO();
            IsEditingPersonal = false;
        }

        private void OpenChangePasswordModal()
        {
            ShowChangePasswordModal = true;
            PasswordChange = new UserChangePasswordRequestDTO();
            PasswordError = string.Empty;
        }

        private void ToggleCurrentPasswordVisibility()
        {
            CurrentPasswordFieldType = CurrentPasswordFieldType == "password" ? "text" : "password";
        }

        private void ToggleNewPasswordVisibility()
        {
            NewPasswordFieldType = NewPasswordFieldType == "password" ? "text" : "password";
        }

        private void ToggleConfirmPasswordVisibility()
        {
            ConfirmPasswordFieldType = ConfirmPasswordFieldType == "password" ? "text" : "password";
        }

        private void ToggleEmailCurrentPasswordVisibility()
        {
            EmailCurrentPasswordFieldType = EmailCurrentPasswordFieldType == "password" ? "text" : "password";
        }

        private void CloseChangePasswordModal()
        {
            ShowChangePasswordModal = false;
            PasswordChange = new UserChangePasswordRequestDTO();
            PasswordError = string.Empty;
        }

        private async Task ChangePassword()
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

            UserChangePasswordRequestDTO request = new UserChangePasswordRequestDTO
            {
                Email = CurrentUser.Email,
                CurrentPassword = PasswordChange.CurrentPassword,
                NewPassword = PasswordChange.NewPassword,
                ConfirmPassword = PasswordChange.ConfirmPassword
            };

            var result = await AuthenticationService.UpdatePasswordAsync(request);

            if (!result)
            {
                PasswordError = "Failed to change password. Please try again.";
                return;
            }

            CloseChangePasswordModal();
            ShowPasswordChangeSuccess = true;
            
            // Refresh the page to update the LastChangedPasswordAt date
            Navigation.NavigateTo("/profile", forceLoad: true);
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

            var result = await AuthenticationService.UpdateEmailAsync(request);

            if (result != null)
            {
                if (result.Success)
                {
                    if (result.RequiresLogout)
                    {
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
            await AuthenticationService.LogoutAsync();
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

        private async Task DeleteAccount()
        {
            ShowDeleteInfoModal = false;
            var result = await UserService.DeleteUserAsync(CurrentUser.Id);
            if (result)
            {
                await AuthenticationService.LogoutAsync();
                Navigation.NavigateTo("/login");
            }
        }

        private void ShowDeleteConfirmModal()
        {
            ShowDeleteConfirmModalBool = true;
        }

        private void CloseDeleteConfirmModal()
        {
            ShowDeleteConfirmModalBool = false;
        }

        private void ConfirmDeleteAccount()
        {
            ShowDeleteConfirmModalBool = false;
            ShowDeleteInfoModal = true;
        }

        private void CloseDeleteInfoModal()
        {
            ShowDeleteInfoModal = false;
        }

        private async Task OnProfileImageSelected(InputFileChangeEventArgs e)
        {
            var file = e.File;
            if (file == null)
                return;

            using var stream = file.OpenReadStream(5 * 1024 * 1024); // max 5MB
            var imageUrl = await UserService.UploadProfilePictureAsync(CurrentUser.Id, stream, file.Name);
            if (!string.IsNullOrEmpty(imageUrl))
            {
                CurrentUser.ProfilePictureUrl = imageUrl;
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "profilePictureUrl", imageUrl);
                ProfilePictureService.SetUrl(imageUrl);
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task TriggerProfileImageInput()
        {
            await JSRuntime.InvokeVoidAsync("triggerProfileImageInput", "profileImageInput");
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
