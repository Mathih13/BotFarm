import { Link } from '@tanstack/react-router'
import { AlertTriangle } from 'lucide-react'
import { Button } from '~/components/ui/button'

interface FirstRunBannerProps {
  missingPaths?: string[]
  invalidPaths?: string[]
}

export function FirstRunBanner({ missingPaths = [], invalidPaths = [] }: FirstRunBannerProps) {
  const formatPathName = (path: string) => {
    const names: Record<string, string> = {
      MMAPsFolderPath: 'MMaps',
      VMAPsFolderPath: 'VMaps',
      MAPsFolderPath: 'Maps',
      DBCsFolderPath: 'DBCs',
    }
    return names[path] || path
  }

  return (
    <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 mb-6">
      <div className="flex items-start gap-3">
        <AlertTriangle className="h-5 w-5 text-amber-500 mt-0.5 flex-shrink-0" />
        <div className="flex-1">
          <h3 className="font-semibold text-amber-800">Configuration Required</h3>
          <p className="text-sm text-amber-700 mt-1">
            BotFarm needs to be configured before it can run tests. Please configure the data paths
            to point to your TrinityCore data files.
          </p>

          {(missingPaths.length > 0 || invalidPaths.length > 0) && (
            <div className="mt-2 text-sm text-amber-700">
              {missingPaths.length > 0 && (
                <p>
                  <span className="font-medium">Not configured:</span>{' '}
                  {missingPaths.map(formatPathName).join(', ')}
                </p>
              )}
              {invalidPaths.length > 0 && (
                <p>
                  <span className="font-medium">Invalid paths:</span>{' '}
                  {invalidPaths.map(formatPathName).join(', ')}
                </p>
              )}
            </div>
          )}

          <div className="mt-3">
            <Link to="/settings">
              <Button size="sm" variant="default">
                Configure Now
              </Button>
            </Link>
          </div>
        </div>
      </div>
    </div>
  )
}
