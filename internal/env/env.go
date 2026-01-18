package env

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
)

// GetDefaultAppDir returns the default application directory
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
		home, _ := os.UserHomeDir()
		baseDir = filepath.Join(home, ".local", "share")
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
		// Legacy paths for backwards compatibility
		filepath.Join(appDir, "release"),
		filepath.Join(appDir, "release", "package"),
		filepath.Join(appDir, "release", "package", "game"),
		filepath.Join(appDir, "release", "package", "game", "latest"),
		filepath.Join(appDir, "UserData"),
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

// GetUserDataDir returns the user data directory (legacy)
func GetUserDataDir() string {
	return filepath.Join(GetDefaultAppDir(), "UserData")
}

// GetGameDir returns the game directory (legacy)
func GetGameDir(version string) string {
	return filepath.Join(GetDefaultAppDir(), "release", "package", "game", version)
}

// ========== INSTANCE-BASED PATHS ==========

// GetInstancesDir returns the instances directory
func GetInstancesDir() string {
	return filepath.Join(GetDefaultAppDir(), "instances")
}

// GetInstanceDir returns the directory for a specific instance
// Format: instances/{branch}-v{version}
func GetInstanceDir(branch string, version int) string {
	instanceName := fmt.Sprintf("%s-v%d", branch, version)
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
	
	// Check for the game client executable
	var clientPath string
	switch runtime.GOOS {
	case "darwin":
		clientPath = filepath.Join(gameDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient")
	case "windows":
		clientPath = filepath.Join(gameDir, "Client", "HytaleClient.exe")
	default:
		clientPath = filepath.Join(gameDir, "Client", "HytaleClient")
	}
	
	fmt.Printf("[DEBUG] Checking client at: %s\n", clientPath)
	
	if _, err := os.Stat(clientPath); err == nil {
		fmt.Printf("[DEBUG] Client executable found!\n")
		return true
	}
	
	// Fallback: check if Client folder exists with any content
	clientDir := filepath.Join(gameDir, "Client")
	if entries, err := os.ReadDir(clientDir); err == nil && len(entries) > 0 {
		fmt.Printf("[DEBUG] Client folder has %d entries, considering installed\n", len(entries))
		return true
	}
	
	// Final fallback: check if game directory has any content (at least a few files)
	if entries, err := os.ReadDir(gameDir); err == nil && len(entries) >= 3 {
		fmt.Printf("[DEBUG] Game dir has %d entries, considering installed\n", len(entries))
		return true
	}
	
	fmt.Printf("[DEBUG] No valid game installation found\n")
	return false
}

// GetInstalledVersions returns all installed versions for a specific branch
// This is optimized for fast checking
func GetInstalledVersions(branch string) []int {
	instancesDir := GetInstancesDir()
	prefix := branch + "-v"
	
	entries, err := os.ReadDir(instancesDir)
	if err != nil {
		return []int{}
	}
	
	var versions []int
	for _, entry := range entries {
		if entry.IsDir() && len(entry.Name()) > len(prefix) && entry.Name()[:len(prefix)] == prefix {
			versionStr := entry.Name()[len(prefix):]
			if version, err := fmt.Sscanf(versionStr, "%d", new(int)); err == nil && version == 1 {
				var v int
				fmt.Sscanf(versionStr, "%d", &v)
				versions = append(versions, v)
			}
		}
	}
	return versions
}
