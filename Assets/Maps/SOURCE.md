# Map attribution

Map imagery comes from the wardogs.tech public map viewer. This source edition bundles only low-resolution fallback PNGs. Detailed WebP pyramids are installed manually under `%LOCALAPPDATA%/MystieHook/Maps/<map>/<package SHA-256>/`.

Bakurani uses levels 0–6; Ozeti and Zestafona use levels 0–7. Tiles are 256 pixels. Preserve existing package version folders; expected hashes are in Services/LocalMaps.cs and WebRadar/server.mjs. No map requests are sent to a remote server by this application.