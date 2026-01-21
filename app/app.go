package app

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"

	"HyPrism/internal/config"
	"HyPrism/internal/env"
	"HyPrism/internal/game"
	"HyPrism/internal/mods"
	"HyPrism/internal/news"
	"HyPrism/internal/pwr"

	wailsRuntime "github.com/wailsapp/wails/v2/pkg/runtime"
)

// App struct
type App struct {
	ctx         context.Context
	cfg         *config.Config
	newsService *news.NewsService
}

// ProgressUpdate represents download/install progress
type ProgressUpdate struct {
	Stage       string  `json:"stage"`
	Progress    float64 `json:"progress"`
	Message     string  `json:"message"`
	CurrentFile string  `json:"currentFile"`
	Speed       string  `json:"speed"`
	Downloaded  int64   `json:"downloaded"`
	Total       int64   `json:"total"`
}

// NewApp creates a new App instance
func NewApp() *App {
	cfg, _ := config.Load()
	if cfg == nil {
		cfg = config.Default()
	}
	return &App{
		cfg:         cfg,
		newsService: news.NewNewsService(),
	}
}

// Startup is called when the app starts
func (a *App) Startup(ctx context.Context) {
	a.ctx = ctx

	fmt.Println("╔══════════════════════════════════════════════════════════════╗")
	fmt.Println("║           HyPrism - Hytale Launcher Starting...             ║")
	fmt.Printf("║           Version: %-43s║\n", AppVersion)
	fmt.Println("╚══════════════════════════════════════════════════════════════╝")

	// Set custom instance directory if configured
	if a.cfg.CustomInstanceDir != "" {
		env.SetCustomInstanceDir(a.cfg.CustomInstanceDir)
		fmt.Printf("Using custom instances directory: %s\n", a.cfg.CustomInstanceDir)
	}

	// Initialize environment
	if err := env.CreateFolders(); err != nil {
		fmt.Printf("Warning: Failed to create folders: %v\n", err)
	}

	// Check for launcher updates in background
	go func() {
		fmt.Println("Starting background update check...")
		a.checkUpdateSilently()
	}()
}

// Shutdown is called when the app closes
func (a *App) Shutdown(ctx context.Context) {
	fmt.Println("HyPrism shutting down...")
}

// SelectInstanceDirectory opens a folder picker dialog and saves the selected directory
func (a *App) SelectInstanceDirectory() (string, error) {
	selectedDir, err := wailsRuntime.OpenDirectoryDialog(a.ctx, wailsRuntime.OpenDialogOptions{
		Title: "Select Instances Directory",
	})
	if err != nil {
		return "", fmt.Errorf("failed to open directory dialog: %w", err)
	}
	
	if selectedDir == "" {
		// User cancelled the dialog
		return "", nil
	}
	
	// Create directory if it doesn't exist
	if err := os.MkdirAll(selectedDir, 0755); err != nil {
		return "", fmt.Errorf("failed to create directory: %w\n\nPlease ensure:\n• The drive is properly connected\n• You have write permissions\n• The path is valid", err)
	}
	
	// Verify the directory is writable (important for external drives)
	testFile := filepath.Join(selectedDir, ".hyprism-test")
	if err := os.WriteFile(testFile, []byte("test"), 0644); err != nil {
		return "", fmt.Errorf("directory is not writable: %w\n\nPlease check:\n• Drive is not read-only\n• You have write permissions\n• Drive has free space", err)
	}
	os.Remove(testFile)
	
	// Save to config
	a.cfg.CustomInstanceDir = selectedDir
	env.SetCustomInstanceDir(selectedDir)
	if err := config.Save(a.cfg); err != nil {
		return "", fmt.Errorf("failed to save config: %w", err)
	}
	
	fmt.Printf("Instance directory updated to: %s\n", selectedDir)
	return selectedDir, nil
}

// progressCallback sends progress updates to frontend
func (a *App) progressCallback(stage string, progress float64, message string, currentFile string, speed string, downloaded, total int64) {
	wailsRuntime.EventsEmit(a.ctx, "progress-update", ProgressUpdate{
		Stage:       stage,
		Progress:    progress,
		Message:     message,
		CurrentFile: currentFile,
		Speed:       speed,
		Downloaded:  downloaded,
		Total:       total,
	})
}

// emitError sends structured errors to frontend
func (a *App) emitError(err error) {
	if appErr, ok := err.(*AppError); ok {
		wailsRuntime.EventsEmit(a.ctx, "error", appErr)
	} else {
		wailsRuntime.EventsEmit(a.ctx, "error", NewAppError(ErrorTypeUnknown, err.Error(), err))
	}
}

// AppVersion is the current launcher version - set at build time via ldflags
var AppVersion string = "dev"

// AppTitle is the app window title - set at build time via ldflags
var AppTitle string = "HyPrism - Hytale Launcher"

// GetLauncherVersion returns the current launcher version
func (a *App) GetLauncherVersion() string {
	return AppVersion
}

// GetVersions returns current and latest game versions
func (a *App) GetVersions() (currentVersion string, latestVersion string) {
	current := pwr.GetLocalVersion()
	latest := pwr.FindLatestVersion("release")
	return current, strconv.Itoa(latest)
}

// DownloadAndLaunch downloads the game if needed and launches it
func (a *App) DownloadAndLaunch(playerName string) error {
	// Validate nickname
	if len(playerName) == 0 {
		err := ValidationError("Please enter a nickname")
		a.emitError(err)
		return err
	}

	if len(playerName) > 16 {
		err := ValidationError("Nickname is too long (max 16 characters)")
		a.emitError(err)
		return err
	}

	// Get configured version type and version
	versionType := a.GetVersionType()
	version := a.GetSelectedVersion()

	// Ensure game is installed for the configured version type and version
	if err := game.EnsureInstalledVersionSpecific(a.ctx, versionType, version, a.progressCallback); err != nil {
		wrappedErr := GameError("Failed to install or update game", err)
		a.emitError(wrappedErr)
		return wrappedErr
	}

	// Launch the game with branch, version, and online mode settings
	a.progressCallback("launch", 100, "Launching game...", "", "", 0, 0)

	// Use online mode from config
	opts := game.LaunchOptions{
		PlayerName: playerName,
		Branch:     versionType,
		Version:    version,
		OnlineMode: a.cfg.OnlineMode,
		AuthDomain: a.cfg.AuthDomain,
	}

	if err := game.LaunchInstanceWithOptions(opts); err != nil {
		wrappedErr := GameError("Failed to launch game", err)
		a.emitError(wrappedErr)
		return wrappedErr
	}

	return nil
}

// GetLogs returns launcher logs
func (a *App) GetLogs() (string, error) {
	logPath := filepath.Join(env.GetDefaultAppDir(), "logs", "launcher.log")
	data, err := os.ReadFile(logPath)
	if err != nil {
		return "", err
	}
	return string(data), nil
}

// ==================== MOD MANAGER ====================

// SearchMods searches for mods on CurseForge
func (a *App) SearchMods(query string, categoryID int, page int) (*mods.SearchResult, error) {
	return mods.SearchMods(a.ctx, mods.SearchModsParams{
		Query:      query,
		CategoryID: categoryID,
		SortField:  "2", // Popularity
		SortOrder:  "desc",
		PageSize:   20,
		Index:      page * 20,
	})
}

// GetInstalledMods returns all installed mods (legacy)
func (a *App) GetInstalledMods() ([]mods.Mod, error) {
	return mods.GetInstalledMods()
}

// GetInstanceInstalledMods returns installed mods for a specific instance
func (a *App) GetInstanceInstalledMods(branch string, version int) ([]mods.Mod, error) {
	return mods.GetInstanceInstalledMods(branch, version)
}

// GetModDetails returns detailed info about a specific mod from CurseForge
func (a *App) GetModDetails(modID int) (*mods.CurseForgeMod, error) {
	return mods.GetModDetails(a.ctx, modID)
}

// GetModFiles returns available files/versions for a mod from CurseForge
func (a *App) GetModFiles(modID int) ([]mods.ModFile, error) {
	return mods.GetModFiles(a.ctx, modID)
}

// InstallMod downloads and installs a mod from CurseForge (legacy)
func (a *App) InstallMod(modID int) error {
	cfMod, err := mods.GetModDetails(a.ctx, modID)
	if err != nil {
		return err
	}

	return mods.DownloadMod(a.ctx, *cfMod, func(progress float64, message string) {
		wailsRuntime.EventsEmit(a.ctx, "mod-progress", map[string]interface{}{
			"progress": progress,
			"message":  message,
		})
	})
}

// InstallModToInstance downloads and installs a mod to a specific instance
func (a *App) InstallModToInstance(modID int, branch string, version int) error {
	cfMod, err := mods.GetModDetails(a.ctx, modID)
	if err != nil {
		return err
	}

	return mods.DownloadModToInstance(a.ctx, *cfMod, branch, version, func(progress float64, message string) {
		wailsRuntime.EventsEmit(a.ctx, "mod-progress", map[string]interface{}{
			"progress": progress,
			"message":  message,
		})
	})
}

// InstallModFile downloads and installs a specific mod file version from CurseForge (legacy)
func (a *App) InstallModFile(modID int, fileID int) error {
	return mods.DownloadModFile(a.ctx, modID, fileID, func(progress float64, message string) {
		wailsRuntime.EventsEmit(a.ctx, "mod-progress", map[string]interface{}{
			"progress": progress,
			"message":  message,
		})
	})
}

// InstallModFileToInstance downloads and installs a specific mod file version to an instance
func (a *App) InstallModFileToInstance(modID int, fileID int, branch string, version int) error {
	return mods.DownloadModFileToInstance(a.ctx, modID, fileID, branch, version, func(progress float64, message string) {
		wailsRuntime.EventsEmit(a.ctx, "mod-progress", map[string]interface{}{
			"progress": progress,
			"message":  message,
		})
	})
}

// UninstallMod removes an installed mod (legacy)
func (a *App) UninstallMod(modID string) error {
	return mods.RemoveMod(modID)
}

// UninstallInstanceMod removes an installed mod from an instance
func (a *App) UninstallInstanceMod(modID string, branch string, version int) error {
	return mods.RemoveInstanceMod(modID, branch, version)
}

// ToggleMod enables or disables a mod (legacy)
func (a *App) ToggleMod(modID string, enabled bool) error {
	return mods.ToggleMod(modID, enabled)
}

// ToggleInstanceMod enables or disables a mod in an instance
func (a *App) ToggleInstanceMod(modID string, enabled bool, branch string, version int) error {
	return mods.ToggleInstanceMod(modID, enabled, branch, version)
}

// GetModCategories returns available mod categories
func (a *App) GetModCategories() ([]mods.ModCategory, error) {
	return mods.GetCategories(a.ctx)
}

// CheckModUpdates checks for mod updates (legacy)
func (a *App) CheckModUpdates() ([]mods.Mod, error) {
	return mods.CheckForUpdates(a.ctx)
}

// CheckInstanceModUpdates checks for mod updates in an instance
func (a *App) CheckInstanceModUpdates(branch string, version int) ([]mods.Mod, error) {
	return mods.CheckInstanceForUpdates(a.ctx, branch, version)
}

// OpenModsFolder opens the mods folder in file explorer (legacy)
func (a *App) OpenModsFolder() error {
	modsDir := mods.GetModsDir()
	if err := os.MkdirAll(modsDir, 0755); err != nil {
		return err
	}
	return openFolder(modsDir)
}

// OpenInstanceModsFolder opens the mods folder for a specific instance
func (a *App) OpenInstanceModsFolder(branch string, version int) error {
	modsDir := mods.GetInstanceModsDir(branch, version)
	if err := os.MkdirAll(modsDir, 0755); err != nil {
		return err
	}
	return openFolder(modsDir)
}

// ==================== UTILITY ====================

// openFolder opens a folder in the system file explorer
func openFolder(path string) error {
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		cmd = exec.Command("explorer", path)
	case "darwin":
		cmd = exec.Command("open", path)
	case "linux":
		cmd = exec.Command("xdg-open", path)
	default:
		return fmt.Errorf("unsupported platform")
	}
	return cmd.Start()
}

// OpenGameFolder opens the game folder for latest instance
func (a *App) OpenGameFolder() error {
	gameDir := env.GetInstanceDir("release", 0)
	if err := os.MkdirAll(gameDir, 0755); err != nil {
		return err
	}
	return openFolder(gameDir)
}

// GetGamePath returns the game installation path for latest instance
func (a *App) GetGamePath() string {
	return env.GetInstanceDir("release", 0)
}

// IsGameInstalled checks if the game is installed (latest instance)
func (a *App) IsGameInstalled() bool {
	return env.IsVersionInstalled("release", 0)
}

// QuickLaunch launches the game with saved nickname
func (a *App) QuickLaunch() error {
	nick := a.cfg.Nick
	if nick == "" {
		nick = "Player"
	}
	return a.DownloadAndLaunch(nick)
}

// ExitGame terminates the running game process
func (a *App) ExitGame() error {
	return game.KillGame()
}

// IsGameRunning returns whether the game is currently running
func (a *App) IsGameRunning() bool {
	return game.IsGameRunning()
}

// GetGameLogs returns the game log content
func (a *App) GetGameLogs() (string, error) {
	return game.GetGameLogs()
}

// GetAvailableVersions returns list of available game versions (release and prerelease)
func (a *App) GetAvailableVersions() map[string]int {
	versions := make(map[string]int)
	versions["release"] = pwr.FindLatestVersion("release")
	versions["pre-release"] = pwr.FindLatestVersion("pre-release")
	return versions
}

// GetVersionList returns all available version numbers for a branch (latest=0, then specific versions)
func (a *App) GetVersionList(branch string) []int {
	latest := pwr.FindLatestVersion(branch)
	if latest <= 0 {
		return []int{0} // Always include version 0 (latest auto-updating instance)
	}
	// Return versions: 0 (latest), then latest down to 1 (newest first)
	versions := make([]int, latest+1)
	versions[0] = 0 // Version 0 = auto-updating latest
	for i := 0; i < latest; i++ {
		versions[i+1] = latest - i
	}
	return versions
}

// IsVersionInstalled checks if a specific branch/version combination is installed
func (a *App) IsVersionInstalled(branch string, version int) bool {
	return env.IsVersionInstalled(branch, version)
}

// GetInstalledVersionsForBranch returns all installed version numbers for a specific branch
func (a *App) GetInstalledVersionsForBranch(branch string) []int {
	return env.GetInstalledVersions(branch)
}

// CheckLatestNeedsUpdate checks if the 'latest' instance needs updating
// Returns true if latest is installed but not at current latest version
func (a *App) CheckLatestNeedsUpdate(branch string) bool {
	// Check if latest instance is installed
	if !env.IsVersionInstalled(branch, 0) {
		return false
	}
	
	// Get the actual latest version number
	latestVersion := pwr.FindLatestVersion(branch)
	if latestVersion <= 0 {
		return false
	}
	
	// Check if the latest instance has the current version
	// We can check this by looking at the instance's installed version file
	instanceDir := env.GetInstanceDir(branch, 0)
	versionFile := filepath.Join(instanceDir, "version.txt")
	data, err := os.ReadFile(versionFile)
	if err != nil {
		// No version file means fresh install or corrupted
		return true
	}
	
	installedVersionStr := string(data)
	installedVersionStr = filepath.Base(strings.TrimSpace(installedVersionStr))
	installedVersion, err := strconv.Atoi(installedVersionStr)
	if err != nil {
		return true
	}
	
	// If installed version is less than latest, needs update
	return installedVersion < latestVersion
}

// GetCurrentVersion returns the currently installed game version with formatted date
func (a *App) GetCurrentVersion() string {
	return pwr.GetLocalVersionFull()
}

// InstalledVersion represents an installed game version
type InstalledVersion struct {
	Version     int    `json:"version"`
	VersionType string `json:"versionType"`
	InstallDate string `json:"installDate"`
}

// GetInstalledVersions returns all installed game versions
func (a *App) GetInstalledVersions() []InstalledVersion {
	versions := pwr.GetInstalledVersions()
	result := make([]InstalledVersion, 0, len(versions))
	for _, v := range versions {
		result = append(result, InstalledVersion{
			Version:     v.Version,
			VersionType: v.VersionType,
			InstallDate: v.InstallDate,
		})
	}
	return result
}

// SwitchVersion switches to a different installed version
func (a *App) SwitchVersion(version int) error {
	return pwr.SwitchVersion(version)
}

// DownloadVersion downloads a specific version type
func (a *App) DownloadVersion(versionType string, playerName string) error {
	if versionType != "release" && versionType != "prerelease" {
		return fmt.Errorf("invalid version type: %s", versionType)
	}
	
	// Validate nickname
	if len(playerName) == 0 {
		err := ValidationError("Please enter a nickname")
		a.emitError(err)
		return err
	}

	if len(playerName) > 16 {
		err := ValidationError("Nickname is too long (max 16 characters)")
		a.emitError(err)
		return err
	}

	// Install specific version
	if err := game.EnsureInstalledVersion(a.ctx, versionType, a.progressCallback); err != nil {
		wrappedErr := GameError("Failed to install game version", err)
		a.emitError(wrappedErr)
		return wrappedErr
	}

	// Launch the game
	a.progressCallback("launch", 100, "Launching game...", "", "", 0, 0)

	if err := game.LaunchInstance(playerName, versionType, 0); err != nil {
		wrappedErr := GameError("Failed to launch game", err)
		a.emitError(wrappedErr)
		return wrappedErr
	}

	return nil
}

// ==================== NEWS ====================

// GetNews fetches news from hytale.com
func (a *App) GetNews(limit int) ([]news.NewsItem, error) {
	if limit <= 0 {
		limit = 5
	}
	return a.newsService.GetNews(limit)
}
