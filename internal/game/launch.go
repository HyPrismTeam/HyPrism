package game

import (
	"crypto/md5"
	"encoding/hex"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"

	"HyPrism/internal/env"
)

// Legacy Launch() removed - use LaunchInstance() instead

// LaunchInstance launches a specific branch/version instance
func LaunchInstance(playerName string, branch string, version int) error {
	baseDir := env.GetDefaultAppDir()
	
	// Get instance-specific game directory
	gameDir := env.GetInstanceGameDir(branch, version)
	
	// Verify client exists
	var clientPath string
	switch runtime.GOOS {
	case "darwin":
		clientPath = filepath.Join(gameDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient")
	case "windows":
		clientPath = filepath.Join(gameDir, "Client", "HytaleClient.exe")
	default:
		clientPath = filepath.Join(gameDir, "Client", "HytaleClient")
	}

	if _, err := os.Stat(clientPath); err != nil {
		return fmt.Errorf("game client not found at %s (instance %s v%d not installed): %w", clientPath, branch, version, err)
	}

	// Use instance-specific UserData
	userDataDir := env.GetInstanceUserDataDir(branch, version)
	_ = os.MkdirAll(userDataDir, 0755)

	// Set up Java path
	var jrePath string
	jreDir := filepath.Join(baseDir, "jre")
	
	switch runtime.GOOS {
	case "darwin":
		javaDir := filepath.Join(baseDir, "java")
		javaHomeBin := filepath.Join(javaDir, "Contents", "Home", "bin")
		
		if _, err := os.Stat(javaHomeBin); err != nil {
			os.RemoveAll(javaDir)
			os.MkdirAll(filepath.Join(javaDir, "Contents", "Home"), 0755)
			os.Symlink(filepath.Join(jreDir, "bin"), filepath.Join(javaDir, "Contents", "Home", "bin"))
			os.Symlink(filepath.Join(jreDir, "lib"), filepath.Join(javaDir, "Contents", "Home", "lib"))
		}
		jrePath = filepath.Join(baseDir, "java", "Contents", "Home", "bin", "java")
	case "windows":
		jrePath = filepath.Join(jreDir, "bin", "java.exe")
	default:
		jrePath = filepath.Join(jreDir, "bin", "java")
	}

	if _, err := os.Stat(jrePath); err != nil {
		return fmt.Errorf("Java not found at %s: %w", jrePath, err)
	}

	fmt.Printf("=== LAUNCH INSTANCE ===\n")
	fmt.Printf("Branch: %s, Version: %d\n", branch, version)
	fmt.Printf("Game dir: %s\n", gameDir)
	fmt.Printf("UserData: %s\n", userDataDir)
	fmt.Printf("========================\n")

	var cmd *exec.Cmd
	if runtime.GOOS == "darwin" {
		appBundlePath := filepath.Join(gameDir, "Client", "Hytale.app")
		cmd = exec.Command("open", appBundlePath, 
			"--args",
			"--app-dir", gameDir,
			"--user-dir", userDataDir,
			"--java-exec", jrePath,
			"--auth-mode", "offline",
			"--uuid", "00000000-1337-1337-1337-000000000000",
			"--name", playerName,
		)
	} else if runtime.GOOS == "windows" {
		// Windows - launch directly without special working directory
		cmd = exec.Command(clientPath,
			"--app-dir", gameDir,
			"--user-dir", userDataDir,
			"--java-exec", jrePath,
			"--auth-mode", "offline",
			"--uuid", "00000000-1337-1337-1337-000000000000",
			"--name", playerName,
		)
		cmd.SysProcAttr = getWindowsSysProcAttr()
	} else {
		clientDir := filepath.Join(gameDir, "Client")
		cmd = exec.Command(clientPath,
			"--app-dir", gameDir,
			"--user-dir", userDataDir,
			"--java-exec", jrePath,
			"--auth-mode", "offline",
			"--uuid", "00000000-1337-1337-1337-000000000000",
			"--name", playerName,
		)
		// Set LD_LIBRARY_PATH for Linux to find SDL3_image and other libraries
		env := os.Environ()
		existingLD := os.Getenv("LD_LIBRARY_PATH")
		if existingLD != "" {
			cmd.Env = append(env, fmt.Sprintf("LD_LIBRARY_PATH=%s:%s", clientDir, existingLD))
		} else {
			cmd.Env = append(env, fmt.Sprintf("LD_LIBRARY_PATH=%s", clientDir))
		}
	}
	
	cmd.Dir = baseDir
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	// Set environment for non-Linux platforms (Linux already has it set above)
	if runtime.GOOS != "linux" {
		cmd.Env = os.Environ()
	}

	if err := cmd.Start(); err != nil {
		return fmt.Errorf("failed to start game: %w", err)
	}

	gameProcess = cmd.Process
	gameRunning = true
	
	go func() {
		cmd.Wait()
		gameProcess = nil
		gameRunning = false
	}()

	return nil
}

var gameProcess *os.Process
var gameRunning bool

// KillGame terminates the running game process
func KillGame() error {
	if !gameRunning {
		return fmt.Errorf("no game process running")
	}
	
	// Try to kill by process reference first
	if gameProcess != nil {
		err := gameProcess.Kill()
		if err == nil {
			gameProcess = nil
			gameRunning = false
			fmt.Println("Game process terminated")
			return nil
		}
	}
	
	// If that fails, try to find and kill by name
	if runtime.GOOS == "darwin" {
		exec.Command("pkill", "-f", "Hytale").Run()
	} else if runtime.GOOS == "windows" {
		cmd := exec.Command("taskkill", "/F", "/IM", "HytaleClient.exe")
		cmd.SysProcAttr = getWindowsSysProcAttr()
		cmd.Run()
	} else {
		exec.Command("pkill", "-f", "HytaleClient").Run()
	}
	
	gameProcess = nil
	gameRunning = false
	fmt.Println("Game process terminated")
	return nil
}

// IsGameRunning checks if the game process is still running
func IsGameRunning() bool {
	// Active check for the game process by name
	var isRunning bool
	
	if runtime.GOOS == "darwin" {
		// Check for Hytale process on macOS
		out, err := exec.Command("pgrep", "-f", "Hytale").Output()
		isRunning = err == nil && len(out) > 0
	} else if runtime.GOOS == "windows" {
		// Check for HytaleClient.exe on Windows using WMI (no window flash)
		isRunning = isWindowsProcessRunning("HytaleClient.exe")
	} else {
		// Linux
		out, err := exec.Command("pgrep", "-f", "HytaleClient").Output()
		isRunning = err == nil && len(out) > 0
	}
	
	// Update global state
	gameRunning = isRunning
	if !isRunning {
		gameProcess = nil
	}
	
	return isRunning
}

// WaitForGameExit waits for the game to exit and returns
func WaitForGameExit() {
	if !gameRunning {
		return
	}
	
	// Poll until the game is no longer running
	for IsGameRunning() {
		// Sleep briefly to avoid busy-waiting
		// The IsGameRunning function already checks the actual process
	}
	
	gameProcess = nil
	gameRunning = false
}

// GetGameLogs returns the game log file content
func GetGameLogs() (string, error) {
	baseDir := env.GetDefaultAppDir()
	
	// Try multiple log paths based on typical Hytale log locations
	paths := []string{
		// UserData logs
		filepath.Join(baseDir, "UserData", "logs", "latest.log"),
		filepath.Join(baseDir, "UserData", "logs", "game.log"),
		filepath.Join(baseDir, "UserData", "logs", "client.log"),
		// Instance logs (latest)
		filepath.Join(env.GetInstanceGameDir("release", 0), "logs", "latest.log"),
		filepath.Join(env.GetInstanceGameDir("release", 0), "logs", "game.log"),
		filepath.Join(env.GetInstanceGameDir("release", 0), "Client", "logs", "latest.log"),
		// HyPrism specific log
		filepath.Join(baseDir, "logs", "game.log"),
	}
	
	var allLogs strings.Builder
	foundAny := false
	
	for _, p := range paths {
		if data, err := os.ReadFile(p); err == nil && len(data) > 0 {
			foundAny = true
			allLogs.WriteString(fmt.Sprintf("=== %s ===\n", filepath.Base(p)))
			
			content := string(data)
			// Return last 30KB of each log
			if len(content) > 30*1024 {
				content = content[len(content)-30*1024:]
			}
			allLogs.WriteString(content)
			allLogs.WriteString("\n\n")
		}
	}
	
	if foundAny {
		return allLogs.String(), nil
	}
	
	// List what directories exist to help debug
	var debug strings.Builder
	debug.WriteString("No game logs found. Checking directories:\n\n")
	
	checkDirs := []string{
		filepath.Join(baseDir, "UserData"),
		filepath.Join(baseDir, "UserData", "logs"),
		env.GetInstanceDir("release", 0),
		filepath.Join(env.GetInstanceGameDir("release", 0), "logs"),
	}
	
	for _, dir := range checkDirs {
		if _, err := os.Stat(dir); err == nil {
			debug.WriteString(fmt.Sprintf("✓ %s exists\n", dir))
			// List files in the directory
			if entries, err := os.ReadDir(dir); err == nil {
				for _, e := range entries {
					debug.WriteString(fmt.Sprintf("   - %s\n", e.Name()))
				}
			}
		} else {
			debug.WriteString(fmt.Sprintf("✗ %s not found\n", dir))
		}
	}
	
	return debug.String(), nil
}

// UUID represents a UUID
type UUID [16]byte

// OfflineUUID generates an offline UUID from a player name
func OfflineUUID(name string) UUID {
	data := "OfflinePlayer:" + name
	hash := md5.Sum([]byte(data))
	
	// Set version to 3 (name-based)
	hash[6] = (hash[6] & 0x0f) | 0x30
	// Set variant to 1
	hash[8] = (hash[8] & 0x3f) | 0x80
	
	return UUID(hash)
}

// String returns the UUID as a string
func (u UUID) String() string {
	hex := hex.EncodeToString(u[:])
	return fmt.Sprintf("%s-%s-%s-%s-%s",
		hex[0:8], hex[8:12], hex[12:16], hex[16:20], hex[20:32])
}

// setSDLVideoDriver sets the SDL_VIDEODRIVER environment variable for Linux
func setSDLVideoDriver(cmd *exec.Cmd) {
	if runtime.GOOS != "linux" {
		return
	}

	// Check if running under Wayland
	waylandDisplay := os.Getenv("WAYLAND_DISPLAY")
	xdgSession := os.Getenv("XDG_SESSION_TYPE")

	if waylandDisplay != "" || strings.ToLower(xdgSession) == "wayland" {
		cmd.Env = append(os.Environ(), "SDL_VIDEODRIVER=wayland,x11")
	}
}
