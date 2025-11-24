using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Profile
{
    public partial class ProfilePage : ComponentBase
    {
        private UserProfile CurrentUser { get; set; } = new UserProfile
        {
            FullName = "Maria Popescu",
            Email = "maria.popescu@email.com",
            Phone = "+40 745 123 456",
            Address = "Strada Florilor 45, București",
            BirthDate = "15 Martie 1985",
            Age = 39,
            Role = "Administrator"
        };

        private UserProfile EditUser { get; set; } = new UserProfile();
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
        private PasswordChangeModel PasswordChange { get; set; } = new PasswordChangeModel();
        private string PasswordError { get; set; } = string.Empty;

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
            EditUser = new UserProfile
            {
                FullName = CurrentUser.FullName,
                Email = CurrentUser.Email,
                Phone = CurrentUser.Phone,
                Address = CurrentUser.Address,
                BirthDate = CurrentUser.BirthDate,
                Age = CurrentUser.Age,
                Role = CurrentUser.Role
            };
            IsEditingPersonal = true;
        }

        private void SavePersonalInfo()
        {
            // TODO: Call API to save changes
            CurrentUser.FullName = EditUser.FullName;
            CurrentUser.Email = EditUser.Email;
            CurrentUser.Phone = EditUser.Phone;
            CurrentUser.Address = EditUser.Address;
            CurrentUser.BirthDate = EditUser.BirthDate;
            IsEditingPersonal = false;
        }

        private void CancelEditPersonal()
        {
            IsEditingPersonal = false;
        }

        private void OpenChangePasswordModal()
        {
            ShowChangePasswordModal = true;
            PasswordChange = new PasswordChangeModel();
            PasswordError = string.Empty;
        }

        private void CloseChangePasswordModal()
        {
            ShowChangePasswordModal = false;
            PasswordChange = new PasswordChangeModel();
            PasswordError = string.Empty;
        }

        private void ChangePassword()
        {
            PasswordError = string.Empty;

            if (string.IsNullOrWhiteSpace(PasswordChange.CurrentPassword))
            {
                PasswordError = "Introdu parola curentă";
                return;
            }

            if (string.IsNullOrWhiteSpace(PasswordChange.NewPassword))
            {
                PasswordError = "Introdu parola nouă";
                return;
            }

            if (PasswordChange.NewPassword.Length < 8)
            {
                PasswordError = "Parola trebuie să aibă minim 8 caractere";
                return;
            }

            if (PasswordChange.NewPassword != PasswordChange.ConfirmPassword)
            {
                PasswordError = "Parolele nu se potrivesc";
                return;
            }

            // TODO: Call API to change password
            CloseChangePasswordModal();
        }

        private class UserProfile
        {
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string BirthDate { get; set; } = string.Empty;
            public int Age { get; set; }
            public string Role { get; set; } = string.Empty;
        }

        private class NotificationPreferences
        {
            public bool EmailNotifications { get; set; }
            public bool PushNotifications { get; set; }
            public bool CriticalAlerts { get; set; }
            public bool DailySummary { get; set; }
        }

        private class PasswordChangeModel
        {
            public string CurrentPassword { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
            public string ConfirmPassword { get; set; } = string.Empty;
        }
    }
}
