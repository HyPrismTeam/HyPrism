package pwr

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"

	"HyPrism/internal/env"
	"HyPrism/internal/util/download"
)

// getOS returns the operating system name in the format expected by Hytale's patch server
func getOS() string {
	switch runtime.GOOS {
	case "windows":
		return "windows"
	case "darwin":
		return "darwin"
	case "linux":
		return "linux"
	default:
		return "unknown"
	}
}

// getArch returns the architecture in the format expected by Hytale's patch server
func getArch() string {
	switch runtime.GOARCH {
	case "amd64", "x64":
		return "amd64"
	case "arm64":
		return "arm64"
	default:
		return runtime.GOARCH
	}
}

// normalizeVersionType converts version type to the API format
// "prerelease" or "pre-release" -> "pre-release"
// "release" -> "release"
func normalizeVersionType(versionType string) string {
	if versionType == "prerelease" || versionType == "pre-release" {
		return "pre-release"
	}
	return versionType
}

// VersionCheckResult contains the result of a version check
type VersionCheckResult struct {
	LatestVersion int
	SuccessURL    string
	CheckedURLs   []string
	Error         error
}

// FindLatestVersion finds the latest game version
func FindLatestVersion(versionType string) int {
	result := performVersionCheck(versionType)
	return result.LatestVersion
}

// FindLatestVersionWithDetails returns detailed version check results
func FindLatestVersionWithDetails(versionType string) VersionCheckResult {
	return performVersionCheck(versionType)
}

func performVersionCheck(versionType string) VersionCheckResult {
	result := VersionCheckResult{}
	
	osName := getOS()
	arch := getArch()
	apiVersionType := normalizeVersionType(versionType)
	
	if osName == "unknown" {
		result.Error = fmt.Errorf("unsupported operating system")
		return result
	}

	// Use known latest versions as starting points for faster checking
	// Release is around v3, Pre-release is around v7
	var startVersion int
	if apiVersionType == "pre-release" {
		startVersion = 10 // Start checking from v10 down
	} else {
		startVersion = 5 // Start checking from v5 down
	}

	client := download.GetSharedClient()

	// Check versions in parallel from startVersion down to 1
	type versionCheck struct {
		version int
		exists  bool
		url     string
	}
	
	checkChan := make(chan versionCheck, startVersion)
	
	// Launch parallel checks
	for v := 1; v <= startVersion; v++ {
		go func(ver int) {
			url := fmt.Sprintf("https://game-patches.hytale.com/patches/%s/%s/%s/0/%d.pwr",
				osName, arch, apiVersionType, ver)
			
			resp, err := client.Head(url)
			exists := err == nil && resp.StatusCode == http.StatusOK
			if resp != nil {
				resp.Body.Close()
			}
			
			checkChan <- versionCheck{version: ver, exists: exists, url: url}
		}(v)
	}
	
	// Collect results
	for i := 0; i < startVersion; i++ {
		check := <-checkChan
		result.CheckedURLs = append(result.CheckedURLs, check.url)
		if check.exists && check.version > result.LatestVersion {
			result.LatestVersion = check.version
			result.SuccessURL = check.url
		}
	}

	fmt.Printf("Latest %s version found: %d\n", apiVersionType, result.LatestVersion)
	return result
}

// GetLocalVersion returns the currently installed version
func GetLocalVersion() string {
	versionFile := filepath.Join(env.GetDefaultAppDir(), "version.txt")
	data, err := os.ReadFile(versionFile)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(data))
}

// GetLocalVersionFull returns a formatted version string with date
func GetLocalVersionFull() string {
	versionFile := filepath.Join(env.GetDefaultAppDir(), "version.txt")
	data, err := os.ReadFile(versionFile)
	if err != nil {
		return "Not installed"
	}
	
	version := strings.TrimSpace(string(data))
	if version == "" || version == "0" {
		return "Not installed"
	}
	
	// Check file modification time for version date
	info, err := os.Stat(versionFile)
	if err == nil {
		t := info.ModTime()
		return fmt.Sprintf("%s (build %s)", t.Format("2006.01.02"), version)
	}
	
	return fmt.Sprintf("build %s", version)
}

// SaveLocalVersion saves the version number
func SaveLocalVersion(version int) error {
	versionFile := filepath.Join(env.GetDefaultAppDir(), "version.txt")
	return os.WriteFile(versionFile, []byte(strconv.Itoa(version)), 0644)
}

// DownloadPWR downloads a PWR patch file - matches Hytale-F2P implementation
func DownloadPWR(ctx context.Context, versionType string, fromVer, toVer int, progressCallback func(stage string, progress float64, message string, currentFile string, speed string, downloaded, total int64)) (string, error) {
	osName := getOS()
	arch := getArch()
	apiVersionType := normalizeVersionType(versionType)

	// Try patch URL - for fresh install always use 0 as fromVer
	// The Hytale patch server provides full game at /0/{version}.pwr
	var url string
	var useFromZero bool
	
	// First try the incremental patch if we have a previous version
	if fromVer > 0 {
		url = fmt.Sprintf("https://game-patches.hytale.com/patches/%s/%s/%s/%d/%d.pwr",
			osName, arch, apiVersionType, fromVer, toVer)
		
		// Quick check if incremental patch exists
		client := &http.Client{Timeout: 10 * time.Second}
		resp, err := client.Head(url)
		if err != nil || resp.StatusCode != http.StatusOK {
			// Incremental patch not available, use full install from 0
			fmt.Printf("Incremental patch %d->%d not available, using full install\n", fromVer, toVer)
			useFromZero = true
		}
		if resp != nil {
			resp.Body.Close()
		}
	} else {
		useFromZero = true
	}
	
	// Use full game patch from version 0
	if useFromZero {
		url = fmt.Sprintf("https://game-patches.hytale.com/patches/%s/%s/%s/0/%d.pwr",
			osName, arch, apiVersionType, toVer)
	}

	fmt.Printf("Downloading PWR from: %s\n", url)

	cacheDir := env.GetCacheDir()
	if err := os.MkdirAll(cacheDir, 0755); err != nil {
		return "", fmt.Errorf("failed to create cache directory: %w", err)
	}
	
	pwrPath := filepath.Join(cacheDir, fmt.Sprintf("%d.pwr", toVer))

	// First do a HEAD request to get expected file size
	headReq, err := http.NewRequestWithContext(ctx, "HEAD", url, nil)
	if err != nil {
		return "", fmt.Errorf("failed to create HEAD request: %w", err)
	}
	headReq.Header.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
	
	headClient := &http.Client{Timeout: 30 * time.Second}
	headResp, err := headClient.Do(headReq)
	var expectedSize int64
	if err == nil && headResp.StatusCode == http.StatusOK {
		expectedSize = headResp.ContentLength
		headResp.Body.Close()
		fmt.Printf("Expected PWR file size: %d bytes\n", expectedSize)
	}

	// Check if already cached AND complete
	if info, err := os.Stat(pwrPath); err == nil && info.Size() > 0 {
		// Verify file is complete (matches expected size or at least > 1GB for a full game patch)
		if expectedSize > 0 && info.Size() == expectedSize {
			fmt.Printf("PWR file found in cache (verified): %s (%d bytes)\n", pwrPath, info.Size())
			return pwrPath, nil
		} else if expectedSize > 0 && info.Size() < expectedSize {
			fmt.Printf("PWR file in cache is incomplete (%d of %d bytes), re-downloading...\n", info.Size(), expectedSize)
			os.Remove(pwrPath)
		} else if expectedSize == 0 && info.Size() > 1024*1024*1024 {
			// If we couldn't get expected size, assume files > 1GB are complete
			fmt.Printf("PWR file found in cache: %s (%d bytes)\n", pwrPath, info.Size())
			return pwrPath, nil
		} else {
			fmt.Printf("PWR file in cache may be incomplete (%d bytes), re-downloading...\n", info.Size())
			os.Remove(pwrPath)
		}
	}

	if progressCallback != nil {
		progressCallback("download", 0, "Downloading Hytale...", filepath.Base(pwrPath), "", 0, 0)
	}

	// Download with retries and resume capability
	maxRetries := 5
	var lastErr error
	
	for attempt := 1; attempt <= maxRetries; attempt++ {
		if attempt > 1 {
			fmt.Printf("Retry attempt %d/%d for PWR download...\n", attempt, maxRetries)
			if progressCallback != nil {
				progressCallback("download", 0, fmt.Sprintf("Retrying download (attempt %d/%d)...", attempt, maxRetries), filepath.Base(pwrPath), "", 0, 0)
			}
			time.Sleep(2 * time.Second)
		}
		
		err := downloadPWRFile(ctx, url, pwrPath, expectedSize, progressCallback)
		if err == nil {
			return pwrPath, nil
		}
		
		lastErr = err
		fmt.Printf("Download attempt %d failed: %v\n", attempt, err)
	}
	
	return "", fmt.Errorf("failed to download after %d attempts: %w", maxRetries, lastErr)
}

func downloadPWRFile(ctx context.Context, url, pwrPath string, expectedSize int64, progressCallback func(stage string, progress float64, message string, currentFile string, speed string, downloaded, total int64)) error {
	// Check if partial file exists
	var resumeFrom int64 = 0
	if stat, err := os.Stat(pwrPath); err == nil {
		resumeFrom = stat.Size()
		fmt.Printf("Resuming download from %d bytes\n", resumeFrom)
	}

	// Create HTTP request with proper headers (like Hytale-F2P)
	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
	req.Header.Set("Accept", "*/*")
	req.Header.Set("Accept-Language", "en-US,en;q=0.9")
	req.Header.Set("Referer", "https://launcher.hytale.com/")
	
	if resumeFrom > 0 {
		req.Header.Set("Range", fmt.Sprintf("bytes=%d-", resumeFrom))
	}

	client := &http.Client{
		Timeout: 60 * time.Minute, // Increased timeout for large files
	}

	resp, err := client.Do(req)
	if err != nil {
		return fmt.Errorf("failed to download patch: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK && resp.StatusCode != http.StatusPartialContent {
		return fmt.Errorf("patch not available: HTTP %d from %s", resp.StatusCode, url)
	}

	total := resp.ContentLength
	if resp.StatusCode == http.StatusPartialContent {
		total += resumeFrom
	}
	
	if resumeFrom == 0 {
		fmt.Printf("PWR file size: %d bytes (%.2f GB)\n", total, float64(total)/(1024*1024*1024))
	}

	// Open file for writing (append if resuming)
	var file *os.File
	if resp.StatusCode == http.StatusPartialContent && resumeFrom > 0 {
		file, err = os.OpenFile(pwrPath, os.O_APPEND|os.O_WRONLY, 0644)
	} else {
		file, err = os.Create(pwrPath)
		resumeFrom = 0
	}
	if err != nil {
		return fmt.Errorf("failed to create patch file: %w", err)
	}
	defer file.Close()

	buf := make([]byte, 32*1024)
	downloaded := resumeFrom
	lastUpdate := time.Now()
	lastDownloaded := downloaded

	for {
		n, err := resp.Body.Read(buf)
		if n > 0 {
			if _, writeErr := file.Write(buf[:n]); writeErr != nil {
				return writeErr
			}
			downloaded += int64(n)

			// Update progress every 100ms
			if time.Since(lastUpdate) >= 100*time.Millisecond {
				speed := float64(downloaded-lastDownloaded) / time.Since(lastUpdate).Seconds()
				speedStr := formatSpeed(speed)
				progress := float64(downloaded) / float64(total) * 100

				if progressCallback != nil {
					progressCallback("download", progress, "Downloading game patch...", filepath.Base(pwrPath), speedStr, downloaded, total)
				}

				lastUpdate = time.Now()
				lastDownloaded = downloaded
			}
		}
		if err != nil {
			if err != io.EOF {
				return fmt.Errorf("read error: %w", err)
			}
			break
		}
	}

	fmt.Printf("Download complete: %d bytes\n", downloaded)

	// Verify download is complete
	if total > 0 && downloaded < total {
		return fmt.Errorf("download incomplete: got %d of %d bytes (%.1f%%), will retry", 
			downloaded, total, float64(downloaded)/float64(total)*100)
	}

	// Final size verification
	info, err := os.Stat(pwrPath)
	if err != nil {
		return fmt.Errorf("failed to verify downloaded file: %w", err)
	}
	if total > 0 && info.Size() != total {
		return fmt.Errorf("downloaded file size mismatch: expected %d, got %d bytes", total, info.Size())
	}

	fmt.Printf("Download verified: %d bytes\n", info.Size())
	
	if progressCallback != nil {
		progressCallback("download", 100, "Download complete", "", "", downloaded, total)
	}
	
	return nil
}

func formatSpeed(bytesPerSec float64) string {
	if bytesPerSec < 1024 {
		return fmt.Sprintf("%.0f B/s", bytesPerSec)
	} else if bytesPerSec < 1024*1024 {
		return fmt.Sprintf("%.1f KB/s", bytesPerSec/1024)
	} else {
		return fmt.Sprintf("%.1f MB/s", bytesPerSec/(1024*1024))
	}
}

// InstalledVersion represents an installed game version
type InstalledVersion struct {
	Version     int    `json:"version"`
	VersionType string `json:"versionType"`
	InstallDate string `json:"installDate"`
}

// GetInstalledVersions returns all installed game versions
func GetInstalledVersions() []InstalledVersion {
	baseDir := env.GetDefaultAppDir()
	versionsDir := filepath.Join(baseDir, "release", "package", "game")
	
	var versions []InstalledVersion
	
	entries, err := os.ReadDir(versionsDir)
	if err != nil {
		return versions
	}
	
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		
		name := entry.Name()
		if name == "latest" {
			continue
		}
		
		// Try to parse version number
		v, err := strconv.Atoi(name)
		if err != nil {
			continue
		}
		
		// Get install date from directory modification time
		info, err := entry.Info()
		installDate := ""
		if err == nil {
			installDate = info.ModTime().Format("2006-01-02")
		}
		
		versions = append(versions, InstalledVersion{
			Version:     v,
			VersionType: "release",
			InstallDate: installDate,
		})
	}
	
	// Also add current version if installed as "latest"
	latestPath := filepath.Join(versionsDir, "latest")
	if info, err := os.Stat(latestPath); err == nil && info.IsDir() {
		currentVer := GetLocalVersion()
		if currentVer != "" && currentVer != "0" {
			v, err := strconv.Atoi(currentVer)
			if err == nil {
				// Check if this version is already in the list
				found := false
				for _, iv := range versions {
					if iv.Version == v {
						found = true
						break
					}
				}
				if !found {
					versions = append(versions, InstalledVersion{
						Version:     v,
						VersionType: "release",
						InstallDate: info.ModTime().Format("2006-01-02"),
					})
				}
			}
		}
	}
	
	return versions
}

// SwitchVersion switches to a different installed version
func SwitchVersion(version int) error {
	baseDir := env.GetDefaultAppDir()
	versionsDir := filepath.Join(baseDir, "release", "package", "game")
	versionDir := filepath.Join(versionsDir, strconv.Itoa(version))
	latestDir := filepath.Join(versionsDir, "latest")
	
	// Check if version exists
	if _, err := os.Stat(versionDir); os.IsNotExist(err) {
		return fmt.Errorf("version %d is not installed", version)
	}
	
	// Remove current latest symlink/directory
	if err := os.RemoveAll(latestDir); err != nil {
		return fmt.Errorf("failed to remove current version: %w", err)
	}
	
	// Create symlink to the new version
	if err := os.Symlink(versionDir, latestDir); err != nil {
		// If symlink fails (e.g., on Windows without admin), copy instead
		return copyDir(versionDir, latestDir)
	}
	
	// Update version file
	return SaveLocalVersion(version)
}

// copyDir copies a directory recursively
func copyDir(src, dst string) error {
	return filepath.Walk(src, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		
		relPath, err := filepath.Rel(src, path)
		if err != nil {
			return err
		}
		
		dstPath := filepath.Join(dst, relPath)
		
		if info.IsDir() {
			return os.MkdirAll(dstPath, info.Mode())
		}
		
		// Copy file
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		
		return os.WriteFile(dstPath, data, info.Mode())
	})
}
