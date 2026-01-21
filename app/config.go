package app

import (
	"fmt"
	
	"HyPrism/internal/config"
	"HyPrism/internal/env"
	"HyPrism/internal/pwr"
)

// SetNick sets the player nickname
func (a *App) SetNick(nick string) error {
	a.cfg.Nick = nick
	return config.Save(a.cfg)
}

// GetNick returns the player nickname
func (a *App) GetNick() string {
	return a.cfg.Nick
}

// GetConfig returns the full config
func (a *App) GetConfig() *config.Config {
	return a.cfg
}

// SaveConfig saves the configuration
func (a *App) SaveConfig() error {
	return config.Save(a.cfg)
}

// SetMusicEnabled sets music enabled state and saves it
func (a *App) SetMusicEnabled(enabled bool) error {
	a.cfg.MusicEnabled = enabled
	return config.Save(a.cfg)
}

// GetMusicEnabled returns the music enabled state
func (a *App) GetMusicEnabled() bool {
	return a.cfg.MusicEnabled
}

// GetVersionType returns the current version type (release or pre-release)
func (a *App) GetVersionType() string {
	if a.cfg.VersionType == "" {
		return "release"
	}
	return a.cfg.VersionType
}

// SetVersionType sets the version type and saves it
func (a *App) SetVersionType(versionType string) error {
	if versionType != "release" && versionType != "pre-release" && versionType != "prerelease" {
		versionType = "release"
	}
	// Normalize to API format
	if versionType == "prerelease" {
		versionType = "pre-release"
	}
	a.cfg.VersionType = versionType
	return config.Save(a.cfg)
}

// GetSelectedVersion returns the currently selected version number
func (a *App) GetSelectedVersion() int {
	if a.cfg.SelectedVersion <= 0 {
		// Return latest for current branch
		return pwr.FindLatestVersion(a.GetVersionType())
	}
	return a.cfg.SelectedVersion
}

// SetSelectedVersion sets the selected version number
func (a *App) SetSelectedVersion(version int) error {
	a.cfg.SelectedVersion = version
	return config.Save(a.cfg)
}

// VersionCheckInfo represents version availability information
type VersionCheckInfo struct {
	Available bool `json:"available"`
	Version   int  `json:"version"`
}

// CheckVersionAvailability checks if a version is available for the current platform
func (a *App) CheckVersionAvailability() VersionCheckInfo {
	versionType := a.GetVersionType()
	result := pwr.FindLatestVersionWithDetails(versionType)
	return VersionCheckInfo{
		Available: result.LatestVersion > 0,
		Version:   result.LatestVersion,
	}
}

// GetCustomInstanceDir returns the custom instance directory path
func (a *App) GetCustomInstanceDir() string {
	return a.cfg.CustomInstanceDir
}

// SetCustomInstanceDir sets a custom directory for instances
func (a *App) SetCustomInstanceDir(path string) error {
	a.cfg.CustomInstanceDir = path
	env.SetCustomInstanceDir(path) // Update the env module
	return config.Save(a.cfg)
}

// GetAutoUpdateLatest returns whether the latest instance should auto-update
func (a *App) GetAutoUpdateLatest() bool {
	return a.cfg.AutoUpdateLatest
}

// SetAutoUpdateLatest sets whether the latest instance should auto-update
func (a *App) SetAutoUpdateLatest(enabled bool) error {
	a.cfg.AutoUpdateLatest = enabled
	return config.Save(a.cfg)
}

// GetOnlineMode returns whether online mode is enabled
func (a *App) GetOnlineMode() bool {
	return a.cfg.OnlineMode
}

// SetOnlineMode sets whether online mode is enabled
func (a *App) SetOnlineMode(enabled bool) error {
	// If switching from online to offline, restore original binaries
	if !enabled && a.cfg.OnlineMode {
		fmt.Println("Switching to offline mode, restoring original binaries...")
		// This is a placeholder - in current version we don't patch binaries
		// so no restoration needed
	}
	
	a.cfg.OnlineMode = enabled
	return config.Save(a.cfg)
}

// GetAuthDomain returns the custom auth domain
func (a *App) GetAuthDomain() string {
	return a.cfg.AuthDomain
}

// SetAuthDomain sets the custom auth domain
func (a *App) SetAuthDomain(domain string) error {
	a.cfg.AuthDomain = domain
	return config.Save(a.cfg)
}
