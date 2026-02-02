using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace KanbanBackend.Tests;

public class AuthIntegrationTests : IntegrationTestBase
{
    public AuthIntegrationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task FullAuthFlow_RegisterLoginRefreshLogout_WorksCorrectly()
    {
        var client = Factory.CreateClient();
        var email = $"test_{Guid.NewGuid()}@example.com";
        var password = "Password123!";

        // 1. Register
        var registerQuery = new
        {
            query = $@"
                mutation {{
                    register(email: ""{email}"", password: ""{password}"") {{
                        email
                    }}
                }}"
        };

        var registerResponse = await client.PostAsJsonAsync("/graphql", registerQuery);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("errors", registerBody.ToLower());

        // 2. Login
        var loginQuery = new
        {
            query = $@"
                mutation {{
                    login(email: ""{email}"", password: ""{password}"") {{
                        accessToken
                        user {{ email }}
                    }}
                }}"
        };

        var loginResponse = await client.PostAsJsonAsync("/graphql", loginQuery);
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        
        Assert.DoesNotContain("errors", loginBody.ToLower());
        Assert.Contains("accessToken", loginBody);

        // Check Cookie
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        Assert.NotEmpty(cookies);
        var authCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(authCookie);
        Assert.Contains("httponly", authCookie.ToLower());

        // Extract AccessToken (optional verification)
        // Extract Refresh Cookie Value for next request
        var cookieValue = Regex.Match(authCookie, "refreshToken=([^;]+)").Groups[1].Value;

        // 3. Refresh (Use Cookie)
        var refreshClient = Factory.CreateClient(); // New client to simulate fresh request (or just add header)
        refreshClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={cookieValue}");

        var refreshQuery = new
        {
            query = @"
                mutation {
                    refreshToken {
                        accessToken
                        user { email }
                    }
                }"
        };

        var refreshResponse = await refreshClient.PostAsJsonAsync("/graphql", refreshQuery);
        refreshResponse.EnsureSuccessStatusCode();
        var refreshBody = await refreshResponse.Content.ReadAsStringAsync();
        
        Assert.DoesNotContain("errors", refreshBody.ToLower());
        Assert.Contains("accessToken", refreshBody);
        
        // Assert New Cookie (Rotation)
        var refreshCookies = refreshResponse.Headers.GetValues("Set-Cookie").ToList();
        var newAuthCookie = refreshCookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(newAuthCookie);
        var newCookieValue = Regex.Match(newAuthCookie, "refreshToken=([^;]+)").Groups[1].Value;

        Assert.NotEqual(cookieValue, newCookieValue); // Rotation confirmed

        // 4. Logout
        var logoutClient = Factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={newCookieValue}");

        var logoutQuery = new
        {
            query = @"
                mutation {
                    logout
                }"
        };

        var logoutResponse = await logoutClient.PostAsJsonAsync("/graphql", logoutQuery);
        logoutResponse.EnsureSuccessStatusCode();
        
        // Assert Cookie Cleared
        var logoutCookies = logoutResponse.Headers.GetValues("Set-Cookie").ToList();
        var clearCookie = logoutCookies.FirstOrDefault(c => c.StartsWith("refreshToken=;"));
        Assert.NotNull(clearCookie); // Should be empty/expired
    }
}
