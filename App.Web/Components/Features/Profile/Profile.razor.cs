using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Features.Profile;

public partial class Profile : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private I18nService I18n { get; set; } = default!;
    [Inject] private ThemeService Theme { get; set; } = default!;

    private ProfileDto? profile;
    private bool loading = true;

    private string? editFullName;
    private string? editPhone;
    private DateOnly? editDob;
    private string? editCompany;
    private string? editPosition;

    private bool savingProfile;
    private string? profileSuccess;
    private List<string> profileErrors = [];

    private string currentPassword = "";
    private string newPassword = "";
    private string confirmPassword = "";
    private bool savingPwd;
    private string? pwdSuccess;
    private List<string> pwdErrors = [];

    protected override async Task OnInitializedAsync()
    {
        profile = await Api.CallApiAsync<ProfileDto>("api/auth/profile");
        if (profile is not null)
        {
            editFullName = profile.FullName;
            editPhone = profile.PhoneNumber;
            editDob = profile.DateOfBirth;
            editCompany = profile.Company;
            editPosition = profile.Position;
        }
        loading = false;
    }

    private async Task SaveProfile()
    {
        profileSuccess = null;
        profileErrors = [];
        savingProfile = true;

        var response = await Api.PutAsync("api/auth/profile", new
        {
            FullName = editFullName,
            PhoneNumber = editPhone,
            DateOfBirth = editDob?.ToString("yyyy-MM-dd"),
            Company = editCompany,
            Position = editPosition
        });

        if (response.IsSuccessStatusCode)
        {
            profileSuccess = "Profile updated successfully.";
            profile = await Api.CallApiAsync<ProfileDto>("api/auth/profile");
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            profileErrors.Add(string.IsNullOrWhiteSpace(body) ? "Failed to update profile." : body);
        }

        savingProfile = false;
    }

    private async Task ChangePassword()
    {
        pwdSuccess = null;
        pwdErrors = [];

        if (newPassword != confirmPassword)
        {
            pwdErrors.Add("New passwords do not match.");
            return;
        }

        if (newPassword.Length < 8)
        {
            pwdErrors.Add("Password must be at least 8 characters.");
            return;
        }

        savingPwd = true;

        var response = await Api.PostAsync("api/auth/change-password", new
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword
        });

        if (response.IsSuccessStatusCode)
        {
            pwdSuccess = "Password updated successfully.";
            currentPassword = "";
            newPassword = "";
            confirmPassword = "";
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            pwdErrors.Add(string.IsNullOrWhiteSpace(body) ? "Failed to change password." : body);
        }

        savingPwd = false;
    }

    protected static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    private record ProfileDto(
        Guid Id,
        string Email,
        string? FullName,
        string? PhoneNumber,
        DateOnly? DateOfBirth,
        string? Company,
        string? Position,
        DateTime CreatedAt,
        DateTime? LastLoginAt);
}
