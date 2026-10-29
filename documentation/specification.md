# PhotoCloud – Spezifikation

> Entwurf. Zeilen mit `TODO` sind noch offen.

## 1. Ziel
Fotos vom Smartphone per WLAN über eine Browsermaske (Chrome) direkt in einen von mehreren in der Konfiguration festgelegten Ordnern auf der WD My Cloud EX2 Ultra hochladen, ohne USB-Kabel.

## 2. Architektur
```
Smartphone (Chrome) --WLAN--> Windows-PC (ASP.NET Core / Kestrel) --SMB--> WD My Cloud EX2 Ultra
```
- Die Anwendung läuft auf dem Windows-PC (kein Raspberry Pi, keine Software auf dem Router (Vodafone Box) oder dem NAS).
- Technologie: C#, .NET 10, ASP.NET Core Minimal API, statisches HTML/JS-Frontend.
- Auf dem PC gibt es zusätzlich eine kleine Desktop-GUI auf Basis von Avalonia UI (siehe Abschnitt 7a) zur Anzeige der Konfiguration und der Upload-Logs.

## 3. Konfiguration
Cloud-Zugang, Zielordner, Dateilimits und erlaubte Geräte werden in einer JSON-Datei konfiguriert (`appsettings.json` bzw. eigene `photocloud.config.json`):

```json
{
  "Cloud-Settings": {
    "Host": "hubercloud",
    "Port": 445,
    "Share": "hubercloud",
    "User": "mein-benutzer",
    "Password": "mein-passwort"
  },
  "Server": {
    "Port": 5000,
    "AllowedClientIps": ["127.0.0.1", "192.168.178.31", "192.168.178.32"]
  },
  "Cloud-Storage": {
    "TargetFolders": ["Bilder\\Urlaub", "Bilder\\Familie", "Bilder\\Sonstiges"],
    "MaxFileSizeMb": 100,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".heic", ".webp"]
  },
  "Unittest": {
    "TargetFolder": "03_Bilder\\###photocloud"
  }
}
```

- `Server.Port`: Port des Webservers (TCP), den das Smartphone aufruft (`http://<PC>:5000`). Nicht zu verwechseln mit `Cloud-Settings.Port` (SMB zum NAS). Dieser Port muss in der Windows-Firewall-Regel (Abschnitt 5) freigegeben sein.
- `Cloud-Storage.MaxFileSizeMb`: maximale Größe pro Datei in MB. Größere Dateien werden abgelehnt und als `Error` geloggt. Das Kestrel-Request-Limit wird daraus abgeleitet.
- `Cloud-Storage.AllowedExtensions`: erlaubte Dateiendungen (ohne Beachtung der Groß-/Kleinschreibung). Andere Dateien werden abgelehnt und als `Error` geloggt. Der `accept`-Filter im Datei-Picker der Webmaske wird daraus erzeugt.
- `Server.AllowedClientIps`: Nur diese Geräte dürfen den Webserver nutzen: der PC selbst (`127.0.0.1`, damit die Maske auch lokal unter `http://127.0.0.1:<Port>` nutzbar ist; nicht `localhost`, da dieser je nach System auf IPv6 `::1` auflösen kann) und die zwei Smartphones. Die Smartphone-Adressen sind Platzhalter. Alle anderen Anfragen werden mit `403` abgelehnt und im Log vermerkt. Es gibt keinen Login.
- Damit die IPs stabil bleiben, im Router (Vodafone Box) bei beiden Smartphones eine feste IPv4-Adresse bzw. DHCP-Reservierung einrichten.
- Der Server lauscht ausschließlich auf IPv4 (`0.0.0.0:<Server.Port>`). IPv6-Adressen sind nicht vorgesehen und stehen nicht in der Whitelist.

- `Cloud-Settings.Host`, `Port`, `Share`, `User`, `Password`: Anmeldedaten für den SMB-Zugriff auf die WD My Cloud (Standardport SMB: 445). Host kann ein Rechnername oder eine IP-Adresse sein. Die Anwendung baut daraus den UNC-Pfad (`\\Host\Share`, hier `\\hubercloud\hubercloud`, wie im Windows-Explorer) und authentifiziert sich damit.
- `User` und `Password` sollten für einen zuverlässigen Start explizit mit den NAS-Zugangsdaten befüllt sein. Leere Werte funktionieren nur, wenn Windows dem aktuellen Benutzer bereits Zugriff auf das Share geben kann (z. B. über eine bestehende Explorer-Verbindung oder gespeicherte Windows-Anmeldedaten). Die Beispielwerte in `config.example.json` sind Platzhalter.
- `Cloud-Storage.TargetFolders`: exakte Liste der Ordner (relativ zum Share), in die hochgeladen werden darf. Die Namen sind nur Beispiele. Regeln:
  - Es wird ausschließlich in genau diese Ordner geschrieben. Es gibt keine Navigation in Unterordner und kein Durchsuchen der Cloud.
  - Die Anwendung legt keine Ordner an. Ein konfigurierter Ordner, der nicht existiert, wird als `Error` geloggt und in der Webmaske als nicht verfügbar markiert.
  - Der Client wählt den Ordner nur aus dieser Liste (z. B. per Index/Name). Beliebige Pfadangaben vom Client werden abgelehnt.
- `Unittest.TargetFolder`: Testordner für die Unit-Tests (relativ zum Share, hier `\\hubercloud\hubercloud\03_Bilder\###photocloud`). Er wird ausschließlich von den Tests verwendet, nicht von der Webmaske, und muss manuell existieren.
- Das Passwort liegt im Klartext in der Datei. Deshalb wird die echte Konfigurationsdatei per `.gitignore` von Git ausgeschlossen, und im Repository liegt stattdessen eine `config.example.json` ohne Geheimnisse.

| Einstellung | Wert |
|---|---|
| Betriebsart | Desktop-Anwendung mit GUI (Kestrel läuft darin eingebettet). Start manuell, kein Autostart. Kein Windows-Dienst, da Dienste keine GUI anzeigen können. |
| Port | Konfigurierbar über `Server.Port` in der JSON (Standard: `5000`) |
| Zugriff | Nur Heim-WLAN (kein Port-Forwarding im Router) |
| HTTPS | Nein, HTTP im Heimnetz. Damit entfallen PWA-Installation und Web Share Target (siehe „Kann“-Liste, falls später gewünscht). |

## 4. Funktionale Anforderungen
### Muss
- [ ] Zugriffsschutz über Freigabe bekannter Geräte (IP-Whitelist aus der JSON), kein Login
- [ ] Zielordner aus der in der JSON konfigurierten Liste (`Cloud-Storage.TargetFolders`) auswählen; keine Unterordner-Navigation, kein Anlegen neuer Ordner
- [ ] Mehrere Bilder über den System-Picker des Smartphones auswählen (`<input type="file" multiple accept="image/*">`); die Miniaturansicht der Galerie liefert der System-Picker des Smartphones, die Maske selbst zeigt keine Bildvorschau
- [ ] Bilder mehrfach hintereinander hinzufügen (Sammeln aus mehreren Alben/Tagen), bevor hochgeladen wird
- [ ] Reset: Button „Neu beginnen“ verwirft die gesamte Auswahl, um von vorn zu starten (keine Abwahl einzelner Bilder)
- [ ] Upload mit Fortschrittsanzeige pro Datei; Status (wartend/hochgeladen/Fehler) als Eintrag in einer Dateiliste
- [ ] Existierende Dateien nicht überschreiben: Bei Namenskollision wird ein Zähler angehängt, z. B. `foto (1).jpg`, `foto (2).jpg`

## 5. Nicht-funktionale Anforderungen
- Maximale Dateigröße und erlaubte Dateiendungen sind in der JSON konfigurierbar (`Cloud-Storage.MaxFileSizeMb`, `Cloud-Storage.AllowedExtensions`, Standard: 100 MB und `.jpg .jpeg .png .heic .webp`)
- Streaming-Upload, keine vollständige Pufferung im RAM
- Abbruch mitten im Upload (z. B. WLAN weg): Die Datei wird direkt am Zielort geschrieben; bei Abbruch oder Fehler wird die unvollständige Datei gelöscht und der Vorgang als `Error` geloggt.
- Mehrere Uploads gleichzeitig (z. B. von beiden Smartphones) sind erlaubt. Die Vergabe des Dateinamens bei Kollisionen muss dabei threadsicher sein, damit sich zwei Uploads nicht überschreiben.
- Ist das NAS beim Start nicht erreichbar, startet die Anwendung trotzdem samt Webserver. Die GUI zeigt den Status „nicht erreichbar“, und Uploads schlagen als `Error` fehl, bis das NAS wieder erreichbar ist.
- Schutz vor Path Traversal (`..`): Der Client darf nur Ordner aus `Cloud-Storage.TargetFolders` wählen, Dateinamen werden bereinigt (keine Pfadanteile)
- Windows-Firewall: Damit das Smartphone den Webserver erreicht, ist eine eingehende Regel für den Serverport nötig (Standard: 5000, TCP, entspricht `Server.Port`). Die Regel gilt nur für das Netzwerkprofil **Privat**.
  - Windows ordnet jedes Netzwerk einem Profil zu: *Privat* (vertrauenswürdig, z. B. Heim-WLAN), *Öffentlich* (fremde Netze wie Café, Hotel, Bahn-WLAN) oder *Domäne* (Firmennetz). Die Regel greift nur im Privatprofil. In öffentlichen Netzen bleibt der Port gesperrt, sodass dort niemand auf den Server zugreifen kann.
  - Voraussetzung: Das Heimnetz muss in Windows als **Privat** eingestellt sein (Einstellungen → Netzwerk und Internet → WLAN/Ethernet → Netzwerkprofil). Ist es auf „Öffentlich“ gesetzt, erreicht das Smartphone den Server trotz Regel nicht.
  - Einrichtung: Beim ersten Start fragt Windows nach der Freigabe; dort nur „Private Netzwerke“ aktivieren. Alternativ per PowerShell als Administrator:
    ```powershell
    New-NetFirewallRule -DisplayName "PhotoCloud" -Direction Inbound -Protocol TCP -LocalPort 5000 -Profile Private -Action Allow
    ```
  - Die Firewall-Regel ergänzt die IP-Whitelist (`Server.AllowedClientIps`) als zweite Schutzschicht.
- Feste IP für den PC per DHCP-Reservierung im Router (empfohlen)

## 6. API (Entwurf)
| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/api/folders` | Konfigurierte Zielordner (`Cloud-Storage.TargetFolders`) auflisten |
| POST | `/api/upload` | Dateien in einen der konfigurierten Zielordner hochladen (`multipart/form-data`) |

## 7. UI-Ablauf (Smartphone)
1. `http://<PC-Name>:<Port>` aufrufen (kein Login, Zugriff nur für freigegebene Geräte).
2. Zielordner aus der Liste wählen.
3. Über „Bilder auswählen“ den System-Picker öffnen, durch die Galerie scrollen und mehrere Bilder wählen (weitere Auswahlen können hinzugefügt werden).
4. Die Maske zeigt die Anzahl der gewählten Bilder. Mit „Neu beginnen“ wird die Auswahl komplett verworfen.
5. „Hochladen“ starten.
6. Fortschritt und Ergebnis pro Datei in der Dateiliste ansehen.

Hinweis: Eine Webseite kann nicht selbst die gesamte Galerie als Miniaturansicht aufbauen (Browser-Datenschutz). Die Galerieansicht mit Miniaturen liefert der System-Picker des Smartphones.

## 7a. Windows-GUI (Desktop)
Kleine GUI auf dem Windows-PC, die den eingebetteten Webserver hostet. Technologie: Avalonia UI (.NET 10, C#, MVVM).

### Bereich 1: Einstellungen (nur Anzeige, nicht editierbar)
- Zeigt die Werte aus der JSON-Konfiguration: `Cloud-Settings.Host`, `Port`, `Share`, `User`, `Server.Port`, `Cloud-Storage.TargetFolders`, `MaxFileSizeMb`, `AllowedExtensions`, `Server.AllowedClientIps`. Der Bereich `Unittest` wird nicht angezeigt, da er nur für Tests relevant ist.
- Das Passwort wird maskiert dargestellt (`********`).
- Änderungen erfolgen ausschließlich in der JSON-Datei; die Anwendung liest sie nur beim Start ein (kein Neu-Laden zur Laufzeit; nach Änderungen die Anwendung neu starten).
- Zusätzlich sinnvoll: Pfad der Konfigurationsdatei, URL des Webservers (z. B. `http://192.168.178.20:5000`), Status der NAS-Verbindung (verbunden/nicht erreichbar).

### Bereich 2: Logeinträge (Uploads)
- Tabelle/Liste mit einem Eintrag pro Ereignis (übertragenes Bild, abgelehnter Zugriff, Fehler): Stufe, Zeitstempel, Dateiname, Zielordner (aus `Cloud-Storage.TargetFolders`), Dateigröße, Ergebnis/Meldung.
- Neue Einträge erscheinen live, neueste zuerst.
- Jeder Eintrag hat eine Stufe (`Info` oder `Error`), die visuell sofort erkennbar ist: eigene Spalte mit Symbol (ℹ / ⚠) und Farbe (Info neutral/grün, Error rot, Zeile ggf. rot hinterlegt). Zusätzlich lässt sich das Log nach Stufe filtern (Alle / nur Errors).
- Die Einträge werden zusätzlich in eine Logdatei geschrieben: `logs\upload-JJJJ-MM-TT.log` neben der Anwendung, mit täglicher Rotation (jeden Tag eine neue Datei). Beim Start werden die letzten 500 Einträge aus den Logdateien in die GUI geladen, sodass frühere Uploads nach einem Neustart sichtbar sind. Logdateien werden nie automatisch gelöscht.
- Jeder Upload-Vorgang wird pro Datei protokolliert (Erfolg als `Info`, Fehler als `Error`), unabhängig davon, ob er erfolgreich war.
- Fehler (NAS nicht erreichbar, Anmeldung am Share fehlgeschlagen, abgelehnte Dateien oder Geräte) werden als `Error` protokolliert, erfolgreiche Übertragungen als `Info`. Die Stufe steht auch in der Logdatei.

### Weitere Hinweise
- Kein Tray-Icon: Die GUI wird normal in der Taskleiste minimiert.
- Kein Start/Stopp-Button: Der Webserver läuft, solange die Anwendung läuft; zum Beenden die Anwendung schließen.

## 7b. Projektstruktur und Unit-Tests
Solution `PhotoCloud` mit vier Projekten:
- `src\PhotoCloud.Core`: Klassenbibliothek (Konfiguration, Cloud-Storage-Zugriff, Logging); ohne GUI- und Web-Abhängigkeiten.
- `src\PhotoCloud.Web`: ASP.NET Core Minimal API und Webmaske (eingebettetes `index.html`), als Bibliothek; `PhotoCloudWebHost.Build` wird von der App gestartet.
- `src\PhotoCloud.App`: Avalonia-GUI, hostet den Webserver.
- `tests\PhotoCloud.Tests`: xUnit-Tests.

### Tests
- Framework: xUnit (.NET 10), eigenes Projekt `PhotoCloud.Tests`. Der Integrationstest ist in VS Code im Test Explorer (C# Dev Kit) ausführbar, alternativ per `dotnet test`.
- Die Logik für den Speicherzugriff liegt in `PhotoCloud.Core`, damit sie ohne Avalonia und Kestrel testbar ist.
- Testdaten: das Standardfoto `tests\PhotoCloud.Tests\TestData\unittest.jpg` (wird beim Build ins Testausgabeverzeichnis kopiert).
- Konfiguration für die Tests: `photocloud.config.json` im Repo-Root (von Git ausgeschlossen, Vorlage: `config.example.json`) oder Pfad per Umgebungsvariable `PHOTOCLOUD_CONFIG`. Fehlt sie oder ist das NAS nicht erreichbar, wird der Integrationstest übersprungen.
- Ausführen: `dotnet test tests\PhotoCloud.Tests` oder im VS-Code-Test-Explorer.

### Test (erste Ausbaustufe)
- **Standardfoto auf die Cloud schieben** (Integrationstest gegen die echte WD My Cloud)
  - Kopiert `unittest.jpg` über den Storage-Dienst in den Ordner `Unittest.TargetFolder` (JSON-Bereich `Unittest`, relativ zum Share, z. B. `03_Bilder\###photocloud` = `\\hubercloud\hubercloud\03_Bilder\###photocloud`). Der Ordner gehört nicht zu `Cloud-Storage.TargetFolders` und muss vorab manuell existieren, da die Anwendung keine Ordner anlegt.
  - Prüft, dass die Datei auf dem Share existiert und byte-identisch ist (Größe/Hash). Die Testdatei bleibt im NAS-Testordner erhalten; bei erneutem Testlauf wird wegen der Namenskollision eine nummerierte Kopie angelegt.
  - Benötigt die echte Konfiguration (JSON); ist sie nicht vorhanden oder das NAS nicht erreichbar, wird der Test übersprungen. Kennzeichnung: `Trait("Category", "Integration")`.
2. **Beispiel-Logfile schreiben**
   - Schreibt je einen `Info`- und einen `Error`-Beispieleintrag in eine Logdatei in einem temporären Verzeichnis.
   - Prüft, dass die Datei angelegt wird und Stufe, Zeitstempel, Dateiname und Zielordner im Eintrag stehen.
   - Läuft ohne NAS und ohne Konfiguration.

## 8. Offene Fragen
- Aktuell keine.

## 9. Abnahmekriterien
- Ein Foto vom Smartphone landet per WLAN im gewählten NAS-Ordner.
- Zugriff ist nur von Geräten in `Server.AllowedClientIps` möglich.
- Pfade außerhalb der konfigurierten Zielordner sind nicht erreichbar; es werden keine Ordner angelegt.
- Die GUI zeigt die Konfiguration schreibgeschützt (Passwort maskiert) und listet jeden Upload mit Zeitstempel, Dateiname und Zielordner.
