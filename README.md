# ShardLegacy

**Enterprise Container Deployment Platform**

Deploy containerized applications from GitHub repositories or local directories. ShardLegacy scans for Docker configuration, builds images, starts containers, generates Nginx reverse proxy configs, and provides real-time deployment logs with a 6-stage pipeline.

---

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Backend API** | ASP.NET Core (.NET 10) | RESTful API with SSE streaming |
| **Frontend** | React 19 + Vite | SPA with real-time deployment UI |
| **Containerization** | Docker Engine | Image builds, container lifecycle |
| **Reverse Proxy** | Nginx | Dynamic subdomain routing |
| **Real-time** | Server-Sent Events | Live deployment log streaming |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/) (must be running)
- [Git](https://git-scm.com/)

## Quick Start

```bash
# 1. Clone and restore
git clone <repository-url>
cd ShardLegacy
dotnet restore

# 2. Install frontend deps
cd shardlegacy.client
npm install
cd ..

# 3. Run
cd ShardLegacy.Server
dotnet run
```

Open `https://localhost:55730` in your browser.

---

## Deployment Pipeline

ShardLegacy runs a 6-stage pipeline for every deployment:

| Stage | Description |
|-------|-------------|
| **1. Clone** | Clone GitHub repo or scan local directory, detect framework |
| **2. Environment** | Generate subdomain, assign port, inject environment variables |
| **3. Build** | Run `docker build` or `docker compose build` |
| **4. Deploy** | Start container with port mapping (tries 80, 8080, 3000) |
| **5. Health Check** | HTTP health check with 5 retry attempts |
| **6. Proxy** | Generate Nginx reverse proxy configuration |

Each stage streams real-time logs via Server-Sent Events to the browser.

## Subdomain Generation

Every deployment receives a unique subdomain:

```
<project-name>.<adjective>.localhost
```

Example: `my-api.stellar.localhost`, `frontend.nimbus.localhost`

## Supported Frameworks

Auto-detected during the scan phase:

| Framework | Detection |
|-----------|-----------|
| React | `package.json` contains `react` |
| Next.js | `package.json` contains `next` |
| Vue.js | `package.json` contains `vue` |
| Express.js | `package.json` contains `express` |
| Django | `manage.py` present |
| Flask | `requirements.txt` without `manage.py` |
| ASP.NET | `.csproj` files present |
| Go | `go.mod` present |
| Rust | `Cargo.toml` present |
| Java/Spring | `pom.xml` or `build.gradle` present |

## API Reference

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/projects` | List all deployments |
| `GET` | `/api/projects/{id}` | Get deployment details |
| `POST` | `/api/projects/scan` | Scan source for Docker files |
| `POST` | `/api/projects/deploy` | Start deployment (background) |
| `GET` | `/api/projects/{id}/logs/stream` | SSE real-time log stream |
| `POST` | `/api/projects/{id}/teardown` | Stop container, remove config |
| `DELETE` | `/api/projects/{id}` | Remove deployment record |
| `GET` | `/api/projects/{id}/nginx-config` | Get generated Nginx config |

## Project Structure

```
ShardLegacy/
├── ShardLegacy.Server/
│   ├── Controllers/
│   │   └── ProjectsController.cs      # API + SSE streaming
│   ├── Models/
│   │   └── ProjectDeployment.cs        # All domain models
│   ├── Services/
│   │   └── DeploymentService.cs        # Core deployment engine
│   ├── Program.cs
│   └── Dockerfile
└── shardlegacy.client/
    └── src/
        ├── pages/
        │   ├── Dashboard.jsx           # Overview + stats
        │   └── Deploy.jsx              # Deploy form + live pipeline + logs
        ├── App.jsx
        ├── index.css
        └── main.jsx
```

## License

Proprietary. All rights reserved.