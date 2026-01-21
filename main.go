package main

import (
	"HyPrism/app"
	"embed"

	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	"github.com/wailsapp/wails/v2/pkg/options/linux"
	"github.com/wailsapp/wails/v2/pkg/options/mac"
	"github.com/wailsapp/wails/v2/pkg/options/windows"
)

//go:embed all:frontend/dist
var assets embed.FS

func main() {
	// Create an instance of the app structure
	application := app.NewApp()

	err := wails.Run(&options.App{
		Title:         app.AppTitle,
		Width:         1280,
		Height:        720,
		MinWidth:      1280,
		MinHeight:     720,
		Frameless:     true,
		AssetServer: &assetserver.Options{
			Assets: assets,
		},
		BackgroundColour: &options.RGBA{R: 9, G: 9, B: 9, A: 255},
		OnStartup:        application.Startup,
		OnShutdown:       application.Shutdown,
		CSSDragProperty:  "--wails-draggable",
		Bind: []interface{}{
			application,
		},
		Windows: &windows.Options{
			IsZoomControlEnabled:           false,
			WebviewIsTransparent:           false,
			WindowIsTranslucent:            false,
			DisableWindowIcon:              false,
			DisableFramelessWindowDecorations: false,
			WebviewUserDataPath:            "",
			ZoomFactor:                     1.0,
		},
		Mac: &mac.Options{
			TitleBar:             mac.TitleBarHiddenInset(),
			WebviewIsTransparent: true,
			WindowIsTranslucent:  false,
		},
		Linux: &linux.Options{
			WindowIsTranslucent: false,
			WebviewGpuPolicy:    linux.WebviewGpuPolicyNever,
			// ProgramName helps with window grouping on Linux desktops
			ProgramName: "HyPrism",
		},
	})

	if err != nil {
		println("Error:", err.Error())
	}
}
