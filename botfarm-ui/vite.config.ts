import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { defineConfig } from 'vite'
import tsConfigPaths from 'vite-tsconfig-paths'
import viteReact from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { nitro } from 'nitro/vite'
export default defineConfig({
  server: {
    port: 5173,
  },
  plugins: [
    tailwindcss(),
    tsConfigPaths({
      projects: ['./tsconfig.json'],
    }),
    tanstackStart({
      srcDirectory: 'src',
    }),
    viteReact(),
    nitro({
      devProxy: {
        '/api': { target: 'http://localhost:5000/api', changeOrigin: true },
        '/hubs': { target: 'http://localhost:5000/hubs', ws: true, changeOrigin: true },
      },
      routeRules: {
        '/api/**': { proxy: 'http://localhost:5000/api/**' },
        '/hubs/**': { proxy: 'http://localhost:5000/hubs/**' },
      },
    }),
  ],
})
