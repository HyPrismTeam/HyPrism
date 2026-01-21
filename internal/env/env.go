package env

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
)

// IsFlatpak returns true if running inside a Flatpak sandbox
func IsFlatpak() bool {
	// Flatpak sets FLATPAK_ID environment variable
	if os.Getenv("FLATPAK_ID") != "" {
		return true
	}
	// Also check for /.flatpak-info file which exists in Flatpak sandboxes
	if _, err := os.Stat("/.flatpak-info"); err == nil {
		return true
	}
	return false
}

// GetDefaultAppDir returns the default application directory
// On Linux, respects XDG_DATA_HOME for proper Flatpak sandboxing
func GetDefaultAppDir() string {
	var baseDir string

	switch runtime.GOOS {
	case "windows":
		baseDir = os.Getenv("LOCALAPPDATA")
		if baseDir == "" {
			baseDir = os.Getenv("APPDATA")
		}
	case "darwin":
		home, _ := os.UserHomeDir()
		baseDir = filepath.Join(home, "Library", "Application Support")
	default: // Linux and others
		// Use XDG_DATA_HOME if set (Flatpak sets this to ~/.var/app/<app-id>/data)
		// This ensures proper sandboxing in Flatpak
		xdgDataHome := os.Getenv("XDG_DATA_HOME")
		if xdgDataHome != "" {
			baseDir = xdgDataHome
		} else {
			home, _ := os.UserHomeDir()
			baseDir = filepath.Join(home, ".local", "share")
		}
	}

	return filepath.Join(baseDir, "HyPrism")
}

// CreateFolders creates the required folder structure
func CreateFolders() error {
	appDir := GetDefaultAppDir()

	folders := []string{
		appDir,
		filepath.Join(appDir, "instances"),
		filepath.Join(appDir, "jre"),
		filepath.Join(appDir, "butler"),
		filepath.Join(appDir, "cache"),
		filepath.Join(appDir, "logs"),
		filepath.Join(appDir, "crashes"),
		filepath.Join(appDir, "UserData"),
	}

	// Create instances directory in custom location if configured
	instancesDir := GetInstancesDir()
	if err := os.MkdirAll(instancesDir, 0755); err != nil {
		return err
	}

	for _, folder := range folders {
		if err := os.MkdirAll(folder, 0755); err != nil {
			return err
		}
	}

	return nil
}

// GetCacheDir returns the cache directory
func GetCacheDir() string {
	return filepath.Join(GetDefaultAppDir(), "cache")
}

// GetLogsDir returns the logs directory
func GetLogsDir() string {
	return filepath.Join(GetDefaultAppDir(), "logs")
}

// GetJREDir returns the JRE directory
func GetJREDir() string {
	return filepath.Join(GetDefaultAppDir(), "jre")
}

// GetButlerDir returns the Butler directory
func GetButlerDir() string {
	return filepath.Join(GetDefaultAppDir(), "butler")
}

// GetUserDataDir returns the user data directory (legacy, for migration)
func GetUserDataDir() string {
	return filepath.Join(GetDefaultAppDir(), "UserData")
}

// ========== INSTANCE-BASED PATHS ==========

// customInstanceDir stores the custom instance directory path
var customInstanceDir string

// SetCustomInstanceDir sets a custom directory for instances
func SetCustomInstanceDir(dir string) {
	customInstanceDir = dir
}

// GetInstancesDir returns the instances directory
func GetInstancesDir() string {
	if customInstanceDir != "" {
		return customInstanceDir
	}
	return filepath.Join(GetDefaultAppDir(), "instances")
}

// GetInstanceDir returns the directory for a specific instance
// Format: instances/{branch}-v{version} or instances/latest for auto-updating
func GetInstanceDir(branch string, version int) string {
	var instanceName string
	if version == 0 {
		// Version 0 means "latest" auto-updating instance
		instanceName = "latest"
	} else {
		instanceName = fmt.Sprintf("%s-v%d", branch, version)
	}
	return filepath.Join(GetInstancesDir(), instanceName)
}

// GetInstanceGameDir returns the game directory for an instance
func GetInstanceGameDir(branch string, version int) string {
	return filepath.Join(GetInstanceDir(branch, version), "game")
}

// GetInstanceModsDir returns the mods directory for an instance
func GetInstanceModsDir(branch string, version int) string {
	return filepath.Join(GetInstanceDir(branch, version), "mods")
}

// GetInstanceSavesDir returns the saves/worlds directory for an instance
func GetInstanceSavesDir(branch string, version int) string {
	return filepath.Join(GetInstanceDir(branch, version), "saves")
}

// GetInstanceUserDataDir returns the UserData directory for an instance
func GetInstanceUserDataDir(branch string, version int) string {
	return filepath.Join(GetInstanceDir(branch, version), "UserData")
}

// CreateInstanceFolders creates all necessary folders for an instance
func CreateInstanceFolders(branch string, version int) error {
	folders := []string{
		GetInstanceDir(branch, version),
		GetInstanceGameDir(branch, version),
		GetInstanceModsDir(branch, version),
		GetInstanceSavesDir(branch, version),
		GetInstanceUserDataDir(branch, version),
		filepath.Join(GetInstanceUserDataDir(branch, version), "Mods"),
	}

	for _, folder := range folders {
		if err := os.MkdirAll(folder, 0755); err != nil {
			return err
		}
	}

	return nil
}

// ListInstances returns all installed instance directories
func ListInstances() ([]string, error) {
	instancesDir := GetInstancesDir()
	entries, err := os.ReadDir(instancesDir)
	if err != nil {
		if os.IsNotExist(err) {
			return []string{}, nil
		}
		return nil, err
	}

	var instances []string
	for _, entry := range entries {
		if entry.IsDir() {
			instances = append(instances, entry.Name())
		}
	}
	return instances, nil
}

// IsVersionInstalled checks if a specific branch/version is installed
// It checks if the instance directory and game files exist
func IsVersionInstalled(branch string, version int) bool {
	instanceDir := GetInstanceDir(branch, version)
	gameDir := GetInstanceGameDir(branch, version)
	
	fmt.Printf("[DEBUG] IsVersionInstalled: Checking %s v%d\n", branch, version)
	fmt.Printf("[DEBUG] Instance dir: %s\n", instanceDir)
	fmt.Printf("[DEBUG] Game dir: %s\n", gameDir)
	
	// First check if instance directory exists
	if _, err := os.Stat(instanceDir); os.IsNotExist(err) {
		fmt.Printf("[DEBUG] Instance directory does not exist\n")
		return false
	}
	
	// Check if game directory exists
	if _, err := os.Stat(gameDir); os.IsNotExist(err) {
		fmt.Printf("[DEBUG] Game directory does not exist\n")
		return false
	}
	
	// Check if Client folder exists with content (simplest check that works)
	clientDir := filepath.Join(gameDir, "Client")
	if entries, err := os.ReadDir(clientDir); err == nil && len(entries) > 0 {
		fmt.Printf("[DEBUG] Client folder found with %d entries - game is installed\n", len(entries))
		return true
	}
	
	// If no Client folder, check if game directory has content (at least a few files/folders)
	if entries, err := os.ReadDir(gameDir); err == nil && len(entries) >= 2 {
		fmt.Printf("[DEBUG] Game dir has %d entries - considering installed\n", len(entries))
		return true
	}
	
	fmt.Printf("[DEBUG] No valid game installation found\n")
	return false
}

// GetInstalledVersions returns all installed versions for a specific branch
// This verifies that each version directory actually has game files
func GetInstalledVersions(branch string) []int {
	instancesDir := GetInstancesDir()
	prefix := branch + "-v"
	
	entries, err := os.ReadDir(instancesDir)
	if err != nil {
		return []int{}
	}
	
	var versions []int
	
	// Check for 'latest' directory (version 0) - verify it's actually installed
	for _, entry := range entries {
		if entry.IsDir() && entry.Name() == "latest" {
			// Verify the game is actually installed in "latest"
			if IsVersionInstalled(branch, 0) {
				versions = append(versions, 0)
			}
			break
		}
	}
	
	// Check for versioned instances (release-v4, pre-release-v8, etc.)
	for _, entry := range entries {
		if entry.IsDir() && len(entry.Name()) > len(prefix) && entry.Name()[:len(prefix)] == prefix {
			versionStr := entry.Name()[len(prefix):]
			var v int
			if _, err := fmt.Sscanf(versionStr, "%d", &v); err == nil && v > 0 {
				// Verify the game is actually installed in this version
				if IsVersionInstalled(branch, v) {
					versions = append(versions, v)
				}
			}
		}
	}
	return versions
}
