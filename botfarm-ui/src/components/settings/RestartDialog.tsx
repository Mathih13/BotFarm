import { useNavigate } from '@tanstack/react-router'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '~/components/ui/dialog'
import { Button } from '~/components/ui/button'
import { AlertTriangle } from 'lucide-react'

interface RestartDialogProps {
  open: boolean
  onClose: () => void
}

export function RestartDialog({ open, onClose }: RestartDialogProps) {
  const navigate = useNavigate()

  const handleGoToDashboard = () => {
    onClose()
    navigate({ to: '/' })
  }

  return (
    <Dialog open={open} onOpenChange={(isOpen) => !isOpen && onClose()}>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-5 w-5 text-amber-500" />
            <DialogTitle>Application Restarting</DialogTitle>
          </div>
          <DialogDescription className="pt-2">
            Configuration has been saved successfully. The application is now restarting
            to apply the new settings.
          </DialogDescription>
        </DialogHeader>

        <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 text-sm text-amber-800">
          <p className="font-medium mb-2">Please note:</p>
          <ul className="list-disc list-inside space-y-1">
            <li>Any running tests will be stopped</li>
            <li>All bot connections will be closed</li>
            <li>The page will need to be refreshed after restart</li>
          </ul>
        </div>

        <DialogFooter className="flex-col sm:flex-row gap-2">
          <Button variant="outline" onClick={onClose}>
            Dismiss
          </Button>
          <Button variant="secondary" onClick={handleGoToDashboard}>
            Go to Dashboard
          </Button>
          <Button onClick={() => window.location.reload()}>
            Refresh Page
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
