using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for authenticating with the custom auth server.
/// Handles session creation and token retrieval.
/// </summary>
public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _authServerUrl;

    public AuthService(HttpClient httpClient, string authDomain)
    {
        _httpClient = httpClient;

        // Normalize auth domain to sessions subdomain
        if (!authDomain.StartsWith("http://") && !authDomain.StartsWith("https://"))
        {
            // If it's just a domain like "sessions.sanasol.ws" or "sanasol.ws"
            if (authDomain.StartsWith("sessions."))
            {
                _authServerUrl = $"https://{authDomain}";
            }
            else
            {
                // Add sessions subdomain if not present
                _authServerUrl = $"https://sessions.{authDomain}";
            }
        }
        else
        {
            _authServerUrl = authDomain;
        }

        Logger.Info("Auth", $"Auth server URL: {_authServerUrl}");
    }

    /// <summary>
    /// Create a game session and get an authentication token.
    /// This is used for the authentication flow.
    /// </summary>
    public async Task<AuthTokenResult> GetGameSessionTokenAsync(string uuid, string playerName)
    {
        try
        {
            Logger.Info("Auth", $"Requesting game session for {playerName} ({uuid})...");

            var requestBody = new GameSessionRequest
            {
                UUID = uuid,
                Name = playerName,
                Scopes = new[] { "hytale:client", "hytale:server" }  // Request both client and server scopes
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // POST to /game-session/child endpoint
            var response = await _httpClient.PostAsync($"{_authServerUrl}/game-session/child", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Logger.Warning("Auth", $"Auth server returned {response.StatusCode}: {errorBody}");

                // Try alternate endpoint
                Logger.Info("Auth", "Trying /game-session endpoint...");
                response = await _httpClient.PostAsync($"{_authServerUrl}/game-session", content);

                if (!response.IsSuccessStatusCode)
                {
                    errorBody = await response.Content.ReadAsStringAsync();
                    Logger.Error("Auth", $"Auth failed: {response.StatusCode} - {errorBody}");
                    return new AuthTokenResult
                    {
                        Success = false,
                        Error = $"Auth server returned {response.StatusCode}"
                    };
                }
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            Logger.Info("Auth", $"Auth response received ({responseBody.Length} chars)");

            // Parse the response - it should contain a JWT token
            var result = JsonSerializer.Deserialize<GameSessionResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new AuthTokenResult
                {
                    Success = false,
                    Error = "Failed to parse auth response"
                };
            }

            // The token could be in various fields depending on API version
            // /game-session/child returns 'identityToken' as primary field
            string? token = result.IdentityToken ?? result.Token ?? result.AccessToken ?? result.JwtToken ?? result.SessionToken ?? result.SessionTokenAlt;

            if (string.IsNullOrEmpty(token))
            {
                // Maybe the entire response is the token
                if (responseBody.StartsWith("eyJ"))
                {
                    token = responseBody.Trim().Trim('"');
                }
            }

            if (string.IsNullOrEmpty(token))
            {
                Logger.Warning("Auth", $"No token found in response: {responseBody}");
                return new AuthTokenResult
                {
                    Success = false,
                    Error = "No token in response"
                };
            }

            Logger.Success("Auth", "Game session token obtained successfully");

            return new AuthTokenResult
            {
                Success = true,
                Token = token,
                SessionToken = result.SessionToken ?? result.SessionTokenAlt ?? token, // Use session token if available, otherwise reuse identity token
                UUID = result.UUID ?? uuid,
                Name = result.Name ?? playerName
            };
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Auth", $"Network error: {ex.Message}");
            return new AuthTokenResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Auth", $"Auth error: {ex.Message}");
            return new AuthTokenResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Validate an existing token is still valid
    /// </summary>
    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_authServerUrl}/validate");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class GameSessionRequest
{
    [JsonPropertyName("uuid")]
    public string UUID { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("scopes")]
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public class GameSessionResponse
{
    // Primary token field (from /game-session/child endpoint)
    [JsonPropertyName("identityToken")]
    public string? IdentityToken { get; set; }

    // Alternative token fields for compatibility
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("jwt_token")]
    public string? JwtToken { get; set; }

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    [JsonPropertyName("session_token")]
    public string? SessionTokenAlt { get; set; }

    [JsonPropertyName("uuid")]
    public string? UUID { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }
}

public class AuthTokenResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? SessionToken { get; set; }
    public string? UUID { get; set; }
    public string? Name { get; set; }
    public string? Error { get; set; }
}
