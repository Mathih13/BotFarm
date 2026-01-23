import { useQuery } from '@tanstack/react-query'
import { itemsApi } from '~/lib/api'
import { cn } from '~/lib/utils'

type IconSize = 'tiny' | 'small' | 'medium' | 'large'

const sizeClasses: Record<IconSize, string> = {
  tiny: 'w-4 h-4',
  small: 'w-5 h-5',
  medium: 'w-9 h-9',
  large: 'w-14 h-14',
}

interface ItemIconProps {
  entry: number
  size?: IconSize
  className?: string
}

export function ItemIcon({ entry, size = 'medium', className }: ItemIconProps) {
  const { data: icons, isLoading } = useQuery({
    queryKey: ['itemIcons', entry],
    queryFn: () => itemsApi.getIcons([entry]),
    staleTime: Infinity, // Icons never change
    enabled: entry > 0,
  })

  const iconUrl = icons?.[entry]
  const sizeClass = sizeClasses[size]

  if (!entry || entry <= 0) {
    return (
      <div
        className={cn(
          sizeClass,
          'rounded bg-muted border border-border flex items-center justify-center text-muted-foreground text-xs',
          className
        )}
      >
        ?
      </div>
    )
  }

  if (isLoading) {
    return (
      <div
        className={cn(
          sizeClass,
          'rounded bg-muted border border-border animate-pulse',
          className
        )}
      />
    )
  }

  if (!iconUrl) {
    return (
      <div
        className={cn(
          sizeClass,
          'rounded bg-muted border border-border flex items-center justify-center text-muted-foreground text-xs',
          className
        )}
      >
        ?
      </div>
    )
  }

  return (
    <img
      src={iconUrl}
      alt={`Item ${entry}`}
      className={cn(sizeClass, 'rounded border border-border object-cover', className)}
      loading="lazy"
    />
  )
}

// Hook for batched icon fetching (useful when displaying many items)
export function useItemIcons(entries: number[]) {
  const validEntries = entries.filter((e) => e > 0)

  return useQuery({
    queryKey: ['itemIcons', ...validEntries.sort()],
    queryFn: () => itemsApi.getIcons(validEntries),
    staleTime: Infinity,
    enabled: validEntries.length > 0,
  })
}
