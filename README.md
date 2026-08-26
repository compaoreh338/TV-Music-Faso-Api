# TV-Music Faso — API

API ASP.NET Core (HTTPS `https://localhost:7245`). Dépôt séparé du desktop et du tableau de bord React.

Le desktop (`TV-Music-Faso`) et le web (`TV-Music-Faso-Web`) parlent à **cette** API. Même PostgreSQL (Docker du desktop).

## Lancer

1. Postgres depuis le desktop : `docker compose up -d`
2. API :

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet run --project src/TVMusicFaso.Api
```

Comptes démo : `sara.programmateur`, `ibrahim.technicien`, `marie.direction`.

## Production (SPA servie par l’API)

Depuis le dépôt web : `npm run build`, puis copier `dist/` vers `src/TVMusicFaso.Api/wwwroot`.
