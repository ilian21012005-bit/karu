# Karu

Replay des N dernières minutes (défaut 5 min) pour Windows, pensé pour Valorant.
Encodeur **NVIDIA NVENC** : la puce dédiée travaille, le jeu n’est presque pas touché.

## Télécharger / Installer (Windows)

1. Va sur **[Releases](https://github.com/ilian21012005-bit/karu/releases)**
2. Télécharge **`Karu-Setup-x.y.z.exe`**
3. Lance le setup (assistant en français) → Suivant → Installer
4. Ouvre **Karu** depuis le Bureau ou le menu Démarrer

### Prérequis
- Windows 10 / 11 **64 bits**
- Carte **NVIDIA** + drivers à jour
- Valorant en **Plein écran fenêtré** recommandé

### SmartScreen
L’installateur n’est pas encore signé Authenticode; SmartScreen peut donc afficher un avertissement. Télécharge-le uniquement depuis la page Releases officielle du projet. Les prochaines releases incluront un fichier `.sha256` pour vérifier que le téléchargement correspond bien au setup publié. Cette empreinte contrôle l’intégrité du fichier, mais ne prouve pas l’identité de son éditeur.

Pour vérifier une release qui fournit le fichier de checksum, place le setup et son `.sha256` dans le même dossier, ouvre PowerShell dans ce dossier et lance :

```powershell
$setup = Get-ChildItem .\Karu-Setup-*.exe | Select-Object -First 1
$expected = (Get-Content "$($setup.FullName).sha256").Split(' ')[0]
$actual = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$actual -eq $expected
```

Le résultat doit être `True`. Si le résultat est `False`, ne lance pas le setup. Les anciennes releases peuvent ne pas encore avoir de fichier `.sha256`.

Désinstallation : Paramètres Windows → Applications → Karu, ou le raccourci « Désinstaller » dans le menu Démarrer.

---

## Utilisation rapide

1. Au premier lancement : onboarding (pseudo, F9, clips auto)
2. Démarre **Karu** **avant** la partie → statut **Buffer actif** quand Valorant tourne
3. **F9** (modifiable) pour sauver le buffer (même en alt-tab)
4. Library : dossiers, lecture LibVLC (audio jeu + Discord), tags, trim début/fin, favoris
5. **Highlights auto** (ON par défaut) : pseudo exact + focus Valo pour la détection OCR
6. Fermer la fenêtre = tray ; **Quitter** depuis l’icône = arrêt réel

### Checklist amie (si « ça marche pas »)

1. Pseudo Valorant **exact** dans Réglages  
2. Valorant lancé → statut **Buffer actif** (pas « En attente de Valorant »)  
3. Détection killfeed cochée (+ 3K / 4K / Ace)  
4. Pour les auto-clips : Valo **au premier plan** (OCR seulement là — perf)

Réglages : `%AppData%\Karu\config.json`  
Clips : `Vidéos\Karu` (métadonnées dans un dossier `.karu`)  
Buffer temp : `%LocalAppData%\Karu\buffer`

## Aide et signalement

Dans **Réglages**, la version de Karu apparaît en bas de la section Application. Pour un problème, ouvre un [rapport de bug](https://github.com/ilian21012005-bit/karu/issues/new?template=bug_report.yml) et indique cette version, ta version de Windows et le symptôme observé.

Le journal local est dans `%LocalAppData%\Karu\log.txt`. Si tu en partages un extrait, relis-le d’abord et masque ton nom Windows, ton pseudo en jeu et les chemins de tes fichiers. N’ajoute pas de clip vidéo ni ton `config.json` à un ticket public.

## Build développeur

```powershell
# FFmpeg (une fois)
powershell -File tools/download-ffmpeg.ps1

dotnet test
dotnet build src/Karu.App/Karu.App.csproj -c Release
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
