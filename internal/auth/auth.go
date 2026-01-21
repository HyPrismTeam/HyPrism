package auth

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

const (
	// DefaultAuthDomain is the default domain for the auth server
	DefaultAuthDomain = "sanasol.ws"
)

// TokenRequest represents the request body for fetching auth tokens
type TokenRequest struct {
	UUID   string   `json:"uuid"`
	Name   string   `json:"name"`
	Scopes []string `json:"scopes"`
}

// TokenResponse represents the response from the auth server
type TokenResponse struct {
	IdentityToken string `json:"identityToken,omitempty"`
	SessionToken  string `json:"sessionToken,omitempty"`
	// Alternative casing from some servers
	IdentityTokenAlt string `json:"IdentityToken,omitempty"`
	SessionTokenAlt  string `json:"SessionToken,omitempty"`
}

// AuthTokens contains the identity and session tokens
type AuthTokens struct {
	IdentityToken string
	SessionToken  string
}

// GetAuthServerURL returns the full auth server URL for a domain
func GetAuthServerURL(domain string) string {
	if domain == "" {
		domain = DefaultAuthDomain
	}
	return fmt.Sprintf("https://sessions.%s", domain)
}

// FetchAuthTokens fetches auth tokens from the auth server
// This allows connecting to custom servers for multiplayer
func FetchAuthTokens(uuid, name, authDomain string) (*AuthTokens, error) {
	serverURL := GetAuthServerURL(authDomain)
	endpoint := fmt.Sprintf("%s/game-session/child", serverURL)

	fmt.Printf("Fetching auth tokens from %s\n", endpoint)

	reqBody := TokenRequest{
		UUID:   uuid,
		Name:   name,
		Scopes: []string{"hytale:server", "hytale:client"},
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return nil, fmt.Errorf("failed to marshal request: %w", err)
	}

	client := &http.Client{
		Timeout: 10 * time.Second,
	}

	req, err := http.NewRequest("POST", endpoint, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "HyPrism-Launcher")

	resp, err := client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("failed to fetch auth tokens: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("auth server returned status %d", resp.StatusCode)
	}

	var tokenResp TokenResponse
	if err := json.NewDecoder(resp.Body).Decode(&tokenResp); err != nil {
		return nil, fmt.Errorf("failed to decode response: %w", err)
	}

	tokens := &AuthTokens{}

	// Handle both casing variants
	if tokenResp.IdentityToken != "" {
		tokens.IdentityToken = tokenResp.IdentityToken
	} else if tokenResp.IdentityTokenAlt != "" {
		tokens.IdentityToken = tokenResp.IdentityTokenAlt
	}

	if tokenResp.SessionToken != "" {
		tokens.SessionToken = tokenResp.SessionToken
	} else if tokenResp.SessionTokenAlt != "" {
		tokens.SessionToken = tokenResp.SessionTokenAlt
	}

	fmt.Println("Auth tokens received from server")
	return tokens, nil
}

// GenerateLocalTokens generates fallback local tokens for offline testing
// These won't pass signature validation but allow offline mode
func GenerateLocalTokens(uuid, name string) *AuthTokens {
	// Generate simple placeholder tokens for offline mode
	// The game will use these with --auth-mode offline
	return &AuthTokens{
		IdentityToken: fmt.Sprintf("local-%s-%s", uuid, name),
		SessionToken:  fmt.Sprintf("session-%s-%s", uuid, name),
	}
}
