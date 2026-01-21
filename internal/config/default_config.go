package config

// Config represents the launcher configuration
type Config struct {
	Version           string `toml:"version" json:"version"`
	Nick              string `toml:"nick" json:"nick"`
	MusicEnabled      bool   `toml:"music_enabled" json:"musicEnabled"`
	VersionType       string `toml:"version_type" json:"versionType"`
	SelectedVersion   int    `toml:"selected_version" json:"selectedVersion"`
	CustomInstanceDir string `toml:"custom_instance_dir" json:"customInstanceDir"` // Custom path for instances
	AutoUpdateLatest  bool   `toml:"auto_update_latest" json:"autoUpdateLatest"`   // Auto-update latest instance
	OnlineMode        bool   `toml:"online_mode" json:"onlineMode"`                // Enable online multiplayer
	AuthDomain        string `toml:"auth_domain" json:"authDomain"`                // Custom auth server domain
}

// Default returns the default configuration
func Default() *Config {
	return &Config{
		Version:           "1.0.0",
		Nick:              "HyPrism",
		MusicEnabled:      true,
		VersionType:       "release",
		SelectedVersion:   0,  // 0 means use latest
		CustomInstanceDir: "", // Empty means use default
		AutoUpdateLatest:  true,
		OnlineMode:        true, // Online mode enabled by default
		AuthDomain:        "",   // Empty uses default auth domain
	}
}
