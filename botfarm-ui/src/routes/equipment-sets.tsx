import { Outlet, createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/equipment-sets')({
  component: EquipmentSetsLayout,
})

function EquipmentSetsLayout() {
  return <Outlet />
}
