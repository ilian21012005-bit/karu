# Karu — brief projet pour analyse & refonte esthétique

Document destiné à une **autre IA** (ou designer) : contexte produit, architecture, fichiers UI, contraintes, et brief esthétique.  
Repo : https://github.com/ilian21012005-bit/karu  
Chemin local typique : `C:\Users\ilian\Documents\Karu`  
Version app au moment du brief : **1.1.3** (+ thème `riot` rouge/noir en cours local, éventuellement non taggé).

---

## 1. Qu’est-ce que Karu ?

**Replay buffer Valorant local** pour Windows (WPF) :

- Capture écran via **FFmpeg + NVENC** (segments TS en boucle).
- Hotkey global (défaut **F9**) → sauve les N dernières minutes en MP4.
- **Highlights auto** : OCR killfeed Windows (Windows.Media.Ocr) + tracker de séries → clip auto **3K / 4K / Ace**.
- Bibliothèque de clips type Medal (dossiers, tags, favoris, lecture).
- **Zéro compte / zéro cloud** — privacy-first, local-only.
- Cible GPU : **NVIDIA uniquement** (positionnement actuel).

Positionnement voulu : battre Medal sur *simplicité d’install + privacy*, pas sur le social.

---

## 2. Stack technique

| Couche | Techno |
|--------|--------|
| UI | **WPF** (.NET 8, `net8.0-windows10.0.19041.0`) + un peu WinForms host |
| Lecteur vidéo | **LibVLCSharp.WinForms 3.6.6** + VideoLAN.LibVLC.Windows (audio jeu + Discord) |
| Capture / mux | FFmpeg (`tools/ffmpeg/ffmpeg.exe`), NVENC |
| Audio capture | NAudio |
| OCR | Windows.Media.Ocr (killfeed) |
| Config | `%AppData%\Karu\config.json` |
| Clips | `Vidéos\Karu` (+ sidecars `.karu`) |
| Buffer temp | `%LocalAppData%\Karu\buffer` |
| Installer | Inno Setup via `tools/build-installer.ps1` + GitHub Actions `release.yml` |
| Tests | xUnit `tests/Karu.Tests` |

Solution : `Karu.sln`  
Projets :

- `src/Karu.App` — UI + services runtime  
- `src/Karu.Core` — config, naming, ffmpeg args, trackers, library model  
- `tests/Karu.Tests`

---

## 3. Architecture runtime (à ne pas casser)

```
App.xaml.cs
  ├─ ConfigStore.Load
  ├─ Onboarding (first-run)
  ├─ MainWindow
  ├─ ReplayBufferService (NVENC)     → buffer ON si process Valorant tourne
  ├─ KillfeedMonitor (OCR)           → OCR ON seulement si Valo au premier plan
  ├─ GameForegroundMonitor
  ├─ GlobalHotkeyService (F9)
  ├─ TrayService
  └─ ClipLibrary / ThumbnailService / LibVLC player
```

**Règle perf (non négociable)** :

- Buffer = process Valorant.
- OCR = focus Valo uniquement (pas quand on regarde des vidéos Val).
- Trim / re-export = **uniquement hors jeu** (à l’export).
- Pas d’overlay in-game (anti-cheat + perf).

Statuts UI unifiés (`Karu.Core/CaptureStatusText.cs`) :

- `En attente de Valorant`
- `Buffer actif`
- `Buffer actif · détection ON`
- `Buffer actif · focus Valo pour détection`
- `Buffer actif · pseudo manquant`
- `Buffer en pause (batterie)`

---

## 4. Structure UI actuelle

Fenêtre principale : `MainWindow.xaml` / `MainWindow.xaml.cs`

| Zone | Contenu |
|------|---------|
| Rail gauche 64px | Logo + nav Library / Settings (style Medal) |
| `ClipsView` | Titre, statut, health OCR, bannière pseudo, dossiers, grille de cards clips |
| `DetailView` | Lecteur LibVLC + seek + trim début/fin + tags |
| `SettingsView` | Thème, système, replay, highlights, calibration killfeed |
| First-run | `OnboardingWindow.cs` (code-behind WPF) |
| Calibration | `CalibrationWindow.cs` (fullscreen drag rect) |

Styles / brushes globaux : `App.xaml`  
Palettes runtime : `Services/ThemeService.cs`

Thèmes connus : `riot` (rouge `#FF4655` + noir), `carbon`, `aventurine`, `plum`, `ember`, `forest`, `ocean`, `holst`, `crimson`.

---

## 5. Fichiers à toucher pour l’esthétique (priorité)

### Priorité haute (look & feel)

| Fichier | Rôle |
|---------|------|
| `src/Karu.App/App.xaml` | Brushes, FontFamily, styles Button / SideRail / SectionTitle / ListBox |
| `src/Karu.App/MainWindow.xaml` | Layout Library / Detail / Settings |
| `src/Karu.App/MainWindow.xaml.cs` | `ClipListItem` (badges ACE/3K), bindings UI |
| `src/Karu.App/Services/ThemeService.cs` | Palettes couleurs |
| `src/Karu.App/OnboardingWindow.cs` | First-run (construit en code) |
| `src/Karu.App/Assets/` | `karu-logo.png`, `karu.ico` |

### Priorité moyenne (copy / micro-UX visuelle)

| Fichier | Rôle |
|---------|------|
| `src/Karu.App/App.xaml.cs` | Wire statut / onboarding / toasts (pas le layout) |
| `src/Karu.Core/AppConfig.cs` | Thème défaut (`riot`), highlights ON |
| `README.md` | Docs utilisateur |

### À éviter sauf nécessité produit

- `KillfeedMonitor.cs`, `ReplayBufferService.cs`, `KillStreakTracker.cs` — cœur capture/OCR  
- `FfmpegArgumentBuilder.cs` — args encode  
- Logic hotkey / process gate / anti-cheat assumptions  

---

## 6. Brief esthétique (état + problème)

### Ce qui a déjà été tenté (insuffisant)

Approche « skin B » (thème `riot`) :

- Noir mat `#0A0A0B` / panels `#141416`
- Accent Valorant `#FF4655`
- Typo Bahnschrift
- Coins 4px, badges ACE/3K/4K uppercase
- Titre « KARU » rouge dans la Library

**Verdict utilisateur :** *toujours moche* — un reskin de palette ne suffit pas.  
Le problème est surtout la **structure visuelle** (dashboard Medal/SaaS : rail + cards arrondies + beaucoup de chrome), pas seulement les couleurs.

### Direction souhaitée (ami / produit)

- **Rouge + noir** (confirmé : « elle kiffe le rouge et noir »)
- Vibe **arcade compétitive** type Valorant / Riot — pas purple SaaS, pas cream editorial, pas glow violet
- Moins « généré par IA / template dashboard »
- Rester **lisible en 2 minutes** pour une amie non-tech
- Local, rapide, pas de cosplay scanlines kitsch

### Approches envisagées (non toutes faites)

1. ~~Skin couleurs seulement~~ → fait, jugé insuffisant  
2. **Refonte layout** (recommandée ensuite) : hero clip / moins de cards / statut type HUD / hiérarchie plus forte  
3. Full Riot cosplay (glows, angles coupés partout) → risque kitsch, à éviter en premier  

### Anti-patterns à éviter (règles design du projet)

- Thème purple-on-white / indigo glow  
- Warm cream + serif terracotta  
- Layout « broadsheet » dense  
- Trop de pills, stat strips, badges flottants sur le hero  
- Cards partout si une bordure ne sert pas l’interaction  
- Overlay in-game  

### Références utiles à regarder (inspiration, pas copie)

- Medal.tv (library) — ce qu’on a trop imité  
- NVIDIA ShadowPlay / GeForce Experience — simplicité  
- Client Valorant / Riot — contraste, typo display, rouge  
- CapCut / DaVinci lite — outils média (option « pro froid » si pivot)

---

## 7. Fonctionnalités UX déjà en place (ne pas régresser)

- Onboarding first-run : pseudo, hotkey F9, toggle clips auto, calib optionnelle  
- Highlights ON par défaut (CreateDefault) ; configs existantes non forcées  
- Bannière « Entre ton pseudo… »  
- Health OCR (point vert/orange + dernière ligne)  
- Empty state library (3 étapes)  
- Toast tray « Triple/Ace sauvé »  
- Noms fichiers `Ace_pseudo_yyyy-MM-dd_HH-mm-ss.mp4`  
- Trim début/fin + export ffmpeg (copy puis reencode filet)  
- FileSystemWatcher refresh clips  
- Checklist amie dans Settings  

---

## 8. Contraintes non négociables pour toute modif

1. **WPF desktop** — pas de rewrite Electron/React sauf décision produit explicite.  
2. **Perf in-game** — pas de nouveau polling lourd, pas d’OCR permanent hors focus, pas d’overlay.  
3. **Local-only** — pas de compte, cloud, feed social.  
4. **NVIDIA / NVENC** pour l’instant.  
5. **Français** UI + messages utilisateur.  
6. Garder hotkey global + tray + single-instance.  
7. Tests xUnit verts si tu touches Core (`dotnet test`).  
8. Build : `dotnet build src/Karu.App/Karu.App.csproj -c Release`

---

## 9. Comment lancer / tester le look

**Lanceur recommandé** (cwd + LibVLC corrects) :

```powershell
cd C:\Users\ilian\Documents\Karu
powershell -File lancer-karu.ps1
```

Ou à la main :

```powershell
cd C:\Users\ilian\Documents\Karu
powershell -File tools/download-ffmpeg.ps1   # une fois
dotnet build src/Karu.App/Karu.App.csproj -c Release
# IMPORTANT : WorkingDirectory = dossier de l'exe (libvlc)
Start-Process .\src\Karu.App\bin\Release\net8.0-windows10.0.19041.0\Karu.exe `
  -WorkingDirectory (Resolve-Path .\src\Karu.App\bin\Release\net8.0-windows10.0.19041.0)
```

Si ça ne démarre pas : lire `%LocalAppData%\Karu\log.txt` et tuer une instance déjà ouverte (`Get-Process Karu | Stop-Process`).

Thème forcé dans config :

```
%AppData%\Karu\config.json  →  "theme": "riot"
```

Release GitHub : tags `v*` → workflow **Release Windows** produit `Karu-Setup-x.y.z.exe`.

---

## 10. Mission demandée à l’IA suivante

Tu es chargée d’une **analyse + propositions / implémentation esthétique**.

### Livrables attendus

1. **Diagnostic** : pourquoi l’UI actuelle fait encore « basique / IA » (structure, typo, rythme, densité).  
2. **Direction visuelle unique** rouge+noir compétitive (variables couleur, typo, règles de composition).  
3. **Mock / plan de layout** (même en ASCII ou description section par section) pour Library + Detail + Settings + Onboarding.  
4. **Implémentation WPF** dans les fichiers listés §5, sans casser capture/OCR.  
5. Screenshots mentaux ou checklist de validation : first viewport Library, détail clip, settings, onboarding.

### Critères de succès

- En 3 secondes : on comprend que c’est un outil **clips Valorant**, pas un admin panel générique.  
- Le rouge est signature, le noir est dominant, peu de gris-bleu SaaS.  
- Moins de chrome, plus de hiérarchie (1 job par zone).  
- Toujours utilisable par une amie : statut buffer lisible, pseudo, F9.  
- Aucune régression perf capture.

### Questions ouvertes (à trancher avec l’utilisateur si besoin)

- Garder la grille de cards ou passer à liste + hero player ?  
- Rail Medal vs top bar vs HUD minimal ?  
- Combien de thèmes garder (1 fort `riot` vs 8 palettes) ?  
- Logo / wordmark à redessiner ?

---

## 11. Carte rapide des services (référence)

```
src/Karu.App/Services/
  ReplayBufferService.cs      # capture NVENC
  KillfeedMonitor.cs          # OCR loop
  ClipPlaybackController.cs   # LibVLC
  ClipTrimService.cs          # trim export
  GameForegroundMonitor.cs
  GameProcessNames (Core)     # détection process Valo
  GlobalHotkeyService.cs
  ThemeService.cs             # palettes
  TrayService.cs
  ThumbnailService.cs
  AudioCaptureService.cs
  FfmpegSupport.cs
  …

src/Karu.Core/
  AppConfig.cs / ConfigStore
  CaptureStatusText.cs
  ClipFileNamer.cs
  ClipLibrary.cs / ClipMetadata.cs
  KillStreakTracker.cs
  HighlightClipScheduler.cs
  FfmpegArgumentBuilder.cs
  …
```

---

## 12. Notes auteur

- Propriétaire : Ilian — produit pour usage perso + amie Valorant.  
- Feedback récent : *« app toujours moche »* après le skin rouge ; besoin d’une **vraie refonte visuelle**, pas d’un énième thème.  
- Spec skin partielle : `docs/superpowers/specs/2026-10-15-karu-rouge-skin-design.md`  
- Context-mode Cursor est configuré dans `.cursor/` (MCP/hooks) — optionnel pour l’IA design.

**Commence par lire `MainWindow.xaml` + `App.xaml` + `ThemeService.cs`, puis propose une direction layout avant de tout réécrire.**
