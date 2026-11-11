using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: optional password authentication — the login page and the password dialog.</summary>
public sealed partial class AppsPage
{
    /// <summary>The dashboard menu's label for the password action while no password is set.</summary>
    public const string SetPasswordLabel = "Set password";

    /// <summary>The dashboard menu's label for the password action while a password is set.</summary>
    public const string ChangePasswordLabel = "Change password";

    /// <summary>The dashboard menu's label for the remove action, offered only while a password is set.</summary>
    public const string RemovePasswordLabel = "Remove password";

    /// <summary>The login page's password field; shown while a password is set and this context holds no valid session.</summary>
    public ILocator LoginPasswordBox => Page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Password", Exact = true });

    /// <summary>The login page's "Forgot password?" link; opens the reset instructions for a locked-out user.</summary>
    public ILocator ForgotPasswordLink => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Forgot password?", Exact = true });

    /// <summary>The password reset dialog, which explains removing the password manually from appsettings.json.</summary>
    public ILocator PasswordResetDialog => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Remove the dashboard password", Exact = true });

    /// <summary>The login page's "Sign in" button.</summary>
    public ILocator LoginButton => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in", Exact = true });

    /// <summary>The login page's error slot; shows the API's message after a failed sign-in.</summary>
    public ILocator LoginError => Page.GetByRole(AriaRole.Alert);

    /// <summary>The password dialog, addressed by its accessible name ('Set password' or 'Change password').</summary>
    public ILocator PasswordDialog(string title) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    /// <summary>Waits until the login page is shown (a password is set and this context holds no valid session).</summary>
    public async Task WaitForLoginPageAsync(int timeoutMs = 30_000)
        => await LoginPasswordBox.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>Types the given password on the login page and signs in. A wrong password keeps the form open and shows the API's message.</summary>
    public async Task SubmitLoginAsync(string password)
    {
        await LoginPasswordBox.FillAsync(password);
        await LoginButton.ClickAsync();
    }

    /// <summary>
    /// Opens the password dialog from the dashboard menu. Its menu label and the dialog's
    /// title depend on whether a password is already set ('Set password' or 'Change password').
    /// </summary>
    public async Task<ILocator> OpenPasswordDialogAsync(bool passwordSet, int timeoutMs = StateChangeTimeoutMs)
    {
        var label = passwordSet ? ChangePasswordLabel : SetPasswordLabel;
        var menu = await OpenDashboardMenuAsync();
        await MenuItem(menu, label).ClickAsync();

        var dialog = PasswordDialog(label);
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
        return dialog;
    }

    /// <summary>
    /// Fills in the password dialog's fields: the current password in change mode, and the
    /// new password twice (an empty new password removes the password).
    /// </summary>
    public static async Task FillPasswordDialogAsync(ILocator dialog, string? current, string next)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (current is not null)
            await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Current password", Exact = true }).FillAsync(current);

        await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "New password", Exact = true }).FillAsync(next);
        await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Confirm new password", Exact = true }).FillAsync(next);
    }

    /// <summary>The password dialog's submit-error slot; shows the API's message after a rejected save, keeping the dialog open.</summary>
    public static ILocator PasswordDialogError(ILocator dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return dialog.GetByRole(AriaRole.Alert);
    }

    /// <summary>
    /// Submits the password dialog and waits for it to close once the save succeeds. The
    /// submit button reads 'Set password' in open mode and 'Save' in change mode.
    /// </summary>
    public static async Task SavePasswordAsync(ILocator dialog, bool passwordSet, int timeoutMs = StateChangeTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = passwordSet ? "Save" : SetPasswordLabel, Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }

    /// <summary>
    /// Sets the dashboard's password from open mode via the dashboard menu's 'Set password'
    /// action. The user stays in the app: the session cookie is issued with the save.
    /// </summary>
    public async Task SetPasswordAsync(string next, int timeoutMs = StateChangeTimeoutMs)
    {
        var dialog = await OpenPasswordDialogAsync(passwordSet: false, timeoutMs);
        await FillPasswordDialogAsync(dialog, current: null, next);
        await SavePasswordAsync(dialog, passwordSet: false, timeoutMs);
    }

    /// <summary>Changes the dashboard's password via the dashboard menu's 'Change password' action; an empty new password removes it.</summary>
    public async Task ChangePasswordAsync(string current, string next, int timeoutMs = StateChangeTimeoutMs)
    {
        var dialog = await OpenPasswordDialogAsync(passwordSet: true, timeoutMs);
        await FillPasswordDialogAsync(dialog, current, next);
        await SavePasswordAsync(dialog, passwordSet: true, timeoutMs);
    }
}
