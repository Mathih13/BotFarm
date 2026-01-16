/// <reference types="vite/client" />
import {
  HeadContent,
  Link,
  Outlet,
  Scripts,
  createRootRoute,
} from '@tanstack/react-router'
import { TanStackRouterDevtools } from '@tanstack/react-router-devtools'
import * as React from 'react'
import { DefaultCatchBoundary } from '~/components/DefaultCatchBoundary'
import { NotFound } from '~/components/NotFound'
import appCss from '~/styles/app.css?url'
import { seo } from '~/utils/seo'
import { useSignalR } from '~/lib/signalr'

export const Route = createRootRoute({
  head: () => ({
    meta: [
      {
        charSet: 'utf-8',
      },
      {
        name: 'viewport',
        content: 'width=device-width, initial-scale=1',
      },
      ...seo({
        title: 'BotFarm Test Runner',
        description: 'Web UI for BotFarm automated testing framework',
      }),
    ],
    links: [
      { rel: 'stylesheet', href: appCss },
      { rel: 'icon', href: '/favicon.ico' },
    ],
  }),
  errorComponent: DefaultCatchBoundary,
  notFoundComponent: () => <NotFound />,
  component: RootComponent,
})

function RootComponent() {
  const { isConnected, error } = useSignalR()

  return (
    <html>
      <head>
        <HeadContent />
      </head>
      <body>
        <div className="min-h-screen flex flex-col bg-gray-50">
          {/* Header */}
          <header className="bg-white border-b border-gray-200 shadow-sm">
            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
              <div className="flex justify-between items-center h-16">
                <div className="flex items-center gap-8">
                  <Link to="/" className="text-xl font-bold text-blue-500">
                    BotFarm
                  </Link>
                  <nav className="flex gap-6">
                    <Link
                      to="/"
                      activeProps={{ className: 'text-blue-500 font-semibold' }}
                      inactiveProps={{ className: 'text-gray-600 hover:text-gray-900' }}
                      activeOptions={{ exact: true }}
                    >
                      Dashboard
                    </Link>
                    <Link
                      to="/tests"
                      activeProps={{ className: 'text-blue-500 font-semibold' }}
                      inactiveProps={{ className: 'text-gray-600 hover:text-gray-900' }}
                    >
                      Tests
                    </Link>
                    <Link
                      to="/suites"
                      activeProps={{ className: 'text-blue-500 font-semibold' }}
                      inactiveProps={{ className: 'text-gray-600 hover:text-gray-900' }}
                    >
                      Suites
                    </Link>
                  </nav>
                </div>
                <div className="flex items-center gap-2">
                  <div
                    className={`w-2 h-2 rounded-full ${isConnected ? 'bg-green-500' : 'bg-red-500'}`}
                    title={isConnected ? 'Connected' : error || 'Disconnected'}
                  />
                  <span className="text-sm text-gray-500">
                    {isConnected ? 'Live' : 'Offline'}
                  </span>
                </div>
              </div>
            </div>
          </header>

          {/* Main content */}
          <main className="flex-1">
            <Outlet />
          </main>
        </div>
        <TanStackRouterDevtools position="bottom-right" />
        <Scripts />
      </body>
    </html>
  )
}
