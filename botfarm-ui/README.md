# BotFarm Web UI

Real-time monitoring dashboard for BotFarm bots.

## Features

- **Live Bot Status** - See all connected bots with real-time updates
- **Route Management** - Assign and monitor bot routes
- **Test Run Dashboard** - Track E2E test execution and results
- **Interactive Map** - Visualize bot positions on the world map
- **Statistics** - Bot counts, level distribution, combat status

## Technology Stack

- [TanStack Start](https://tanstack.com/start) - Full-stack React framework
- [TanStack Router](https://tanstack.com/router) - Type-safe routing
- [TanStack Query](https://tanstack.com/query) - Server state management
- [Tailwind CSS](https://tailwindcss.com/) - Styling
- TypeScript - Type safety

## Development

### Prerequisites

- Node.js 20+
- npm

### Getting Started

```bash
# Install dependencies
npm install

# Start development server
npm run dev
```

The dev server runs at http://localhost:3000 with hot module reloading.

### Development with BotFarm

To develop the UI alongside the BotFarm backend:

```bash
# Terminal 1: Start BotFarm with dev UI flag (doesn't auto-launch UI)
./BotFarm.exe --dev-ui

# Terminal 2: Start UI dev server
cd botfarm-ui
npm run dev
```

## Production Build

```bash
# Build for production
npm run build

# Output is in .output/ directory
```

The production build creates a Node.js server in `.output/server/index.mjs`.

## Architecture

### Directory Structure

```
botfarm-ui/
├── app/
│   ├── components/     # Reusable UI components
│   ├── routes/         # Page routes (file-based routing)
│   ├── api/            # API utilities
│   └── styles/         # Global styles
├── public/             # Static assets
└── .output/            # Production build output
```

### Backend Integration

The UI communicates with BotFarm via:

- **REST API** - Bot status, route management, test commands
- **WebSocket** - Real-time updates (positions, combat, etc.)

API endpoints are served by BotFarm on the same port (3000).

### Key Routes

- `/` - Dashboard with bot overview
- `/bots` - Detailed bot list and management
- `/routes` - Route configuration
- `/tests` - Test run management
- `/map` - Interactive world map

## Building for Release

When building BotFarm for release, the UI is built and included automatically:

```bash
# From repo root
./build-release.sh
```

The build script:
1. Runs `npm ci && npm run build`
2. Copies `.output/` to the release package
3. BotFarm serves the UI from the bundled files

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PORT` | Server port | 3000 |
| `NODE_ENV` | Environment | development |
