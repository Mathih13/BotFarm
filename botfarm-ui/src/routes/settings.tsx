import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { configApi } from '~/lib/api'
import type { ConfigResponse, ConfigUpdateRequest } from '~/lib/types'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '~/components/ui/card'
import { Button } from '~/components/ui/button'
import { ConfigForm } from '~/components/settings/ConfigForm'
import { RestartDialog } from '~/components/settings/RestartDialog'

export const Route = createFileRoute('/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const [config, setConfig] = useState<ConfigResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showRestartDialog, setShowRestartDialog] = useState(false)
  const [hasChanges, setHasChanges] = useState(false)

  // Load config on mount
  useEffect(() => {
    async function loadConfig() {
      try {
        const configRes = await configApi.getConfig()
        setConfig(configRes)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load configuration')
      } finally {
        setLoading(false)
      }
    }
    loadConfig()
  }, [])

  const handleConfigChange = (newConfig: ConfigResponse) => {
    setConfig(newConfig)
    setHasChanges(true)
  }

  const handleSave = async () => {
    if (!config) return

    setSaving(true)
    setError(null)

    try {
      const updateRequest: ConfigUpdateRequest = {
        hostname: config.hostname,
        port: config.port,
        username: config.username,
        password: config.password,
        raPort: config.raPort,
        realmID: config.realmID,
        minBotsCount: config.minBotsCount,
        maxBotsCount: config.maxBotsCount,
        randomBots: config.randomBots,
        createAccountOnly: config.createAccountOnly,
        mmapsFolderPath: config.mmapsFolderPath,
        vmapsFolderPath: config.vmapsFolderPath,
        mapsFolderPath: config.mapsFolderPath,
        dbcsFolderPath: config.dbcsFolderPath,
        mySQLHost: config.mySQLHost,
        mySQLPort: config.mySQLPort,
        mySQLUser: config.mySQLUser,
        mySQLPassword: config.mySQLPassword,
        mySQLCharactersDB: config.mySQLCharactersDB,
        mySQLWorldDB: config.mySQLWorldDB,
        enableWebUI: config.enableWebUI,
        webUIPort: config.webUIPort,
      }

      const result = await configApi.updateConfig(updateRequest)

      if (result.success) {
        setHasChanges(false)
        if (result.restartRequired) {
          setShowRestartDialog(true)
        }
      } else {
        setError(result.message || 'Failed to save configuration')
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading configuration...</div>
      </div>
    )
  }

  if (error && !config) {
    return (
      <div className="max-w-4xl mx-auto px-4 py-8">
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          Error: {error}
        </div>
      </div>
    )
  }

  return (
    <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex justify-between items-center mb-8">
        <div>
          <h1 className="text-2xl font-bold">Settings</h1>
          <p className="text-muted-foreground mt-1">
            Configure BotFarm server connection, data paths, and other settings.
          </p>
        </div>
        <div className="flex items-center gap-4">
          {hasChanges && (
            <span className="text-sm text-amber-600">Unsaved changes</span>
          )}
          <Button
            onClick={handleSave}
            disabled={!hasChanges || saving}
          >
            {saving ? 'Saving...' : 'Save Changes'}
          </Button>
        </div>
      </div>

      {error && (
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive mb-6">
          {error}
        </div>
      )}

      {config && (
        <ConfigForm
          config={config}
          onChange={handleConfigChange}
        />
      )}

      <RestartDialog
        open={showRestartDialog}
        onClose={() => setShowRestartDialog(false)}
      />
    </div>
  )
}
