SOLUTION    := RomForge.slnx
UI_PROJECT  := src/RomForge.UI/RomForge.UI.csproj
APP_BUNDLE  := artifacts/RomForge.app
DEV_BUNDLE  := artifacts/RomForge-dev.app
PUBLISH_DIR := artifacts/publish/osx-arm64
BUILD_OUT   := artifacts/bin/RomForge.UI/debug/net10.0

.PHONY: build test run check clean coverage package sonar sonar-start

build:
	dotnet build $(SOLUTION) --verbosity quiet

test:
	dotnet test $(SOLUTION) --verbosity quiet

run: build
	rm -rf $(DEV_BUNDLE)
	mkdir -p $(DEV_BUNDLE)/Contents/MacOS $(DEV_BUNDLE)/Contents/Resources
	cp -R $(BUILD_OUT)/. $(DEV_BUNDLE)/Contents/MacOS/
	cp packaging/macos/Info.plist $(DEV_BUNDLE)/Contents/
	cp packaging/macos/icon.icns $(DEV_BUNDLE)/Contents/Resources/
	chmod +x $(DEV_BUNDLE)/Contents/MacOS/RomForge
	open $(DEV_BUNDLE)

check:
	dotnet clean $(SOLUTION) --verbosity quiet
	dotnet build $(SOLUTION) --verbosity quiet
	dotnet test $(SOLUTION) --verbosity quiet

coverage:
	dotnet test $(SOLUTION) --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory artifacts/coverage --verbosity quiet

clean:
	dotnet clean $(SOLUTION) --verbosity quiet

package:
	dotnet publish $(UI_PROJECT) -c Release -r osx-arm64 --self-contained true -o $(PUBLISH_DIR)

# One-time setup:
#   brew install sonar-scanner
#   docker run -d --name sonarqube -p 9000:9000 sonarqube:lts-community
#   Log in at http://localhost:9000 (admin / admin on first run)
#   My Account → Security → Generate Token → export SONAR_TOKEN=<token>
sonar-start:
	docker run -d --name sonarqube -p 9000:9000 sonarqube:lts-community

sonar: coverage
	sonar-scanner
