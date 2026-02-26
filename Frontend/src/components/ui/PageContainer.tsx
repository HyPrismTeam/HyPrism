import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

interface PageContainerProps {
  children: ComponentChildren;
  className?: string;
  contentClassName?: string;
}

export function PageContainer({ children, className = '', contentClassName = '' }: PageContainerProps) {
  return (
    <div className={`h-full w-full overflow-y-auto overflow-x-hidden pb-28 ${className}`.trim()}>
      <div className={`mx-auto w-full max-w-6xl px-4 px-6 px-8 pt-6 ${contentClassName}`.trim()}>
        {children}
      </div>
    </div>
  );
}
