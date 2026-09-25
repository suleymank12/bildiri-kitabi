import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button, EmptyState } from '../components/ui';

interface ErrorBoundaryState {
  failed: boolean;
}

/** Catches unexpected rendering errors so the user sees a plain message instead of an empty page. */
export class ErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { failed: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // Only in development: the browser console is the place for details, the page shows none.
    if (import.meta.env.DEV) {
      console.error(error, info.componentStack);
    }
  }

  override render(): ReactNode {
    if (!this.state.failed) {
      return this.props.children;
    }

    return (
      <EmptyState
        headingLevel="h1"
        title="Bir şeyler ters gitti"
        action={
          <Button
            variant="primary"
            onClick={() => {
              window.location.reload();
            }}
          >
            Sayfayı yenile
          </Button>
        }
      >
        Beklenmeyen bir hata oluştu. Sayfayı yenileyerek devam edebilirsiniz; verileriniz sunucuda saklanıyor.
      </EmptyState>
    );
  }
}
