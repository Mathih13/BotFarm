import { useState } from 'react'
import type { ConfigResponse } from '~/lib/types'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Checkbox } from '~/components/ui/checkbox'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '~/components/ui/collapsible'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { PathInput } from './PathInput'

interface ConfigFormProps {
  config: ConfigResponse
  onChange: (config: ConfigResponse) => void
}

export function ConfigForm({ config, onChange }: ConfigFormProps) {
  const [openSections, setOpenSections] = useState<Record<string, boolean>>({
    server: true,
    bots: true,
    paths: true,
    mysql: false,
    webui: false,
  })

  const toggleSection = (section: string) => {
    setOpenSections((prev) => ({ ...prev, [section]: !prev[section] }))
  }

  const updateField = <K extends keyof ConfigResponse>(
    field: K,
    value: ConfigResponse[K]
  ) => {
    onChange({ ...config, [field]: value })
  }

  return (
    <div className="space-y-4">
      {/* Server Connection */}
      <Card>
        <Collapsible open={openSections.server} onOpenChange={() => toggleSection('server')}>
          <CollapsibleTrigger asChild>
            <CardHeader className="cursor-pointer hover:bg-muted/50 transition-colors">
              <div className="flex items-center gap-2">
                {openSections.server ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                <CardTitle className="text-lg">Server Connection</CardTitle>
              </div>
            </CardHeader>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="hostname">Hostname</Label>
                <Input
                  id="hostname"
                  value={config.hostname}
                  onChange={(e) => updateField('hostname', e.target.value)}
                  placeholder="localhost"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="port">Auth Port</Label>
                <Input
                  id="port"
                  type="number"
                  value={config.port}
                  onChange={(e) => updateField('port', parseInt(e.target.value) || 0)}
                  placeholder="3724"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="username">Username</Label>
                <Input
                  id="username"
                  value={config.username}
                  onChange={(e) => updateField('username', e.target.value)}
                  placeholder="admin"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="password">Password</Label>
                <Input
                  id="password"
                  type="password"
                  value={config.password}
                  onChange={(e) => updateField('password', e.target.value)}
                  placeholder="admin"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="raPort">Remote Access Port</Label>
                <Input
                  id="raPort"
                  type="number"
                  value={config.raPort}
                  onChange={(e) => updateField('raPort', parseInt(e.target.value) || 0)}
                  placeholder="3443"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="realmID">Realm ID</Label>
                <Input
                  id="realmID"
                  type="number"
                  value={config.realmID}
                  onChange={(e) => updateField('realmID', parseInt(e.target.value) || 0)}
                  placeholder="0"
                />
              </div>
            </CardContent>
          </CollapsibleContent>
        </Collapsible>
      </Card>

      {/* Bot Settings */}
      <Card>
        <Collapsible open={openSections.bots} onOpenChange={() => toggleSection('bots')}>
          <CollapsibleTrigger asChild>
            <CardHeader className="cursor-pointer hover:bg-muted/50 transition-colors">
              <div className="flex items-center gap-2">
                {openSections.bots ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                <CardTitle className="text-lg">Bot Settings</CardTitle>
              </div>
            </CardHeader>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <CardContent className="space-y-4">
              <p className="text-sm text-muted-foreground">
                These settings only apply to automatic bots spawned with the <code className="bg-muted px-1 rounded">--auto</code> flag.
                Bots created by test routes or suites use their own harness configuration.
              </p>
              <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="minBotsCount">Min Bots Count</Label>
                <Input
                  id="minBotsCount"
                  type="number"
                  value={config.minBotsCount}
                  onChange={(e) => updateField('minBotsCount', parseInt(e.target.value) || 0)}
                  placeholder="15"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="maxBotsCount">Max Bots Count</Label>
                <Input
                  id="maxBotsCount"
                  type="number"
                  value={config.maxBotsCount}
                  onChange={(e) => updateField('maxBotsCount', parseInt(e.target.value) || 0)}
                  placeholder="15"
                />
              </div>
              <div className="flex items-center space-x-2 pt-6">
                <Checkbox
                  id="randomBots"
                  checked={config.randomBots}
                  onCheckedChange={(checked) => updateField('randomBots', checked === true)}
                />
                <Label htmlFor="randomBots" className="cursor-pointer">
                  Random Bots
                </Label>
              </div>
              <div className="flex items-center space-x-2 pt-6">
                <Checkbox
                  id="createAccountOnly"
                  checked={config.createAccountOnly}
                  onCheckedChange={(checked) => updateField('createAccountOnly', checked === true)}
                />
                <Label htmlFor="createAccountOnly" className="cursor-pointer">
                  Create Account Only
                </Label>
              </div>
              </div>
            </CardContent>
          </CollapsibleContent>
        </Collapsible>
      </Card>

      {/* Data Paths */}
      <Card>
        <Collapsible open={openSections.paths} onOpenChange={() => toggleSection('paths')}>
          <CollapsibleTrigger asChild>
            <CardHeader className="cursor-pointer hover:bg-muted/50 transition-colors">
              <div className="flex items-center gap-2">
                {openSections.paths ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                <CardTitle className="text-lg">Data Paths</CardTitle>
              </div>
            </CardHeader>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <CardContent className="space-y-4">
              <PathInput
                label="MMaps Folder Path"
                value={config.mmapsFolderPath}
                onChange={(value) => updateField('mmapsFolderPath', value)}
                pathType="mmaps"
                placeholder="C:\path\to\mmaps"
              />
              <PathInput
                label="VMaps Folder Path"
                value={config.vmapsFolderPath}
                onChange={(value) => updateField('vmapsFolderPath', value)}
                pathType="vmaps"
                placeholder="C:\path\to\vmaps"
              />
              <PathInput
                label="Maps Folder Path"
                value={config.mapsFolderPath}
                onChange={(value) => updateField('mapsFolderPath', value)}
                pathType="maps"
                placeholder="C:\path\to\maps"
              />
              <PathInput
                label="DBCs Folder Path"
                value={config.dbcsFolderPath}
                onChange={(value) => updateField('dbcsFolderPath', value)}
                pathType="dbcs"
                placeholder="C:\path\to\dbc"
              />
            </CardContent>
          </CollapsibleContent>
        </Collapsible>
      </Card>

      {/* MySQL Settings */}
      <Card>
        <Collapsible open={openSections.mysql} onOpenChange={() => toggleSection('mysql')}>
          <CollapsibleTrigger asChild>
            <CardHeader className="cursor-pointer hover:bg-muted/50 transition-colors">
              <div className="flex items-center gap-2">
                {openSections.mysql ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                <CardTitle className="text-lg">MySQL Database</CardTitle>
              </div>
            </CardHeader>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="mySQLHost">Host</Label>
                <Input
                  id="mySQLHost"
                  value={config.mySQLHost}
                  onChange={(e) => updateField('mySQLHost', e.target.value)}
                  placeholder="localhost"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="mySQLPort">Port</Label>
                <Input
                  id="mySQLPort"
                  type="number"
                  value={config.mySQLPort}
                  onChange={(e) => updateField('mySQLPort', parseInt(e.target.value) || 0)}
                  placeholder="3306"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="mySQLUser">User</Label>
                <Input
                  id="mySQLUser"
                  value={config.mySQLUser}
                  onChange={(e) => updateField('mySQLUser', e.target.value)}
                  placeholder="trinity"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="mySQLPassword">Password</Label>
                <Input
                  id="mySQLPassword"
                  type="password"
                  value={config.mySQLPassword}
                  onChange={(e) => updateField('mySQLPassword', e.target.value)}
                  placeholder="trinity"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="mySQLCharactersDB">Characters DB</Label>
                <Input
                  id="mySQLCharactersDB"
                  value={config.mySQLCharactersDB}
                  onChange={(e) => updateField('mySQLCharactersDB', e.target.value)}
                  placeholder="characters"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="mySQLWorldDB">World DB</Label>
                <Input
                  id="mySQLWorldDB"
                  value={config.mySQLWorldDB}
                  onChange={(e) => updateField('mySQLWorldDB', e.target.value)}
                  placeholder="world"
                />
              </div>
            </CardContent>
          </CollapsibleContent>
        </Collapsible>
      </Card>

      {/* Web UI Settings */}
      <Card>
        <Collapsible open={openSections.webui} onOpenChange={() => toggleSection('webui')}>
          <CollapsibleTrigger asChild>
            <CardHeader className="cursor-pointer hover:bg-muted/50 transition-colors">
              <div className="flex items-center gap-2">
                {openSections.webui ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                <CardTitle className="text-lg">Web UI</CardTitle>
              </div>
            </CardHeader>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <div className="flex items-center space-x-2 pt-6">
                <Checkbox
                  id="enableWebUI"
                  checked={config.enableWebUI}
                  onCheckedChange={(checked) => updateField('enableWebUI', checked === true)}
                />
                <Label htmlFor="enableWebUI" className="cursor-pointer">
                  Enable Web UI
                </Label>
              </div>
              <div className="space-y-2">
                <Label htmlFor="webUIPort">Web UI Port</Label>
                <Input
                  id="webUIPort"
                  type="number"
                  value={config.webUIPort}
                  onChange={(e) => updateField('webUIPort', parseInt(e.target.value) || 0)}
                  placeholder="5000"
                />
              </div>
            </CardContent>
          </CollapsibleContent>
        </Collapsible>
      </Card>
    </div>
  )
}
