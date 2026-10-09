# Karu

Replay des N dernières minutes (défaut 5 min) pour Windows, pensé pour Valorant.
Encodeur **NVIDIA NVENC** : la puce dédiée travaille, le jeu n’est presque pas touché.

## Télécharger / Installer (Windows)

1. Va sur **[Releases](https://github.com/ilian21012005-bit/clip-buffer/releases)**
2. Télécharge **`Karu-Setup-x.y.z.exe`**
3. Lance le setup (assistant en français) → Suivant → Installer
4. Ouvre **Karu** depuis le Bureau ou le menu Démarrer

### Prérequis
- Windows 10 / 11 **64 bits**
- Carte **NVIDIA** + drivers à jour
- Valorant en **Plein écran fenêtré** recommandé

### SmartScreen
Si Windows affiche « a protégé votre PC » : **Informations complémentaires** → **Exécuter quand même**  
(le setup n’est pas encore signé Authenticode).

Désinstallation : Paramètres Windows → Applications → Karu, ou le raccourci « Désinstaller » dans le menu Démarrer.

---

## Utilisation rapide

1. Démarre **Karu** **avant** la partie (statut **Buffer actif**)
2. **F9** (modifiable) pour sauver le buffer
3. Onglet **Clips** : dossiers, lecture, tags, favoris
4. **Highlights auto** : active le killfeed, entre ton pseudo exact, coche Ace / Triple / Quad
5. Fermer la fenêtre = reste en barre d’état ; **Quitter** depuis l’icône tray = arrêt réel

Réglages : `%AppData%\ClipBuffer\config.json`  
Clips : `Vidéos\ClipBuffer` (métadonnées dans un dossier `.karu`)  
Buffer temp : `%LocalAppData%\ClipBuffer\buffer`

## Build développeur

```powershell
# FFmpeg (une fois)
powershell -File tools/download-ffmpeg.ps1

dotnet test
dotnet build src/ClipBuffer.App/ClipBuffer.App.csproj -c Release
```

### Produire l’installateur localement

```powershell
powershell -File tools/build-installer.ps1 -Version 1.0.0
# → artifacts/installer/Karu-Setup-1.0.0.exe
```

Le script publie une app **self-contained** (pas besoin d’installer .NET) + FFmpeg, puis compile un setup **Inno Setup**.

### Release GitHub

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Le workflow **Release Windows** construit `Karu-Setup-*.exe` et publie la release automatiquement.

## Licences (FFmpeg / NVENC)

- **FFmpeg** : le setup redistribue `ffmpeg.exe` (build GPL BtbN) + licences. Voir [`third_party/ffmpeg/LICENSE.txt`](third_party/ffmpeg/LICENSE.txt).
- **NVENC** : aucun driver NVIDIA embarqué — encodage via les drivers système.

## Mises à jour

Réglages → **Vérifier les mises à jour** (API GitHub Releases).  
Option : vérifier au démarrage.

## Laptop / GPU

- Option **Suspendre le buffer sur batterie** (défaut : oui)
- Préférence GPU Windows « Haute performance » pour `Karu.exe`

## Vanguard / anti-cheat

- Capture **écran** (ddagrab), jamais d’injection dans Valorant
- Raccourci global via `RegisterHotKey` (pas de `WH_KEYBOARD_LL`)
