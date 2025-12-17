#!/usr/bin/env bash
#set -euo pipefail

# build.sh
# - Pulls repo
# - Builds with Unity
# - Uploads Web builds to R2 or Desktop builds to Steam

##### Constants (defaults – may be overridden)
CONFIG_FILE_NAME="build.config"
R2_BUCKET="radio13au-r2"
R2_GAME_DIRECTORY="game"
R2_ENDPOINT_URL="https://2ff85e4a7b98df295713377e6e90c6be.r2.cloudflarestorage.com"
BUILD_DIR="Build"
BUILD_TARGET=""                # REQUIRED
APP_NAME=""                	   # REQUIRED
TARGET_BRANCH="main"
UNITY_PATH="C:/Program Files/Unity/Hub/Editor/6000.4.0a5/Editor/unity.exe"
DEV_BUILD_FLAG=""
STEAM_ACCOUNT=""               # REQUIRED for non-Web
STEAM_BUILD_SCRIPT=""          # REQUIRED for non-Web. Relative to path 
DO_BUILD="TRUE"
DO_PULL="TRUE"
DO_PUBLISH="TRUE"
CONFIGURE_AWS="FALSE"
CONFIGURE_STEAM="FALSE"
LOG_FILE=""

##### Helpers
log() { echo "[build] $*"; }
fatal() { echo "[build][ERROR] $*" >&2; exit 1; }
# Function to run when an error (ERR signal) occurs
error_handler() {
    local last_command=$BASH_COMMAND
    local line=${BASH_LINENO[0]}
    echo "[build] FATAL ERROR on line $line: '$last_command' failed" >&2
    # Perform cleanup actions here if necessary
    exit 1
}
trap 'error_handler' ERR
set -e # Combine with set -e to trigger the trap on any non-zero exit

# Track child process so we can forward Ctrl+C
UNITY_PID=""

on_interrupt() {
	log "Interrupt received. Stopping build…"
	if [[ -n "$UNITY_PID" ]] && kill -0 "$UNITY_PID" 2>/dev/null; then
	log "Killing unity process $UNITY_PID..."
	kill "$UNITY_PID" || true
	wait "$UNITY_PID" 2>/dev/null || true
	fi
	exit 130
}
trap on_interrupt INT # Run cleanup function on Ctrl+C

##### Config handling
read_variables_from_config() {
	local config_path="$(dirname "$0")/${CONFIG_FILE_NAME}"

	if [[ ! -f "$config_path" ]]; then
		log "Config file not found. Creating default at ${config_path}"
		cat >"$config_path" <<EOF
# build.config
# Lines must be KEY=VALUE

# Required
# NOTE: Remember to escape any special characters! IE: my\$username SHOULD BE my\\\$username
# BUILD_TARGET=Web|Windows|Mac|Linux
# APP_NAME=
# STEAM_ACCOUNT=
# Build script is relative to the .steam/sdk/tools/ContentBuilder/scripts/ folder. IE: app_build_4258160.vdf
# STEAM_BUILD_SCRIPT=

# Optional
TARGET_BRANCH=main
UNITY_PATH="C:/Program Files/Unity/Hub/Editor/6000.4.0b1/Editor/unity.exe"

# DEV_BUILD_FLAG is either TRUE, FALSE or empty
DEV_BUILD_FLAG=
EOF
	fi

	# shellcheck source=/dev/null
	source "$config_path"
}

read_variables_from_args() {
	# Supported flags:
	# --target Web|Windows|Mac|Linux
	# --branch <branch>
	# --unity "path"
	# --dev
	# --steam-user <user>
	# --steam-pass <pass>

	while [[ $# -gt 0 ]]; do
		case "$1" in
			--target) BUILD_TARGET="$2"; shift 2 ;;
			--branch) TARGET_BRANCH="$2"; shift 2 ;;
			--unity) UNITY_PATH="$2"; shift 2 ;;
			--dev) DEV_BUILD_FLAG="Dev"; shift ;;
			--steam-user) STEAM_ACCOUNT="$2"; shift 2 ;;
			--steam-build-script) STEAM_BUILD_SCRIPT="$2"; shift 2 ;;
			--no-build) DO_BUILD="FALSE"; shift ;;
			--no-pull) DO_PULL="FALSE"; shift ;;
			--no-publish) DO_PUBLISH="FALSE"; shift ;;
			--configure-aws) CONFIGURE_AWS="TRUE"; shift ;;
			--configure-steam) CONFIGURE_STEAM="TRUE"; shift ;;
			--app-name) APP_NAME="$2"; shift 2 ;;
			--log-file) LOG_FILE=$2; shift 2 ;;
			*) fatal "Unknown argument: $1" ;;
		esac
	done
}

validate_variables() {
	[[ -n "$BUILD_TARGET" ]] || fatal "BUILD_TARGET is required"
	[[ -n "$APP_NAME" ]] || fatal "APP_NAME is required"

	case "$BUILD_TARGET" in
		Web|Windows|Mac|Linux) ;;
		*) fatal "Invalid BUILD_TARGET: $BUILD_TARGET" ;;
	esac

	[[ -x "$UNITY_PATH" || -f "$UNITY_PATH" ]] || fatal "Unity not found at: $UNITY_PATH"

	if [[ "$BUILD_TARGET" != "Web" && "$DO_PUBLISH" == "TRUE" ]]; then
		[[ -n "$STEAM_ACCOUNT" ]] || fatal "STEAM_ACCOUNT required for non-Web builds"
		[[ -n "$STEAM_BUILD_SCRIPT" ]] || fatal "STEAM_BUILD_SCRIPT required for non-Web builds"
	fi
}

##### Actions
move_to_main_dir() {
	cd "$(dirname "$0")/.."
}

configure_aws() {
	aws configure list
	aws configure
}

delete_uploaded_game() {
	log "Command: aws s3 rm "s3://${R2_BUCKET}/${R2_GAME_DIRECTORY}/${APP_NAME}" --endpoint-url "$R2_ENDPOINT_URL" --recursive"
	aws s3 rm "s3://${R2_BUCKET}/${R2_GAME_DIRECTORY}/${APP_NAME}" \
		--endpoint-url "$R2_ENDPOINT_URL" --recursive
}

upload_game() {
	log "Command: aws s3 cp "${BUILD_DIR}/${BUILD_TARGET}/latest/Player/${APP_NAME}" "s3://${R2_BUCKET}/${R2_GAME_DIRECTORY}/${APP_NAME}" --recursive --endpoint-url "$R2_ENDPOINT_URL""
	aws s3 cp "${BUILD_DIR}/${BUILD_TARGET}/latest/Player/${APP_NAME}" \
		"s3://${R2_BUCKET}/${R2_GAME_DIRECTORY}/${APP_NAME}" \
		--recursive --endpoint-url "$R2_ENDPOINT_URL"
}

pull() {
	log "...pulling from ${TARGET_BRANCH}..."
	git fetch --all
	git reset --hard "origin/${TARGET_BRANCH}"
	git clean -fd
	git pull origin "$TARGET_BRANCH"
}

build() {
	log "...starting build for ${BUILD_TARGET}..."
	
	if [[ "$LOG_FILE" == "" ]]; then
		LOG_FILE="${BUILD_DIR}/${BUILD_TARGET}/build_log.txt"
		log "Writing to default log file location: ${LOG_FILE}"
	else
		log "Writing to log file: ${LOG_FILE}"
	fi
	
	"$UNITY_PATH" \
		-projectPath . \
		-activeBuildProfile "Assets/Settings/Build Profiles/${BUILD_TARGET}.asset" \
		-executeMethod "BuildScript.BuildPlayerAndBundles${DEV_BUILD_FLAG}" \
		-logFile "${LOG_FILE}" \
		-app-name "${APP_NAME}" \
		-quit -batchmode &
		
	UNITY_PID=$!
	wait "$UNITY_PID"
	UNITY_PID=""
}

clone_to_steamsdk() {
	STEAM_BUILD_DIR=".steam/sdk/tools/ContentBuilder/content/${BUILD_TARGET}"
	log "Deleting old content dir: ${STEAM_BUILD_DIR}"
	rm -rf "${STEAM_BUILD_DIR}"
	log "Creating steam content dir: ${STEAM_BUILD_DIR}"
	mkdir -p ".steam/sdk/tools/ContentBuilder/content/${BUILD_TARGET}"
	log "Copying to steam content dir from \"${BUILD_DIR}/${BUILD_TARGET}/latest/Player\" to ${STEAM_BUILD_DIR}"
	cp -R "${BUILD_DIR}/${BUILD_TARGET}/latest/Player" "${STEAM_BUILD_DIR}"
}

configure_steam() {
	log "Configuring steam for account $STEAM_ACCOUNT
NOTE: Just enter your password, deal with SteamGuard, and then type 'quit'. Subsequent logins with same username will work without prompts.
^^^^ INSTRUCTIONS ^^^^
--------------------------------------------------------------------"
	
	"../.steam/sdk/tools/ContentBuilder/builder/steamcmd.exe" \
		+login "$STEAM_ACCOUNT"
}

run_steamsdk_build() {
	log "Running steam build..."
	".steam/sdk/tools/ContentBuilder/builder/steamcmd.exe" \
		+login "$STEAM_ACCOUNT" \
		+run_app_build ../scripts/${STEAM_BUILD_SCRIPT} \
		+quit
}

##### Main
if [[ "$#" == 0 || "$1" == "--help" || "$1" == "-h" ]]; then
	echo "Builds the project and distributes it. Options:
**
 SETUP:
	-h | --help: 		This text!
	--configure-aws: 	Runs the AWS setup to allow you to build for web
	--configure-steam: 	Runs the Steam setup to remove password/SteamGuard prompts
**
 REQUIRED IN CLI OR CONFIG:
	--target <option>: 	Sets build target (Options: Web|Windows|Mac|Linux)
	--app-name <Name>:	Sets the app name
	--unity <Path>:		Sets the path to the unity editor making the build
**
 CUSTOMIZABLE IN CONFIG:
	--branch <Name>:	Sets the branch to pull from (default: \"main\")
	--steam-user <User>:	Sets the steam username when publishing to steam (Best to set this in the config file. TODO: Add a secret file...)
	--steam-build-script <File>:	Sets the steam build script to run (Relative to the '.../ContentBuilder/scripts/' folder. IE: app_build_4258160.vdf)
	--dev: 			Makes the build a dev build
**
 TOOLS:
	--no-pull: 		Doesn't pull
	--no-build: 		Doesn't build
	--no-publish: 		Doesn't publish
	--log-file <Path>:	Path to log file.
"
	exit 0
fi

read_variables_from_config
read_variables_from_args "$@"

if [[ "$CONFIGURE_AWS" == "TRUE" ]]; then
	configure_aws
fi

if [[ "$CONFIGURE_STEAM" == "TRUE" ]]; then
	configure_steam
fi

validate_variables
move_to_main_dir

log "...setup complete..."

if [[ "$DO_PULL" == "TRUE" ]]; then
	pull
else
	log "Skipping pull..."
fi
if [[ "$DO_BUILD" == "TRUE" ]]; then
	build
else
	log "Skipping build..."
fi

if [[ "$DO_PUBLISH" == "TRUE" ]]; then
	if [[ "$BUILD_TARGET" == "Web" ]]; then
		log "...performing web operations."
		log "HINT: If you get errors here, add '--configure-aws' to CLI args and complete the setup. (+ Also add the --no-build and --no-pull args)"
		
		delete_uploaded_game
		upload_game
	else
		clone_to_steamsdk
		run_steamsdk_build
	fi
else
	log "Skipping publish..."
fi