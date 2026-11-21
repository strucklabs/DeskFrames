# DeskFrames

Bilderrahmen für den Windows-Desktop (C# / .NET 10 / WPF). DeskFrames legt eigene Fotos als schlichte Rahmen auf den
Desktop – hinter alle Fenster, wie das Hintergrundbild. Im Alltag stören sie nicht: Klicks gehen durch sie hindurch,
Desktop-Symbole bleiben anklickbar, und bei „Desktop anzeigen“ (Win+D) bleiben sie sichtbar.

Die Oberfläche ist englisch.

## Starten

`DeskFrames.exe` starten. Beim ersten Start öffnet sich das Verwaltungsfenster; danach läuft DeskFrames still im
Infobereich der Taskleiste (Symbol: Bilderrahmen mit Berg und Sonne).

Ein erneuter Doppelklick auf die EXE öffnet das Verwaltungsfenster des bereits laufenden Programms.

## Bedienung

| Was | Wie |
|---|---|
| Rahmen hinzufügen | Infobereich-Symbol → **Add frame…** oder im Verwaltungsfenster |
| Verschieben | **Strg+Umschalt gedrückt halten** (Rahmen wird grün umrandet) und ziehen |
| Größe ändern | Strg+Umschalt halten und an einem Rand oder einer Ecke ziehen – das Seitenverhältnis bleibt |
| Bild wechseln, Farbe, Breite, entfernen | Strg+Umschalt halten und **Rechtsklick** auf den Rahmen, oder im Verwaltungsfenster |
| Rahmen finden | Im Verwaltungsfenster auswählen – er blinkt kurz auf |
| Gleicher Stil für alle | Verwaltungsfenster → **Apply to all frames** |
| Mit Windows starten | Infobereich → **Start with Windows** oder Häkchen im Verwaltungsfenster |
| Beenden | Infobereich → **Exit** |

Sobald Strg+Umschalt losgelassen werden, lassen die Rahmen Klicks wieder durch.

**Gut zu wissen**

- Das Bild bestimmt die Form des Rahmens. Ein neues Bild mit anderer Form übernimmt seine Form; Fläche und Mitte des
  Rahmens bleiben dabei ungefähr gleich.
- Ändert man die Rahmenbreite, bleibt das Bild gleich groß und am selben Platz; der Rahmen wächst nach außen.
- Bilddateien werden nur **verknüpft** – nie kopiert, verändert oder verschoben. Wird eine Datei verschoben oder
  gelöscht, zeigt der Rahmen „Image not found“; über **Change image…** lässt sich ein neues Bild wählen.
- Die Rahmen liegen über den Desktop-Symbolen. Ein Symbol unter einem Rahmen ist verdeckt, lässt sich aber anklicken.
- Lesbar ist alles, was Windows selbst öffnen kann: JPG, PNG, BMP, GIF (erstes Bild), TIFF, ICO; WebP/HEIC/AVIF,
  wenn die passenden Erweiterungen aus dem Microsoft Store installiert sind. Handyfotos werden richtig gedreht.
- Nur lokale Bilder; keine Diashow, keine Animationen.

## Wo die Daten liegen

| Datei | Inhalt |
|---|---|
| `%APPDATA%\DeskFrames\frames.json` | Rahmen: Bildpfad, Lage, Größe, Farbe, Breite (von Hand lesbar) |
| `%APPDATA%\DeskFrames\error.log` | Unerwartete Fehler (nur wenn welche auftreten) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `DeskFrames` | Nur wenn „Start with Windows“ an ist |

Ist `frames.json` beschädigt, startet DeskFrames leer und legt die alte Datei als `frames.json.bad` daneben.

**Entfernen:** „Start with Windows“ ausschalten, beenden, den Programmordner und `%APPDATA%\DeskFrames` löschen.

## Mehrere Monitore

Jeder Rahmen merkt sich seinen Monitor und den Abstand zu dessen Ecke. Fehlt der Monitor beim Start, erscheint der
Rahmen auf dem Hauptmonitor; steckt er wieder, kehrt der Rahmen beim nächsten Start dorthin zurück. Bei Monitoren mit
unterschiedlicher Skalierung wirkt ein Rahmen überall gleich groß.

## Lizenz

DeskFrames steht unter der MIT-Lizenz, siehe [`LICENSE`](LICENSE). Das Paket enthält die .NET-Laufzeit von
Microsoft (ebenfalls MIT-Lizenz), damit kein .NET installiert sein muss.

## Für Entwickler

```
dotnet build -c Release
dotnet test tests\DeskFrames.Tests
bin\Release\net10.0-windows\DeskFrames.exe
```

- `DeskFrames.exe --config <Datei>` arbeitet mit einer anderen Rahmen-Datei (für Probeläufe).
- Programmsymbol neu erzeugen: `powershell -NoProfile -File Assets\make-icon.ps1`
- Aufbau: `Desktop\DesktopLayer` hält die Rahmen direkt über dem Fenster mit den Desktop-Symbolen (so bleiben sie bei
  Win+D sichtbar), `Frames\` Rahmenfenster und Verwaltung, `Model\` reine Rechnungen (Lage, Seitenverhältnis) und
  `frames.json`, `Imaging\` Bilder laden, `UI\` Infobereich und Verwaltungsfenster.
- `probe\` enthält die Machbarkeitsprobe der Desktop-Ebene (nur zur Referenz, nicht Teil der EXE).
- Plan und Entscheidungen: `PLAN-DESKFRAMES.md`.
