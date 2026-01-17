import { Outlet, createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/routes')({
  component: RoutesLayout,
})

function RoutesLayout() {
  return <Outlet />
}
