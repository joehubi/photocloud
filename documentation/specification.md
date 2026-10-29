# PhotoCloud – Spezifikation

> Bitte die Felder ausfüllen bzw. Vorgaben anpassen. Zeilen mit `TODO` sind offen.

## 1. Ziel
Fotos vom Smartphone per WLAN über eine Browsermaske (Chrome) direkt in einen gewählten Ordner auf der WD My Cloud EX2 Ultra hochladen, ohne USB-Kabel.

## 2. Architektur
```
Smartphone (Chrome) --WLAN--> Windows-PC (ASP.NET Core / Kestrel) --SMB--> WD My Cloud EX2 Ultra
```
- Die Anwendung läuft auf dem Windows-PC (kein Raspberry Pi, keine Software auf der Fritzbox oder dem NAS).
- Technologie: C#, .NET 8, ASP.NET Core Minimal API, statisches HTML/JS-Frontend.

## 3. Konfiguration
| Einstellung | Wert |
|---|---|
| Basisverzeichnis auf dem NAS (UNC-Pfad) | TODO, z. B. `\\WDMyCloudEX2Ultra\Public\Bilder` |
| Zugangsdaten zum NAS-Share | TODO: Windows-Anmeldeinformationsverwaltung / separater Benutzer |
| Betriebsart | TODO: Konsolenanwendung / Windows-Dienst |
| Port | TODO, Vorschlag: `5000` |
| Zugriff | Nur Heim-WLAN (kein Port-Forwarding in der Fritzbox) |
| HTTPS | TODO: nein (HTTP im Heimnetz) / ja (mkcert, z. B. für PWA) |

## 4. Funktionale Anforderungen
### Muss
- [ ] Login mit Passwort/Token (TODO: Wert und Ablage festlegen)
- [ ] Ordnerliste anzeigen und in Unterordner navigieren (nur unterhalb des Basisverzeichnisses)
- [ ] Neuen Ordner anlegen
- [ ] Mehrere Bilder aus der Galerie auswählen
- [ ] Upload mit Fortschrittsanzeige pro Datei
- [ ] Existierende Dateien nicht überschreiben (Suffix bei Namenskollision)

### Kann (später)
- [ ] Umbenennung nach EXIF-Aufnahmedatum
- [ ] Duplikaterkennung per Hash
- [ ] Chunked Upload / Wiederaufnahme
- [ ] Vorschaubilder
- [ ] Installierbare PWA
- [ ] Videos unterstützen (TODO: ja/nein)

## 5. Nicht-funktionale Anforderungen
- Maximale Dateigröße: TODO, Vorschlag: 100 MB (Videos: TODO)
- Erlaubte Dateiendungen: TODO, Vorschlag: `.jpg .jpeg .png .heic .webp`
- Streaming-Upload, keine vollständige Pufferung im RAM
- Schutz vor Path Traversal (`..`), Pfad-Normalisierung
- Windows-Firewall: eingehende Regel nur für das Privatprofil
- Feste IP für den PC per Fritzbox-Reservierung (empfohlen)

## 6. API (Entwurf)
| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/api/login` | Anmeldung |
| GET | `/api/folders?path=` | Unterordner auflisten |
| POST | `/api/folders` | Ordner anlegen |
| POST | `/api/upload` | Dateien in Zielordner hochladen (`multipart/form-data`) |

## 7. UI-Ablauf (Smartphone)
1. `http://<PC-Name>:<Port>` aufrufen und anmelden.
2. Zielordner wählen oder anlegen.
3. Bilder auswählen und hochladen.
4. Fortschritt und Ergebnis pro Datei ansehen.

## 8. Offene Fragen
- TODO

## 9. Abnahmekriterien
- Ein Foto vom Smartphone landet per WLAN im gewählten NAS-Ordner.
- Zugriff ohne Login ist nicht möglich.
- Pfade außerhalb des Basisverzeichnisses sind nicht erreichbar.
