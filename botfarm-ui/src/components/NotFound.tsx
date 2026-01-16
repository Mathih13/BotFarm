import { Link } from '@tanstack/react-router'
import { Button } from '~/components/ui/button'

export function NotFound({ children }: { children?: any }) {
  return (
    <div className="space-y-2 p-2">
      <div className="text-muted-foreground">
        {children || <p>The page you are looking for does not exist.</p>}
      </div>
      <p className="flex items-center gap-2 flex-wrap">
        <Button
          onClick={() => window.history.back()}
          variant="default"
          size="sm"
          className="uppercase font-black"
        >
          Go back
        </Button>
        <Button asChild variant="secondary" size="sm" className="uppercase font-black">
          <Link to="/">Start Over</Link>
        </Button>
      </p>
    </div>
  )
}
