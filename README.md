# PhotoCloud

Windows-Anwendung, mit der du Fotos vom Smartphone direkt per WLAN in feste Ordner deiner Cloud-Festplatte (NAS) hochlädst – ohne USB-Kabel.

## Was die App macht

- Läuft auf dem Windows-PC und startet einen kleinen Web-Server im Heimnetz.
- Am Smartphone öffnest du in Chrome eine Webmaske, wählst einen **Zielordner** und die **Bilder** und lädst sie hoch.
- Der PC schreibt die Dateien per SMB (`\\Host\Share`) direkt in den gewählten Ordner auf dem NAS.
- Es gibt keinen Login. Zugriff haben nur Geräte, deren IP in `Server.AllowedClientIps` steht.
- Die Desktop-Oberfläche (Avalonia) zeigt die Einstellungen (nur lesen), den Upload-Log mit Filter und die Adresse für das Smartphone.
- Uploads werden in `logs\upload-JJJJ-MM-TT.log` protokolliert.

Die vollständige Spezifikation steht in [documentation/specification.md](documentation/specification.md).

## Voraussetzungen

- Windows mit [.NET 10 SDK](https://dotnet.microsoft.com/download) (zum Bauen und Starten aus dem Quellcode)
- PC und Smartphone im selben WLAN/Heimnetz
- Erreichbares NAS mit einem Benutzer, der in den Zielordnern schreiben darf

## Konfiguration: `photocloud.config.json` anlegen

Die App startet **nur mit einer eigenen `photocloud.config.json`**. Sie enthält Zugangsdaten und ist deshalb per `.gitignore` vom Repository ausgeschlossen.

1. `config.example.json` kopieren und in `photocloud.config.json` umbenennen. Ablageort: Repository-Root (oder ein Ordner darüber, in dem die App gestartet wird).
2. Werte anpassen:

| Block | Attribut | Bedeutung |
|---|---|---|
| `Cloud-Settings` | `Host`, `Share` | NAS-Freigabe als `\\Host\Share` |
| | `User`, `Password` | NAS-Zugangsdaten (am besten ein eigener Benutzer mit nur den nötigen Rechten) |
| | `Port` | Derzeit ohne Wirkung (SMB nutzt 445) |
| `Server` | `Port` | Port des Web-Servers (Standard 5000) |
| | `AllowedClientIps` | Erlaubte IPs, z. B. `127.0.0.1` und die Smartphone-IP(s) |
| `Cloud-Storage` | `TargetFolders` | Feste Liste der Zielordner relativ zur Freigabe (müssen existieren) |
| | `MaxFileSizeMb` | Maximale Dateigröße pro Bild |
| | `AllowedExtensions` | Erlaubte Dateiendungen |
| `Unittest` | `TargetFolder` | Ordner für den NAS-Integrationstest |

Die Datei wird nur beim Start gelesen. Nach Änderungen die App neu starten.

Alternativ kann der Pfad über die Umgebungsvariable `PHOTOCLOUD_CONFIG` angegeben werden.

## Einmalig einrichten

- **Smartphone-IP:** Dem Smartphone im Router eine feste IP geben (DHCP-Reservierung) und sie in `AllowedClientIps` eintragen. Ebenso dem PC eine feste IP geben, damit die Adresse stabil bleibt.
- **Firewall** (PowerShell als Administrator):

  ```powershell
  New-NetFirewallRule -DisplayName "PhotoCloud" -Direction Inbound -Protocol TCP -LocalPort 5000 -Profile Private -Action Allow
  ```

  Bei geändertem `Server.Port` den Port anpassen.

## Starten

**In VS Code:** Run-Konfiguration **„PhotoCloud.App bauen und starten“** (F5). Sie baut die App und startet sie.

**Per Kommandozeile:**

```powershell
dotnet run --project src\PhotoCloud.App
```

Die Statuszeile der App zeigt die Adresse für das Smartphone, z. B. `http://192.168.0.17:5000`. Diese Adresse in Chrome am Smartphone öffnen. Am PC selbst funktioniert `http://127.0.0.1:5000`.

Ist das NAS beim Start offline, startet die App trotzdem und zeigt „Cloud nicht erreichbar“.

### Fehlermeldung „SMB-Verbindung mit anderen Zugangsdaten“ (Windows-Fehler 1219)

Windows erlaubt pro Server nur eine Anmeldung. Bestehende Verbindung trennen:

```powershell
net use \\hubercloud\hubercloud /delete
```

(Host und Share an die eigene Config anpassen.) In VS Code gibt es dafür die Run-Konfiguration **„NAS Benutzer Reset“**.

## Tests

```powershell
dotnet test
```

Der NAS-Integrationstest (`CloudUploadTests`) braucht eine gültige `photocloud.config.json` mit `Unittest.TargetFolder` und wird übersprungen, wenn Config oder NAS nicht erreichbar sind. Die Testdatei `unittest.jpg` bleibt bewusst im Testordner auf dem NAS liegen.

## Projektstruktur

| Ordner | Inhalt |
|---|---|
| `src\PhotoCloud.Core` | Config, SMB-Verbindung, Speichern der Dateien, Log |
| `src\PhotoCloud.Web` | Web-Server (IP-Whitelist, API) und Webmaske |
| `src\PhotoCloud.App` | Avalonia-Desktop-Oberfläche, bettet den Web-Server ein |
| `tests\PhotoCloud.Tests` | Unit- und Integrationstests |
| `documentation` | Spezifikation und technische Beschreibungen |
