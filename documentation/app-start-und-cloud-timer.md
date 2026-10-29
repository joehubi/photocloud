# App-Start und Cloud-Überwachung

Beschreibt, was beim Start der Desktop-App passiert und wie die Erreichbarkeit der Cloud (NAS) zyklisch angezeigt wird.
Quelle: `src\PhotoCloud.App\MainWindow.axaml.cs`.

## App-Start

`StartAsync` läuft **einmal**, sobald das Fenster geöffnet wird (`Opened`-Event).

```mermaid
flowchart TD
    A[Fenster geöffnet] --> B{photocloud.config.json gefunden?}
    B -- nein --> E1[Statuszeile: Config nicht gefunden<br/>App bleibt offen, kein Server]
    B -- ja --> C{Config lesbar?}
    C -- nein --> E2[Statuszeile: Fehler beim Lesen<br/>kein Server]
    C -- ja --> D[Einstellungen anzeigen<br/>Passwort maskiert]
    D --> F[Letzte 500 Logeinträge aus logs\ laden]
    F --> G[Erster Cloud-Check im Hintergrund]
    G --> H[Web-Server bauen und starten<br/>IPv4, Server.Port]
    H -- Port belegt o. ä. --> E3[Statuszeile: Server konnte nicht starten]
    H -- ok --> I[Status anzeigen<br/>Server-URL, Cloud-Status, Smartphone-Adresse]
    I --> J[30-s-Timer starten]
```

Hinweise:
- Ist die Cloud beim Start offline, startet die App und der Server trotzdem; die Statuszeile zeigt „Cloud nicht erreichbar“.
- Die Config wird nur beim Start gelesen. Änderungen erfordern einen Neustart.
- Beim Schließen des Fensters wird der Timer gestoppt und der Server beendet.
- Geloggt werden (Info): „Anwendung gestartet“ nach dem erfolgreichen Serverstart und „Anwendung wird beendet“ beim Schließen. Scheitert der Serverstart (z. B. Port belegt), gibt es einen Error-Eintrag. Ohne gefundene oder lesbare Config wird nichts geloggt, da dann kein Log-Ordner feststeht.

## Cloud-Timer (alle 30 Sekunden)

Ein `DispatcherTimer` ruft bei jedem Tick `CheckNas` im Hintergrund auf und aktualisiert danach die Statuszeile.

```mermaid
sequenceDiagram
    participant T as Timer (30 s)
    participant W as Hintergrund-Thread
    participant N as NAS (SMB)
    participant U as Statuszeile (UI)

    loop alle 30 Sekunden
        T->>W: CheckNas()
        W->>N: SmbConnection.Connect (Zugangsdaten)
        W->>N: Directory.Exists(\\Host\Share)
        N-->>W: ja / nein / Fehler
        W-->>T: online = true/false
        T->>U: ShowStatus(online)
        Note over U: Server-URL, Cloud-Status und<br/>Smartphone-Adresse(n) neu ermittelt
    end
```

Hinweise:
- Der Check beeinflusst **nur die Anzeige**. Uploads prüfen die Cloud nicht vorher, sie schreiben direkt und loggen einen Fehler, falls das scheitert.
- Eine ausgefallene Cloud kann bis zu 30 s lang noch als „verbunden“ angezeigt werden.
- Das Intervall ist fest im Code (nicht konfigurierbar).
- Eine Exception im Check zählt als „nicht erreichbar“.
