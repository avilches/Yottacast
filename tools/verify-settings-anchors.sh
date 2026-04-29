#!/usr/bin/env bash
# Verifica visualmente que los anchors de System Settings navegan a la sección correcta.
# Uso: sh tools/verify-settings-anchors.sh
# Ejecutar antes de releases o tras actualizar macOS.
# Cada URL se abre con 1 s de delay para que puedas ver el resultado.

set -euo pipefail

ITEMS=(
    # Primer nivel
    "Wi-Fi|com.apple.preference.network"
    "Bluetooth|com.apple.preferences.Bluetooth"
    "Notifications|com.apple.preference.notifications"
    "Sound|com.apple.preference.sound"
    "General|com.apple.preference.general"
    "Privacy & Security|com.apple.preference.security"
    "Displays|com.apple.preference.displays"
    "Desktop & Dock|com.apple.preference.exposeclassic"
    "Battery|com.apple.preference.battery"
    "Keyboard|com.apple.preference.keyboard"
    "Date & Time|com.apple.preference.datetime"
    "Language & Region|com.apple.Localization"
    "Sharing|com.apple.preferences.sharing"
    "Accessibility|com.apple.preference.universalaccess"
    # Privacy & Security
    "Camera|com.apple.preference.security?Privacy_Camera"
    "Microphone|com.apple.preference.security?Privacy_Microphone"
    "Location Services|com.apple.preference.security?Privacy_LocationServices"
    "Contacts Permissions|com.apple.preference.security?Privacy_ContactsFull"
    "Calendars Permissions|com.apple.preference.security?Privacy_Calendars"
    "Reminders Permissions|com.apple.preference.security?Privacy_Reminders"
    "Photos Permissions|com.apple.preference.security?Privacy_Photos"
    "Bluetooth Permissions|com.apple.preference.security?Privacy_Bluetooth"
    "Screen Recording|com.apple.preference.security?Privacy_ScreenCapture"
    "Accessibility Apps|com.apple.preference.security?Privacy_Accessibility"
    "Full Disk Access|com.apple.preference.security?Privacy_AllFiles"
    "Files and Folders|com.apple.preference.security?Privacy_FilesAndFolders"
    "Home Permissions|com.apple.preference.security?Privacy_HomeKit"
    "Media & Apple Music|com.apple.preference.security?Privacy_MediaLibrary"
    "Motion & Fitness|com.apple.preference.security?Privacy_Motion"
    "Speech Recognition|com.apple.preference.security?Privacy_SpeechRecognition"
    "Automation Permissions|com.apple.preference.security?Privacy_Automation"
    "Developer Tools|com.apple.preference.security?Privacy_DevTools"
    "Analytics & Improvements|com.apple.preference.security?Privacy_Analytics"
    "Apple Advertising|com.apple.preference.security?Privacy_Advertising"
    "FileVault|com.apple.preference.security?FDE"
    "Firewall|com.apple.preference.security?Firewall"
    "Advanced Security|com.apple.preference.security?Advanced"
    # Keyboard
    "Keyboard Shortcuts|com.apple.preference.keyboard?Shortcuts"
    "Text Replacements|com.apple.preference.keyboard?Text"
    "Dictation|com.apple.preference.keyboard?Dictation"
    "Input Sources|com.apple.preference.keyboard?InputSources"
    # Displays
    "Night Shift|com.apple.preference.displays?nightShift"
    "Display Resolution|com.apple.preference.displays?scaled"
    "Color Profile|com.apple.preference.displays?ColorProfile"
    # Desktop & Dock
    "Hot Corners|com.apple.preference.exposeclassic?hotcorners"
    "Dock Settings|com.apple.preference.exposeclassic?dock"
    # Sound
    "Sound Output|com.apple.preference.sound?output"
    "Sound Input|com.apple.preference.sound?input"
    # General
    "Login Items|com.apple.preference.general?LoginItems"
    "AirDrop & Handoff|com.apple.preference.general?AirDrop"
    # Language & Region
    "Language|com.apple.Localization?language"
    "Region|com.apple.Localization?region"
    "Calendar Format|com.apple.Localization?calendar"
    # Date & Time
    "Time Zone|com.apple.preference.datetime?TimeZone"
    # Sharing
    "Screen Sharing|com.apple.preferences.sharing?Services_ScreenSharing"
    "File Sharing|com.apple.preferences.sharing?Services_ARDService"
    "Printer Sharing|com.apple.preferences.sharing?Services_PrinterSharing"
    "Remote Login|com.apple.preferences.sharing?Services_RemoteLogin"
    "Remote Management|com.apple.preferences.sharing?Services_ARD"
    "Internet Sharing|com.apple.preferences.sharing?Services_InternetSharing"
    "Content Caching|com.apple.preferences.sharing?Services_NetworkCache"
    # Accessibility
    "VoiceOver|com.apple.preference.universalaccess?VoiceOver"
    "Zoom|com.apple.preference.universalaccess?Seeing_Zoom"
    "Display Accessibility|com.apple.preference.universalaccess?Seeing_Display"
    "Spoken Content|com.apple.preference.universalaccess?Seeing_Content"
    "Audio Descriptions|com.apple.preference.universalaccess?Seeing_Audio"
    "Audio Accessibility|com.apple.preference.universalaccess?Hearing_Audio"
    "RTT|com.apple.preference.universalaccess?Hearing_RTT"
    "Keyboard Accessibility|com.apple.preference.universalaccess?Keyboard"
    "Pointer Control|com.apple.preference.universalaccess?Mouse"
    "Switch Control|com.apple.preference.universalaccess?Switch"
    # Battery
    "Battery Options|com.apple.preference.battery?options"
    "Battery Usage History|com.apple.preference.battery?UsageHistory"
)

total=${#ITEMS[@]}
echo "Verificando $total anchors de System Settings..."
echo "Cierra System Settings antes de empezar para ver los cambios claramente."
echo ""

for i in "${!ITEMS[@]}"; do
    item="${ITEMS[$i]}"
    name="${item%%|*}"
    identifier="${item##*|}"
    echo "[$((i+1))/$total] $name"
    open "x-apple.systempreferences:$identifier"
    sleep 1
done

echo ""
echo "Hecho. Marca en BuiltinPanels.cs los anchors que no navegaron correctamente."
echo "Actualiza el comentario de versión si cambias el catálogo."
