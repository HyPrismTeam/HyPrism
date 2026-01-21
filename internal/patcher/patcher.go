package patcher

import (
	"archive/zip"
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"
)

const (
	// Original Hytale domain to replace
	OriginalDomain = "hytale.com"
	// Default auth domain for online multiplayer
	DefaultAuthDomain = "sanasol.ws"
)

// PatchFlag stores metadata about the patch
type PatchFlag struct {
	PatchedAt      string `json:"patchedAt"`
	OriginalDomain string `json:"originalDomain"`
	TargetDomain   string `json:"targetDomain"`
	PatcherVersion string `json:"patcherVersion"`
}

// PatchResult contains the result of a patching operation
type PatchResult struct {
	Success        bool   `json:"success"`
	AlreadyPatched bool   `json:"alreadyPatched"`
	PatchCount     int    `json:"patchCount"`
	Error          string `json:"error,omitempty"`
}

// ClientPatcher handles patching Hytale binaries to use custom auth server
type ClientPatcher struct {
	targetDomain string
	patchedFlag  string
}

// NewClientPatcher creates a new patcher with the specified target domain
func NewClientPatcher(targetDomain string) *ClientPatcher {
	if targetDomain == "" {
		targetDomain = DefaultAuthDomain
	}
	// Domain length must match original for binary patching to work
	if len(targetDomain) != len(OriginalDomain) {
		fmt.Printf("Warning: Domain %q length (%d) doesn't match original %q (%d), using default\n",
			targetDomain, len(targetDomain), OriginalDomain, len(OriginalDomain))
		targetDomain = DefaultAuthDomain
	}
	return &ClientPatcher{
		targetDomain: targetDomain,
		patchedFlag:  ".patched_custom",
	}
}

// stringToUTF16LE converts a string to UTF-16LE bytes (how .NET stores strings)
func stringToUTF16LE(s string) []byte {
	buf := make([]byte, len(s)*2)
	for i := 0; i < len(s); i++ {
		binary.LittleEndian.PutUint16(buf[i*2:], uint16(s[i]))
	}
	return buf
}

// stringToUTF8 converts a string to UTF-8 bytes (how Java stores strings)
func stringToUTF8(s string) []byte {
	return []byte(s)
}

// findAndReplaceDomainSmart handles domain replacement for .NET AOT binaries
// .NET stores strings in various formats (UTF-16LE, length-prefixed, etc.)
// Optimized version: modifies data in-place without extra allocations
func (p *ClientPatcher) findAndReplaceDomainSmart(data []byte) ([]byte, int) {
	count := 0

	// Get UTF-16LE patterns for old and new domains (without last char)
	oldNoLast := stringToUTF16LE(OriginalDomain[:len(OriginalDomain)-1])
	newNoLast := stringToUTF16LE(p.targetDomain[:len(p.targetDomain)-1])

	oldLastCharByte := OriginalDomain[len(OriginalDomain)-1]
	newLastCharByte := p.targetDomain[len(p.targetDomain)-1]

	// Find and replace in-place for better performance
	pos := 0
	for pos < len(data) {
		idx := bytes.Index(data[pos:], oldNoLast)
		if idx == -1 {
			break
		}
		idx += pos

		lastCharPos := idx + len(oldNoLast)
		if lastCharPos+1 > len(data) {
			pos = idx + 1
			continue
		}

		lastCharFirstByte := data[lastCharPos]

		// Check if this looks like a valid domain occurrence
		if lastCharFirstByte == oldLastCharByte {
			// Copy new domain (without last char) in-place
			copy(data[idx:], newNoLast)
			// Update last char
			data[lastCharPos] = newLastCharByte
			count++
		}
		pos = idx + 1
	}

	return data, count
}

// findAndReplaceDomainUTF8 handles domain replacement for Java JAR files
// Optimized: modifies data in-place
func (p *ClientPatcher) findAndReplaceDomainUTF8(data []byte) ([]byte, int) {
	count := 0

	oldUTF8 := stringToUTF8(OriginalDomain)
	newUTF8 := stringToUTF8(p.targetDomain)

	// Find and replace in-place
	pos := 0
	for pos < len(data) {
		idx := bytes.Index(data[pos:], oldUTF8)
		if idx == -1 {
			break
		}
		idx += pos
		copy(data[idx:], newUTF8)
		count++
		pos = idx + len(oldUTF8)
	}

	return data, count
}

// isPatchedAlready checks if a binary has already been patched
func (p *ClientPatcher) isPatchedAlready(binaryPath string) bool {
	flagFile := binaryPath + p.patchedFlag
	data, err := os.ReadFile(flagFile)
	if err != nil {
		return false
	}

	var flag PatchFlag
	if err := json.Unmarshal(data, &flag); err != nil {
		return false
	}

	return flag.TargetDomain == p.targetDomain
}

// markAsPatched creates a flag file indicating the binary was patched
func (p *ClientPatcher) markAsPatched(binaryPath string) error {
	flag := PatchFlag{
		PatchedAt:      time.Now().Format(time.RFC3339),
		OriginalDomain: OriginalDomain,
		TargetDomain:   p.targetDomain,
		PatcherVersion: "1.0.0",
	}

	data, err := json.MarshalIndent(flag, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(binaryPath+p.patchedFlag, data, 0644)
}

// backupBinary creates a backup of the original binary
func (p *ClientPatcher) backupBinary(binaryPath string) (string, error) {
	backupPath := binaryPath + ".original"
	if _, err := os.Stat(backupPath); err == nil {
		fmt.Println("  Backup already exists")
		return backupPath, nil
	}

	fmt.Printf("  Creating backup at %s\n", filepath.Base(backupPath))
	src, err := os.ReadFile(binaryPath)
	if err != nil {
		return "", err
	}
	return backupPath, os.WriteFile(backupPath, src, 0644)
}

// RestoreBinary restores the original binary from backup
func (p *ClientPatcher) RestoreBinary(binaryPath string) error {
	backupPath := binaryPath + ".original"
	if _, err := os.Stat(backupPath); err != nil {
		return fmt.Errorf("no backup found to restore: %s", backupPath)
	}

	fmt.Printf("Restoring backup from %s\n", filepath.Base(backupPath))
	src, err := os.ReadFile(backupPath)
	if err != nil {
		return err
	}

	if err := os.WriteFile(binaryPath, src, 0755); err != nil {
		return err
	}

	// Remove patch flag
	flagFile := binaryPath + p.patchedFlag
	os.Remove(flagFile)

	fmt.Println("Binary restored successfully")
	return nil
}

// signBinary signs a binary on macOS using ad-hoc signature (simpler version like Hytale-F2P)
func (p *ClientPatcher) signBinary(binaryPath string, deep bool) error {
	if runtime.GOOS != "darwin" {
		return nil
	}

	fmt.Printf("Signing %s...\n", filepath.Base(binaryPath))

	// Remove extended attributes (quarantine)
	exec.Command("xattr", "-cr", binaryPath).Run()

	// Sign with ad-hoc signature - simple and direct
	args := []string{"--force", "--sign", "-"}
	if deep {
		args = append([]string{"--deep"}, args...)
	}
	args = append(args, binaryPath)

	cmd := exec.Command("codesign", args...)
	if output, err := cmd.CombinedOutput(); err != nil {
		fmt.Printf("Warning: codesign failed: %s\n", string(output))
		return err
	}

	fmt.Printf("Signed %s successfully\n", filepath.Base(binaryPath))
	return nil
}

// signAppBundle signs a macOS app bundle (simpler version like Hytale-F2P)
func (p *ClientPatcher) signAppBundle(appBundlePath string) error {
	if runtime.GOOS != "darwin" {
		return nil
	}

	fmt.Printf("Signing app bundle %s...\n", filepath.Base(appBundlePath))

	// Remove extended attributes recursively
	exec.Command("xattr", "-cr", appBundlePath).Run()

	// Sign with deep signing - simple approach
	cmd := exec.Command("codesign", "--force", "--deep", "--sign", "-", appBundlePath)
	
	if output, err := cmd.CombinedOutput(); err != nil {
		fmt.Printf("Warning: codesign failed: %s\n", string(output))
		return err
	}

	fmt.Printf("App bundle signed successfully\n")
	return nil
}

// FindClientPath finds the client binary based on platform
func (p *ClientPatcher) FindClientPath(gameDir string) string {
	var candidates []string

	switch runtime.GOOS {
	case "darwin":
		candidates = []string{
			filepath.Join(gameDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient"),
			filepath.Join(gameDir, "Client", "HytaleClient"),
		}
	case "windows":
		candidates = []string{
			filepath.Join(gameDir, "Client", "HytaleClient.exe"),
		}
	default:
		candidates = []string{
			filepath.Join(gameDir, "Client", "HytaleClient"),
		}
	}

	for _, candidate := range candidates {
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}
	}
	return ""
}

// FindServerPath finds the server JAR path
func (p *ClientPatcher) FindServerPath(gameDir string) string {
	candidates := []string{
		filepath.Join(gameDir, "Server", "HytaleServer.jar"),
		filepath.Join(gameDir, "Server", "server.jar"),
	}

	for _, candidate := range candidates {
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}
	}
	return ""
}

// PatchClient patches the client binary to use custom auth server
func (p *ClientPatcher) PatchClient(clientPath string, progressCallback func(msg string, percent int)) PatchResult {
	if progressCallback == nil {
		progressCallback = func(msg string, percent int) {}
	}

	fmt.Println("=== Client Patcher ===")
	fmt.Printf("Target: %s\n", clientPath)
	fmt.Printf("Replacing: %s -> %s\n", OriginalDomain, p.targetDomain)

	if _, err := os.Stat(clientPath); err != nil {
		errMsg := fmt.Sprintf("Client binary not found: %s", clientPath)
		fmt.Println(errMsg)
		return PatchResult{Success: false, Error: errMsg}
	}

	if p.isPatchedAlready(clientPath) {
		fmt.Printf("Client already patched for %s, skipping\n", p.targetDomain)
		progressCallback("Client already patched", 100)
		return PatchResult{Success: true, AlreadyPatched: true, PatchCount: 0}
	}

	progressCallback("Preparing to patch client...", 10)
	fmt.Println("Creating backup...")
	if _, err := p.backupBinary(clientPath); err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to create backup: %v", err)}
	}

	progressCallback("Reading client binary...", 20)
	fmt.Println("Reading client binary...")
	data, err := os.ReadFile(clientPath)
	if err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to read client: %v", err)}
	}
	fmt.Printf("Binary size: %.2f MB\n", float64(len(data))/1024/1024)

	progressCallback("Patching domain references...", 50)
	fmt.Printf("Patching domain references (in-place optimization)...\n")
	patchedData, count := p.findAndReplaceDomainSmart(data)

	fmt.Printf("Patched %d domain occurrences\n", count)

	if count == 0 {
		fmt.Println("No occurrences found - binary may already be modified or has different format")
		return PatchResult{Success: true, PatchCount: 0}
	}

	progressCallback("Writing patched binary...", 80)
	fmt.Println("Writing patched binary...")
	if err := os.WriteFile(clientPath, patchedData, 0755); err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to write patched client: %v", err)}
	}

	if err := p.markAsPatched(clientPath); err != nil {
		fmt.Printf("Warning: Failed to mark as patched: %v\n", err)
	}

	// DON'T sign here - will be done after patching in launch.go
	// Signing needs to happen AFTER all patching is complete

	progressCallback("Patching complete", 100)
	fmt.Printf("Successfully patched %d domain occurrences\n", count)
	fmt.Println("=== Patching Complete ===")

	return PatchResult{Success: true, PatchCount: count}
}

// PatchServer patches the server JAR to use custom auth server
func (p *ClientPatcher) PatchServer(serverPath string, progressCallback func(msg string, percent int)) PatchResult {
	if progressCallback == nil {
		progressCallback = func(msg string, percent int) {}
	}

	fmt.Println("=== Server Patcher ===")
	fmt.Printf("Target: %s\n", serverPath)
	fmt.Printf("Replacing: %s -> %s\n", OriginalDomain, p.targetDomain)

	if _, err := os.Stat(serverPath); err != nil {
		errMsg := fmt.Sprintf("Server JAR not found: %s", serverPath)
		fmt.Println(errMsg)
		return PatchResult{Success: false, Error: errMsg}
	}

	if p.isPatchedAlready(serverPath) {
		fmt.Printf("Server already patched for %s, skipping\n", p.targetDomain)
		progressCallback("Server already patched", 100)
		return PatchResult{Success: true, AlreadyPatched: true, PatchCount: 0}
	}

	progressCallback("Preparing to patch server...", 10)
	fmt.Println("Creating backup...")
	if _, err := p.backupBinary(serverPath); err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to create backup: %v", err)}
	}

	progressCallback("Opening server JAR...", 20)
	fmt.Println("Opening server JAR...")

	// Open and read the JAR (ZIP) file
	reader, err := zip.OpenReader(serverPath)
	if err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to open JAR: %v", err)}
	}
	defer reader.Close()

	fmt.Printf("JAR contains %d entries\n", len(reader.File))

	progressCallback("Patching class files...", 40)
	fmt.Println("Scanning JAR entries for domain references...")

	oldUTF8 := stringToUTF8(OriginalDomain)
	totalCount := 0

	// Create new JAR in memory
	var buf bytes.Buffer
	writer := zip.NewWriter(&buf)

	for _, file := range reader.File {
		// Only patch relevant file types
		shouldPatch := strings.HasSuffix(file.Name, ".class") ||
			strings.HasSuffix(file.Name, ".properties") ||
			strings.HasSuffix(file.Name, ".json") ||
			strings.HasSuffix(file.Name, ".xml") ||
			strings.HasSuffix(file.Name, ".yml")

		rc, err := file.Open()
		if err != nil {
			return PatchResult{Success: false, Error: fmt.Sprintf("Failed to read JAR entry: %v", err)}
		}

		data, err := io.ReadAll(rc)
		rc.Close()
		if err != nil {
			return PatchResult{Success: false, Error: fmt.Sprintf("Failed to read entry data: %v", err)}
		}

		// Only patch if domain is present (optimization)
		if shouldPatch && bytes.Contains(data, oldUTF8) {
			patchedData, count := p.findAndReplaceDomainUTF8(data)
			if count > 0 {
				totalCount += count
				data = patchedData
			}
		}

		// Write entry to new JAR
		header, err := zip.FileInfoHeader(file.FileInfo())
		if err != nil {
			return PatchResult{Success: false, Error: fmt.Sprintf("Failed to create header: %v", err)}
		}
		header.Name = file.Name
		header.Method = file.Method

		w, err := writer.CreateHeader(header)
		if err != nil {
			return PatchResult{Success: false, Error: fmt.Sprintf("Failed to create entry: %v", err)}
		}

		if _, err := w.Write(data); err != nil {
			return PatchResult{Success: false, Error: fmt.Sprintf("Failed to write entry: %v", err)}
		}
	}

	if err := writer.Close(); err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to close JAR: %v", err)}
	}

	if totalCount == 0 {
		fmt.Println("No occurrences of hytale.com found in server JAR entries")
		return PatchResult{Success: true, PatchCount: 0}
	}

	progressCallback("Writing patched JAR...", 80)
	fmt.Println("Writing patched JAR...")
	if err := os.WriteFile(serverPath, buf.Bytes(), 0644); err != nil {
		return PatchResult{Success: false, Error: fmt.Sprintf("Failed to write patched JAR: %v", err)}
	}

	if err := p.markAsPatched(serverPath); err != nil {
		fmt.Printf("Warning: Failed to mark as patched: %v\n", err)
	}

	progressCallback("Server patching complete", 100)
	fmt.Printf("Successfully patched %d occurrences in server\n", totalCount)
	fmt.Println("=== Server Patching Complete ===")

	return PatchResult{Success: true, PatchCount: totalCount}
}

// EnsurePatched ensures both client and server are patched before launching
func (p *ClientPatcher) EnsurePatched(gameDir string, progressCallback func(msg string, percent int)) PatchResult {
	if progressCallback == nil {
		progressCallback = func(msg string, percent int) {}
	}

	result := PatchResult{Success: true}
	totalPatches := 0

	// Patch client
	clientPath := p.FindClientPath(gameDir)
	if clientPath != "" {
		progressCallback("Patching client binary...", 10)
		clientResult := p.PatchClient(clientPath, func(msg string, pct int) {
			progressCallback(fmt.Sprintf("Client: %s", msg), pct/2)
		})
		if !clientResult.Success {
			return clientResult
		}
		totalPatches += clientResult.PatchCount
		result.AlreadyPatched = clientResult.AlreadyPatched
	} else {
		fmt.Println("Warning: Could not find HytaleClient binary")
	}

	// Patch server (non-fatal if not found - like Hytale-F2P)
	serverPath := p.FindServerPath(gameDir)
	if serverPath != "" {
		progressCallback("Patching server JAR...", 50)
		serverResult := p.PatchServer(serverPath, func(msg string, pct int) {
			progressCallback(fmt.Sprintf("Server: %s", msg), 50+pct/2)
		})
		// Don't fail if server patching fails - just warn
		if !serverResult.Success {
			fmt.Printf("Warning: Server patching failed: %s\n", serverResult.Error)
		} else {
			totalPatches += serverResult.PatchCount
			result.AlreadyPatched = result.AlreadyPatched && serverResult.AlreadyPatched
		}
	} else {
		fmt.Println("Warning: Could not find HytaleServer.jar (this is OK for client-only)")
	}

	result.PatchCount = totalPatches
	progressCallback("Patching complete", 100)
	return result
}

// RestorePatched restores both client and server from backups (for switching to offline mode)
func (p *ClientPatcher) RestorePatched(gameDir string, progressCallback func(msg string, percent int)) PatchResult {
	if progressCallback == nil {
		progressCallback = func(msg string, percent int) {}
	}

	result := PatchResult{Success: true}

	// Restore client
	clientPath := p.FindClientPath(gameDir)
	if clientPath != "" {
		progressCallback("Restoring client binary...", 25)
		if err := p.RestoreBinary(clientPath); err != nil {
			fmt.Printf("Warning: Could not restore client: %v\n", err)
		} else {
			// Re-sign on macOS after restore
			if runtime.GOOS == "darwin" {
				appBundlePath := filepath.Dir(filepath.Dir(filepath.Dir(clientPath)))
				if strings.HasSuffix(appBundlePath, ".app") {
					p.signAppBundle(appBundlePath)
				} else {
					p.signBinary(clientPath, false)
				}
			}
		}
	}

	// Restore server
	serverPath := p.FindServerPath(gameDir)
	if serverPath != "" {
		progressCallback("Restoring server JAR...", 75)
		if err := p.RestoreBinary(serverPath); err != nil {
			fmt.Printf("Warning: Could not restore server: %v\n", err)
		}
	}

	progressCallback("Restore complete", 100)
	return result
}

// GetTargetDomain returns the configured target domain
func (p *ClientPatcher) GetTargetDomain() string {
	return p.targetDomain
}

// IsPatched checks if the game is already patched
func (p *ClientPatcher) IsPatched(gameDir string) bool {
	clientPath := p.FindClientPath(gameDir)
	if clientPath == "" {
		return false
	}
	return p.isPatchedAlready(clientPath)
}
