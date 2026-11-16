# Plan: DeskFrames – Bilderrahmen auf dem Desktop

Stand: 16.11.2026 · Status: **abgenommen** (Fragen beantwortet, siehe „Entscheidungen“) · S0 bestanden · nächster Schritt: S1

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
  "Autostart": false,
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
- [ ] Tests grün: Speichern → Laden ergibt dieselben Rahmen; fehlende/leere/beschädigte Datei → leere Liste ohne Ausnahme; unbekannte Felder werden ignoriert; atomares Schreiben
- [ ] Tests grün: Monitor-Rückfall (fehlender Monitor → Hauptmonitor, vollständig sichtbar)

### S2 – Rahmenfenster
Bild + Rahmenstil, Platzhalter, Verschieben und Skalieren mit fester Form, Speichern beim Loslassen.
- [ ] Tests grün: Seitenverhältnis-Rechnung für alle 8 Griffe (Form bleibt, gegenüberliegende Ecke fest, Mindest-/Höchstgröße, Rundung ohne Drift bei vielen kleinen Schritten)
- [ ] Tests grün: EXIF-Drehung bestimmt das Seitenverhältnis; Bild wechseln hält Fläche und Mittelpunkt
- [ ] Fehlendes Bild zeigt den Platzhalter; Bilddatei wird nicht gesperrt (lässt sich bei laufendem Programm umbenennen)

### S3 – Tray und Verwaltungsfenster
- [ ] Alle Befehle aus „Bedienung“ funktionieren; Änderungen sind sofort sichtbar und gespeichert
- [ ] Kontextmenü am Rahmen bei Strg+Umschalt

### S4 – Autostart, mehrere Monitore, Feinschliff
- [ ] Autostart an/aus über Tray; nach Windows-Anmeldung erscheinen alle Rahmen an derselben Stelle
- [ ] Ziehen über die Monitorgrenze (unterschiedliche DPI) ohne Springen; nach Neustart am richtigen Ort
- [ ] Explorer-Neustart, Monitor ab-/anstecken, Auflösung ändern: kein Absturz, Rahmen sichtbar
- [ ] Programmsymbol, README.md (deutsch), Version 0.1.0

### S5 – Probelauf und Paket
- [ ] Probelauf auf dem Testrechner mit allen Abnahmekriterien der Aufgabe (Liste unten)
- [ ] `DeskFrames-0.1.0.zip` auf dem Desktop (eigenständige EXE, kein .NET nötig)

## Abnahmekriterien (aus der Aufgabe)

1. Mindestens zwei Rahmen mit verschiedenen Bildern gleichzeitig auf dem Desktop, hinter allen Fenstern, bei Win+D sichtbar
2. Klicks ohne Strg+Umschalt gehen durch die Rahmen auf den Desktop (Symbole bleiben anklickbar)
3. Mit Strg+Umschalt verschieben und an Rändern skalieren, Seitenverhältnis bleibt
4. Nach Beenden und Neustart sowie mit Autostart nach Anmeldung: alle Rahmen an derselben Stelle mit demselben Bild
5. Fehlende Bilddatei → Platzhalter statt Fehler
6. Mehrere Monitore mit unterschiedlicher Skalierung funktionieren
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
