import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '-'
  if (seconds < 0) return '-'

  const mins = Math.floor(seconds / 60)
  const secs = Math.floor(seconds % 60)

  if (mins === 0) {
    return `${secs}s`
  }
  return `${mins}m ${secs}s`
}

export function formatDateTime(dateString: string | null | undefined): string {
  if (!dateString) return '-'

  try {
    const date = new Date(dateString)
    return date.toLocaleString()
  } catch {
    return dateString
  }
}

export function getStatusColor(status: string): string {
  switch (status) {
    case 'Completed':
      return 'text-green-600 border-green-200 bg-green-50'
    case 'Failed':
    case 'TimedOut':
      return 'text-red-600 border-red-200 bg-red-50'
    case 'Running':
    case 'SettingUp':
      return 'text-blue-600 border-blue-200 bg-blue-50'
    case 'Pending':
      return 'text-yellow-600 border-yellow-200 bg-yellow-50'
    case 'Stopped':
      return 'text-gray-600 border-gray-200 bg-gray-50'
    default:
      return 'text-gray-600 border-gray-200 bg-gray-50'
  }
}

export function getStatusIcon(status: string): string {
  switch (status) {
    case 'Completed':
      return '✓'
    case 'Failed':
    case 'TimedOut':
      return '✗'
    case 'Running':
      return '▶'
    case 'SettingUp':
      return '⏳'
    case 'Pending':
      return '○'
    case 'Stopped':
      return '■'
    default:
      return '○'
  }
}

export function isRunning(status: string): boolean {
  return status === 'Running' || status === 'SettingUp' || status === 'Pending'
}

export function getClassColor(className: string): string {
  const colors: Record<string, string> = {
    Warrior: '#C79C6E',
    Paladin: '#F58CBA',
    Hunter: '#ABD473',
    Rogue: '#FFF569',
    Priest: '#D0D0D0',
    DeathKnight: '#C41F3B',
    Shaman: '#0070DE',
    Mage: '#69CCF0',
    Warlock: '#9482C9',
    Druid: '#FF7D0A',
  }
  return colors[className] || '#888888'
}
