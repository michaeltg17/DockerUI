using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace E2E.Tests.Basic;

/// <summary>
/// End-to-end tests for the optional single-password authentication: setting a password
/// locks the dashboard behind a login page, the password can be changed and removed, and a
/// locked instance answers unauthenticated API calls with a 401 problem document.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class PasswordScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    /// <summary>How long to wait for a password change to reach the scenario's appsettings on the host.</summary>
    const int SettingsWriteTimeoutSeconds = 15;

    /// <summary>How long to wait for the dashboard to return to open mode after a password removal.</summary>
    const int OpenModeTimeoutSeconds = 30;

    /// <summary>The password the tests set; the UI requires at least 16 characters.</summary>
    const string Password = "e2e-password-12345";

    /// <summary>The password the tests change to.</summary>
    const string SecondPassword = "e2e-password-changed";

    /// <summary>A wrong password; at least 16 characters so it passes the dialog's validation.</summary>
    const string WrongPassword = "wrong-password-4321";

    [Fact]
    public async Task Setting_a_password_locks_the_dashboard_until_signing_in()
    {
        await EnsurePasswordRemovedAsync();

        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            // Open mode: the dashboard loads without a login page.
            await apps.WaitForAppAsync("web-stack");

            // Setting the first password from the dashboard menu keeps the user in the app.
            await apps.SetPasswordAsync(Password);
            await apps.WaitForAppAsync("web-stack");

            // The password is persisted in the scenario's appsettings (bind-mounted into the dashboard).
            await WaitForPasswordStoredAsync();

            // A fresh context (no session cookie) meets the login page instead of the dashboard.
            await using var locked = await browser.NewContextAsync(environment);
            var loginPage = new AppsPage(await locked.NewPageAsync());
            await loginPage.LoadAsync(environment.BaseUrl);
            await loginPage.WaitForLoginPageAsync();

            // A wrong password keeps the form open and shows the API's message.
            await loginPage.SubmitLoginAsync(WrongPassword);
            await loginPage.LoginError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
            (await loginPage.LoginError.InnerTextAsync())
                .Should().Be("Incorrect password.", "because the login answers with the API's problem detail");
            (await loginPage.LoginPasswordBox.CountAsync())
                .Should().Be(1, "because a failed sign-in keeps the login page open");

            // The correct password signs in and shows the dashboard.
            await loginPage.SubmitLoginAsync(Password);
            await loginPage.WaitForAppAsync("web-stack");
        }
        finally
        {
            await EnsurePasswordRemovedAsync();
        }
    }

    [Fact]
    public async Task Changing_and_removing_a_password_updates_the_dashboard()
    {
        await EnsurePasswordRemovedAsync();

        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForAppAsync("web-stack");
            await apps.SetPasswordAsync(Password);
            await apps.WaitForAppAsync("web-stack");

            // With a password set, the dashboard menu offers the change action instead of the set action.
            var menu = await apps.OpenDashboardMenuAsync();
            (await AppsPage.MenuItem(menu, AppsPage.ChangePasswordLabel).CountAsync())
                .Should().Be(1, "because a set password offers the change action");
            (await AppsPage.MenuItem(menu, AppsPage.SetPasswordLabel).CountAsync())
                .Should().Be(0, "because the set action is gone while a password is set");

            // A wrong current password is rejected and keeps the dialog open.
            var dialog = await apps.OpenPasswordDialogAsync(passwordSet: true);
            await AppsPage.FillPasswordDialogAsync(dialog, WrongPassword, SecondPassword);
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();

            var error = AppsPage.PasswordDialogError(dialog);
            await error.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
            (await error.InnerTextAsync())
                .Should().Be("Incorrect password.", "because changing the password requires the correct current one");

            // The correct current password changes it.
            await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Current password", Exact = true }).FillAsync(Password);
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
            await apps.WaitForAppAsync("web-stack");

            // A fresh context must sign in with the new password; the old one no longer works.
            await using var locked = await browser.NewContextAsync(environment);
            var loginPage = new AppsPage(await locked.NewPageAsync());
            await loginPage.LoadAsync(environment.BaseUrl);
            await loginPage.WaitForLoginPageAsync();

            await loginPage.SubmitLoginAsync(Password);
            await loginPage.LoginError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
            (await loginPage.LoginError.InnerTextAsync())
                .Should().Be("Incorrect password.", "because the password was changed");

            await loginPage.SubmitLoginAsync(SecondPassword);
            await loginPage.WaitForAppAsync("web-stack");

            // Leaving the new password empty removes it and opens the dashboard again.
            await apps.ChangePasswordAsync(SecondPassword, string.Empty);
            await apps.WaitForAppAsync("web-stack");

            // The dashboard is open again: a fresh context loads it without a login page,
            // and the password is gone from the scenario's appsettings.
            await using var open = await browser.NewContextAsync(environment);
            var openPage = new AppsPage(await open.NewPageAsync());
            await openPage.LoadAsync(environment.BaseUrl);
            await openPage.WaitForAppAsync("web-stack");
            await WaitForPasswordRemovedAsync();
        }
        finally
        {
            await EnsurePasswordRemovedAsync();
        }
    }

    [Fact]
    public async Task Locked_dashboard_answers_unauthenticated_calls_with_a_401_problem()
    {
        await EnsurePasswordRemovedAsync();

        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = new AuthApiClient(environment.BaseUrl);
        using var anonymous = new AuthApiClient(environment.BaseUrl);

        try
        {
            // Open mode accepts the first password without a session; the response also
            // signs in the client that set it.
            using (var set = await api.PostAsync(
                       new Uri(environment.BaseUrl, "api/auth/change-password"),
                       new { currentPassword = (string?)null, newPassword = Password },
                       cancellationToken))
            {
                set.StatusCode
                    .Should().Be(HttpStatusCode.OK, "because open mode accepts the first password without a session");
            }

            // Unauthenticated API calls get a 401 problem document.
            foreach (var path in new[] { "api/apps", "api/auth/me" })
            {
                using var response = await anonymous.GetAsync(new Uri(environment.BaseUrl, path), cancellationToken);
                response.StatusCode
                    .Should().Be(HttpStatusCode.Unauthorized, $"because '{path}' is protected while a password is set");
                response.Content.Headers.ContentType?.MediaType
                    .Should().Be("application/problem+json", "because the API reports problems as RFC 9457 documents");

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                document.RootElement.GetProperty("status").GetInt32().Should().Be(401);
                document.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
                document.RootElement.GetProperty("detail").GetString().Should().Be("Not signed in.");
            }

            // A wrong password names itself...
            using (var wrongLogin = await anonymous.PostAsync(
                       new Uri(environment.BaseUrl, "api/auth/login"),
                       new { password = WrongPassword },
                       cancellationToken))
            {
                wrongLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because the password is wrong");

                using var document = JsonDocument.Parse(await wrongLogin.Content.ReadAsStringAsync(cancellationToken));
                document.RootElement.GetProperty("detail").GetString()
                    .Should().Be("Incorrect password.", "because the problem names the wrong password");
            }

            // ...and the session the password set carries unlocks the API.
            using (var appsResponse = await api.GetAsync(new Uri(environment.BaseUrl, "api/apps"), cancellationToken))
            {
                appsResponse.StatusCode
                    .Should().Be(HttpStatusCode.OK, "because the client that set the password is signed in");
            }

            using (var meResponse = await api.GetAsync(new Uri(environment.BaseUrl, "api/auth/me"), cancellationToken))
            {
                meResponse.StatusCode.Should().Be(HttpStatusCode.OK, "because the signed-in client may query its session");

                using var document = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync(cancellationToken));
                document.RootElement.GetProperty("passwordSet").GetBoolean()
                    .Should().BeTrue("because the password is set");
            }

            // The SPA shell itself stays public, so the login page loads without a session.
            await using var context = await browser.NewContextAsync(environment);
            var apps = new AppsPage(await context.NewPageAsync());
            await apps.LoadAsync(environment.BaseUrl);
            await apps.WaitForLoginPageAsync();
        }
        finally
        {
            await EnsurePasswordRemovedAsync();
        }
    }

    /// <summary>
    /// Puts the shared dashboard back into open mode (no password), whatever state a previous
    /// test or run left it in: it removes the test password through the API (signing in first
    /// if needed) and resets the scenario's appsettings.json on the host for a leftover the
    /// test password cannot open.
    /// </summary>
    async Task EnsurePasswordRemovedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = new AuthApiClient(environment.BaseUrl);

        if (!await TryRemoveTestPasswordAsync(client, cancellationToken))
            ResetPasswordInScenarioSettings();

        await WaitUntilOpenAsync(client, cancellationToken);
    }

    /// <summary>
    /// Removes the password through the API when it is the test password: a signed-in client
    /// removes it directly, and a locked dashboard is signed in first. Reports whether the
    /// dashboard is open afterwards.
    /// </summary>
    static async Task<bool> TryRemoveTestPasswordAsync(AuthApiClient client, CancellationToken cancellationToken)
    {
        var passwordSet = await client.GetPasswordSetAsync(cancellationToken);

        if (passwordSet is false)
            return true; // Open mode: nothing to remove.

        if (passwordSet is not null)
            return await client.RemovePasswordAsync(Password, cancellationToken); // Signed in; remove directly.

        return await client.TrySignInAsync(Password, cancellationToken) &&
               await client.RemovePasswordAsync(Password, cancellationToken);
    }

    /// <summary>Waits until the dashboard reports open mode (no password set) again.</summary>
    static async Task WaitUntilOpenAsync(AuthApiClient client, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(OpenModeTimeoutSeconds);

        while (true)
        {
            if (await client.GetPasswordSetAsync(cancellationToken) is false)
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The dashboard did not return to open mode (no password set).");

            await Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>
    /// Waits until the scenario's appsettings (bind-mounted into the dashboard) carry the
    /// password hash, and proves it is stored as a hash, never in plain text.
    /// </summary>
    static async Task WaitForPasswordStoredAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(SettingsWriteTimeoutSeconds);

        while (true)
        {
            string? hash = null;

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath, cancellationToken));
                hash = document.RootElement
                    .GetProperty("DockerUI")
                    .GetProperty("Auth")
                    .GetProperty("AdminPasswordHash")
                    .GetString();
            }
            catch (KeyNotFoundException)
            {
                // The password has not been persisted yet.
            }
            catch (JsonException)
            {
                // The settings file is mid-rewrite; read it again.
            }

            if (!string.IsNullOrWhiteSpace(hash))
            {
                hash.Should().StartWith(
                    "PBKDF2-SHA256$",
                    "because the password is stored as a self-describing hash, never in plain text");
                return;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The password hash was not persisted to the scenario's appsettings.");

            await Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>Waits until the scenario's appsettings carry no password hash anymore.</summary>
    static async Task WaitForPasswordRemovedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(SettingsWriteTimeoutSeconds);

        while (true)
        {
            var passwordStored = false;

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath, cancellationToken));
                passwordStored = document.RootElement
                    .GetProperty("DockerUI")
                    .TryGetProperty("Auth", out var auth) &&
                    auth.TryGetProperty("AdminPasswordHash", out var hash) &&
                    hash.GetString() is { Length: > 0 };
            }
            catch (JsonException)
            {
                // The settings file is mid-rewrite; read it again.
            }

            if (!passwordStored)
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The password hash was not removed from the scenario's appsettings.");

            await Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>
    /// Removes the 'DockerUI:Auth' section from the scenario's appsettings.json on the host
    /// (a leftover a crashed earlier run left behind). The file is bind-mounted into the
    /// dashboard, whose settings poll reloads the configuration and opens it again.
    /// </summary>
    static void ResetPasswordInScenarioSettings()
    {
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");

        var settings = JsonNode.Parse(File.ReadAllText(appSettingsPath)) as JsonObject
            ?? throw new InvalidOperationException("The scenario's appsettings.json must contain a JSON object.");

        if (settings["DockerUI"] is not JsonObject dockerUi ||
            dockerUi["Auth"] is null)
        {
            return;
        }

        dockerUi.Remove("Auth");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(appSettingsPath, settings.ToJsonString(options) + Environment.NewLine);
    }
}

/// <summary>
/// A client for the auth endpoints that keeps the session cookie across calls, so the tests
/// can drive the locked dashboard the way a second browser would.
/// </summary>
sealed class AuthApiClient(Uri baseUrl) : IDisposable
{
    readonly HttpClient client = new(new HttpClientHandler
    {
        UseCookies = true,
        CheckCertificateRevocationList = true,
        CookieContainer = new CookieContainer(),
    })
    {
        BaseAddress = baseUrl,
        Timeout = TimeSpan.FromSeconds(10),
    };

    public void Dispose() => client.Dispose();

    /// <summary>
    /// The dashboard's password state: false in open mode, true when signed in, null when a
    /// password is set and this client holds no session (a 401).
    /// </summary>
    public async Task<bool?> GetPasswordSetAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(baseUrl, "api/auth/me"), cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("passwordSet").GetBoolean();
    }

    /// <summary>Signs in with the given password; reports whether the API accepted the call.</summary>
    public async Task<bool> TrySignInAsync(string password, CancellationToken cancellationToken)
        => (await PostAsync(new Uri(baseUrl, "api/auth/login"), new { password }, cancellationToken)).IsSuccessStatusCode;

    /// <summary>Removes the password with the given current password; reports whether the API accepted the call.</summary>
    public async Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken)
        => (await PostAsync(
                   new Uri(baseUrl, "api/auth/change-password"),
                   new { currentPassword, newPassword = (string?)null },
                   cancellationToken))
               .IsSuccessStatusCode;

    public async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
        => await client.GetAsync(uri, cancellationToken);

    public async Task<HttpResponseMessage> PostAsync(Uri uri, object body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await client.PostAsync(uri, content, cancellationToken);
    }
}
