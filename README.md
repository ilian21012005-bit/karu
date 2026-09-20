# Clip Buffer

Replay des N dernières minutes (défaut 5 min) pour Windows, pensé pour Valorant.
Encodeur **NVIDIA NVENC** : la puce dédiée travaille, le jeu n’est presque pas touché.

## Lancer

1. GPU NVIDIA + drivers à jour
2. Dans Valorant : **Plein écran fenêtré**
3. Autoriser le micro Windows si tu veux ta voix dans les clips
4. Démarre Clip Buffer **avant** la partie
5. Appuie sur **F9** (modifiable) pour sauver le buffer

Le programme se met dans la barre d’état. Fermer la fenêtre le laisse tourner ; **Quitter** depuis l’icône l’arrête vraiment.

Réglages persistés dans `%AppData%\ClipBuffer\config.json`.
Clips par défaut dans `Vidéos\ClipBuffer`.
Buffer temporaire dans `%LocalAppData%\ClipBuffer\buffer`.

## Build

```powershell
# FFmpeg (une fois)
powershell -File tools/download-ffmpeg.ps1

dotnet test
dotnet run --project src/ClipBuffer.App/ClipBuffer.App.csproj
```

Le build copie `tools/ffmpeg/ffmpeg.exe` à côté de l’appli.

## Perf

Ce n’est pas 0 % mathématique : il faut encoder en continu. Cible ShadowPlay : NVENC, pas d’overlay, pas de cloud, pas d’IA. En pratique souvent moins de 1–3 % FPS sur une RTX.

## Vanguard

Capture **écran** (Desktop Duplication), jamais d’injection dans Valorant.
