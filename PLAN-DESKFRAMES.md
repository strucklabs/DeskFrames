# Plan: DeskFrames – Bilderrahmen auf dem Desktop

Stand: 16.11.2026 · Status: **abgenommen** (Fragen beantwortet, siehe „Entscheidungen“) · **fertig**: S0–S5 abgenommen (S4 ohne Handtests, bewusst verzichtet), Paket `DeskFrames.zip` auf dem Desktop

Kleines Windows-Programm (C# / .NET 10 / WPF), das lokale Bilder als schlichte Rahmen auf den Desktop legt –
hinter allen Fenstern, durchklickbar, sichtbar bei „Desktop anzeigen“. Oberfläche englisch, Doku deutsch.

## Technischer Ansatz (wird in S0 geprüft)

**Jeder Rahmen ist ein eigenes, randloses WPF-Fenster** (transparent, ohne Taskleisten-Eintrag, nimmt nie den Fokus).

| Anforderung | Lösung |
|---|---|
| Hinter allen Fenstern | Z-Reihenfolge bestimmt allein DeskFrames (siehe nächste Zeile); jeder andere Versuch, sie zu ändern, wird in `WM_WINDOWPOSCHANGING` abgefangen |
| Sichtbar bei Win+D | **Variante C (gewählt nach S0):** Rahmen liegen immer direkt über dem Fenster, das gerade die Desktop-Symbole (`SHELLDLL_DefView`) trägt – `Progman` im Normalfall, `WorkerW` bei Win+D. Nachgeführt per `SetWinEventHook` (Vordergrund-Wechsel), 300 ms danach und einmal pro Sekunde. Verworfen: A (Besitzer `Progman`, scheitert an Win+D), B (Rahmen bei Win+D nach ganz oben heben). |
| Durchklickbar | `WS_EX_TRANSPARENT` wie in PerfOverlay; Desktop-Symbole unter einem Rahmen bleiben anklickbar |
| Strg+Umschalt | Timer (80 ms) prüft die Tasten wie in PerfOverlay; gedrückt → Durchklicken aus, farbige Markierung, Ziehen/Skalieren möglich; losgelassen → wieder durchklickbar, Position wird gespeichert |
| Explorer-Neustart | Auf die Meldung `TaskbarCreated` hin Besitzer/Ebene neu setzen und Tray-Symbol neu anlegen |
| Mehrere Monitore / DPI | Manifest *PerMonitorV2*; Position in **physischen Pixeln** über Win32 setzen (wie PerfOverlay `Reposition`), weil WPF-Koordinaten bei gemischter Skalierung nicht eindeutig sind |

Bewusst **nicht** gewählt: das Einhängen in die Hintergrundbild-Ebene (`WorkerW`, Technik von Wallpaper Engine).
Dort lägen die Rahmen *unter* den Desktop-Symbolen und wären nicht mehr greifbar zum Verschieben; außerdem hat
Microsoft diese Ebene mit Windows 11 24H2 umgebaut. Folge unseres Ansatzes: Rahmen liegen **über** den
Desktop-Symbolen (Symbole darunter bleiben per Klick erreichbar, sind aber verdeckt).

## Daten und Speicherung

Datei `%APPDATA%\DeskFrames\frames.json` (von Hand lesbar), z. B.:

```json
{
  "Version": 1,
  "Frames": [
    { "Id": "…", "ImagePath": "C:\\Bilder\\urlaub.jpg", "AspectRatio": 1.5,
      "Monitor": "\\\\.\\DISPLAY2", "X": 120, "Y": 80, "Width": 360,
      "BorderColor": "#FFFFFF", "BorderWidth": 6 }
  ]
}
```

- **Position:** physische Pixel relativ zur linken oberen Ecke des Monitors + Gerätename des Monitors.
  Fehlt der Monitor beim Start, landet der Rahmen auf dem Hauptmonitor, vollständig sichtbar (Datei bleibt unverändert,
  bis der Nutzer ihn bewegt – steckt der Monitor wieder, kommt der Rahmen beim nächsten Start dorthin zurück).
- **Größe:** Breite in geräteunabhängigen Einheiten (wie normale Fenster: auf einem 150-%-Monitor wirkt ein Rahmen
  gleich groß wie auf einem 100-%-Monitor). Höhe = Breite / Seitenverhältnis, wird nicht gespeichert.
- **Seitenverhältnis** wird beim Laden des Bildes bestimmt (inkl. EXIF-Drehung von Handyfotos) und gespeichert –
  so behält der Rahmen seine Form auch, wenn das Bild fehlt.
- **Bilder** werden nur verknüpft. Laden mit `CacheOption.OnLoad` (Datei wird nicht gesperrt) und auf
  Bildschirmgröße herunterskaliert dekodiert (wenig Speicher auch bei 24-MP-Fotos).
- Speichern atomar (temporäre Datei + Umbenennen); beschädigte/fehlende Datei → leerer Start, nie ein Absturz;
  beschädigte Datei wird als `frames.json.bad` beiseitegelegt.
- **Autostart:** Eintrag unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (keine Admin-Rechte nötig).
  Steht nur dort, nicht zusätzlich in `frames.json` – so können beide Stellen nie widersprüchlich sein.
- Von Hand bearbeitete Dateien: Kommentare, Komma am Ende und klein geschriebene Namen sind erlaubt; unsinnige Werte
  (negative Größe, ungültige Farbe, Rahmenbreite > 40) werden repariert statt den Rahmen zu verwerfen.

## Bedienung

**Tray-Symbol** (Rechtsklick): `Add frame…` · `Manage frames…` · `Start with Windows` (Haken) · `Exit`.
Linksklick/Doppelklick öffnet das Verwaltungsfenster. Pro-Rahmen-Befehle (Bild wechseln, Farbe, Breite, Entfernen)
liegen im Verwaltungsfenster; zusätzlich Rechtsklick auf einen Rahmen bei gedrückter Strg+Umschalt → Kontextmenü
mit denselben Befehlen (spart die Suche nach dem richtigen Listeneintrag).

**Verwaltungsfenster** „DeskFrames“ (klein, normales Fenster):
- Liste aller Rahmen mit Miniatur und Dateiname (fehlende Bilder markiert)
- Vorschau des gewählten Rahmens
- `Change image…`, Rahmenfarbe (Farbfelder + eigene Farbe), Rahmenbreite (Schieberegler 0–40 px), `Remove`
- Hinweiszeile: „Hold Ctrl+Shift to move or resize frames on the desktop.“
- Gewählter Rahmen blinkt auf dem Desktop kurz auf, damit man ihn findet

**Neuer Rahmen:** Dateiauswahl → Rahmen erscheint mittig auf dem Monitor mit dem Mauszeiger, Breite 320 (Hochformat:
Höhe 320). **Bild wechseln:** neues Seitenverhältnis, die Fläche bleibt etwa gleich, Mittelpunkt bleibt.

**Fehlendes Bild:** Rahmen bleibt mit gespeichertem Seitenverhältnis stehen, zeigt eine dezente graue Fläche mit
„Image not found“ und dem Dateinamen. Wird die Datei wieder an den Ort gelegt, erscheint sie beim nächsten Start.

**Skalieren:** Ziehen an Ecke oder Rand (Griffzone 10 px); das Seitenverhältnis bleibt fest. Am Rand links/rechts
bestimmt die Breite, oben/unten die Höhe; an Ecken die größere Bewegung. Die gegenüberliegende Ecke/Kante bleibt
stehen. Mindestgröße 60 px an der kürzeren Seite, höchstens Monitorgröße.

## Schritte

Jeder Schritt endet mit Abnahme (Commit nach Rückfrage).

### S0 – Machbarkeitsprobe Desktop-Ebene
Wegwerf-Probe (eigener Ordner `probe/`): zwei Rahmen mit festen Testbildern, Strg+Umschalt-Verschieben, Varianten A
und B per Schalter.
- [ ] P1 Beide Rahmen liegen hinter allen normalen Fenstern, auch nach Klick auf Desktop und Alt+Tab
- [ ] P2 Win+D und Klick auf „Desktop anzeigen“ unten rechts: Rahmen bleiben sichtbar; erneut Win+D → Fenster kommen zurück, Rahmen weiterhin dahinter
- [ ] P3 Ohne Strg+Umschalt gehen Klicks/Doppelklicks durch auf Desktop-Symbole und den Desktop (Rechtsklick-Menü des Desktops)
- [ ] P4 Mit Strg+Umschalt lässt sich ein Rahmen verschieben; danach wieder durchklickbar
- [ ] P5 Rahmen erscheinen nicht in Taskleiste und Alt+Tab, stehlen nie den Fokus
- [ ] P6 Explorer-Neustart (Task-Manager): Rahmen bleiben bzw. sind danach wieder korrekt eingeordnet
- [ ] P7 Rahmen auf beiden Monitoren (unterschiedliche Skalierung) korrekt platziert und scharf

**Zwischenergebnis (16.11., eigene Vorprüfung auf dem Testrechner, Windows 10 22H2):**
- Variante A **scheitert**: Bei Win+D holt Windows nicht `Progman`, sondern ein `WorkerW`-Fenster nach vorn, in das
  die Desktop-Symbole umziehen. Rahmen mit Besitzer `Progman` bleiben dahinter.
- Neue **Variante C funktioniert**: Die Rahmen liegen immer *direkt über dem Fenster, das gerade die Desktop-Symbole
  trägt* (`Progman` im Normalfall, `WorkerW` bei Win+D). Nachgeführt bei jedem Vordergrund-Wechsel, 300 ms danach
  und jede Sekunde als Sicherheitsnetz (ohne Änderung kein Eingriff). Alle anderen Versuche, die Z-Reihenfolge der
  Rahmen zu ändern, werden ignoriert. Variante B wird damit überflüssig.
- Geprüft: P1 (hinter Fenstern), P2 (Win+D hin und zurück, Bildschirmfoto), P3 teilweise (Punkt auf einem Rahmen
  trifft bei Win+D die Symbol-Liste des Desktops). Offen für den Handtest: P3 von Hand, P4–P7.
- Beobachtung: Beim Zurückkehren aus Win+D liegen die Rahmen für ca. 50 ms noch über anderen Fenstern, bis Explorer
  die Symbole zurück in `Progman` gelegt hat – im Probelauf auf Flackern achten.

**Ergebnis: S0 bestanden (16.11.2026) – P1–P7 alle erfüllt, gewählt ist Variante C.** Die Probe bleibt
als Referenz in `probe/` liegen; das eigentliche Programm übernimmt daraus die Einordnungs-Logik.

### S1 – Grundgerüst, Datenmodell, Speichern/Laden
Projekt `DeskFrames` + Testprojekt `tests/DeskFrames.Tests` (xUnit wie PerfOverlay), Fehlerprotokoll, Einzelinstanz
(zweiter Start öffnet das Verwaltungsfenster der laufenden Instanz).
- [x] Tests grün: Speichern → Laden ergibt dieselben Rahmen; fehlende/leere/beschädigte/gesperrte Datei → leere Liste ohne Ausnahme (beschädigte als `.bad` gesichert); unbekannte Felder werden ignoriert; atomares Schreiben
- [x] Tests grün: Monitor-Rückfall (fehlender Monitor → Hauptmonitor, vollständig sichtbar), Rückrechnung absolute Lage ↔ gespeicherte Lage, Monitor mit negativen Koordinaten
- [x] Einzelinstanz: zweiter Start beendet sich und meldet sich bei der laufenden Instanz (Hinweis im Infobereich; ab S3 öffnet er das Verwaltungsfenster)

Stand 16.11.: 34 Tests grün. Tray hat vorerst nur `Exit`; Rahmen erscheinen erst mit S2.

### S2 – Rahmenfenster
Bild + Rahmenstil, Platzhalter, Verschieben und Skalieren mit fester Form, Speichern beim Loslassen.
- [x] Tests grün: Seitenverhältnis-Rechnung für alle 8 Griffe (Form bleibt, gegenüberliegende Ecke fest, Mindest-/Höchstgröße, Rundung ohne Drift bei vielen kleinen Schritten)
- [x] Tests grün: EXIF-Drehung bestimmt das Seitenverhältnis (inkl. Drehrichtung per Pixelprüfung); Bild wechseln hält Fläche und Mittelpunkt
- [x] Fehlendes Bild zeigt den Platzhalter; Bilddatei wird nicht gesperrt (Test: Datei lässt sich nach dem Laden umbenennen und löschen)

**S2 abgenommen (16.11.2026), Handtest bestanden.** Stand 16.11.: 100 Tests grün. Eigene Vorprüfung auf dem Testrechner mit simulierter Maus: alle 8 Griffe
(gegenüberliegende Kante fest, Seitenverhältnis 1,60 ± Rundung, Mindestgröße 96 × 60), Verschieben auf den zweiten
Monitor wird mit dessen Namen gespeichert, nach Neustart pixelgenau an derselben Stelle.

Festlegungen in S2:
- `Width` in `frames.json` ist die **Bildbreite ohne Rahmen** – so hat das Bild immer exakt sein Seitenverhältnis.
- Skalieren fragt Maus und Taste per Timer ab (10 ms) statt per Mausfang: Die Rahmen werden nie aktiv, dadurch
  ginge der Fang sofort verloren.
- Ändert sich das Bild hinter einem Pfad (Datei ersetzt), übernimmt der Rahmen beim Start das neue
  Seitenverhältnis (Fläche und Mitte bleiben).
- Bilder werden nur so groß dekodiert wie nötig (mit Reserve) und nach dem Vergrößern schärfer nachgeladen.
- Testschalter `DeskFrames.exe --config <Datei>` für Probeläufe mit eigener Rahmen-Datei.
- Tray vorläufig mit `Add frame…` und `Exit`; der Rest folgt in S3.

### S3 – Tray und Verwaltungsfenster
- [x] Alle Befehle aus „Bedienung“ funktionieren; Änderungen sind sofort sichtbar und gespeichert
- [x] Kontextmenü am Rahmen bei Strg+Umschalt

**S3 abgenommen (16.11.2026), Handtest bestanden.** Stand 16.11.: 112 Tests grün (neu: Autostart mit eigenem Testschlüssel, Rahmenbreite ändern). Eigene Vorprüfung
mit Testdatei: Verwaltungsfenster per zweitem Start geöffnet; Farbe, Schieberegler und `Apply to all frames`
wirken sofort und werden gespeichert (Bild bleibt an seinem Platz, der Rahmen wächst nach außen); Kontextmenü am
Rahmen erscheint, „Border width → 6 px“ wirkt; Aufblinken hebt den Rahmen kurz vor alle Fenster und ordnet ihn
danach wieder ein. Nicht automatisch geprüft: Rückfrage beim Entfernen, Dateiauswahl (Bild wechseln/hinzufügen),
Farbdialog, Tray-Menü – gehören in den Handtest.

Festlegungen in S3:
- Autostart schon in S3 (Tray + Häkchen im Fenster); er gilt nur als „an“, wenn er auf genau diese EXE zeigt –
  ein Eintrag einer anderen Kopie wird nie stillschweigend umgebogen.
- Beim allerersten Start (noch keine `frames.json`) öffnet sich das Verwaltungsfenster von selbst.
- Ein zweiter Start (Doppelklick auf die EXE) öffnet das Verwaltungsfenster der laufenden Instanz.
- Rahmenbreite ändern: Das Bild bleibt gleich groß und am selben Ort, der Rahmen wächst nach außen.
- Aufblinken: dreimal grün, der Rahmen liegt dafür gut eine Sekunde über allen Fenstern.
- Programmsymbol (Rahmen mit Landschaft) wird zur Laufzeit gezeichnet; die .ico-Datei für die EXE folgt in S4.
- Rückfragen von Windows (Entfernen) zeigen ihre Schaltflächen in der Systemsprache („Ja“/„Nein“).

### S4 – Autostart, mehrere Monitore, Feinschliff
**Verzicht (16.11.2026) auf die Handtests zu Monitoren/Skalierung, Explorer-Neustart und Autostart.**
Die Behandlung wird trotzdem eingebaut; die drei Punkte gelten als *eingebaut, nicht von Hand geprüft*.

- [ ] Autostart an/aus über Tray; nach Windows-Anmeldung erscheinen alle Rahmen an derselben Stelle – *Handtest verzichtet*
- [ ] Ziehen über die Monitorgrenze (unterschiedliche DPI) ohne Springen; nach Neustart am richtigen Ort – *Handtest verzichtet*
- [ ] Explorer-Neustart, Monitor ab-/anstecken, Auflösung ändern: kein Absturz, Rahmen sichtbar – *Handtest verzichtet*
- [x] Programmsymbol, README.md (deutsch), Version 0.1.0

Stand 16.11.: 112 Tests grün; EXE und Fenster tragen das neue Symbol; Start, Verwaltungsfenster per zweitem Start
und Rahmen ohne Fehlereintrag geprüft.

Eingebaut in S4 (ohne Handtest, siehe oben):
- **Monitor an-/abgesteckt, Auflösung/Anordnung geändert** (`DisplaySettingsChanged`): alle Rahmen werden nach
  800 ms aus der gespeicherten Lage neu gesetzt (fehlender Monitor → Hauptmonitor, gespeicherte Lage bleibt).
- **Skalierung eines Monitors geändert / Rahmen über DPI-Grenze gezogen**: WPF skaliert das Fenster (PerMonitorV2),
  danach wird die Lage exakt nachgesetzt; die Bildbreite ist in geräteunabhängigen Einheiten gespeichert.
- **Explorer-Neustart** (`TaskbarCreated`): Rahmen werden nach 500 ms neu eingeordnet (zusätzlich zur
  Sekunden-Prüfung); das Infobereich-Symbol legt Windows Forms selbst neu an.
- **Autostart**: Eintrag unter `HKCU\…\Run` (seit S3, Tests mit eigenem Schlüssel).
- Programmsymbol `Assets\DeskFrames.ico` (16–256 px) aus `Assets\make-icon.ps1`, eingebettet für Infobereich und
  Fenster.

### S5 – Probelauf und Paket
- [x] Probelauf auf dem Testrechner mit der EXE aus dem Paket – **bestanden (16.11.2026)**; Kriterien 1–3,
  5, 7 zusätzlich in den Handtests S0–S3 bestätigt
- [x] `DeskFrames.zip` auf dem Desktop (Name nach dem Muster PerfOverlay.zip; Ordner `DeskFrames` mit
  eigenständiger `DeskFrames.exe`, kein .NET nötig, und `LIESMICH.txt`)

Stand 16.11.: ZIP 66 MB (EXE 165 MB unkomprimiert). Bewusst **ohne** Komprimierung der Einzeldatei: Mit ihr bleiben
die entpackten Programmteile im Speicher und der Bedarf verdoppelt sich (257 statt 130 MB). Selbst geprüft: ZIP an
anderer Stelle entpackt, EXE gestartet – Rahmen erscheinen, Verwaltungsfenster per zweitem Start, kein Fehlereintrag.
Paket neu bauen:
`dotnet publish DeskFrames.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist\DeskFrames`,
dann `LIESMICH.txt` und `LICENSE` dazulegen und `dist\DeskFrames` zippen.

**Veröffentlichung (21.11.2026):** Repository öffentlich unter MIT-Lizenz (Copyright strucklabs); vorher wurde der
Vorname aus Dateien und gesamtem Verlauf entfernt. `LICENSE` liegt seitdem auch im ZIP-Paket.

## Abnahmekriterien (aus der Aufgabe)

1. Mindestens zwei Rahmen mit verschiedenen Bildern gleichzeitig auf dem Desktop, hinter allen Fenstern, bei Win+D sichtbar
2. Klicks ohne Strg+Umschalt gehen durch die Rahmen auf den Desktop (Symbole bleiben anklickbar)
3. Mit Strg+Umschalt verschieben und an Rändern skalieren, Seitenverhältnis bleibt
4. Nach Beenden und Neustart sowie mit Autostart nach Anmeldung: alle Rahmen an derselben Stelle mit demselben Bild
   (Neustart geprüft; Autostart nach Anmeldung: auf den Handtest verzichtet)
5. Fehlende Bilddatei → Platzhalter statt Fehler
6. Mehrere Monitore mit unterschiedlicher Skalierung funktionieren
   (zwei Monitore mit gleicher Skalierung geprüft; unterschiedliche Skalierung: auf den Handtest verzichtet)
7. Tests für Speichern/Laden und Seitenverhältnis grün; Probelauf auf dem Testrechner bestanden
8. Fertiges Programm als ZIP auf dem Desktop

## Grenzen

Keine Animationen (Ausnahme: kurzes Aufblinken zum Finden), keine Diashow, keine
Online-Bilder, Hintergrundbild bleibt unangetastet, Bilddateien werden nie verändert, kopiert oder verschoben.

## Entscheidungen (16.11.2026)

1. Rahmen liegen **über den Desktop-Symbolen** – in Ordnung.
2. **Strg+Umschalt** wie PerfOverlay; laufen beide, werden beide greifbar – in Ordnung.
3. **Kurzes Aufblinken** des gewählten Rahmens ist erlaubt (einzige Ausnahme von „keine Animationen“).
4. **Standardstil:** sehr dunkles Grau kurz vor Schwarz (`#1C1C1C`), 6 px. Stil gilt pro Rahmen; zusätzlich
   Schaltfläche `Apply to all frames` im Verwaltungsfenster.
5. **Erst lokal**, kein GitHub.

## Ideen (nur notiert, nicht Teil dieses Plans)

- Leichter Schatten oder abgerundete Ecken als weitere Stile
- Rahmen sperren (gegen versehentliches Verschieben)
- Reihenfolge im Verwaltungsfenster ändern, um die Überlappung zu steuern (welcher Rahmen oben liegt) –
  Wunsch vom 16.11.2026
